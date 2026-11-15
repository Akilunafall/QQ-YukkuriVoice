using System.Text.Json;
using System.Diagnostics;
using System.IO;
using Yukkuri.Services;

var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YukkuriVoice");
var bridge = new QQLocalBridge(root);
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40));
if (args.Contains("--prepare"))
{
    bridge.Prepare();
    Console.WriteLine(bridge.RuntimeDirectory);
    return;
}
var window = IntPtr.Zero;
if (args.Contains("--native-window"))
{
    using var qq = Process.GetProcessById(QQMemoryInjector.FindQQProcess());
    window = qq.MainWindowHandle;
}
var status = await bridge.ConnectAsync(window, cancellation.Token);
Console.WriteLine("QQ active conversation detected: " + (status.GetProperty("chatType").GetInt32() is 1 or 2));
void RequireTestGroup(QQActiveConversation peer)
{
    var groupId = Environment.GetEnvironmentVariable("YUKKURI_TEST_GROUP_ID");
    var groupName = Environment.GetEnvironmentVariable("YUKKURI_TEST_GROUP_NAME");
    if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(groupName))
        throw new InvalidOperationException(
            "Set YUKKURI_TEST_GROUP_ID and YUKKURI_TEST_GROUP_NAME to an explicitly authorized test group before sending.");
    if (peer.ChatType != 2 || peer.PeerUid != groupId || peer.Name != groupName)
        throw new InvalidOperationException("The active chat is outside the explicitly configured test group.");
}
if (args.FirstOrDefault() == "mini-ui")
{
    var peer = QQLocalBridge.ParseConversation(status);
    RequireTestGroup(peer);
    var thread = new Thread(() =>
    {
        var app = new Yukkuri.App(startDesktop: false);
        app.InitializeComponent();
        var main = new Yukkuri.MainWindow(new Storage(root));
        var mini = new Yukkuri.MiniWindow(main) {
            ShowInTaskbar = true, WindowStyle = System.Windows.WindowStyle.SingleBorderWindow,
            Left = 250, Top = 180
        };
        app.MainWindow = mini;
        mini.Closed += (_, _) => main.Close();
        main.Show();
        main.Hide();
        mini.Show();
        app.Run();
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
}
else if (args.FirstOrDefault() == "send-test")
{
    var peer = QQLocalBridge.ParseConversation(status);
    RequireTestGroup(peer);
    var audio = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.GetFullPath(".test-data/storage/Audio/reimu.wav");
    Console.WriteLine("Sending one voice test to the authorized group.");
    var sent = await bridge.SendVoiceAsync(peer, audio, cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(sent));
}
else if (args.FirstOrDefault() == "encode")
{
    using var metrics = JsonDocument.Parse(File.ReadAllText(Path.Combine(".test-data", "test-results.json")));
    var results = new List<object>();
    foreach (var audio in metrics.RootElement.GetProperty("AudioMetrics").EnumerateArray())
    {
        var directory = Path.Combine(bridge.RuntimeDirectory, "Audio");
        Directory.CreateDirectory(directory);
        var converted = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            QQLocalBridge.PrepareSendAudio(audio.GetProperty("FilePath").GetString()!, converted);
            var result = await bridge.RequestAsync("encode", new { audioPath = converted }, cancellation.Token);
            results.Add(new { voice = audio.GetProperty("Name").GetString(), result });
        }
        finally { File.Delete(converted); }
    }
    File.WriteAllText(Path.Combine(".test-data", "local-qq-audio-results.json"), JsonSerializer.Serialize(results));
    Console.WriteLine("Encoded inside real QQ process without sending: " + results.Count);
}
else
{
    var target = await bridge.RequestAsync(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "probeRpc", new { }, cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(target));
}
