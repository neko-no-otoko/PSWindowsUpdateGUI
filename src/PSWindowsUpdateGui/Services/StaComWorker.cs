using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PSWindowsUpdateGui.Services;

internal sealed class StaComWorker : IDisposable
{
    private const uint WorkMessage = 0x8001;
    private const uint QuitMessage = 0x0012;
    private const uint PeekNoRemove = 0;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ConcurrentQueue<Action> _work = new();
    private uint _threadId;
    private bool _disposed;

    public StaComWorker(string name)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    public Task<T> InvokeAsync<T>(Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async () =>
        {
            try { completion.TrySetResult(await operation().ConfigureAwait(true)); }
            catch (OperationCanceledException exception) { completion.TrySetCanceled(exception.CancellationToken); }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return completion.Task;
    }

    public Task InvokeAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async () =>
        {
            try { await operation().ConfigureAwait(true); completion.TrySetResult(); }
            catch (OperationCanceledException exception) { completion.TrySetCanceled(exception.CancellationToken); }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return completion.Task;
    }

    public Task<T> InvokeAsync<T>(Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try { completion.TrySetResult(operation()); }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return completion.Task;
    }

    private void Post(Action action)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(StaComWorker));
        _work.Enqueue(action);
        if (!PostThreadMessage(_threadId, WorkMessage, UIntPtr.Zero, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not wake the WUA STA dispatcher.");
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _ = PeekMessage(out _, IntPtr.Zero, 0, 0, PeekNoRemove);
        SynchronizationContext.SetSynchronizationContext(new PumpSynchronizationContext(Post));
        _ready.Set();

        while (true)
        {
            var result = GetMessage(out var message, IntPtr.Zero, 0, 0);
            if (result == 0 || message.Message == QuitMessage) break;
            if (result == -1) throw new Win32Exception(Marshal.GetLastWin32Error(), "The WUA STA message pump failed.");
            if (message.Message == WorkMessage)
            {
                while (_work.TryDequeue(out var action)) action();
                continue;
            }
            _ = TranslateMessage(ref message);
            _ = DispatchMessage(ref message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ = PostThreadMessage(_threadId, QuitMessage, UIntPtr.Zero, IntPtr.Zero);
        _ = _thread.Join(TimeSpan.FromSeconds(10));
        _ready.Dispose();
    }

    private sealed class PumpSynchronizationContext : SynchronizationContext
    {
        private readonly Action<Action> _post;
        public PumpSynchronizationContext(Action<Action> post) => _post = post;
        public override void Post(SendOrPostCallback callback, object? state) => _post(() => callback(state));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage message, IntPtr window, uint minimum, uint maximum);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint minimum, uint maximum, uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref NativeMessage message);
}
