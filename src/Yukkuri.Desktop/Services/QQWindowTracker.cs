using System.Runtime.InteropServices;

namespace Yukkuri.Services;

public sealed class QQWindowTracker : IDisposable
{
    private readonly ForegroundCallback _callback;
    private IntPtr _hook;
    public IntPtr LastWindow { get; private set; }

    public QQWindowTracker()
    {
        _callback = (_, _, window, _, _, _, _) => Capture(window);
        _hook = SetWinEventHook(3, 3, IntPtr.Zero, _callback, 0, 0, 2);
        Capture(NativeMethods.GetForegroundWindow());
    }

    private void Capture(IntPtr window)
    {
        if (window == IntPtr.Zero) return;
        NativeMethods.GetWindowThreadProcessId(window, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            if (process.ProcessName.Equals("QQ", StringComparison.OrdinalIgnoreCase)) LastWindow = window;
        }
        catch (ArgumentException) { }
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    private delegate void ForegroundCallback(IntPtr hook, uint eventId, IntPtr window,
        int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module,
        ForegroundCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
}
