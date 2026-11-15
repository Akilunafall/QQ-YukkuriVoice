using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Yukkuri.Services;

public sealed class QQLocalBridge
{
    private readonly string _runtime;
    private readonly string _pipe;
    private readonly SemaphoreSlim _connectGate = new(1);
    private int _processId;
    private string _secret = "";
    private string _libraryPath = "";
    public string RuntimeDirectory => _runtime;

    public QQLocalBridge(string dataRoot)
    {
        _runtime = Path.Combine(Path.GetFullPath(dataRoot), "QQBridge", "Local-v2");
        _pipe = "YukkuriVoice-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_runtime)))[..24];
    }

    public void Prepare()
    {
        Directory.CreateDirectory(_runtime);
        var source = Path.Combine(AppContext.BaseDirectory, "Assets", "qq-bridge");
        foreach (var name in new[] { "bridge.cjs", "handler.cjs", "renderer.cjs", "YukkuriQQInjector.dll",
                     "silk/index.cjs", "silk/silk.wasm", "silk/LICENSE.txt" })
        {
            var input = Path.Combine(source, name);
            if (!File.Exists(input)) throw new InvalidOperationException("缺少本地 QQ 模块：" + name);
            var output = Path.Combine(_runtime, name);
            if (name == "YukkuriQQInjector.dll")
            {
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)))[..16];
                output = Path.Combine(_runtime, "native", "YukkuriQQInjector-" + hash + ".dll");
                _libraryPath = output;
            }
            if (File.Exists(output) && File.ReadAllBytes(input).SequenceEqual(File.ReadAllBytes(output))) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.Copy(input, output, true);
        }
        var config = Path.Combine(_runtime, "connection.json");
        if (File.Exists(config))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(config));
            if (document.RootElement.TryGetProperty("secret", out var value)) _secret = value.GetString() ?? "";
        }
        if (_secret.Length != 64) _secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var json = JsonSerializer.Serialize(new { pipe = _pipe, secret = _secret });
        if (!File.Exists(config) || File.ReadAllText(config) != json)
        {
            File.WriteAllText(config + ".tmp", json);
            File.Move(config + ".tmp", config, true);
        }
    }

    public async Task<JsonElement> ConnectAsync(IntPtr qqWindow, CancellationToken token)
    {
        await _connectGate.WaitAsync(token);
        try
        {
            Prepare();
            if (_processId != 0)
            {
                try { using var previous = Process.GetProcessById(_processId); if (previous.HasExited) _processId = 0; }
                catch (ArgumentException) { _processId = 0; }
            }
            try
            {
                var active = await RequestAsync("active", new { windowHandle = qqWindow.ToInt64().ToString() },
                    token, TimeSpan.FromMilliseconds(800));
                if (qqWindow != IntPtr.Zero)
                {
                    NativeMethods.GetWindowThreadProcessId(qqWindow, out var foregroundProcess);
                    if (foregroundProcess != 0 && foregroundProcess != _processId)
                        throw new InvalidOperationException("当前聊天属于另一个 QQ 进程；请只保留一个登录中的 QQ 再连接。");
                }
                return active;
            }
            catch (Exception error) when (error is IOException or TimeoutException or OperationCanceledException)
            {
                token.ThrowIfCancellationRequested();
            }
            _processId = QQMemoryInjector.FindQQProcess(qqWindow);
            var module = Path.Combine(_runtime, "bridge.cjs");
            var errorPath = Path.Combine(_runtime, "bootstrap-error.txt");
            var bootstrap = "(function(){try{if(typeof process==='undefined'||process.type!=='browser')return;" +
                "process.getBuiltinModule('module').createRequire(process.resourcesPath+'/app/package.json')(" +
                JsonSerializer.Serialize(module) + ");}catch(e){try{process.getBuiltinModule('fs').writeFileSync(" +
                JsonSerializer.Serialize(errorPath) + ",String(e&&e.stack||e));}catch(_){}}})()";
            await Task.Run(() => QQMemoryInjector.Inject(_processId,
                _libraryPath, bootstrap), token);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
            wait.CancelAfter(TimeSpan.FromSeconds(12));
            try
            {
                return await RequestAsync("active", new { windowHandle = qqWindow.ToInt64().ToString() }, wait.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                var detail = File.Exists(errorPath) ? File.ReadAllText(errorPath) : "";
                throw new InvalidOperationException("QQ 本地模块尚未响应，请切换一次 QQ 聊天窗口后重试。" +
                    (string.IsNullOrWhiteSpace(detail) ? "" : "\n" + detail));
            }
        }
        finally { _connectGate.Release(); }
    }

    public async Task<QQActiveConversation> GetActiveAsync(IntPtr qqWindow, CancellationToken token)
    {
        var result = await ConnectAsync(qqWindow, token);
        return ParseConversation(result);
    }

    public static QQActiveConversation ParseConversation(JsonElement result)
    {
        var peer = new QQActiveConversation(result.GetProperty("windowId").GetInt32(),
            result.GetProperty("chatType").GetInt32(), result.GetProperty("peerUid").GetString() ?? "",
            result.GetProperty("name").GetString() ?? "", result.GetProperty("uin").GetString() ?? "");
        if (peer.WindowId < 1 || peer.ChatType is not (1 or 2) || string.IsNullOrWhiteSpace(peer.PeerUid))
            throw new InvalidOperationException("QQ 当前会话无效，请重新打开目标聊天窗口。");
        return peer;
    }

    public async Task<QQDirectSendResult> SendVoiceAsync(QQActiveConversation peer, string audioPath,
        CancellationToken token)
    {
        var directory = Path.Combine(_runtime, "Audio");
        Directory.CreateDirectory(directory);
        var requestId = Guid.NewGuid().ToString("N");
        var converted = Path.Combine(directory, requestId + ".wav");
        try
        {
            await Task.Run(() => PrepareSendAudio(audioPath, converted), token);
            var result = await RequestAsync("sendVoice", new
            {
                requestId, peer.WindowId, peer.ChatType, peer.PeerUid, audioPath = converted
            }, token, TimeSpan.FromSeconds(50));
            var messageId = result.GetProperty("messageId").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(messageId) || messageId == "0")
                throw new InvalidOperationException("QQ 没有确认语音消息，结果未知；请检查聊天记录，避免重复发送。");
            return new(messageId, peer.Label);
        }
        catch (Exception error) when (!token.IsCancellationRequested &&
            error is OperationCanceledException or IOException or System.Text.Json.JsonException)
        {
            throw new InvalidOperationException("QQ 发送响应中断或超时，结果未知；请先检查 QQ 聊天记录，勿重复发送。");
        }
        finally
        {
            try { File.Delete(converted); } catch (IOException) { }
        }
    }

    public static void PrepareSendAudio(string input, string output)
    {
        using var reader = new WaveFileReader(input);
        if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm || reader.WaveFormat.Channels != 1 ||
            reader.WaveFormat.BitsPerSample != 16 || reader.TotalTime.TotalSeconds is <= 0 or > 58)
            throw new InvalidOperationException("QQ 语音需要 58 秒以内的单声道 PCM WAV。");
        ISampleProvider provider = reader.ToSampleProvider();
        if (provider.WaveFormat.SampleRate != 24000) provider = new WdlResamplingSampleProvider(provider, 24000);
        WaveFileWriter.CreateWaveFile16(output, provider);
    }

    public async Task<JsonElement> RequestAsync(string action, object parameters, CancellationToken token,
        TimeSpan? timeout = null)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        cancellation.CancelAfter(timeout ?? TimeSpan.FromSeconds(35));
        using var pipe = new NamedPipeClientStream(".", _pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cancellation.Token);
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var serverId))
            throw new InvalidOperationException("无法核实 QQ 本地连接。");
        using (var server = Process.GetProcessById((int)serverId))
            if (!server.ProcessName.Equals("QQ", StringComparison.OrdinalIgnoreCase) ||
                (_processId != 0 && _processId != server.Id))
                throw new InvalidOperationException("本地连接不属于目标 QQ 进程。");
        _processId = (int)serverId;
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { action, parameters, secret = _secret }).AsMemory(), cancellation.Token);
        var line = await reader.ReadLineAsync(cancellation.Token);
        if (line is null || line.Length > 1_000_000) throw new IOException("QQ 本地连接没有返回有效数据。");
        using var response = JsonDocument.Parse(line);
        if (!response.RootElement.GetProperty("ok").GetBoolean())
            throw new InvalidOperationException(response.RootElement.GetProperty("error").GetString());
        return response.RootElement.GetProperty("data").Clone();
    }

    [DllImport("kernel32.dll")] private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint id);
}

public sealed record QQActiveConversation(int WindowId, int ChatType, string PeerUid, string Name, string Uin)
{
    public string Label => $"{(ChatType == 2 ? "群" : "好友")} · {(string.IsNullOrWhiteSpace(Name) ? Uin : Name)}";
}
