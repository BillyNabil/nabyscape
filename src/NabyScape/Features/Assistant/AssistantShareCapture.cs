using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT;

namespace NabyScape;

// The Windows picker owns consent and window/display selection. No input injection.
internal sealed class AssistantShareCapture : IDisposable
{
    private GraphicsCaptureItem? item;
    private IDirect3DDevice? device;
    private readonly CancellationTokenSource lifetime=new();
    private bool disposed;
    private readonly object gate=new();
    private int active;
    internal event Action? SourceClosed;
    internal string Name=>item?.DisplayName??"Shared source";
    internal async Task<bool> Pick(IntPtr owner)
    {
        if(!GraphicsCaptureSession.IsSupported())throw new InvalidOperationException("Screen sharing needs a supported Windows graphics device.");
        var picker=new GraphicsCapturePicker();WinRT.Interop.InitializeWithWindow.Initialize(picker,owner);
        item=await picker.PickSingleItemAsync();if(item==null)return false;
        item.Closed+=Closed;device=CreateDevice();return true;
    }
    private void Closed(GraphicsCaptureItem sender,object args)
    {lock(gate){if(disposed)return;lifetime.Cancel();}SourceClosed?.Invoke();}
    internal async Task<AssistantScreenFrame> Capture(AssistantScreenTarget target,CancellationToken token)
    {
        GraphicsCaptureItem source;IDirect3DDevice graphics;CancellationToken shutdown;
        lock(gate)
        {
            if(disposed || item==null || device==null || lifetime.IsCancellationRequested)throw new InvalidOperationException("The shared window closed. Choose another window or display.");
            source=item;graphics=device;shutdown=lifetime.Token;active++;
        }
        try{return await CaptureFrame(target,source,graphics,shutdown,token);}
        finally{lock(gate){active--;if(disposed && active==0)Release();}}
    }
    private static async Task<AssistantScreenFrame> CaptureFrame(AssistantScreenTarget target,GraphicsCaptureItem source,IDirect3DDevice graphics,CancellationToken shutdown,CancellationToken token)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token,shutdown);timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var size=source.Size;if(size.Width<=0 || size.Height<=0)throw new InvalidOperationException("Restore the selected window before sharing it.");
        using var pool=Direct3D11CaptureFramePool.CreateFreeThreaded(graphics,DirectXPixelFormat.B8G8R8A8UIntNormalized,1,size);
        var done=new TaskCompletionSource<SoftwareBitmap>(TaskCreationOptions.RunContinuationsAsynchronously);
        SoftwareBitmap? capturedBitmap=null;
        var callbackGate=new object();Task copying=Task.CompletedTask;bool stopping=false;
        async Task Copy(Direct3D11CaptureFramePool sender)
        {
            try
            {
                using var frame=sender.TryGetNextFrame();if(frame==null)return;
                if(frame.ContentSize.Width!=size.Width || frame.ContentSize.Height!=size.Height)
                {size=frame.ContentSize;sender.Recreate(graphics,DirectXPixelFormat.B8G8R8A8UIntNormalized,1,size);return;}
                // Keep the original stored BGRA, including alpha for alpha-aware resizing.
                var bitmap=await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface,BitmapAlphaMode.Premultiplied);
                // Cleanup owns this bitmap even if cancellation won the completion race.
                // It waits for Copy before disposing, so no unobserved successful result leaks.
                capturedBitmap=bitmap;done.TrySetResult(bitmap);
            }
            catch(Exception ex){done.TrySetException(ex);}
        }
        void Arrived(Direct3D11CaptureFramePool sender,object args)
        {lock(callbackGate){if(!stopping && !done.Task.IsCompleted && copying.IsCompleted)copying=Copy(sender);}}
        pool.FrameArrived+=Arrived;using var session=pool.CreateCaptureSession(source);session.IsCursorCaptureEnabled=false;
        try
        {
            using var registration=timeout.Token.Register(()=>done.TrySetCanceled(timeout.Token));session.StartCapture();var bitmap=await done.Task;return await Encode(target,bitmap,timeout.Token);
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested){throw new InvalidOperationException("The shared source is unavailable or minimized. Restore it, or choose another source.");}
        finally
        {
            Task pending;lock(callbackGate){stopping=true;pending=copying;done.TrySetCanceled();}
            pool.FrameArrived-=Arrived;try{await pending;}finally{capturedBitmap?.Dispose();}
        }
    }
    internal static async Task<AssistantScreenFrame> Encode(AssistantScreenTarget target,SoftwareBitmap source,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        long bytes=(long)source.PixelWidth*source.PixelHeight*4;if(bytes>128*1024*1024)throw new InvalidOperationException("Choose a smaller display or window to share.");
        double scale=Math.Min(1,1280d/Math.Max(source.PixelWidth,source.PixelHeight));
        int width=Math.Max(1,(int)(source.PixelWidth*scale)),height=Math.Max(1,(int)(source.PixelHeight*scale));
        // Scan/legacy alpha interpolation run off the UI thread. The task owns the lock
        // until it completes, including cancellation, so capture cleanup cannot race it.
        var alphaFrame=await Task.Run(()=>EncodeAlphaIfNeeded(target,source,width,height,token),token).ConfigureAwait(false);
        if(alphaFrame!=null)return alphaFrame;
        using var stream=new InMemoryRandomAccessStream();
        var properties=new BitmapPropertySet{{"ImageQuality",new BitmapTypedValue(.75f,Windows.Foundation.PropertyType.Single)}};
        var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId,stream,properties).AsTask(token).ConfigureAwait(false);
        encoder.SetSoftwareBitmap(source);
        encoder.BitmapTransform.ScaledWidth=(uint)width;encoder.BitmapTransform.ScaledHeight=(uint)height;
        encoder.BitmapTransform.InterpolationMode=BitmapInterpolationMode.Linear;
        encoder.IsThumbnailGenerated=false;
        await encoder.FlushAsync().AsTask(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        // Serialize the bounded JPEG; no full-resolution managed pixel array is created.
        using var input=stream.AsStreamForRead();using var output=new MemoryStream();
        await input.CopyToAsync(output,token).ConfigureAwait(false);token.ThrowIfCancellationRequested();
        return new(target,width,height,"data:image/jpeg;base64,"+Convert.ToBase64String(output.GetBuffer(),0,(int)output.Length));
    }
    internal static AssistantScreenFrame? EncodeAlphaIfNeeded(AssistantScreenTarget target,SoftwareBitmap source,int width,int height,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var buffer=source.LockBuffer(BitmapBufferAccessMode.Read);
        using var reference=buffer.CreateReference();
        var access=reference.As<IMemoryBufferByteAccess>();
        try
        {
            access.GetBuffer(out var pixels,out uint capacity);var plane=buffer.GetPlaneDescription(0);
            long last=(long)plane.StartIndex+(long)(source.PixelHeight-1)*plane.Stride;
            if(source.BitmapPixelFormat!=BitmapPixelFormat.Bgra8 || Math.Min(plane.StartIndex,last)<0 ||
                Math.Max(plane.StartIndex,last)+(long)source.PixelWidth*4>capacity)
                throw new InvalidOperationException("The shared source has an unsupported pixel buffer.");
            bool alpha=false;var scan=ArrayPool<int>.Shared.Rent(4096);
            try
            {
                // A bounded 16 KiB scratch block avoids a marshaling call per pixel,
                // without allocating a frame-sized managed array.
                for(int y=0;y<source.PixelHeight && !alpha;y++)
                {
                    token.ThrowIfCancellationRequested();var row=pixels+plane.StartIndex+y*plane.Stride;
                    for(int x=0;x<source.PixelWidth && !alpha;x+=4096)
                    {
                        int count=Math.Min(4096,source.PixelWidth-x);Marshal.Copy(row+x*4,scan,0,count);
                        for(int i=0;i<count;i++)if((uint)scan[i]>>24!=255){alpha=true;break;}
                    }
                }
            }
            finally{ArrayPool<int>.Shared.Return(scan,clearArray:true);}
            if(!alpha)return null;
            // The former pipeline interpreted stored (premultiplied) bytes as straight
            // ARGB, resized with GDI, then dropped alpha for JPEG. Preserve that exact
            // ordering for transparent frames using a borrowed native view, with no
            // full-resolution managed copy. Dispose the view before releasing the lock.
            using var bitmap=new System.Drawing.Bitmap(source.PixelWidth,source.PixelHeight,plane.Stride,System.Drawing.Imaging.PixelFormat.Format32bppArgb,pixels+plane.StartIndex);
            token.ThrowIfCancellationRequested();
            using var reduced=new System.Drawing.Bitmap(bitmap,width,height);
            token.ThrowIfCancellationRequested();using var output=new MemoryStream();
            reduced.Save(output,System.Drawing.Imaging.ImageFormat.Jpeg);
            token.ThrowIfCancellationRequested();
            return new(target,width,height,"data:image/jpeg;base64,"+Convert.ToBase64String(output.GetBuffer(),0,(int)output.Length));
        }
        finally{Marshal.ReleaseComObject(access);}
    }
    [ComImport,Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {void GetBuffer(out IntPtr buffer,out uint capacity);}
    private static IDirect3DDevice CreateDevice()
    {
        IntPtr native=IntPtr.Zero,context=IntPtr.Zero,dxgi=IntPtr.Zero,inspectable=IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero,1,IntPtr.Zero,0x20,IntPtr.Zero,0,7,out native,out _,out context));
            var id=new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");Marshal.ThrowExceptionForHR(Marshal.QueryInterface(native,in id,out dxgi));Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi,out inspectable));return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally{foreach(var handle in new[]{inspectable,dxgi,context,native})if(handle!=IntPtr.Zero)Marshal.Release(handle);}
    }
    public void Dispose()
    {lock(gate){if(disposed)return;disposed=true;lifetime.Cancel();if(item!=null)item.Closed-=Closed;if(active==0)Release();}}
    private void Release(){item=null;(device as IDisposable)?.Dispose();device=null;lifetime.Dispose();}
    [DllImport("d3d11.dll")]private static extern int D3D11CreateDevice(IntPtr adapter,uint driver,IntPtr software,uint flags,IntPtr levels,uint count,uint sdk,out IntPtr device,out uint feature,out IntPtr context);
    [DllImport("d3d11.dll")]private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgi,out IntPtr device);
}
