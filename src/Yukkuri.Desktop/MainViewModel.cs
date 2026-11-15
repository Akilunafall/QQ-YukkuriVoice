using Yukkuri.Services;

namespace Yukkuri;

public sealed class MainViewModel : ObservableObject
{
    public UserSettings Settings { get; }
    public ObservableCollection<VoiceClip> History { get; }
    public ObservableCollection<AudioDevice> Devices { get; } = [];
    public ObservableCollection<ClassicVoicePack> ClassicVoices { get; } = [];
    public string[] Engines { get; } = [SpeechService.YukkuriEngine];
    public event Action<bool>? Edited;
    public event Action? PreferencesChanged;
    public event Action? AutoGenerationChanged;
    private string _text = "慢慢来，今天也要度过悠闲的一天。";
    private string _status = "输入就绪";
    private string _detail = "内置油库里声库可离线合成";
    private string _qq = "正在检测 QQ…";
    private VoiceClip? _clip;
    private bool _busy;
    private bool _playing;
    private bool _sending;
    private string _bridgeStatus = "QQ 进程桥接未连接";
    public string CaptureShortcut { get; set; } = "Ctrl + Alt + Y";
    public string SendShortcut { get; set; } = "Ctrl + Alt + P";
    public string CaptureShortcutHint => CaptureShortcut + "   手动取词";
    private string _conversation = "等待打开目标 QQ 聊天";
    public string SendShortcutHint => $"{SendShortcut}   发送至当前 QQ 会话";
    public string CurrentConversation { get => _conversation; set { _conversation = value; Notify(); } }
    public string QQBridgeStatus { get => _bridgeStatus; set { _bridgeStatus = value; Notify(); } }
    public bool Sending { get => _sending; set { _sending = value; Notify(); Notify(nameof(CanSend)); Notify(nameof(CanGenerate)); Notify(nameof(CanEdit)); } }
    public bool CanEdit => !Sending;
    public bool CanSend => !string.IsNullOrWhiteSpace(Text) && !Busy && !Sending;

