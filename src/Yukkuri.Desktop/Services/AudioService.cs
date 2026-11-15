using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Yukkuri.Services;

public sealed class AudioService : IDisposable
{
    private CancellationTokenSource? _playing;

    public static IReadOnlyList<AudioDevice> GetDevices()
    {
        var devices = new List<AudioDevice> { new("", "系统默认播放设备") };
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                var name = device.FriendlyName;
                var virtualDevice = new[] { "cable", "vb-audio", "voicemeeter", "virtual", "虚拟" }
                    .Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase));
                devices.Add(new(device.ID, name, virtualDevice));
            }
        }
        return devices;
    }

    public void Stop() => _playing?.Cancel();

    public async Task PlayAsync(string path, string deviceId, CancellationToken cancellationToken = default)
    {
        Stop();
        using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _playing = source;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = string.IsNullOrEmpty(deviceId)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                : enumerator.GetDevice(deviceId);
            if (device.State != DeviceState.Active) throw new InvalidOperationException("音频设备已断开，请刷新设备列表。");
            using var file = new AudioFileReader(path);
            using var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 80);
            var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            output.PlaybackStopped += (_, args) =>
            {
                if (args.Exception is not null) complete.TrySetException(args.Exception);
                else complete.TrySetResult();
            };
            var rate = device.AudioClient.MixFormat.SampleRate;
            ISampleProvider provider = file;
            if (provider.WaveFormat.SampleRate != rate) provider = new WdlResamplingSampleProvider(provider, rate);
            output.Init(provider.ToWaveProvider());
            source.Token.ThrowIfCancellationRequested();
            output.Play();
            using var registration = source.Token.Register(() => output.Stop());
            await complete.Task.ConfigureAwait(false);
            source.Token.ThrowIfCancellationRequested();
        }
        finally { if (ReferenceEquals(_playing, source)) _playing = null; }
    }

    public void Dispose() => Stop();
}
