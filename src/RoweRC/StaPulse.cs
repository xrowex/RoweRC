namespace RoweRC;

// Drive Steam on one STA thread without the coarse WM_TIMER cadence of a forms timer.
internal sealed class StaPulse : IDisposable
{
    private readonly Control dispatcher = new();
    private readonly System.Threading.Timer timer;
    private int pending, disposed;
    public StaPulse(Action action)
    {
        _ = dispatcher.Handle;
        timer = new System.Threading.Timer(_ => {
            if (Volatile.Read(ref disposed) != 0 || Interlocked.CompareExchange(ref pending, 1, 0) != 0) return;
            try
            {
                dispatcher.BeginInvoke((Action)(() => {
                    try { if (Volatile.Read(ref disposed) == 0) action(); }
                    finally { Volatile.Write(ref pending, 0); }
                }));
            }
            catch (InvalidOperationException) { Volatile.Write(ref pending, 0); }
        }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(8));
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        timer.Dispose(); dispatcher.Dispose();
    }
}
