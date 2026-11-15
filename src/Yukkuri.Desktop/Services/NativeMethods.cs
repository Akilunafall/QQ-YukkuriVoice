using System.Runtime.InteropServices;

namespace Yukkuri.Services;

internal static class NativeMethods
{
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] input, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Key;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public UIntPtr Extra;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public UIntPtr Extra;
    }

    public static bool IsQQForeground(out IntPtr window, out int processId)
    {
        window = GetForegroundWindow();
        GetWindowThreadProcessId(window, out var pid);
        processId = (int)pid;
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName.Equals("QQ", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void KeyChord(ushort key)
    {
        Input Make(ushort code, bool up = false) => new()
        {
            Type = 1,
            Data = new InputUnion { Keyboard = new KeyboardInput { Key = code, Flags = up ? 2u : 0u } }
        };
        var inputs = new[] { Make(0x11), Make(key), Make(key, true), Make(0x11, true) };
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new InvalidOperationException("无法读取 QQ 输入。请确保 QQ 与本软件都以普通权限运行。");
    }

    public static void EndKey()
    {
        var inputs = new[]
        {
            new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Key = 0x23 } } },
            new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Key = 0x23, Flags = 2 } } }
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    public static void ClickAt(double x, double y)
    {
        SetCursorPos((int)Math.Round(x), (int)Math.Round(y));
        var input = new Input
        {
            Type = 0,
            Data = new InputUnion
            {
                Mouse = new MouseInput { Flags = 0x0002 }
            }
        };
        if (SendInput(1, new[] { input }, Marshal.SizeOf<Input>()) != 1)
            throw new InvalidOperationException("无法点击 QQ 语音按钮。");
        input.Data.Mouse.Flags = 0x0004;
        if (SendInput(1, new[] { input }, Marshal.SizeOf<Input>()) != 1)
            throw new InvalidOperationException("无法完成 QQ 语音按钮点击。");
    }

    public static void MouseDownAt(double x, double y)
    {
        SetCursorPos((int)Math.Round(x), (int)Math.Round(y));
        var input = new Input
        {
            Type = 0,
            Data = new InputUnion
            {
                Mouse = new MouseInput { Flags = 0x0002 }
            }
        };
        if (SendInput(1, new[] { input }, Marshal.SizeOf<Input>()) != 1)
            throw new InvalidOperationException("无法开始 QQ 录音。");
    }

    public static void MouseUp()
    {
        var input = new Input
        {
            Type = 0,
            Data = new InputUnion
            {
                Mouse = new MouseInput { Flags = 0x0004 }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
    }
}
