using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal sealed partial class AssistantView
{
    private bool pasting;
    private void ConfigureClipboard()
    {
        // TextBox's built-in Paste command checks for text, so its Pasting event
        // never fires for an image-only clipboard. Own that command explicitly.
        input.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste,(_,e)=>{e.Handled=true;_=PasteClipboard();},(_,e)=>
        {
            e.Handled=true;try{var data=Clipboard.GetDataObject();e.CanExecute=data!=null && (data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text) || data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent("PNG") || data.GetDataPresent("image/png") || data.GetDataPresent(DataFormats.Bitmap));}
            catch(ExternalException){e.CanExecute=true;}
        }));
        input.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.V && Keyboard.Modifiers==ModifierKeys.Control){e.Handled=true;_=PasteClipboard();}};
    }
    private async Task PasteClipboard()
    {
        if(pasting)return;pasting=true;
        try
        {
            IDataObject? data=null;
            for(int attempt=0;attempt<3;attempt++)
            {
                try{data=Clipboard.GetDataObject();break;}
                catch(ExternalException) when(attempt<2){await Task.Delay(50);}
            }
            if(data==null)return;
            foreach(string format in new[]{"PNG","image/png"})
            {
                if(!data.GetDataPresent(format))continue;
                object? value=data.GetData(format);
                if(value is byte[] bytes){await ai.AttachEncodedImage(bytes);return;}
                if(value is Stream stream)
                {
                    using var copy=new MemoryStream();byte[] buffer=new byte[16384];int count;
                    if(stream.CanSeek)stream.Position=0;
                    while((count=stream.Read(buffer,0,buffer.Length))>0){if(copy.Length+count>AssistantAttachments.MaximumFileBytes)throw new IOException("The clipboard image exceeds 16 MB.");copy.Write(buffer,0,count);}
                    await ai.AttachEncodedImage(copy.ToArray());return;
                }
            }
            if(data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths){await ai.AttachFiles(paths);return;}
            if(Clipboard.ContainsImage() && Clipboard.GetImage() is BitmapSource image){await ai.AttachImage(image);return;}
            string? text=data.GetData(DataFormats.UnicodeText) as string??data.GetData(DataFormats.Text) as string;
            if(text!=null){int start=input.SelectionStart;input.SelectedText=text;input.CaretIndex=start+text.Length;}
        }
        catch(Exception ex){status.Text=ex is ExternalException?"The clipboard is busy. Try pasting again.":"Could not paste: "+ex.Message;status.Visibility=Visibility.Visible;}
        finally{pasting=false;}
    }
}

internal static class AssistantClipboard
{
    internal static async Task CopyImage(byte[] bytes)
    {
        var bitmap=await Task.Run(()=>AssistantAttachments.Thumbnail(bytes,1800));
        var png=await Task.Run(()=>{var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=new MemoryStream();encoder.Save(output);return output.ToArray();});
        var data=new DataObject();data.SetData(DataFormats.Bitmap,bitmap,true);data.SetData("PNG",new MemoryStream(png),false);data.SetData("image/png",new MemoryStream(png),false);Clipboard.SetDataObject(data,true);
    }
}
