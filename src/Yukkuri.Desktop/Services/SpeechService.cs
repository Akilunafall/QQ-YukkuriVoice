using NAudio.Wave;

namespace Yukkuri.Services;

public sealed class SpeechService
{
    public const string YukkuriEngine = "油库里风格 · 内置 AquesTalk";
    public const string ClassicEngine = YukkuriEngine;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<VoiceClip> GenerateAsync(SynthesisRequest request, string destination, CancellationToken token)
    {
        Validate(request);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var kana = ChineseToKana.Convert(request.Text);
                if (string.IsNullOrWhiteSpace(kana))
                    throw new InvalidOperationException("没有可合成的文字，请输入中文、日文假名或标点。");
                var raw = AquesTalkService.Synthesize(
                    request.AquesDll,
                    kana,
                    (int)Math.Round(request.Speed * 100),
                    request.AquesEngine,
                    request.AquesPhont,
                    request.AquesPreset);
                token.ThrowIfCancellationRequested();
                var bytes = raw;
                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, bytes);
                using var reader = new WaveFileReader(new MemoryStream(bytes));
                return new VoiceClip
                {
                    Text = request.Text.Trim(),
                    VoiceName = !string.IsNullOrWhiteSpace(request.VoiceName) ? request.VoiceName
                        : !string.IsNullOrWhiteSpace(request.SystemVoice) ? request.SystemVoice
                        : VoicePreset.All.FirstOrDefault(p => p.Id == request.PresetId)?.Name ?? request.PresetId,
                    Engine = request.Engine,
                    FilePath = destination,
                    Seconds = reader.TotalTime.TotalSeconds
                };
            }, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public static void Validate(SynthesisRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text)) throw new InvalidOperationException("请先输入要说的话。");
        if (request.Text.Length > 240) throw new InvalidOperationException("每条语音最多 240 个字符，请分段生成。");
        if (request.Engine != YukkuriEngine)
            throw new InvalidOperationException("不支持的语音引擎。");
        if (string.IsNullOrWhiteSpace(request.PresetId)) throw new InvalidOperationException("请选择有效声线。");
        if (!double.IsFinite(request.Speed) || request.Speed < 0.65 || request.Speed > 1.6 ||
            !double.IsFinite(request.Pitch) || request.Pitch < 0.8 || request.Pitch > 1.8 ||
            !double.IsFinite(request.Robot) || request.Robot < 0 || request.Robot > 1 ||
            !double.IsFinite(request.Volume) || request.Volume < 0 || request.Volume > 1)
            throw new InvalidOperationException("语音参数超出范围。");
        if (!File.Exists(request.AquesDll))
            throw new InvalidOperationException("所选油库里声库不可用，请在「QQ 与引擎」中重新选择或导入授权引擎文件。");
        if (string.Equals(request.AquesEngine, "AquesTalk2", StringComparison.OrdinalIgnoreCase) &&
            !File.Exists(request.AquesPhont))
            throw new InvalidOperationException("AquesTalk2 音色需要对应的 .phont 音色文件。");
    }
}

