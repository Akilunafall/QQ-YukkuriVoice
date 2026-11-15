using System.Windows.Automation;
using System.Windows.Interop;

namespace Yukkuri.Services;

public sealed class QQDraftMonitor : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _worker;
    private string? _previous;
    private string _status = "";
    private readonly object _snapshotGate = new();
    private IntPtr _lastWindow;
    private string _lastDraft = "";
    private volatile bool _enabled;
    public event Action<string>? DraftChanged;
    public event Action<string>? StatusChanged;
    public bool Enabled { get => _enabled; set => _enabled = value; }

    public (IntPtr Window, string Draft) GetLastSnapshot()
    {
        lock (_snapshotGate) return (_lastWindow, _lastDraft);
    }

    public QQDraftMonitor()
    {
        _worker = new Thread(Poll) { IsBackground = true, Name = "QQ focused draft reader" };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    private void Poll()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                if (!Enabled) SetStatus("QQ 联动已暂停");
                else if (NativeMethods.IsQQForeground(out var activeWindow, out _))
                {
                    lock (_snapshotGate) _lastWindow = activeWindow;
                    if (TryReadFocusedDraft(out var text))
                    {
                        lock (_snapshotGate) _lastDraft = text;
                        SetStatus("QQ 输入框已连接");
                        if (_previous != text)
                        {
                            _previous = text;
                            DraftChanged?.Invoke(text);
                        }
                    }
                    else SetStatus("QQ 已连接 · 等待输入框");
                }
                else
                {
                    var processes = Process.GetProcessesByName("QQ");
                    SetStatus(processes.Length > 0 ? "QQ 已就绪 · 等待输入" : "QQ 未运行");
                    foreach (var process in processes) process.Dispose();
                }
            }
            catch (Exception)
            {
                SetStatus("QQ 输入不可读 · 可用快捷键");
            }
            _stop.Token.WaitHandle.WaitOne(700);
        }
    }

    private void SetStatus(string value)
    {
        if (_status == value) return;
        _status = value;
        StatusChanged?.Invoke(value);
    }

    public static bool IsDraftCandidate(int controlType, string name, bool readOnly)
    {
        if (readOnly || name.Contains("搜索", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("search", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("密码", StringComparison.OrdinalIgnoreCase))
            return false;
        return controlType is 50004 or 50026 or 50030; // Edit, Group, Document.
    }

    public static bool TryReadFocusedDraft(out string text)
    {
        text = "";
        if (!NativeMethods.IsQQForeground(out var window, out var pid)) return false;
        var element = AutomationElement.FocusedElement;
        var windowElement = AutomationElement.FromHandle(window);
        var windowBounds = windowElement.Current.BoundingRectangle;
        for (var depth = 0; depth < 3 && element is not null; depth++)
        {
            var current = element.Current;
            if (current.ProcessId != pid || current.IsPassword) return false;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var raw))
            {
                var value = ((ValuePattern)raw).Current;
                // QQ NT exposes its contenteditable as a writable Group near the bottom.
                var nearComposer = current.BoundingRectangle.Top >= windowBounds.Top + windowBounds.Height * 0.45;
                if (nearComposer && IsDraftCandidate(current.ControlType.Id, current.Name ?? "", value.IsReadOnly))
                {
                    text = (value.Value ?? "").TrimEnd('\r', '\n').Replace("\ufffc", "");
                    if (text.Length > 241) text = text[..241];
                    return true;
                }
            }
            element = TreeWalker.ControlViewWalker.GetParent(element);
        }
        return false;
    }

    public void Dispose()
    {
        _stop.Cancel();
        // A nonresponsive third-party UIA provider must not block application shutdown.
        _worker.Join(300);
    }
}

public sealed class GlobalHotkeys : IDisposable
{
    private readonly IntPtr _window;
    private readonly HwndSource _source;
    public event Action? CaptureRequested;
    public event Action? SendRequested;
    public bool CaptureRegistered { get; }
    public bool SendRegistered { get; }
    public string CaptureLabel { get; }
    public string SendLabel { get; }

    public GlobalHotkeys(Window window)
    {
        _window = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_window)!;
        _source.AddHook(Hook);
        CaptureLabel = Register(0x591, 0x59, "Y", 0x78, "F9");
        SendLabel = Register(0x592, 0x50, "P", 0x79, "F10");
        CaptureRegistered = CaptureLabel != "快捷键不可用";
        SendRegistered = SendLabel != "快捷键不可用";
    }

    private string Register(int id, uint primary, string primaryLabel, uint fallback, string fallbackLabel)
    {
        if (NativeMethods.RegisterHotKey(_window, id, 0x4003, primary)) return "Ctrl + Alt + " + primaryLabel;
        if (NativeMethods.RegisterHotKey(_window, id, 0x4003, fallback)) return "Ctrl + Alt + " + fallbackLabel;
        if (NativeMethods.RegisterHotKey(_window, id, 0x4007, fallback)) return "Ctrl + Alt + Shift + " + fallbackLabel;
        return "快捷键不可用";
    }

    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0312) return IntPtr.Zero;
        if (wParam.ToInt32() == 0x591) { CaptureRequested?.Invoke(); handled = true; }
        if (wParam.ToInt32() == 0x592) { SendRequested?.Invoke(); handled = true; }
        return IntPtr.Zero;
    }

    public static async Task<string> CaptureQQAsync()
    {
        if (!NativeMethods.IsQQForeground(out var window, out _))
            throw new InvalidOperationException("请先将光标放在 QQ 聊天输入框，再按 Ctrl + Alt + Y。");
        var readable = await Task.Run(() => QQDraftMonitor.TryReadFocusedDraft(out var value) ? value : null);
        if (readable is not null) return readable;

        for (var i = 0; i < 30; i++)
        {
            if ((NativeMethods.GetAsyncKeyState(0x11) & 0x8000) == 0 &&
                (NativeMethods.GetAsyncKeyState(0x12) & 0x8000) == 0) break;
            await Task.Delay(35);
        }
        if (NativeMethods.GetForegroundWindow() != window)
            throw new InvalidOperationException("焦点已离开 QQ，请回到聊天输入框重试。");
        // The fallback is invoked explicitly by the user; automatic mode never copies.
        var previous = Clipboard.GetDataObject();
        var before = NativeMethods.GetClipboardSequenceNumber();
        uint copied = before;
        try
        {
            NativeMethods.KeyChord(0x41);
            await Task.Delay(70);
            if (NativeMethods.GetForegroundWindow() != window) throw new InvalidOperationException("QQ 焦点已变化，已取消取词。");
            NativeMethods.KeyChord(0x43);
            for (var i = 0; i < 15 && copied == before; i++)
            {
                await Task.Delay(40);
                copied = NativeMethods.GetClipboardSequenceNumber();
            }
            if (copied == before || !Clipboard.ContainsText())
                throw new InvalidOperationException("没有读到文字。请确认焦点位于 QQ 的聊天输入框。");
            var text = Clipboard.GetText();
            if (text.Length > 240) throw new InvalidOperationException("读取内容超过 240 个字符，请缩短输入。");
            return text;
        }
        finally
        {
            if (NativeMethods.GetForegroundWindow() == window) NativeMethods.EndKey();
            if (copied != before && NativeMethods.GetClipboardSequenceNumber() == copied)
            {
                if (previous is null) Clipboard.Clear();
                else Clipboard.SetDataObject(previous, true);
            }
        }
    }

    public void Dispose()
    {
        NativeMethods.UnregisterHotKey(_window, 0x591);
        NativeMethods.UnregisterHotKey(_window, 0x592);
        _source.RemoveHook(Hook);
    }
}

