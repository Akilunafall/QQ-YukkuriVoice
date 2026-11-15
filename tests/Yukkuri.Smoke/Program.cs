using System.Text.Json;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using NAudio.Wave;
using Yukkuri;
using Yukkuri.Services;

var root = Path.GetFullPath(args.FirstOrDefault() ?? ".test-data");
Directory.CreateDirectory(root);
var passed = new List<string>();
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    passed.Add(name);
    Console.WriteLine("PASS " + name);
}

var storage = new Storage(Path.Combine(root, "storage"));
var settings = new UserSettings { Pitch = 1.46, PresetId = "marisa", AutoGenerate = false };
storage.SaveSettings(settings);
var restored = storage.LoadSettings();
Check(restored.PresetId == "marisa" && !restored.AutoGenerate, "Settings round trip");
Check(restored.Pitch == 1 && restored.Robot == 0 && restored.Volume == 1, "Legacy audio effects migrate to engine originals");
Check(!storage.IsOwnedAudio(Path.Combine(root, "outside.wav")), "History cannot delete external files");
Check(!storage.IsOwnedAudio(Path.Combine(storage.AudioDirectory, "..", "outside.wav")), "Path traversal rejected");
Check(!QQDraftMonitor.IsDraftCandidate(50004, "搜索", false), "QQ search is excluded");
Check(!QQDraftMonitor.IsDraftCandidate(50030, "", true), "Read-only chat history is excluded");
Check(QQDraftMonitor.IsDraftCandidate(50026, "", false), "QQ NT writable Group is accepted");
Check(!QQDraftMonitor.IsDraftCandidate(50020, "", false), "Chat text is excluded");

