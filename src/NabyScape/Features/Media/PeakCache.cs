using System;
using System.Collections.Generic;

namespace NabyScape;

// Enumerates on a two-second lease. A broken endpoint is removed immediately, and
// the next lease retries discovery, so neither errors nor silence cause per-frame COM discovery.
internal sealed class PeakCache<T> : IDisposable
{
    private readonly Func<IEnumerable<T>> enumerate;
    private readonly Func<T,float> sample;
    private readonly Action<T> release;
    private readonly Action<Exception> error;
    private readonly List<T> endpoints=new();
    private DateTimeOffset refreshAt;
    internal PeakCache(Func<IEnumerable<T>> enumerate,Func<T,float> sample,Action<T> release,Action<Exception> error)
    {this.enumerate=enumerate;this.sample=sample;this.release=release;this.error=error;}
    internal float Read(DateTimeOffset now)
    {
        if(now>=refreshAt)
        {
            Dispose();refreshAt=now.AddSeconds(2);
            try{foreach(var endpoint in enumerate())endpoints.Add(endpoint);}catch(Exception ex){error(ex);}
        }
        float peak=0;
        for(int i=endpoints.Count-1;i>=0;i--)
        {
            try{float value=sample(endpoints[i]);if(float.IsFinite(value))peak=Math.Max(peak,Math.Clamp(value,0,1));}
            catch(Exception ex){error(ex);Release(endpoints[i]);endpoints.RemoveAt(i);}
        }
        return peak;
    }
    private void Release(T endpoint){try{release(endpoint);}catch(Exception ex){error(ex);}}
    public void Dispose(){foreach(var endpoint in endpoints)Release(endpoint);endpoints.Clear();}
}
