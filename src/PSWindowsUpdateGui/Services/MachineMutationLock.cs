using System;
using System.Threading;

namespace PSWindowsUpdateGui.Services;

internal sealed class MachineMutationLock : IDisposable
{
    internal const string Name = @"Global\PSWindowsUpdateGUI-WUA-Mutation";
    private readonly Mutex _mutex;
    private bool _acquired;

    private MachineMutationLock()
    {
        _mutex = new Mutex(false, Name);
        try { _acquired = _mutex.WaitOne(TimeSpan.Zero); }
        catch (AbandonedMutexException) { _acquired = true; }
        if (!_acquired)
        {
            _mutex.Dispose();
            throw new InvalidOperationException("Another PSWindowsUpdateGUI update modification is already active on this computer.");
        }
    }

    public static MachineMutationLock Acquire() => new MachineMutationLock();

    public void Dispose()
    {
        if (_acquired)
        {
            _mutex.ReleaseMutex();
            _acquired = false;
        }
        _mutex.Dispose();
    }
}
