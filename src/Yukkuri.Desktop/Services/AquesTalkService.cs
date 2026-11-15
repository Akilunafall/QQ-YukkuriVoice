using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Yukkuri.Services;

public sealed record BundledVoice(string Name, string Path, string Engine = "AquesTalk1",
    string PhontPath = "", int Preset = 0, string Id = "");

public sealed record AquesBridgeRequest(string Dll, string Text, int Speed,
    string Engine = "AquesTalk1", string Phont = "", int Preset = 0);

public static class AquesTalkService
{
    public const string Engine1 = "AquesTalk1";
    public const string Engine2 = "AquesTalk2";
    public const string Engine10 = "AquesTalk10";

    public static IReadOnlyList<BundledVoice> BundledVoices { get; } =
    [
        new("声种: AT1-F1（ゆっくり）", BundledPath("f1"), Id: "reimu"),
        new("声种: AT1-F2", BundledPath("f2"), Id: "marisa"),
        new("声种: AT1-M1", BundledPath("m1"), Id: "youmu"),
        new("声种: AT1-M2", BundledPath("m2"), Id: "m2"),
        new("声种: AT1-DVD", BundledPath("dvd"), Id: "dvd"),
        new("声种: AT1-IMD1", BundledPath("imd1"), Id: "imd1"),
        new("声种: AT1-JGR", BundledPath("jgr"), Id: "jgr"),
        new("声种: AT1-R1", BundledPath("r1"), Id: "r1")
    ];

    public static readonly string[] AquesTalk2Phonts =
    [
        "aq_rm", "aq_f1c", "aq_f3a", "aq_huskey", "aq_m4b", "aq_mf1",
        "aq_rb2", "aq_rb3", "aq_robo", "aq_yukkuri",
        "ar_f4", "ar_m5", "ar_mf2", "ar_rm3", "aq_defo1", "aq_teto1"
    ];

    public static readonly string[] AquesTalk10Presets = ["F1", "F2", "F3", "M1", "M2", "R1", "R2"];

