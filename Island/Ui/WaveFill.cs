using System.Windows;
using System.Windows.Media;

namespace Island;

// Прогресс жидкостью (шаг 62): фигура наполняется до уровня Value, по поверхности бегут три волны с разной скоростью,
// длиной и прозрачностью, сверху тонкая светлая кромка, по жидкости иногда проходит блик.
// Complete(): волна «выплёскивается» (уровень с перехлёстом, всплеск, капли) и затем событие Completed — можно показать «готово».
// Анимация крутится только пока элемент виден и есть что двигать; на скрытом элементе кадры не тратятся.
public sealed class WaveFill : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(WaveFill),
        new FrameworkPropertyMetadata(0.0, (d, _) => ((WaveFill)d).Wake()));
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(Color), typeof(WaveFill),
        new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(WaveFill),
        new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Color Color { get => (Color)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    // -1: круг; иначе прямоугольник со сглаженными углами этого радиуса
    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    // показывать тёмную «пустую» часть фигуры
    public bool Track { get; set; } = true;

    public event Action? Completed;

    private double _shown;            // уровень на экране, плавно догоняет Value
    private double _t;                // время, с
    private double _splash = -1;      // 0..1 пока идёт всплеск, -1 нет
    private TimeSpan _last;
    private bool _hooked;

    public WaveFill()
    {
        IsHitTestVisible = false;
        IsVisibleChanged += (_, _) => Wake();
        Unloaded += (_, _) => Hook(false);
    }

    public void Complete()
    {
        _splash = 0;
        Wake();
    }

    // уровень сразу, без доливания (новая задача, а не продолжение)
    public void Snap(double v) { _shown = Math.Clamp(v, 0, 1); Value = v; InvalidateVisual(); }

    private bool Busy => _splash >= 0 || Math.Abs(_shown - Value) > 0.001 || (Value > 0.001 && Value < 0.999);

    private void Wake() => Hook(IsVisible && Busy);

    private void Hook(bool on)
    {
        if (on == _hooked) { if (on) InvalidateVisual(); return; }
        _hooked = on;
        _last = default;
        if (on) CompositionTarget.Rendering += OnFrame;
        else CompositionTarget.Rendering -= OnFrame;
        InvalidateVisual();
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _last) return;
        double dt = _last == default ? 1 / 60.0 : Math.Min(0.05, (now - _last).TotalSeconds);
        _last = now;
        _t += dt;
        double v = Math.Clamp(Value, 0, 1);
        _shown += (v - _shown) * Math.Min(1, dt * 6);   // доливается плавно, а не прыжками процентов
        if (_splash >= 0)
        {
            _splash += dt / 0.9;
            if (_splash >= 1) { _splash = -1; _shown = 1; Completed?.Invoke(); }
        }
        InvalidateVisual();
        if (!Busy) Hook(false);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 2 || h < 2) return;
        Geometry clip = CornerRadius < 0
            ? new EllipseGeometry(new Rect(0, 0, w, h))
            : Smooth.Shape(w, h, new System.Windows.CornerRadius(Math.Min(CornerRadius, Math.Min(w, h) / 2)));
        var c = Color;
        if (Track) dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x26, c.R, c.G, c.B)), null, clip);

        double s = _splash;
        double level = _shown, boost = 0;
        if (s >= 0)
        {
            // всплеск: уровень перехлёстывает через край и оседает, волны на миг сильнее
            double bell = Math.Sin(Math.PI * s);
            level = 1 + 0.12 * bell;
            boost = bell;
        }
        if (level <= 0.002) return;

        dc.PushClip(clip);
        double baseY = h * (1 - level);
        bool still = level >= 0.999 && s < 0;               // полный и спокойный: без волн
        double amp = still ? 0 : h * (0.055 + 0.12 * boost) * (level < 0.08 ? level / 0.08 : 1);
        // три волны: дальняя медленная и прозрачная, ближняя быстрая и яркая
        (double k, double speed, double a, byte alpha, double lighten)[] waves =
        {
            (1.3, -1.1, 0.8, 0x55, 0.0),
            (2.1, 1.7, 0.65, 0x88, 0.1),
            (1.7, 2.6, 1.0, 0xFF, 0.25),
        };
        StreamGeometry? front = null, rim = null;
        foreach (var (k, speed, a, alpha, lighten) in waves)
        {
            var g = new StreamGeometry();
            var line = new StreamGeometry();
            using (var ctx = g.Open())
            using (var lc = line.Open())
            {
                double step = Math.Max(1, w / 40);
                ctx.BeginFigure(new Point(0, h + 1), true, true);
                bool first = true;
                for (double x = 0; x <= w + step; x += step)
                {
                    double xx = Math.Min(x, w);
                    var p = new Point(xx, baseY + amp * a * Math.Sin(xx / w * Math.PI * 2 * k + _t * speed * Math.PI));
                    ctx.LineTo(p, true, false);
                    if (first) { lc.BeginFigure(p, false, false); first = false; }
                    else lc.LineTo(p, true, false);
                }
                ctx.LineTo(new Point(w, h + 1), true, false);
            }
            g.Freeze();
            line.Freeze();
            rim = line;
            var top = Lighten(c, lighten);
            var br = new LinearGradientBrush(Color.FromArgb(alpha, top.R, top.G, top.B), Color.FromArgb(alpha, c.R, c.G, c.B), 90);
            dc.DrawGeometry(br, null, g);
            front = g;
        }
        // светлая кромка по ближней волне
        if (rim != null && !still)
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x70, 255, 255, 255)), Math.Max(0.6, h / 40)), rim);
        // блик: косая светлая полоса раз в ~2.6 с проходит по жидкости
        double gp = (_t % 2.6) / 2.6;
        if (gp < 0.45)
        {
            double gx = -w * 0.4 + gp / 0.45 * w * 1.8;
            var glint = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(0x55, 255, 255, 255), 0.5),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
                }
            };
            dc.PushTransform(new SkewTransform(-20, 0, gx, h / 2));
            if (front != null) dc.PushClip(front);
            dc.DrawRectangle(glint, null, new Rect(gx - w * 0.18, 0, w * 0.36, h));
            if (front != null) dc.Pop();
            dc.Pop();
        }
        dc.Pop();

        // капли, вылетающие при всплеске (рисуются поверх края, без обрезки)
        if (s >= 0 && s < 0.85)
        {
            var drop = new SolidColorBrush(Lighten(c, 0.2));
            for (int i = 0; i < 3; i++)
            {
                double tt = s / 0.85, dir = (i - 1) * 0.9;
                double x = w / 2 + dir * w * 0.45 * tt;
                double y = h * 0.1 - h * 0.9 * tt + h * 1.6 * tt * tt;   // взлёт и падение
                double r = Math.Max(0.8, w / 18 * (1 - tt * 0.6));
                dc.DrawEllipse(drop, null, new Point(x, y), r, r);
            }
        }
    }

    private static Color Lighten(Color c, double t) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));
}
