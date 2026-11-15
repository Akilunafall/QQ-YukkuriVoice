using Yukkuri.Services;

namespace Yukkuri;

public sealed record VoicePreset(string Id, string Name, string Description, double Pitch = 1, double Robot = 0)
{
    public static readonly VoicePreset[] All =
    [
        new("reimu", "AT1-F1（ゆっくり）", "AquesTalk1"),
        new("marisa", "AT1-F2", "AquesTalk1"),
        new("youmu", "AT1-M1", "AquesTalk1"),
        new("m2", "AT1-M2", "AquesTalk1"),
        new("dvd", "AT1-DVD", "AquesTalk1"),
        new("imd1", "AT1-IMD1", "AquesTalk1"),
        new("jgr", "AT1-JGR", "AquesTalk1"),
        new("r1", "AT1-R1", "AquesTalk1"),
        // Kept as a migration alias for settings created by older builds.
        new("natural", "AT1-F1（原声）", "AquesTalk1")
    ];
}

public sealed class UserSettings
{
    public string PresetId { get; set; } = "reimu";
    public string Engine { get; set; } = SpeechService.YukkuriEngine;
    public string SystemVoice { get; set; } = "";
    public double Speed { get; set; } = 1.0;
    // These values remain in the settings schema for backwards compatibility.
    // The UI no longer exposes them; synthesized audio keeps the selected engine's
    // original timbre instead of adding a quantizer/modulator layer.
    public double Pitch { get; set; } = 1;
    public double Robot { get; set; } = 0;
    public double Volume { get; set; } = 1;
    public bool AutoGenerate { get; set; } = true;
    public bool ListenToQQ { get; set; } = true;
    public bool KeepHistory { get; set; } = true;
    public int DebounceMs { get; set; } = 1200;
    public string PreviewDeviceId { get; set; } = "";
    public string SendDeviceId { get; set; } = "";
    public string AquesDll { get; set; } = "";
    public string AquesEngine { get; set; } = "AquesTalk1";
    public string AquesPhont { get; set; } = "";
    public int AquesPreset { get; set; }
    public List<ClassicVoicePack> ClassicVoices { get; set; } = [];
    public int SendDelaySeconds { get; set; } = 3;
}

public sealed record AudioDevice(string Id, string Name, bool IsVirtual = false)
{
    public override string ToString() => Name;
}

public sealed record ClassicVoicePack(
    string Name,
    string Path,
    string Engine = "AquesTalk1",
    string PhontPath = "",
    int Preset = 0,
    string Id = "",
    bool IsEvaluation = false)
{
    public override string ToString() => Name;
    public string GroupName => Engine switch
    {
        "AquesTalk2" => "AquesTalk 2",
        "AquesTalk10" => "AquesTalk 10",
        _ => "AquesTalk 1"
    };
    public string Availability => IsEvaluation ? "官方评估版：ナ行、マ行会读作ヌ" : "引擎原声音色";
}

public sealed class VoiceClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public string VoiceName { get; set; } = "";
    public string Engine { get; set; } = "";
    public string FilePath { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public double Seconds { get; set; }
    public string DurationLabel => $"{Seconds:0.0} 秒";
    public string TimeLabel => CreatedAt.ToString("MM-dd  HH:mm");
    public string Summary => Text.Replace('\r', ' ').Replace('\n', ' ');
}

public sealed record SynthesisRequest(
    string Text, string PresetId, string Engine, string SystemVoice,
    double Speed, double Pitch, double Robot, double Volume, string AquesDll,
    string AquesEngine = "AquesTalk1", string AquesPhont = "", int AquesPreset = 0,
    string VoiceName = "");

public class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
