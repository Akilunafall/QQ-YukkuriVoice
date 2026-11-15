namespace Yukkuri;

public partial class App : Application
{
    private Mutex? _instance;
    private readonly bool _startDesktop;

    public App() : this(startDesktop: true) { }
    public App(bool startDesktop) => _startDesktop = startDesktop;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!_startDesktop) return;
        base.OnStartup(e);
        _instance = new Mutex(true, @"Local\YukkuriVoice.Desktop.v1", out var created);
        if (!created)
        {
            MessageBox.Show("油库里语音已经在运行，请从任务栏或托盘打开。", "油库里语音");
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            Services.Storage.Log(args.Exception);
            MessageBox.Show("操作未完成：" + args.Exception.Message, "油库里语音");
            args.Handled = true;
        };
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }
}