var bundledVoices = AquesTalkService.BundledVoices.Where(v => File.Exists(v.Path)).ToArray();
Check(bundledVoices.Length == 8, "All eight classic AquesTalk1 voices are bundled");
var kana = ChineseToKana.Convert("慢慢来，今天也要度过悠闲的一天。");
Check(kana.Contains("マンマンライ", StringComparison.Ordinal), "Chinese text converts to Yukkuri kana");
var synth = new SpeechService();
var engineDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "YukkuriVoice", "Engines");
var voices = VoiceCatalog.GetVoices(engineDirectory);
var requireSdks = args.Contains("--require-sdks");
if (requireSdks)
{
    Check(voices.Count(v => v.Engine == AquesTalkService.Engine2) == 14, "Fourteen official AT2 phonts discovered");
    Check(voices.Count(v => v.Engine == AquesTalkService.Engine10) == 7, "Seven official AT10 presets discovered");
}
var audioMetrics = new List<object>();
foreach (var voice in voices)
{
    Check(AquesTalkService.DetectEngine(voice.Path) == voice.Engine, voice.Name + " DLL ABI identified");
    var clip = await synth.GenerateAsync(new("慢慢来，今天也要度过悠闲的一天。", voice.Id,
        SpeechService.YukkuriEngine, "", 1, 1, 0, 1, voice.Path, voice.Engine, voice.PhontPath, voice.Preset, voice.Name),
        Path.Combine(storage.AudioDirectory, voice.Id + ".wav"), CancellationToken.None);
    Check(clip.Seconds > 1 && File.Exists(clip.FilePath), voice.Name + " produces real speech");
    var raw = AquesTalkService.Synthesize(voice.Path, kana, 100, voice.Engine, voice.PhontPath, voice.Preset);
    var savedBytes = await File.ReadAllBytesAsync(clip.FilePath);
    Check(raw.SequenceEqual(savedBytes), voice.Name + " preserves original engine WAV exactly");
    using var wave = new WaveFileReader(new MemoryStream(raw));
    Check(wave.WaveFormat.Encoding == WaveFormatEncoding.Pcm && wave.WaveFormat.Channels == 1 &&
        wave.WaveFormat.BitsPerSample == 16, voice.Name + " valid original PCM WAV");
    using var reader = new AudioFileReader(clip.FilePath);
    var buffer = new float[4096];
    var maximum = 0f;
    double square = 0;
    long sampleCount = 0;
    int read;
    while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        for (var i = 0; i < read; i++)
        {
            maximum = Math.Max(maximum, Math.Abs(buffer[i]));
            square += buffer[i] * buffer[i];
            sampleCount++;
        }
    Check(maximum > 0.02 && maximum <= 1, voice.Name + " is audible with valid native PCM sample range");
    audioMetrics.Add(new { voice.Id, voice.Name, voice.Engine, clip.FilePath, clip.Seconds, wave.WaveFormat.SampleRate, Peak = maximum,
        Rms = Math.Sqrt(square / sampleCount), Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raw)) });
    storage.SaveHistory([clip]);
    Check(storage.LoadHistory().Count == 1, "History survives reload: " + voice.Id);
}
foreach (var engine in voices.GroupBy(v => v.Engine))
{
    var voice = engine.First();
    using var slow = new WaveFileReader(new MemoryStream(AquesTalkService.Synthesize(voice.Path,
        kana, 65, voice.Engine, voice.PhontPath, voice.Preset)));
    using var fast = new WaveFileReader(new MemoryStream(AquesTalkService.Synthesize(voice.Path,
        kana, 160, voice.Engine, voice.PhontPath, voice.Preset)));
    Check(slow.TotalTime > fast.TotalTime * 1.2, engine.Key + " speed changes duration inside engine");
}
if (requireSdks)
{
    foreach (var engine in new[] { AquesTalkService.Engine2, AquesTalkService.Engine10 })
    {
        var voice = voices.First(v => v.Engine == engine);
        var x86 = voice.Path.Replace($"{Path.DirectorySeparatorChar}lib64{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}");
        var raw = AquesTalkService.Synthesize(x86, "ユックリ。", 100, engine, voice.PhontPath, voice.Preset);
        using var wave = new WaveFileReader(new MemoryStream(raw));
        Check(wave.TotalTime.TotalSeconds > 0.1, engine + " x86 bridge calls correct ABI");
    }
}
try
{
    await synth.GenerateAsync(new("取消", "reimu", SpeechService.YukkuriEngine, bundledVoices[0].Name, 1, 1.28, 0.48, 0.85, bundledVoices[0].Path),
        Path.Combine(root, "cancelled.wav"), new CancellationToken(true));
    throw new Exception("Cancellation was ignored");
}
catch (OperationCanceledException) { Check(!File.Exists(Path.Combine(root, "cancelled.wav")), "Cancelled synthesis produces no file"); }
try
{
    SpeechService.Validate(new(new string('中', 241), "reimu", SpeechService.YukkuriEngine, bundledVoices[0].Name, 1, 1, 0, 1, bundledVoices[0].Path));
    throw new Exception("Long text was accepted");
}
catch (InvalidOperationException) { Check(true, "Long text is rejected"); }
try
{
    SpeechService.Validate(new("ゆっくり", "reimu", SpeechService.YukkuriEngine, "", 1, 1, 0, 1, Path.Combine(root, "missing.dll")));
    throw new Exception("Missing engine was accepted");
}
catch (InvalidOperationException) { Check(true, "Missing AquesTalk engine fails clearly"); }
var devices = AudioService.GetDevices();
Check(devices.Count >= 1 && devices[0].Id == "", "Audio outputs enumerate with a default endpoint");