    public static string DefaultVoicePath =>
        BundledVoices.FirstOrDefault(v => File.Exists(v.Path))?.Path ?? "";

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr Synthesize1Delegate(byte[] text, int speed, out int size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr Synthesize2Delegate(byte[] text, int speed, out int size, IntPtr phont);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr Synthesize10Delegate(ref Voice10 voice, byte[] text, out int size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void FreeDelegate(IntPtr wave);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetKeyDelegate(byte[] key);

    [StructLayout(LayoutKind.Sequential)]
    private struct Voice10
    {
        public int Base, Speed, Volume, Pitch, Accent, Lmd, Fsc;
        public Voice10(int bas, int speed, int volume, int pitch, int accent, int lmd, int fsc)
        {
            Base = bas; Speed = speed; Volume = volume; Pitch = pitch;
            Accent = accent; Lmd = lmd; Fsc = fsc;
        }
    }

    public static bool Is32Bit(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt16() != 0x5A4D) throw new InvalidOperationException("所选文件不是 Windows DLL。");
        stream.Position = 0x3C;
        var pe = reader.ReadInt32();
        if (pe < 0 || pe > stream.Length - 6) throw new InvalidOperationException("DLL 文件头无效。");
        stream.Position = pe;
        if (reader.ReadUInt32() != 0x4550) throw new InvalidOperationException("DLL 文件头无效。");
        var machine = reader.ReadUInt16();
        return machine switch
        {
            0x014c => true,
            0x8664 => false,
            _ => throw new InvalidOperationException("请选择 Windows x86 或 x64 版本的 AquesTalk DLL。")
        };
    }

    public static string DetectEngine(string path)
    {
        var exports = ReadExports(path);
        if (exports.Contains("AquesTalk2_Synthe_Utf8") || exports.Contains("AquesTalk2_Synthe"))
            return Engine2;
        var description = FileVersionInfo.GetVersionInfo(path);
        var identity = string.Join(" ", path, description.ProductName, description.FileDescription);
        if (exports.Contains("AquesTalk10_Synthe") || exports.Contains("AquesTalk10_Synthe_Utf8") ||
            identity.Contains("AquesTalk10", StringComparison.OrdinalIgnoreCase) ||
            identity.Contains("aqtk10", StringComparison.OrdinalIgnoreCase))
            return Engine10;
        if (exports.Contains("AquesTalk_Synthe")) return Engine1;
        throw new InvalidOperationException("所选 DLL 不提供 AquesTalk 1、2 或 10 合成接口。");
    }

    public static void ValidateLibrary(string path, string engine)
    {
        Is32Bit(path);
        var names = ReadExports(path);
        var valid = engine switch
        {
            Engine1 => names.Contains("AquesTalk_Synthe") && names.Contains("AquesTalk_FreeWave"),
            Engine2 => names.Contains("AquesTalk2_Synthe_Utf8") && names.Contains("AquesTalk2_FreeWave"),
            Engine10 => (names.Contains("AquesTalk_Synthe") || names.Contains("AquesTalk10_Synthe") ||
                names.Contains("AquesTalk10_Synthe_Utf8")) &&
                (names.Contains("AquesTalk_FreeWave") || names.Contains("AquesTalk10_FreeWave")),
            _ => false
        };
        if (!valid) throw new InvalidOperationException($"所选 DLL 不提供 {engine} 合成接口。");
    }

    public static byte[] Synthesize(string dll, string text, int speed,
        string engine = Engine1, string phont = "", int preset = 0)
        => Synthesize(new AquesBridgeRequest(Path.GetFullPath(dll), text, speed, engine,
            string.IsNullOrWhiteSpace(phont) ? "" : Path.GetFullPath(phont), preset));

    public static byte[] Synthesize(AquesBridgeRequest request)
    {
        ValidateLibrary(request.Dll, request.Engine);
        var is32 = Is32Bit(request.Dll);
        if (is32 && Environment.Is64BitProcess) return RunBridge(request);
        if (!is32 && !Environment.Is64BitProcess) throw new InvalidOperationException("32 位桥接程序不能加载 64 位 DLL。");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var library = NativeLibrary.Load(Path.GetFullPath(request.Dll));
        try
        {
            var prefix = request.Engine == Engine2 ? "AquesTalk2_" : "AquesTalk_";
            var free = Marshal.GetDelegateForFunctionPointer<FreeDelegate>(
                Export(library, prefix + "FreeWave", "AquesTalk10_FreeWave"));
            ApplyLicenseKey(library, prefix, request.Engine == Engine10 ? "AQTK10" : "AQTK1");
            IntPtr wave;
            int size;
            var speed = Math.Clamp(request.Speed, 50, 300);
            if (request.Engine == Engine2)
            {
                if (!File.Exists(request.Phont)) throw new InvalidOperationException("缺少所选 AquesTalk2 的 .phont 音色文件。");
                var data = File.ReadAllBytes(request.Phont);
                if (data.Length is < 16 or > 2_000_000)
                    throw new InvalidOperationException("AquesTalk2 .phont 文件大小无效。");
                var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
                try
                {
                    var synth = Marshal.GetDelegateForFunctionPointer<Synthesize2Delegate>(
                        Export(library, "AquesTalk2_Synthe_Utf8"));
                    wave = synth(Encoding.UTF8.GetBytes(request.Text + "\0"), speed, out size, handle.AddrOfPinnedObject());
                }
                finally { handle.Free(); }
            }
            else if (request.Engine == Engine10)
            {
                var voice = CreateVoice10(request.Preset, speed);
                var utf8 = FindExport(library, "AquesTalk_Synthe_Utf8", "AquesTalk10_Synthe_Utf8");
                var encoding = utf8 != IntPtr.Zero ? Encoding.UTF8 : ShiftJis();
                var synth = Marshal.GetDelegateForFunctionPointer<Synthesize10Delegate>(
                    utf8 != IntPtr.Zero ? utf8 : Export(library, "AquesTalk_Synthe", "AquesTalk10_Synthe"));
                wave = synth(ref voice, encoding.GetBytes(request.Text + "\0"), out size);
            }
            else
            {
                var synth = Marshal.GetDelegateForFunctionPointer<Synthesize1Delegate>(
                    Export(library, "AquesTalk_Synthe"));
                wave = synth(ShiftJis().GetBytes(request.Text + "\0"), speed, out size);
            }
            if (wave == IntPtr.Zero)
                throw new InvalidOperationException($"{request.Engine} 合成失败（错误码 {size}），请检查假名与标点。");
            try
            {
                if (size < 44 || size > 30_000_000) throw new InvalidOperationException("AquesTalk 返回了无效的音频。");
                var bytes = new byte[size];
                Marshal.Copy(wave, bytes, 0, size);
                return bytes;
            }
            finally { free(wave); }
        }
        catch (EntryPointNotFoundException)
        {
            throw new InvalidOperationException($"该 DLL 不提供 {request.Engine} 所需接口。");
        }
        finally { NativeLibrary.Free(library); }
    }

    private static Voice10 CreateVoice10(int preset, int speed)
    {
        // The SDK's gVoice_* parameters define timbre; only spd is overridden.
        var voice = preset switch
        {
            1 => new Voice10(1, 100, 100, 77, 150, 100, 100),
            2 => new Voice10(0, 80, 100, 100, 100, 61, 148),
            3 => new Voice10(2, 100, 100, 30, 100, 100, 100),
            4 => new Voice10(2, 105, 100, 45, 130, 120, 100),
            5 => new Voice10(2, 100, 100, 30, 20, 190, 100),
            6 => new Voice10(1, 70, 100, 50, 50, 50, 180),
            0 => new Voice10(0, 100, 100, 100, 100, 100, 100),
            _ => throw new InvalidOperationException("无效的 AquesTalk10 音色。")
        };
        voice.Speed = speed;
        return voice;
    }

    private static Encoding ShiftJis() =>
        Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    private static IntPtr FindExport(IntPtr library, params string[] names)
    {
        foreach (var name in names)
            if (NativeLibrary.TryGetExport(library, name, out var address)) return address;
        return IntPtr.Zero;
    }

    private static IntPtr Export(IntPtr library, params string[] names)
    {
        var address = FindExport(library, names);
        return address != IntPtr.Zero ? address : throw new EntryPointNotFoundException(string.Join(", ", names));
    }

    private static void ApplyLicenseKey(IntPtr library, string prefix, string variablePrefix)
    {
        foreach (var kind in new[] { "DEV", "USR" })
        {
            var key = Environment.GetEnvironmentVariable(variablePrefix + "_" + kind + "_KEY");
            if (string.IsNullOrWhiteSpace(key)) continue;
            var entry = FindExport(library, prefix + (kind == "DEV" ? "SetDevKey" : "SetUsrKey"));
            if (entry == IntPtr.Zero) continue;
            var set = Marshal.GetDelegateForFunctionPointer<SetKeyDelegate>(entry);
            if (set(Encoding.ASCII.GetBytes(key + "\0")) != 0)
                throw new InvalidOperationException("AquesTalk 授权密钥无效。");
        }
    }

    private static HashSet<string> ReadExports(string path)
    {
        using var stream = File.OpenRead(path);
        using var image = new PEReader(stream);
        var directory = image.PEHeaders.PEHeader?.ExportTableDirectory
            ?? throw new InvalidOperationException("无效的 Windows 引擎文件。");
        if (directory.RelativeVirtualAddress == 0) return [];
        var header = image.GetSectionData(directory.RelativeVirtualAddress).GetReader();
        header.Offset = 24;
        var count = header.ReadInt32();
        header.Offset = 32;
        var namesRva = header.ReadInt32();
        if (count is < 0 or > 4096) throw new InvalidOperationException("引擎 DLL 导出表无效。");
        var names = image.GetSectionData(namesRva).GetReader();
        var result = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var name = image.GetSectionData(names.ReadInt32()).GetReader();
            var bytes = new List<byte>();
            while (name.RemainingBytes > 0 && bytes.Count < 256)
            {
                var value = name.ReadByte();
                if (value == 0) break;
                bytes.Add(value);
            }
            result.Add(Encoding.ASCII.GetString(bytes.ToArray()));
        }
        return result;
    }

    private static byte[] RunBridge(AquesBridgeRequest request)
    {
        var helper = FindBridge();
        if (!File.Exists(helper)) throw new InvalidOperationException("缺少 32 位引擎桥接程序，请使用完整便携版文件夹。");
        var start = new ProcessStartInfo(helper)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 32 位引擎。");
        process.StandardInput.WriteLine(JsonSerializer.Serialize(request));
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(20000))
        {
            process.Kill(true);
            throw new InvalidOperationException("AquesTalk 响应超时。");
        }
        var result = stdout.GetAwaiter().GetResult();
        var error = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new InvalidOperationException("油库里引擎：" + error.Trim());
        return Convert.FromBase64String(result.Trim());
    }

    private static string FindBridge()
    {
        return Path.Combine(AppContext.BaseDirectory, "bridge-x86", "YukkuriAquesBridge.exe");
    }

    private static string BundledPath(string voice) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "voices", voice, "AquesTalk.dll");
}
