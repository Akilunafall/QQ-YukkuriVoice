using System.Text.Json;

namespace Yukkuri.Services;

public sealed class Storage
{
    private readonly string _root;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public string AudioDirectory => Path.Combine(_root, "Audio");
    public string EngineDirectory => Path.Combine(_root, "Engines");
    public string Root => _root;

    public Storage(string? root = null)
    {
        _root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YukkuriVoice");
        Directory.CreateDirectory(AudioDirectory);
    }

    public UserSettings LoadSettings()
    {
        var value = Read<UserSettings>("settings.json") ?? new();
        value.Speed = Math.Clamp(double.IsFinite(value.Speed) ? value.Speed : 1, 0.65, 1.6);
        // Older builds stored the optional "robot" post effect here.  Keep the
        // fields readable for JSON compatibility, but force the clean engine
        // defaults so old settings cannot reintroduce the scratchy sound.
        value.Pitch = 1;
        value.Robot = 0;
        value.Volume = 1;
        value.DebounceMs = Math.Clamp(value.DebounceMs, 600, 4000);
        value.SendDelaySeconds = Math.Clamp(value.SendDelaySeconds, 0, 10);
        if (string.IsNullOrWhiteSpace(value.PresetId)) value.PresetId = "reimu";
        value.Engine = SpeechService.YukkuriEngine;
        if (!File.Exists(value.AquesDll))
            value.AquesDll = AquesTalkService.DefaultVoicePath;
        if (string.IsNullOrWhiteSpace(value.AquesEngine))
            value.AquesEngine = "AquesTalk1";
        if (string.IsNullOrWhiteSpace(value.AquesPhont))
            value.AquesPhont = "";
        return value;
    }

    public List<VoiceClip> LoadHistory() =>
        (Read<List<VoiceClip>>("history.json") ?? []).Where(c => IsOwnedAudio(c.FilePath) && File.Exists(c.FilePath)).Take(60).ToList();

    public void SaveSettings(UserSettings settings) => Write("settings.json", settings);
    public void SaveHistory(IEnumerable<VoiceClip> clips) => Write("history.json", clips.Take(60).ToList());

    public bool IsOwnedAudio(string path)
    {
        try
        {
            return Path.GetFullPath(path).StartsWith(
                Path.GetFullPath(AudioDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public void DeleteAudio(VoiceClip clip)
    {
        if (IsOwnedAudio(clip.FilePath)) File.Delete(clip.FilePath);
    }

    private T? Read<T>(string file)
    {
        var path = Path.Combine(_root, file);
        if (!File.Exists(path)) return default;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8)); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log(ex);
            return default;
        }
    }

    private void Write<T>(string name, T value)
    {
        var destination = Path.Combine(_root, name);
        var temp = destination + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Json), Encoding.UTF8);
        File.Move(temp, destination, true);
    }

    public static void Log(Exception error)
    {
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YukkuriVoice");
            Directory.CreateDirectory(root);
            // Do not log prompts or chat content.
            File.AppendAllText(Path.Combine(root, "errors.log"),
                $"{DateTime.Now:O} {error.GetType().Name}: {error.Message}\n");
        }
        catch { }
    }
}
