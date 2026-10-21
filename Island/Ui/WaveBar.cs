using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Island;

// Прогресс в виде волны из полосок, как у голосовых сообщений в Telegram.
// Высоты полосок считаются по названию трека (у каждой песни своя волна), пройденная часть красится цветом Fill,
// остальное тусклое. Клики обрабатывает хозяин (полоска сама мышь не ловит).
internal sealed class WaveBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(WaveBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    // 0..1: полоски вырастают слева направо при смене трека
    public static readonly DependencyProperty GrowProperty = DependencyProperty.Register(
        nameof(Grow), typeof(double), typeof(WaveBar),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(WaveBar),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, Math.Clamp(value, 0, 1)); }
    public double Grow { get => (double)GetValue(GrowProperty); set => SetValue(GrowProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    private const int Base = 240;
    private readonly double[] _h = new double[Base];
    private string _seed = "\u0001";

    public WaveBar()
    {
        IsHitTestVisible = false;
        Generate("");
    }

    public void SetSeed(string seed)
    {
        if (seed == _seed) return;
        _seed = seed;
        Generate(seed);
        BeginAnimation(GrowProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(750))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        InvalidateVisual();
    }

    private void Generate(string seed)
    {
        uint hash = 2166136261;
        foreach (char c in seed) hash = (hash ^ (uint)c) * 16777619u;
        var rnd = new Random(unchecked((int)hash));
        var a = new double[Base];
        for (int i = 0; i < Base; i++) a[i] = rnd.NextDouble();
        double p1 = rnd.NextDouble() * 6.28, p2 = rnd.NextDouble() * 6.28, p3 = rnd.NextDouble() * 6.28;
        for (int i = 0; i < Base; i++)
        {
            double t = i / (double)Base;
            double env = 0.55 + 0.22 * Math.Sin(t * 9 + p1) + 0.14 * Math.Sin(t * 23 + p2) + 0.09 * Math.Sin(t * 51 + p3);
            double sm = 0.5 * a[i] + 0.25 * a[(i + Base - 1) % Base] + 0.25 * a[(i + 1) % Base];
            double v = 0.12 + 0.88 * Math.Clamp(env * 0.62 + sm * 0.75, 0, 1);
            _h[i] = Math.Clamp(v, 0.16, 1.0);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 6 || h < 6) return;
        double bw = Math.Max(2, Math.Min(4, h * 0.14));
        double gap = bw * 0.85;
        int n = Math.Max(1, (int)((w + gap) / (bw + gap)));
        double total = n * bw + (n - 1) * gap;
        double x0 = (w - total) / 2;
        double played = Value * n;
        double grow = Grow;
        var fill = Fill;

        for (int pass = 0; pass < 2; pass++)
        {
            if (pass == 1) dc.PushOpacity(0.3);
            for (int i = 0; i < n; i++)
            {
                bool isPlayed = i + 0.5 <= played;
                if (isPlayed == (pass == 1)) continue;
                int idx = Math.Min(Base - 1, (int)(i * (double)Base / n));
                double g = Math.Clamp(grow * 1.6 - (double)i / n * 0.6, 0, 1);
                double bh = Math.Max(bw, _h[idx] * h * g);
                dc.DrawRoundedRectangle(fill, null, new Rect(x0 + i * (bw + gap), (h - bh) / 2, bw, bh), bw / 2, bw / 2);
            }
            if (pass == 1) dc.Pop();
        }
    }
}
