using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Yukkuri.Services;
using Forms = System.Windows.Forms;

namespace Yukkuri;

public partial class MainWindow : Window
{
    private readonly Storage _storage;
    private readonly SpeechService _speech = new();
    private readonly AudioService _audio = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly QQLocalBridge _qqBridge;
    private readonly DispatcherTimer _connectionTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _connecting;
    private readonly QQDraftMonitor _monitor = new();
    private readonly DispatcherTimer _debounce = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(45) };
    private readonly Stopwatch _playClock = new();
    private readonly MainViewModel _vm;
    private GlobalHotkeys? _hotkeys;
    private QQWindowTracker? _windowTracker;
    private Forms.NotifyIcon? _tray;
    private MiniWindow? _mini;
    private CancellationTokenSource? _generation;
    private CancellationTokenSource? _playback;
    private long _revision;
    private bool _suppress;
    private bool _closing;
    private bool _fromQQ;
    private bool _capturing;

    public MainWindow() : this(new Storage()) { }

    public MainWindow(Storage storage, bool enableQQConnection = true)
    {
        _storage = storage;
        InitializeComponent();
        _qqBridge = new(_storage.Root);
        _vm = new(_storage);
        DataContext = _vm;
        ComposeNav.IsChecked = true;
        _vm.Edited += OnEdited;
        _vm.AutoGenerationChanged += () =>
        {
            _debounce.Stop();
            if (_vm.AutoGenerate && !_vm.HasClip && !string.IsNullOrWhiteSpace(_vm.Text)) OnEdited(false);
        };
        _vm.PreferencesChanged += PreferencesChanged;
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.CurrentClip)) Waveform.Load(_vm.CurrentClip?.FilePath);
        };
        _vm.History.CollectionChanged += (_, _) => UpdateEmptyHistory();
        UpdateEmptyHistory();
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await GenerateAsync();
        };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveSettings();
        };
        _progressTimer.Tick += (_, _) =>
        {
            Waveform.Progress = Math.Clamp(_playClock.Elapsed.TotalSeconds / Math.Max(0.1, _vm.CurrentClip?.Seconds ?? 1), 0, 1);
            Waveform.InvalidateVisual();
        };
        _monitor.DraftChanged += text => Dispatcher.BeginInvoke(() =>
        {
            if (_closing || !_vm.ListenToQQ || _capturing || _vm.Sending ||
                DraftEditor.IsKeyboardFocusWithin || _mini?.IsKeyboardFocusWithin == true) return;
            _fromQQ = true;
            _vm.Text = text;
        });
        _monitor.StatusChanged += status => Dispatcher.BeginInvoke(() => { if (!_closing) _vm.QQStatus = status; });
        _monitor.Enabled = _vm.ListenToQQ && !_vm.Sending;
        SourceInitialized += (_, _) =>
        {
            _windowTracker = new();
            _hotkeys = new(this);
            _vm.CaptureShortcut = _hotkeys.CaptureLabel;
            _vm.SendShortcut = _hotkeys.SendLabel;
            _vm.Notify(nameof(MainViewModel.CaptureShortcutHint));
            _vm.Notify(nameof(MainViewModel.SendShortcutHint));
            _hotkeys.CaptureRequested += async () => await CaptureAsync();
            _hotkeys.SendRequested += async () => await SendToQQAsync();
            if (!_hotkeys.CaptureRegistered || !_hotkeys.SendRegistered)
            {
                _vm.Status = "部分快捷键被占用";
                _vm.Detail = "仍可使用自动联动和窗口按钮。";
            }
            else
            {
                _vm.Status = "输入就绪";
                _vm.Detail = $"手动取词 {_hotkeys.CaptureLabel} · 一键发送 {_hotkeys.SendLabel}";
            }
            CreateTray();
        };
        _connectionTimer.Tick += async (_, _) =>
        {
            if (!_vm.Sending) await RefreshQQBridgeAsync(false);
        };
        Loaded += async (_, _) =>
        {
            DraftEditor.Focus();
            if (!enableQQConnection) return;
            await RefreshQQBridgeAsync(false);
            if (!_closing) _connectionTimer.Start();
        };
        Closing += OnClosing;
    }

    private void PreferencesChanged()
    {
        _monitor.Enabled = _vm.ListenToQQ && !_vm.Sending;
        _saveTimer.Stop(); _saveTimer.Start();
    }

    private void SaveSettings()
    {
        try { _storage.SaveSettings(_vm.Settings); }
        catch (Exception ex) { Report(ex); }
    }

    private void OnEdited(bool textChanged)
    {
        if (_suppress || _closing) return;
        _revision++;
        _generation?.Cancel();
        _debounce.Stop();
        StopPlayback(false);
        ReleaseTemporaryClip();
        _vm.CurrentClip = null;
        _vm.Status = string.IsNullOrWhiteSpace(_vm.Text) ? "等待输入" : textChanged ? "文字已更新" : "参数已更新";
        _vm.Detail = _vm.AutoGenerate ? "等待输入结束…" : "可手动生成语音";
        if (_vm.AutoGenerate && !string.IsNullOrWhiteSpace(_vm.Text))
        {
            _debounce.Interval = TimeSpan.FromMilliseconds(_vm.Settings.DebounceMs);
            _debounce.Start();
        }
    }

    public async Task GenerateAsync(bool fromSend = false)
    {
        if (_vm.Sending && !fromSend) return;
        _debounce.Stop();
        var revision = ++_revision;
        _generation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _generation = cancellation;
        _vm.Busy = true;
        _vm.Status = "正在合成…";
        _vm.Detail = _vm.PresetName + " · 本地处理";
        VoiceClip? created = null;
        try
        {
            var request = _vm.Request();
            var path = Path.Combine(_storage.AudioDirectory, $"{Guid.NewGuid():N}.wav");
            created = await _speech.GenerateAsync(request, path, cancellation.Token);
            if (_closing || revision != _revision)
            {
                _storage.DeleteAudio(created);
                return;
            }
            ReleaseTemporaryClip();
            _vm.CurrentClip = created;
            if (_vm.KeepHistory)
            {
                _vm.History.Insert(0, created);
                while (_vm.History.Count > 60)
                {
                    var old = _vm.History[^1];
                    _vm.History.RemoveAt(_vm.History.Count - 1);
                    _storage.DeleteAudio(old);
                }
                _storage.SaveHistory(_vm.History);
            }
            _vm.Status = "语音已就绪";
            _vm.Detail = $"{created.DurationLabel} · {created.VoiceName}";
            if (_fromQQ && _mini is not null && !_mini.IsVisible) _mini.Show();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (revision == _revision && !_closing) Report(ex);
        }
        finally
        {
            if (ReferenceEquals(_generation, cancellation))
            {
                _generation = null;
                _vm.Busy = false;
            }
        }
    }

    public async Task PreviewAsync()
    {
        if (_vm.Sending) return;
        if (_vm.CurrentClip is null) await GenerateAsync();
        var clip = _vm.CurrentClip;
        if (clip is null) return;
        await PlayAsync(clip, _vm.Settings.PreviewDeviceId);
    }

    public async Task SendToQQAsync()
    {
        if (_vm.Sending || _closing) return;
        _vm.Sending = true;
        _monitor.Enabled = false;
        try
        {
            var recipient = await _qqBridge.GetActiveAsync(GetQQWindow(), _lifetime.Token);
            _vm.CurrentConversation = recipient.Label;
            if (NativeMethods.IsQQForeground(out _, out _) &&
                QQDraftMonitor.TryReadFocusedDraft(out var draft) && !string.IsNullOrWhiteSpace(draft) &&
                draft != _vm.Text)
            {
                _fromQQ = true;
                _vm.Text = draft;
            }
            if (_vm.CurrentClip is null) await GenerateAsync(fromSend: true);
            var clip = _vm.CurrentClip;
            if (clip is null) return;
            if (clip.Seconds > 58)
                throw new InvalidOperationException("单条语音最多 58 秒，请缩短文字或分段生成。");

            StopPlayback(false);
            var current = await _qqBridge.GetActiveAsync(GetQQWindow(), _lifetime.Token);
            if (current.WindowId != recipient.WindowId || current.PeerUid != recipient.PeerUid ||
                current.ChatType != recipient.ChatType)
                throw new InvalidOperationException("QQ 当前聊天已切换，已取消发送；请确认目标后重试。");
            _vm.Status = "正在提交 QQ 语音…";
            _vm.Detail = recipient.Label;
            var result = await _qqBridge.SendVoiceAsync(recipient, clip.FilePath, _lifetime.Token);
            _vm.Status = "QQ 已接受语音消息";
            _vm.Detail = $"{result.RecipientName} · 消息编号 {result.MessageId}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closing) Report(ex);
        }
        finally
        {
            _vm.Sending = false;
            _monitor.Enabled = !_closing && _vm.ListenToQQ;
        }
    }

    private async Task<bool> RefreshQQBridgeAsync(bool reportError = true)
    {
        if (_connecting || _closing) return false;
        _connecting = true;
        try
        {
            var processes = Process.GetProcessesByName("QQ");
            var running = processes.Length > 0;
            foreach (var process in processes) process.Dispose();
            if (!running) throw new InvalidOperationException("QQ 未运行 · 打开 QQ 后自动连接");
            _vm.QQBridgeStatus = "正在连接本机 QQ…";
            var peer = await _qqBridge.GetActiveAsync(GetQQWindow(), _lifetime.Token);
            if (_closing) return false;
            _vm.CurrentConversation = peer.Label;
            _vm.QQBridgeStatus = "QQ 本地内存连接已就绪 · 无需注册或令牌";
            if (reportError) { _vm.Status = "QQ 已连接"; _vm.Detail = peer.Label; }
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            if (!_closing)
            {
                _vm.CurrentConversation = "等待打开目标 QQ 聊天";
                _vm.QQBridgeStatus = ex.Message;
                if (reportError) Report(ex);
            }
            return false;
        }
        finally { _connecting = false; }
    }

    private IntPtr GetQQWindow()
    {
        if (NativeMethods.IsQQForeground(out var current, out _)) return current;
        return _windowTracker?.LastWindow is { } tracked && tracked != IntPtr.Zero
            ? tracked : _monitor.GetLastSnapshot().Window;
    }

    private async Task PlayAsync(VoiceClip clip, string deviceId)
    {
        StopPlayback(false);
        using var cancellation = new CancellationTokenSource();
        _playback = cancellation;
        try
        {
            _vm.Playing = true;
            _vm.Status = "正在试听…";
            _vm.Detail = clip.VoiceName;
            _playClock.Restart(); _progressTimer.Start();
            await _audio.PlayAsync(clip.FilePath, deviceId, cancellation.Token);
            if (ReferenceEquals(_playback, cancellation))
            {
                _vm.Status = "试听完成";
                _vm.Detail = clip.DurationLabel;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closing) Report(ex); }
        finally
        {
            if (ReferenceEquals(_playback, cancellation))
            {
                _progressTimer.Stop(); _playClock.Stop();
                _playback = null;
                _vm.Playing = false;
            }
        }
    }

    public void StopPlayback(bool status = true)
    {
        _playback?.Cancel();
        _audio.Stop();
        _progressTimer.Stop();
        _playClock.Stop();
        _vm.Playing = false;
        if (status) { _vm.Status = "已停止"; _vm.Detail = "语音保留，可再次播放"; }
    }

    private async Task CaptureAsync()
    {
        if (_capturing) return;
        _capturing = true;
        try
        {
            var text = await GlobalHotkeys.CaptureQQAsync();
            _fromQQ = true;
            var unchanged = _vm.Text == text;
            _vm.Text = text;
            if (!_vm.AutoGenerate || unchanged) await GenerateAsync();
        }
        catch (Exception ex) { Report(ex); }
        finally { _capturing = false; }
    }

    private void Navigate(object sender, RoutedEventArgs e)
    {
        if (ComposePage is null || HistoryPage is null || SettingsPage is null) return;
        var page = (sender as RadioButton)?.Tag as string;
        ComposePage.Visibility = page == "compose" ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = page == "history" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = page switch { "history" => "生成记录", "settings" => "QQ 与引擎", _ => "语音工作台" };
    }

    private async void Generate_Click(object sender, RoutedEventArgs e) => await GenerateAsync();
    private async void Preview_Click(object sender, RoutedEventArgs e) => await PreviewAsync();
    private async void Send_Click(object sender, RoutedEventArgs e) => await SendToQQAsync();
    private void Stop_Click(object sender, RoutedEventArgs e) => StopPlayback();
    private void ClearText(object sender, RoutedEventArgs e) { _fromQQ = false; _vm.Text = ""; DraftEditor.Focus(); }
    private void Example_Click(object sender, RoutedEventArgs e)
    {
        _fromQQ = false;
        _vm.Text = "慢慢来，今天也要度过悠闲的一天。";
    }

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            await GenerateAsync();
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.CurrentClip is not { } clip) return;
        var dialog = new SaveFileDialog
        {
            Title = "导出语音", Filter = "WAV 音频|*.wav", FileName = $"油库里_{DateTime.Now:yyyyMMdd_HHmmss}.wav"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (!string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(clip.FilePath), StringComparison.OrdinalIgnoreCase))
                File.Copy(clip.FilePath, dialog.FileName, true);
            _vm.Status = "已导出 WAV"; _vm.Detail = dialog.FileName;
        }
        catch (Exception ex) { Report(ex); }
    }

    private void RefreshDevices_Click(object sender, RoutedEventArgs e) => _vm.RefreshDevices();
    private async void RefreshQQBridge_Click(object sender, RoutedEventArgs e) => await RefreshQQBridgeAsync();

    private void ImportEngine_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择 AquesTalk 1 / 2 / 10 DLL（可多选）", Filter = "AquesTalk DLL|*.dll", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            foreach (var path in dialog.FileNames) _vm.ImportEngine(path);
            _vm.Status = "油库里声库已关联";
            _vm.Detail = AquesTalkService.Is32Bit(dialog.FileName) ? "x86 · 将通过独立桥接程序合成" : "x64 · 本地合成";
        }
        catch (Exception ex) { Report(ex); }
    }

    private void OpenBridgeGuide(object sender, RoutedEventArgs e) => OpenGuide(sender, e);
    private void OpenAquesSite(object sender, RoutedEventArgs e) => Open("https://www.a-quest.com/download.html");
    private void OpenAudioFolder(object sender, RoutedEventArgs e) => Open(_storage.AudioDirectory);
    private void OpenDataFolder(object sender, RoutedEventArgs e) => Open(_storage.Root);
    private void OpenGuide(object sender, RoutedEventArgs e)
    {
        var guide = Path.Combine(AppContext.BaseDirectory, "使用说明.html");
        if (File.Exists(guide)) Open(guide);
        else Open(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "使用说明.html")));
    }
    private void Open(string location)
    {
        try { Process.Start(new ProcessStartInfo(location) { UseShellExecute = true }); }
        catch (Exception ex) { Report(ex); }
    }

    private async void HistoryPlay_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is VoiceClip clip) await UseHistoryAsync(clip);
    }
    private async void History_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HistoryList.SelectedItem is VoiceClip clip) await UseHistoryAsync(clip);
    }
    private async Task UseHistoryAsync(VoiceClip clip)
    {
        if (_vm.Sending) return;
        if (!File.Exists(clip.FilePath)) { _vm.Status = "音频文件已不存在"; return; }
        _revision++; _generation?.Cancel(); _debounce.Stop(); StopPlayback(false);
        ReleaseTemporaryClip();
        _suppress = true;
        _vm.Text = clip.Text;
        _suppress = false;
        _vm.CurrentClip = clip;
        _fromQQ = false;
        await PreviewAsync();
    }
    private void DeleteHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Sending) return;
        if (HistoryList.SelectedItem is not VoiceClip clip) { _vm.Status = "请先选择一条记录"; return; }
        if (MessageBox.Show(this, "删除这条记录及其本地音频？", "删除语音", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            if (_vm.CurrentClip?.Id == clip.Id) { StopPlayback(false); _vm.CurrentClip = null; }
            _storage.DeleteAudio(clip);
            _vm.History.Remove(clip);
            _storage.SaveHistory(_vm.History);
            _vm.Status = "记录已删除";
        }
        catch (Exception ex) { Report(ex); }
    }
    private void UpdateEmptyHistory() => HistoryEmpty.Visibility = _vm.History.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void ToggleMini(object sender, RoutedEventArgs e)
    {
        if (_mini is null)
        {
            _mini = new(this);
            _mini.Closed += (_, _) => _mini = null;
            _mini.Show();
        }
        else if (_mini.IsVisible) _mini.Hide();
        else _mini.Show();
    }
    public void Restore()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开油库里语音", null, (_, _) => Dispatcher.Invoke(Restore));
        menu.Items.Add("悬浮窗", null, (_, _) => Dispatcher.Invoke(() => ToggleMini(this, new())));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Close));
        _tray = new Forms.NotifyIcon
        {
            Text = "油库里语音 · QQ 输入联动",
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            ContextMenuStrip = menu, Visible = true
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(Restore);
    }
    private void Report(Exception error)
    {
        Storage.Log(error);
        _vm.Status = "操作未完成";
        _vm.Detail = error.Message;
    }
    private void ReleaseTemporaryClip()
    {
        if (_vm.CurrentClip is { } clip && !_vm.History.Any(c => c.Id == clip.Id))
        {
            try { _storage.DeleteAudio(clip); } catch (IOException) { }
        }
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _closing = true; _revision++;
        _lifetime.Cancel();
        _generation?.Cancel(); StopPlayback(false);
        _debounce.Stop(); _saveTimer.Stop(); _progressTimer.Stop(); _connectionTimer.Stop();
        _monitor.Dispose(); _hotkeys?.Dispose(); _windowTracker?.Dispose(); _audio.Dispose(); _tray?.Dispose();
        _mini?.Close();
        ReleaseTemporaryClip();
        SaveSettings();
    }
}
