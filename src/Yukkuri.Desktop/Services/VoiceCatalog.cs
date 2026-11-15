namespace Yukkuri.Services;

public static class VoiceCatalog
{
    public static ClassicVoicePack Canonicalize(ClassicVoicePack voice)
    {
        if (voice.Engine != AquesTalkService.Engine1 || !File.Exists(voice.Path)) return voice;
        var directory = Path.GetDirectoryName(voice.Path);
        var match = AquesTalkService.BundledVoices.FirstOrDefault(v =>
            string.Equals(Path.GetFileName(Path.GetDirectoryName(v.Path)), Path.GetFileName(directory), StringComparison.OrdinalIgnoreCase)
            && File.Exists(v.Path));
        if (match is null) return voice;
        if (!Path.GetFullPath(voice.Path).Equals(match.Path, StringComparison.OrdinalIgnoreCase) &&
            !File.ReadAllBytes(voice.Path).SequenceEqual(File.ReadAllBytes(match.Path))) return voice;
        return new(match.Name, match.Path, match.Engine, match.PhontPath, match.Preset, match.Id);
    }

    public static List<ClassicVoicePack> GetVoices(string localEngineDirectory)
    {
        var voices = AquesTalkService.BundledVoices
            .Where(v => File.Exists(v.Path))
            .Select(v => new ClassicVoicePack(v.Name, v.Path, v.Engine, v.PhontPath, v.Preset, v.Id))
            .ToList();
        if (!Directory.Exists(localEngineDirectory)) return voices;
        foreach (var dll in Directory.EnumerateFiles(localEngineDirectory, "*.dll", SearchOption.AllDirectories))
        {
            // Prefer x64 and ignore duplicate sample and x86 libraries.
            if (!dll.Contains(Path.DirectorySeparatorChar + "lib64" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)) continue;
            if (dll.Contains("aqtk1_win", StringComparison.OrdinalIgnoreCase) &&
                !dll.Contains(Path.DirectorySeparatorChar + "f3" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)) continue;
            try { voices.AddRange(FromLibrary(dll, true)); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or BadImageFormatException)
            { Storage.Log(ex); }
        }
        return voices.DistinctBy(Key).ToList();
    }

    public static IReadOnlyList<ClassicVoicePack> FromLibrary(string path, bool evaluation = false)
    {
        path = Path.GetFullPath(path);
        var engine = AquesTalkService.DetectEngine(path);
        AquesTalkService.ValidateLibrary(path, engine);
        var suffix = evaluation ? "（评估版）" : "";
        if (engine == AquesTalkService.Engine10)
            return AquesTalkService.AquesTalk10Presets.Select((name, index) =>
                new ClassicVoicePack("声种: AT10-" + name + suffix, path, engine,
                    Preset: index, Id: "at10-" + name.ToLowerInvariant(), IsEvaluation: evaluation)).ToList();
        if (engine == AquesTalkService.Engine2)
        {
            var directory = new DirectoryInfo(Path.GetDirectoryName(path)!);
            var root = directory.Name is "lib" or "lib64" or "x86" or "x64"
                ? directory.Parent!.FullName : directory.FullName;
            var phonts = Directory.EnumerateFiles(root, "*.phont", SearchOption.AllDirectories)
                .ToDictionary(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase);
            var names = AquesTalkService.AquesTalk2Phonts.Concat(phonts.Keys.OrderBy(n => n))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return names.Where(phonts.ContainsKey).Select(name =>
                new ClassicVoicePack("声种: AT2-" + name[3..].ToUpperInvariant() + suffix, path, engine,
                    phonts[name], Id: "at2-" + name, IsEvaluation: evaluation)).ToList();
        }

        var voice = new DirectoryInfo(Path.GetDirectoryName(path)!).Name.ToLowerInvariant();
        var label = voice is "f1" or "f2" or "f3" or "m1" or "m2" or "r1" or "dvd" or "imd1" or "jgr"
            ? voice.ToUpperInvariant() : Path.GetFileNameWithoutExtension(path);
        return [new ClassicVoicePack("声种: AT1-" + label + suffix, path, engine,
            Id: "at1-" + voice, IsEvaluation: evaluation)];
    }

    public static string Key(ClassicVoicePack voice) =>
        string.Join("|", voice.Engine, Path.GetFullPath(voice.Path), voice.PhontPath, voice.Preset).ToUpperInvariant();
}
