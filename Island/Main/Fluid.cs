using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Island;

// Затухающая пружина как функция сглаживания: плавный разгон, мягкий перелёт, оседание без дрожи.
public sealed class SpringEase : IEasingFunction
{
    private readonly double _decay, _freq, _norm;

    public SpringEase(double decay = 5.2, double freq = 10)
    {
        _decay = decay;
        _freq = freq;
        _norm = Raw(1);
    }

    private double Raw(double t) =>
        1 - Math.Exp(-_decay * t) * (Math.Cos(_freq * t) + _decay / _freq * Math.Sin(_freq * t));

    public double Ease(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        return Raw(t) / _norm;
    }
}

// cubic-bezier как в CSS: (x1, y1, x2, y2). y больше 1 даёт перелёт, как у капли, которая не сразу успокаивается.
public sealed class BezierEase : IEasingFunction
{
    private readonly double _x1, _y1, _x2, _y2;

    public BezierEase(double x1, double y1, double x2, double y2)
    {
        _x1 = x1; _y1 = y1; _x2 = x2; _y2 = y2;
    }

    private static double B(double u, double p1, double p2)
    {
        double v = 1 - u;
        return 3 * v * v * u * p1 + 3 * v * u * u * p2 + u * u * u;
    }

    private static double D(double u, double p1, double p2)
    {
        double v = 1 - u;
        return 3 * v * v * p1 + 6 * v * u * (p2 - p1) + 3 * u * u * (1 - p2);
    }

    public double Ease(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        double u = t;
        for (int i = 0; i < 8; i++)
        {
            double x = B(u, _x1, _x2) - t;
            if (Math.Abs(x) < 1e-5) break;
            double d = D(u, _x1, _x2);
            if (Math.Abs(d) < 1e-6) break;
            u -= x / d;
        }
        return B(Math.Clamp(u, 0, 1), _y1, _y2);
    }
}

public partial class MainWindow
{
    // ============================================================
    //  ЖИДКИЕ АНИМАЦИИ
    //  1) Размер, ширина и радиус острова идут на мягких пружинах разной жёсткости, поэтому высота
    //     отстаёт от ширины и остров «вытекает» вниз.
    //  2) Желе: при каждом скачке размера остров растягивается и сплющивается с сохранением объёма и качается.
    //  3) Форма: у каждого угла свой радиус, а нижняя кромка вытягивается волной (настоящее изменение контура).
    //  4) Содержимое выливается с размытием и пружиной, элементы всплывают каплями по очереди.
    // ============================================================
    private readonly Spring _jx = new() { K = 230, D = 17 };   // растяжение по ширине
    private readonly Spring _jy = new() { K = 200, D = 16 };     // растяжение по высоте
    private readonly Spring _js = new() { K = 190, D = 15 };   // перекос, градусы
    private readonly Spring _wa = new() { K = 260, D = 18 };     // волна нижней кромки, левая половина
    private readonly Spring _wb = new() { K = 190, D = 15 };   // волна нижней кромки, правая половина (другая частота)
    private readonly ScaleTransform _jScale = new(1, 1);
    private readonly SkewTransform _jSkew = new(0, 0);
    private readonly Dictionary<UIElement, bool> _shown = new();
    private readonly Dictionary<FrameworkElement, int> _blurVer = new();

    private static readonly IEasingFunction Pour = new SpringEase(7.5, 9);
    private static readonly IEasingFunction Gush = new SpringEase(6.5, 10);
    private static readonly IEasingFunction Squish = new BezierEase(0.22, 1.2, 0.36, 1);
    private static readonly IEasingFunction Inhale = new PowerEase { EasingMode = EasingMode.EaseIn, Power = 3 };
    private static readonly IEasingFunction Drain = new CubicEase { EasingMode = EasingMode.EaseIn };

