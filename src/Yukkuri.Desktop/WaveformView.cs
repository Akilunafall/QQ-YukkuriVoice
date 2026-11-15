using System.Windows.Media;
using NAudio.Wave;

namespace Yukkuri;

public sealed class WaveformView : FrameworkElement
{
    private float[] _peaks = [];
    public double Progress { get; set; }
    public void Load(string? path)
    {
        _peaks = [];
        Progress = 0;
        if (path is not null && File.Exists(path))
        {
            using var reader = new AudioFileReader(path);
            var samplesPerBar = Math.Max(1, (int)(reader.Length / 4 / 110));
            var buffer = new float[samplesPerBar];
            var peaks = new List<float>();
            int count;
            while ((count = reader.Read(buffer, 0, buffer.Length)) > 0 && peaks.Count < 112)
            {
                var peak = 0f;
                for (var i = 0; i < count; i++) peak = Math.Max(peak, Math.Abs(buffer[i]));
                peaks.Add(peak);
            }
            _peaks = peaks.ToArray();
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var bars = Math.Max(1, (int)(ActualWidth / 5));
        var dim = new SolidColorBrush(Color.FromRgb(208, 218, 219));
        var active = new SolidColorBrush(Color.FromRgb(25, 126, 118));
        var maximum = _peaks.Length == 0 ? 1f : Math.Max(0.01f, _peaks.Max());
        for (var i = 0; i < bars; i++)
        {
            var peak = _peaks.Length == 0 ? 0 : _peaks[Math.Min(_peaks.Length - 1, i * _peaks.Length / bars)];
            var height = Math.Max(3, Math.Pow(peak / maximum, 0.6) * ActualHeight * 0.9);
            context.DrawRoundedRectangle(_peaks.Length > 0 && (double)i / bars <= Progress ? active : dim,
                null, new Rect(i * 5, (ActualHeight - height) / 2, 2.5, height), 1, 1);
        }
    }
}