    public MainViewModel(Storage storage)
    {
        Settings = storage.LoadSettings();
        var previousVoice = new ClassicVoicePack("", Settings.AquesDll, Settings.AquesEngine,
            Settings.AquesPhont, Settings.AquesPreset);
        var canonical = VoiceCatalog.Canonicalize(previousVoice);
        if (canonical.Path != previousVoice.Path) Settings.AquesDll = canonical.Path;
        History = new(storage.LoadHistory());
        foreach (var pack in VoiceCatalog.GetVoices(storage.EngineDirectory))
            ClassicVoices.Add(pack);
        foreach (var pack in (Settings.ClassicVoices ?? []).Select(VoiceCatalog.Canonicalize))
            if (File.Exists(pack.Path) && !ClassicVoices.Any(v => VoiceCatalog.Key(v) == VoiceCatalog.Key(pack)))
                ClassicVoices.Add(pack);
        if (File.Exists(Settings.AquesDll) &&
            !ClassicVoices.Any(v => v.Path.Equals(Settings.AquesDll, StringComparison.OrdinalIgnoreCase)))
            ImportEngine(Settings.AquesDll);
        if (!File.Exists(Settings.AquesDll))
            Settings.AquesDll = AquesTalkService.DefaultVoicePath;
        var selected = ClassicVoice ?? ClassicVoices.FirstOrDefault();
        if (selected is not null) ApplyVoiceProfile(selected, false);
        RefreshDevices();
    }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            Notify(); Notify(nameof(CharacterCount)); Notify(nameof(CanSend));
            Edited?.Invoke(true);
        }
    }
    public string CharacterCount => $"{Text.Length} / 240";
    public string Engine
    {
        get => Settings.Engine;
        set
        {
            value = SpeechService.YukkuriEngine;
            if (Settings.Engine == value) return;
            Settings.Engine = value; Notify(); Notify(nameof(EngineNote)); Notify(nameof(IsChinese)); Notify(nameof(IsClassic));
            Edited?.Invoke(false); PreferencesChanged?.Invoke();
        }
    }
    public bool IsChinese => false;
    public bool IsClassic => true;
    public string EngineNote => ClassicVoice?.Availability ?? "";
    public string SystemVoice
    {
        get => Settings.SystemVoice;
        set
        {
            if (Settings.SystemVoice == value) return;
            Settings.SystemVoice = value; Notify(); Edited?.Invoke(false); PreferencesChanged?.Invoke();
        }
    }
    public string PresetId => Settings.PresetId;
    public string PresetName => ClassicVoice?.Name ?? "请选择音色";
    public double Speed { get => Settings.Speed; set { Settings.Speed = value; Changed(nameof(Speed)); } }
    public double Pitch { get => Settings.Pitch; set { Settings.Pitch = value; Changed(nameof(Pitch)); } }
    public double Robot { get => Settings.Robot; set { Settings.Robot = value; Changed(nameof(Robot)); } }
    public double Volume { get => Settings.Volume; set { Settings.Volume = value; Changed(nameof(Volume)); } }
    public bool AutoGenerate
    {
        get => Settings.AutoGenerate;
        set
        {
            if (Settings.AutoGenerate == value) return;
            Settings.AutoGenerate = value; Notify(); PreferencesChanged?.Invoke();
            AutoGenerationChanged?.Invoke();
        }
    }
    public bool ListenToQQ
    {
        get => Settings.ListenToQQ;
        set { Settings.ListenToQQ = value; Notify(); PreferencesChanged?.Invoke(); }
    }
    public bool KeepHistory
    {
        get => Settings.KeepHistory;
        set { Settings.KeepHistory = value; Notify(); PreferencesChanged?.Invoke(); }
    }
    public AudioDevice? PreviewDevice
    {
        get => Devices.FirstOrDefault(d => d.Id == Settings.PreviewDeviceId);
        set { Settings.PreviewDeviceId = value?.Id ?? ""; Notify(); PreferencesChanged?.Invoke(); }
    }
    public string AquesPath => string.IsNullOrEmpty(Settings.AquesDll) ? "尚未导入 AquesTalk 引擎" : Settings.AquesDll;
    public ClassicVoicePack? ClassicVoice
    {
        get => ClassicVoices.FirstOrDefault(v =>
            v.Path.Equals(Settings.AquesDll, StringComparison.OrdinalIgnoreCase) &&
            v.Engine == Settings.AquesEngine && v.Preset == Settings.AquesPreset &&
            v.PhontPath.Equals(Settings.AquesPhont, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is null || ReferenceEquals(value, ClassicVoice)) return;
            ApplyVoiceProfile(value, true);
            Notify(); Notify(nameof(AquesPath));
            Edited?.Invoke(false); PreferencesChanged?.Invoke();
        }
    }
    public string Status { get => _status; set { _status = value; Notify(); } }
    public string Detail { get => _detail; set { _detail = value; Notify(); } }
    public string QQStatus { get => _qq; set { _qq = value; Notify(); } }
    public bool Busy { get => _busy; set { _busy = value; Notify(); Notify(nameof(CanGenerate)); Notify(nameof(CanSend)); } }
    public bool Playing { get => _playing; set { _playing = value; Notify(); } }
    public bool CanGenerate => !Busy && !Sending;
    public VoiceClip? CurrentClip
    {
        get => _clip;
        set { _clip = value; Notify(); Notify(nameof(HasClip)); Notify(nameof(ClipTitle)); Notify(nameof(ClipDuration)); Notify(nameof(CanSend)); }
    }
    public bool HasClip => CurrentClip is not null;
    public string ClipTitle => CurrentClip is null ? "等待生成" : $"{CurrentClip.VoiceName}  /  {CurrentClip.Engine.Split('·')[0].Trim()}";
    public string ClipDuration => CurrentClip?.DurationLabel ?? "0.0 秒";

    public void SelectPreset(string id)
    {
        var profile = VoicePreset.All.First(p => p.Id == id);
        Settings.PresetId = id;
        Settings.Pitch = profile.Pitch;
        Settings.Robot = profile.Robot;
        Notify(nameof(PresetId)); Notify(nameof(PresetName)); Notify(nameof(Pitch)); Notify(nameof(Robot));
        Edited?.Invoke(false);
        PreferencesChanged?.Invoke();
    }

    public void ImportEngine(string path)
    {
        var packs = VoiceCatalog.FromLibrary(path);
        if (packs.Count == 0) throw new InvalidOperationException("此 SDK 中未找到对应的 .phont 音色文件。");
        foreach (var pack in packs)
        {
            if (!ClassicVoices.Any(v => VoiceCatalog.Key(v) == VoiceCatalog.Key(pack))) ClassicVoices.Add(pack);
        }
        Settings.ClassicVoices = ClassicVoices.ToList();
        ApplyVoiceProfile(packs[0], true);
        Notify(nameof(AquesPath)); Notify(nameof(ClassicVoice));
        Edited?.Invoke(false); PreferencesChanged?.Invoke();
    }

    public void RefreshDevices()
    {
        try
        {
            var devices = AudioService.GetDevices();
            Devices.Clear();
            foreach (var device in devices) Devices.Add(device);
            Notify(nameof(PreviewDevice));
        }
        catch (Exception ex) { Status = "音频设备不可用"; Detail = ex.Message; }
    }

    private void Changed(string property)
    {
        Notify(property);
        Edited?.Invoke(false);
        PreferencesChanged?.Invoke();
    }

    private void ApplyVoiceProfile(ClassicVoicePack pack, bool notify)
    {
        Settings.AquesDll = pack.Path;
        Settings.AquesEngine = pack.Engine;
        Settings.AquesPhont = pack.PhontPath;
        Settings.AquesPreset = pack.Preset;
        Settings.PresetId = string.IsNullOrEmpty(pack.Id) ? "natural" : pack.Id;
        Settings.Pitch = 1;
        Settings.Robot = 0;
        Settings.Volume = 1;
        if (!notify) return;
        Notify(nameof(PresetId));
        Notify(nameof(PresetName));
        Notify(nameof(Pitch));
        Notify(nameof(Robot));
        Notify(nameof(Volume));
        Notify(nameof(EngineNote));
    }

    public SynthesisRequest Request() => new(Text, PresetId, SpeechService.YukkuriEngine,
        "", Speed, 1, 0, 1, Settings.AquesDll, Settings.AquesEngine, Settings.AquesPhont,
        Settings.AquesPreset, ClassicVoice?.Name ?? "");
}