    private static readonly double[] LiquidScale = { 0.6, 1.0, 1.7 };
    // в режиме notch остров привязан к рамке экрана: растягиваться и качаться ему нельзя
    private double Lq => LiquidOn && !_cfg.NotchMode && _nk.P < 0.5 ? LiquidScale[Math.Clamp(_cfg.LiquidPower, 0, 2)] : 0;

    private void InitFluid()
    {
        // «желе» вставляется в начало цепочки трансформаций острова, остальные (скрытие по Ctrl+Alt+I) не трогаем
        Get(Pill);
        if (Pill.RenderTransform is TransformGroup g)
        {
            g.Children.Insert(0, _jScale);
            g.Children.Insert(1, _jSkew);
        }

        Pill.MouseEnter += (_, _) => { if (_mode == Mode.Mini) Kick(0.9, 1.4); };
        Pill.MouseLeave += (_, _) => { if (_mode == Mode.Mini) Kick(-0.7, -0.9); };
        // Нажатие вдавливает остров, отпускание выстреливает им обратно, но только когда клик по самому острову
        // (он раскроет или свернёт его). Если нажата кнопка, ползунок, плитка или чип, жидкость не трогаем:
        // иначе остров ёжится под курсором, кнопка уезжает из-под мыши и клик не засчитывается.
        Pill.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _ctlHold = OnControl(e.OriginalSource);
            if (!_ctlHold && ClickTogglesIsland) Kick(2.4, -2.8);
        };
        Pill.PreviewMouseLeftButtonUp += (_, _) =>
        {
            bool onControl = _ctlHold;
            _ctlHold = false;
            if (!onControl && ClickTogglesIsland) Kick(-3.0, 4.2);
        };
        ApplyFluidSettings();
    }

    // нажата левая кнопка мыши на «живом» элементе острова (кнопка, ползунок, плитка, чип)
    private bool _ctlHold;

    // true, пока элемент действительно зажат; если мышь отпустили где-то вне острова, флаг сам сбрасывается
    private bool CtlHeld
    {
        get
        {
            if (_ctlHold && Mouse.LeftButton != MouseButtonState.Pressed) _ctlHold = false;
            return _ctlHold;
        }
    }

    // те же условия, при которых Pill_Click раскрывает или сворачивает остров
    // шаг 70: раньше здесь не было таймера, микшера, шторки и переводчика. Pill_Click в них ничего не делает,
    // а остров всё равно «вдавливался» под курсором, и поле ввода или ползунок уезжали из-под мыши
    private bool ClickTogglesIsland => !(_pocketOpen || _actionsOpen || _devicesOpen || _timerOpen || _mixerOpen || _centerOpen || _trOpen);

    // поднимается от места нажатия к острову и ищет кнопку, ползунок перемотки, плитку или чип (у них курсор «рука»)
    private bool OnControl(object? src)
    {
        var d = src as DependencyObject;
        while (d != null && !ReferenceEquals(d, Pill))
        {
            if (d is ButtonBase || d is ScrollBar || ReferenceEquals(d, SliderHost)) return true;
            if (d is FrameworkElement fe && ReferenceEquals(fe.Cursor, Cursors.Hand)) return true;
            d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    // мягкие пружины размера: чем меньше жёсткость и демпфирование, тем жиже остров
    private void ApplyFluidSettings()
    {
        if (LiquidOn)
        {
            _w.K = 155; _w.D = 13.5;
            _h.K = 125; _h.D = 12.5;
            _r.K = 140; _r.D = 13;
        }
        else
        {
            _w.K = _h.K = _r.K = 170;
            _w.D = _h.D = _r.D = 13;
        }
    }

    private void Kick(double x, double y)
    {
        double k = Lq;
        if (k <= 0 || CtlHeld) return;
        _jx.V += x * k;
        _jy.V += y * k;
        EnsureLoop();
    }

    // вызывается при смене целевого размера: чем больше скачок, тем сильнее встряска и волна по нижней кромке
    private void FluidKick(double dw, double dh)
    {
        double k = Lq;
        if (k <= 0 || CtlHeld || (Math.Abs(dw) < 1 && Math.Abs(dh) < 1)) return;
        _jy.V += Math.Clamp(dh * 0.0300 * k, -5, 5);
        _jx.V += Math.Clamp(dw * 0.0260 * k, -5, 5);
        double sgn = Math.Sign(dw + dh * 0.5);
        _js.V += sgn * Math.Min(130, 24 + (Math.Abs(dw) + Math.Abs(dh)) * 0.25) * k;
        double mag = Math.Min(1, (Math.Abs(dw) + Math.Abs(dh)) / 180.0);
        _wa.V += mag * 9 * k * (dh >= 0 ? 1 : 0.6);
        _wb.V += mag * 7 * k * (dw >= 0 ? 1 : 0.7);
    }

    private static bool Calm(Spring s, double pos, double vel) => Math.Abs(s.P) < pos && Math.Abs(s.V) < vel;

    // шаг «желе», возвращает true, пока что-то ещё качается
    private bool FluidStep(double dt)
    {
        _jx.Step(dt); _jy.Step(dt); _js.Step(dt); _wa.Step(dt); _wb.Step(dt);
        if (CtlHeld)
        {
            // кнопка зажата: остаточное качание быстро гасится, чтобы она не уезжала из-под курсора
            double f = Math.Exp(-26 * dt);
            _jx.P *= f; _jx.V *= f; _jy.P *= f; _jy.V *= f; _js.P *= f; _js.V *= f;
            _wa.P *= f; _wa.V *= f; _wb.P *= f; _wb.V *= f;
        }
        if (Calm(_jx, 0.0006, 0.02) && Calm(_jy, 0.0006, 0.02) && Calm(_js, 0.02, 0.4)
            && Calm(_wa, 0.002, 0.05) && Calm(_wb, 0.002, 0.05))
        {
            _jx.P = _jx.V = _jy.P = _jy.V = _js.P = _js.V = 0;
            _wa.P = _wa.V = _wb.P = _wb.V = 0;
            _jScale.ScaleX = _jScale.ScaleY = 1;
            _jSkew.AngleX = 0;
            return false;
        }
        double x = Math.Clamp(_jx.P, -0.28, 0.28), y = Math.Clamp(_jy.P, -0.28, 0.28);
        // объём почти сохраняется: вытянулся по высоте, чуть сузился по ширине
        _jScale.ScaleX = 1 + x - 0.6 * y;
        _jScale.ScaleY = 1 + y - 0.6 * x;
        _jSkew.AngleX = Math.Clamp(_js.P, -8, 8);
        // трансформации считаются от центра острова: центр по Y сдвинут вверх, верх остаётся у края экрана, тянется низ
        double cy = -Pill.ActualHeight / 2;
        _jScale.CenterX = 0; _jScale.CenterY = cy;
        _jSkew.CenterX = 0; _jSkew.CenterY = cy;
        return true;
    }

    // каждый угол со своим радиусом: низ округляется и отстаёт, верх подтягивается, перекос делает форму живой
    private CornerRadius FluidCorners(double r, double h)
    {
        double cap = Math.Max(3, h / 2);
        double y = Math.Clamp(_jy.P, -0.28, 0.28), x = Math.Clamp(_jx.P, -0.28, 0.28);
        double top = Math.Clamp(r * (1 - 3 * y), 3, cap);
        double bot = Math.Clamp(r * (1 + 6 * y + 3 * Math.Abs(x)), 3, cap);
        double sk = Math.Clamp(_js.P / 8, -1, 1) * 0.5;
        return new CornerRadius(
            Math.Clamp(top * (1 + sk), 3, cap), Math.Clamp(top * (1 - sk), 3, cap),
            Math.Clamp(bot * (1 - sk), 3, cap), Math.Clamp(bot * (1 + sk), 3, cap));
    }

    // применяется на каждом кадре из Tick, пока остров качается
    private void FluidApply(double r)
    {
        double w = Pill.Width, h = Pill.Height;
        var cr = NotchCr(FluidCorners(r, h));
        Pill.CornerRadius = cr;
        // волны только внутрь, наружу остров выпирает за счёт растяжения (_jScale)
        double a1 = Math.Clamp(_wa.P, 0, 1) * h * 0.5, a2 = Math.Clamp(_wb.P, 0, 1) * h * 0.5;
        a1 = Math.Min(a1, h * 0.32); a2 = Math.Min(a2, h * 0.32);
        // настоящее изменение контура: углы сглаженные (Smooth.cs), нижняя кромка идёт S-образной волной
        Pill.Clip = a1 > 0.3 || a2 > 0.3 ? Smooth.Shape(w, h, cr, a1, a2, 0, 0, _earNow) : null;
        PillContent.Clip = _pillClip.Get(w, h, cr, 0, 0, -_earNow, 0, _earNow);   // содержимое идёт за размером на каждом кадре
    }

    // В покое содержимое острова обрезается по тому же сглаженному контуру, что и фон (а не по прямоугольнику):
    // обложки, гифка и прочее у края не выходят за мягкие углы. Обрезается именно содержимое, а не сам остров:
    // Clip на острове срезал бы и его свечение и тень (темы «Свечение», «Стекло»).
    private readonly Smooth.Cache _pillClip = new();

    private void FluidRest(double r, double h)
    {
        var cr = NotchCr(new CornerRadius(Math.Clamp(r, 0, h / 2)));
        Pill.CornerRadius = cr;
        Pill.Clip = null;
        // содержимое сдвинуто внутрь на ширину уха (Padding), поэтому и контур сдвинут влево на столько же
        PillContent.Clip = _pillClip.Get(Pill.Width, h, cr, 0, 0, -_earNow, 0, _earNow);
    }

    // размытие на время перетекания слоя: «жидкий» переход вместо простого появления
    private void BlurPulse(FrameworkElement e, double from, double to, int ms)
    {
        if (e.Effect != null && e.Effect is not BlurEffect) return;
        var b = e.Effect as BlurEffect ?? new BlurEffect { Radius = from, RenderingBias = RenderingBias.Performance };
        int ver = (_blurVer.TryGetValue(e, out var v) ? v : 0) + 1;
        _blurVer[e] = ver;
        e.Effect = b;
        var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        a.Completed += (_, _) =>
        {
            if (_blurVer.TryGetValue(e, out var cur) && cur == ver && ReferenceEquals(e.Effect, b))
            {
                b.BeginAnimation(BlurEffect.RadiusProperty, null);
                e.Effect = null;
            }
        };
        b.BeginAnimation(BlurEffect.RadiusProperty, a);
    }

    // Показать или спрятать слой острова.
    // Показ: слой «выливается» с пружиной, по ширине и высоте с разной скоростью, из размытия, прозрачность догоняет чуть позже.
    // Возвращает true, если состояние слоя действительно изменилось.
    private bool Reveal(FrameworkElement e, bool on, double dy = -14)
    {
        bool was = _shown.TryGetValue(e, out var w) ? w : e.Opacity > 0.5;
        _shown[e] = on;
        if (!LiquidOn)
        {
            // шаг 52: слой мог спрятаться ещё «жидко» (съехал вниз и сжался), а показаться уже без жидкости
            // (экономный режим включился сам или включили notch): тогда сдвиг оставался, и текст «проваливался» вниз
            if (on) ResetTx(e);
            FadeTo(e, on);
            return was != on;
        }
        if (was == on) return false;

        double k = Math.Max(0.5, Lq);
        var t = Get(e);
        if (on)
        {
            double fx = 1 - 0.28 * k, fy = 1 - 0.40 * k, y0 = dy * k;
            t.S.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            t.S.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            t.T.BeginAnimation(TranslateTransform.YProperty, null);
            t.S.ScaleX = fx; t.S.ScaleY = fy; t.T.Y = y0;
            t.S.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(fx, 1, TimeSpan.FromMilliseconds(560)) { EasingFunction = Pour });
            t.S.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(fy, 1, TimeSpan.FromMilliseconds(680)) { EasingFunction = Gush });
            t.T.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(y0, 0, TimeSpan.FromMilliseconds(640)) { EasingFunction = Squish });
            e.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(380)) { EasingFunction = Inhale });
            BlurPulse(e, 9 * k, 0, 420);
        }
        else
        {
            t.S.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1 - 0.08 * k, TimeSpan.FromMilliseconds(170)) { EasingFunction = Drain });
            t.S.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1 - 0.14 * k, TimeSpan.FromMilliseconds(170)) { EasingFunction = Drain });
            t.T.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-dy * 0.35 * k, TimeSpan.FromMilliseconds(170)) { EasingFunction = Drain });
            e.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(130)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            BlurPulse(e, 0, 7 * k, 150);
        }
        return true;
    }

    // слой на своё место: без сдвига, сжатия и размытия
    private void ResetTx(FrameworkElement e)
    {
        var t = Get(e);
        t.S.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        t.S.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        t.T.BeginAnimation(TranslateTransform.YProperty, null);
        t.S.ScaleX = 1; t.S.ScaleY = 1; t.T.Y = 0;
        if (e.Effect is BlurEffect b) { b.BeginAnimation(BlurEffect.RadiusProperty, null); e.Effect = null; }
    }

    // убираем из словаря трансформаций элементы, которых уже нет в дереве (пересобранные списки кармана и устройств)
    private void PruneTx()
    {
        List<FrameworkElement>? dead = null;
        foreach (var kv in _tx)
            if (kv.Key.Parent == null && kv.Key.TemplatedParent == null && !ReferenceEquals(kv.Key, Pill))
                (dead ??= new()).Add(kv.Key);
        if (dead != null) foreach (var d in dead) _tx.Remove(d);
    }

    // элементы всплывают каплями по очереди: каждый из маленького, с пружиной и своей задержкой
    private void Droplets(IEnumerable<FrameworkElement> items, int delay0 = 60, int step = 36, double from = 0.4, double dy = 14)
    {
        double k = Lq;
        if (k <= 0) return;
        PruneTx();
        from = 1 - (1 - from) * Math.Min(1.3, k);
        dy *= k;
        int i = 0;
        foreach (var it in items.ToList())
        {
            var t = Get(it);
            t.S.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            t.S.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            t.T.BeginAnimation(TranslateTransform.YProperty, null);
            t.S.ScaleX = from; t.S.ScaleY = from * 0.92; t.T.Y = dy;
            var delay = TimeSpan.FromMilliseconds(delay0 + Math.Min(i, 10) * step);
            t.S.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(from, 1, TimeSpan.FromMilliseconds(580)) { BeginTime = delay, EasingFunction = Gush });
            t.S.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(from * 0.92, 1, TimeSpan.FromMilliseconds(660)) { BeginTime = delay, EasingFunction = Pour });
            t.T.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(dy, 0, TimeSpan.FromMilliseconds(620)) { BeginTime = delay, EasingFunction = Squish });
            i++;
        }
    }

    private IEnumerable<FrameworkElement> FullItems() => new FrameworkElement[]
        { CoverBig, TitleText, ArtistText, FullEq, StarBtn, ExpandBtn, SliderHost, PrevBtn, PlayBtn, NextBtn };

    private IEnumerable<FrameworkElement> PocketItems() =>
        new FrameworkElement[] { TabFiles, TabClips, TabNotes }.Concat(PocketList.Children.OfType<FrameworkElement>());

    private IEnumerable<FrameworkElement> ActionItems() => ActionsGrid.Children.OfType<FrameworkElement>();

    private IEnumerable<FrameworkElement> DeviceItems() =>
        DevOut.Children.OfType<FrameworkElement>().Concat(DevIn.Children.OfType<FrameworkElement>());
}