var sendDirectory = Path.Combine(root, "send-audio");
Directory.CreateDirectory(sendDirectory);
foreach (var voice in voices)
{
    var input = Path.Combine(storage.AudioDirectory, voice.Id + ".wav");
    var output = Path.Combine(sendDirectory, voice.Id + ".wav");
    var original = await File.ReadAllBytesAsync(input);
    QQLocalBridge.PrepareSendAudio(input, output);
    using var converted = new WaveFileReader(output);
    Check(converted.WaveFormat.SampleRate == 24000 && converted.WaveFormat.Channels == 1 &&
        converted.WaveFormat.BitsPerSample == 16, voice.Name + " QQ send copy is 24 kHz PCM");
    var afterConversion = await File.ReadAllBytesAsync(input);
    Check(original.SequenceEqual(afterConversion), voice.Name + " QQ encoding preserves original WAV");
    using var sourceWave = new WaveFileReader(new MemoryStream(original));
    Check(Math.Abs(converted.TotalTime.TotalSeconds - sourceWave.TotalTime.TotalSeconds) < 0.08,
        voice.Name + " QQ send copy preserves duration");
}
using (var peerJson = JsonDocument.Parse("""{"windowId":2,"chatType":2,"peerUid":"456","name":"Fixture","uin":"456"}"""))
{
    var peer = QQLocalBridge.ParseConversation(peerJson.RootElement);
    Check(peer.ChatType == 2 && peer.PeerUid == "456" && peer.WindowId == 2, "Active QQ peer is typed and scoped to window");
}
using (var peerJson = JsonDocument.Parse("""{"windowId":2,"chatType":0,"peerUid":"","name":"","uin":""}"""))
{
    try { QQLocalBridge.ParseConversation(peerJson.RootElement); throw new Exception("Invalid active peer accepted"); }
    catch (InvalidOperationException) { Check(true, "Missing QQ active conversation is rejected"); }
}

foreach (var result in await UiChecks.RunAsync(root, voices)) Check(true, result);

var vmStorage = new Storage(Path.Combine(root, "voice-selection"));
var vm = new MainViewModel(vmStorage);
foreach (var voice in voices)
    if (!vm.ClassicVoices.Any(v => VoiceCatalog.Key(v) == VoiceCatalog.Key(voice))) vm.ImportEngine(voice.Path);
foreach (var voice in vm.ClassicVoices)
{
    vm.ClassicVoice = voice;
    var request = vm.Request();
    Check(request.AquesPhont == voice.PhontPath && request.AquesPreset == voice.Preset &&
        request.AquesEngine == voice.Engine, voice.Name + " selection resolves correct synthesis parameters");
}
vmStorage.SaveSettings(vm.Settings);
var reopened = new MainViewModel(vmStorage);
Check(reopened.ClassicVoice is not null && VoiceCatalog.Key(reopened.ClassicVoice) == VoiceCatalog.Key(vm.ClassicVoice!),
    "AT2 and AT10 selections sharing one DLL survive restart");
var oldVoiceDirectory = Path.Combine(root, "legacy-release", "voices", "f1");
Directory.CreateDirectory(oldVoiceDirectory);
var oldVoicePath = Path.Combine(oldVoiceDirectory, "AquesTalk.dll");
File.Copy(bundledVoices[0].Path, oldVoicePath, true);
vmStorage.SaveSettings(new UserSettings
{
    AquesDll = oldVoicePath,
    ClassicVoices = [new ClassicVoicePack("Old F1", oldVoicePath)]
});
var upgraded = new MainViewModel(vmStorage);
Check(upgraded.ClassicVoice?.Path == bundledVoices[0].Path &&
    upgraded.ClassicVoices.Count(v => v.Path == bundledVoices[0].Path) == 1,
    "Old release bundled paths migrate without duplicate voice entries");

var localBridge = new QQLocalBridge(Path.Combine(root, "local-bridge"));
localBridge.Prepare();
Check(Directory.GetFiles(Path.Combine(localBridge.RuntimeDirectory, "native"), "YukkuriQQInjector-*.dll").Length > 0 &&
    File.Exists(Path.Combine(localBridge.RuntimeDirectory, "silk", "silk.wasm")),
    "Local QQ runtime is bundled without downloads or online authentication");
using (var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(localBridge.RuntimeDirectory, "connection.json"))))
    Check(config.RootElement.GetProperty("secret").GetString()?.Length == 64,
        "Local pipe connection uses automatic per-machine secret");
Check(!JsonSerializer.Serialize(new UserSettings()).Contains("QQBridgeAuthToken"), "Online registration token removed from saved settings");

File.WriteAllText(Path.Combine(root, "test-results.json"), JsonSerializer.Serialize(new
{
    Passed = passed.Count, Checks = passed, Voices = bundledVoices, AudioDevices = devices,
    AudioMetrics = audioMetrics, Timestamp = DateTimeOffset.Now
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"All {passed.Count} checks passed. Audio: {storage.AudioDirectory}");

