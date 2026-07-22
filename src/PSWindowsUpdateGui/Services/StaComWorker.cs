using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace PSWindowsUpdateGui.Services;

internal sealed class StaComWorker : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
    private Dispatcher? _dispatcher;
    private bool _disposed;

    public StaComWorker(string name)
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = name
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    public Task<T> InvokeAsync<T>(Func<Task<T>> operation)
    {
        if (operation == null) throw new ArgumentNullException(nameof(operation));
        if (_disposed || _dispatcher == null) throw new ObjectDisposedException(nameof(StaComWorker));
        return _dispatcher.InvokeAsync(operation, DispatcherPriority.Normal).Task.Unwrap();
    }

    public Task InvokeAsync(Func<Task> operation)
    {
        if (operation == null) throw new ArgumentNullException(nameof(operation));
        if (_disposed || _dispatcher == null) throw new ObjectDisposedException(nameof(StaComWorker));
        return _dispatcher.InvokeAsync(operation, DispatcherPriority.Normal).Task.Unwrap();
    }

    public Task<T> InvokeAsync<T>(Func<T> operation)
    {
        if (operation == null) throw new ArgumentNullException(nameof(operation));
        if (_disposed || _dispatcher == null) throw new ObjectDisposedException(nameof(StaComWorker));
        return _dispatcher.InvokeAsync(operation, DispatcherPriority.Normal).Task;
    }

    private void Run()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _ready.Set();
        Dispatcher.Run();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
        if (!_thread.Join(TimeSpan.FromSeconds(10))) _thread.Interrupt();
        _ready.Dispose();
    }
}
