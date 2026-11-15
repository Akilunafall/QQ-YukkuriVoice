using System.Runtime.InteropServices;
using System.Text;

namespace Yukkuri.Services;

public static class QQMemoryInjector
{
    public static int FindQQProcess(IntPtr preferredWindow = default)
    {
        if (preferredWindow != IntPtr.Zero)
        {
            NativeMethods.GetWindowThreadProcessId(preferredWindow, out var id);
            try
            {
                using var preferred = Process.GetProcessById((int)id);
                if (preferred.ProcessName.Equals("QQ", StringComparison.OrdinalIgnoreCase)) return preferred.Id;
            }
            catch (ArgumentException) { } // QQ may have restarted since the last foreground snapshot.
        }
        var candidates = new List<int>();
        foreach (var process in Process.GetProcessesByName("QQ"))
            using (process)
                if (process.MainWindowHandle != IntPtr.Zero) candidates.Add(process.Id);
        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException("请先打开 QQ 的聊天窗口，软件会自动连接。"),
            _ => throw new InvalidOperationException("检测到多个 QQ 窗口，请先点一下需要发送的聊天窗口。")
        };
    }

    public static void Inject(int processId, string libraryPath, string bootstrap)
    {
        if (!Environment.Is64BitProcess) throw new InvalidOperationException("QQ 内存连接需要 x64 版本的软件。");
        libraryPath = Path.GetFullPath(libraryPath);
        if (!File.Exists(libraryPath)) throw new InvalidOperationException("缺少本地 QQ 注入模块，请保留完整的软件目录。");
        using var process = Process.GetProcessById(processId);
        if (!process.ProcessName.Equals("QQ", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("目标进程不是 QQ。");
        var handle = OpenProcess(0x043A, false, processId);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("无法连接 QQ 内存，请让 QQ 和本软件都以普通用户权限运行。");
        try
        {
            var existing = process.Modules.Cast<ProcessModule>().FirstOrDefault(m =>
                string.Equals(m.FileName, libraryPath, StringComparison.OrdinalIgnoreCase));
            IntPtr remoteModule;
            if (existing is null)
            {
                var localKernel = GetModuleHandle("kernel32.dll");
                var load = GetProcAddress(localKernel, "LoadLibraryW");
                using var ownProcess = Process.GetCurrentProcess();
                var owner = ownProcess.Modules.Cast<ProcessModule>().Single(m => load.ToInt64() >= m.BaseAddress.ToInt64() &&
                    load.ToInt64() < m.BaseAddress.ToInt64() + m.ModuleMemorySize);
                var remoteOwner = process.Modules.Cast<ProcessModule>().Single(m =>
                    m.ModuleName.Equals(owner.ModuleName, StringComparison.OrdinalIgnoreCase)).BaseAddress;
                var remoteLoad = remoteOwner + checked((int)(load.ToInt64() - owner.BaseAddress.ToInt64()));
                Run(handle, remoteLoad, Encoding.Unicode.GetBytes(libraryPath + '\0'), out var loadCode);
                if (loadCode == 0) throw new InvalidOperationException("QQ 未加载本地注入模块。");
                process.Refresh();
                remoteModule = process.Modules.Cast<ProcessModule>().Single(m =>
                    string.Equals(m.FileName, libraryPath, StringComparison.OrdinalIgnoreCase)).BaseAddress;
            }
            else remoteModule = existing.BaseAddress;

            var local = LoadLibraryEx(libraryPath, IntPtr.Zero, 0x1); // Read export RVAs without running DLL code.
            if (local == IntPtr.Zero) throw new InvalidOperationException("无法读取 QQ 注入模块。");
            try
            {
                var entry = GetProcAddress(local, "YukkuriInitialize");
                if (entry == IntPtr.Zero) throw new InvalidOperationException("QQ 注入模块版本不匹配。");
                var remoteEntry = remoteModule + (int)(entry.ToInt64() - local.ToInt64());
                Run(handle, remoteEntry, Encoding.UTF8.GetBytes(bootstrap + '\0'), out var code);
                if (code != 0)
                    throw new InvalidOperationException(code == 2
                        ? "QQ 内存模块已经加载，正在等待本地连接；若连接持续失败，请重新启动 QQ。"
                        : $"QQ 内存模块未能启动（{code}），当前 QQ 版本可能不兼容。");
            }
            finally { FreeLibrary(local); }
        }
        finally { CloseHandle(handle); }
    }

    private static void Run(IntPtr process, IntPtr entry, byte[] data, out uint exitCode)
    {
        var memory = VirtualAllocEx(process, IntPtr.Zero, (nuint)data.Length, 0x3000, 0x04);
        if (memory == IntPtr.Zero) throw new InvalidOperationException("QQ 内存分配失败。");
        var completed = true;
        try
        {
            if (!WriteProcessMemory(process, memory, data, (nuint)data.Length, out var written) ||
                written != (nuint)data.Length) throw new InvalidOperationException("QQ 内存写入失败。");
            var thread = CreateRemoteThread(process, IntPtr.Zero, 0, entry, memory, 0, IntPtr.Zero);
            if (thread == IntPtr.Zero) throw new InvalidOperationException("QQ 本地模块执行失败。");
            try
            {
                completed = WaitForSingleObject(thread, 10000) == 0;
                if (!completed) throw new InvalidOperationException("QQ 注入等待超时，请检查 QQ 是否仍然响应。");
                if (!GetExitCodeThread(thread, out exitCode))
                    throw new InvalidOperationException("无法取得 QQ 注入结果。");
            }
            finally { CloseHandle(thread); }
        }
        finally
        {
            // A timed-out thread may still read its arguments.
            if (completed) VirtualFreeEx(process, memory, 0, 0x8000);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll")] private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, nuint size, uint type, uint protection);
    [DllImport("kernel32.dll")] private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, nuint size, uint type);
    [DllImport("kernel32.dll")] private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] data, nuint count, out nuint written);
    [DllImport("kernel32.dll")] private static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attributes, nuint stack, IntPtr entry, IntPtr argument, uint flags, IntPtr id);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool GetExitCodeThread(IntPtr thread, out uint code);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
