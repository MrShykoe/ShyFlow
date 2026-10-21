using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Island;

// Сглаженные углы (corner smoothing, «суперэллипс», как у иконок iOS и скруглений в Figma).
// Обычное скругление это дуга окружности: прямая сторона резко переходит в кривую, и на глаз виден «излом».
// Здесь кривизна нарастает плавно: сначала две безье-кривые, посередине короткая дуга, потом снова безье.
// Формулы из статьи Figma про corner smoothing. Для капсулы (радиус равен половине высоты) форма остаётся кругом.
internal static class Smooth
{
    // 0 это обычная дуга, 1 самое мягкое. 0.6 как пресет «iOS» в Figma; с шага 39 мягче: 0.8.
    // Меняется в одном месте для всей программы.
    public const double Amount = 0.8;

    // параметры одного угла (см. рисунок 11.1 в статье Figma)
    internal struct Corner
    {
        public double R, A, B, C, D, P, Arc;
    }

    private static double Rad(double deg) => deg * Math.PI / 180;

    // доля стороны, которую угол может занять с учётом соседнего угла на той же стороне
    private static double Share(double r, double side, double adj) =>
        r + adj <= 0 ? double.MaxValue : r / (r + adj) * side;

    private static double Budget(double r, double sideA, double adjA, double sideB, double adjB) =>
        Math.Min(Share(r, sideA, adjA), Share(r, sideB, adjB));

    internal static Corner Params(double r, double s, double budget)
    {
        var k = new Corner();
        if (r <= 0.01 || budget <= 0.01) return k;
        r = Math.Min(r, budget);
        s = Math.Clamp(s, 0, 1);
        double p = (1 + s) * r;
        // мало места: сначала уменьшаем сглаживание, дуга остаётся
        s = Math.Min(s, Math.Max(0, budget / r - 1));
        p = Math.Min(p, budget);

        double arcMeasure = 90 * (1 - s);
        double arc = Math.Sin(Rad(arcMeasure / 2)) * r * Math.Sqrt(2);
        double alpha = (90 - arcMeasure) / 2;
        double p3p4 = r * Math.Tan(Rad(alpha / 2));
        double beta = 45 * s;
        double c = p3p4 * Math.Cos(Rad(beta));
        double d = c * Math.Tan(Rad(beta));
        double b = Math.Max(0, (p - arc - c - d) / 3);

        k.R = r; k.A = 2 * b; k.B = b; k.C = c; k.D = d; k.P = p; k.Arc = arc;
        return k;
    }

    // Рисует один сглаженный угол из точки (x, y), двигаясь в направлении (ux, uy); поворот на 90° по часовой стрелке (vx, vy),
    // а с left = true против часовой (вогнутые «уши» выреза в режиме notch). После вызова (x, y) это конец угла.
    // mirror зеркалит по X вокруг pivot.
    internal static void DrawCorner(StreamGeometryContext c, ref double x, ref double y, double ux, double uy, Corner k,
        bool mirror = false, double pivot = 0, bool left = false)
    {
        if (k.P <= 0.01) return;
        double vx = left ? uy : -uy, vy = left ? -ux : ux;
        // дуга идёт по часовой, если поворот вправо; зеркало по X и поворот влево её разворачивают
        var sweep = mirror ^ left ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;
        double x0 = x, y0 = y;
        Point At(double du, double dv)
        {
            double px = x0 + du * ux + dv * vx, py = y0 + du * uy + dv * vy;
            return new Point(mirror ? pivot - px : px, py);
        }
        double a = k.A, b = k.B, cc = k.C, d = k.D, L = k.Arc;
        // 1) кривизна нарастает
        c.BezierTo(At(a, 0), At(a + b, 0), At(a + b + cc, d), true, false);
        // 2) дуга окружности
        double eu = a + b + cc + L, ev = d + L;
        c.ArcTo(At(eu, ev), new Size(k.R, k.R), 0, false, sweep, true, false);
        // 3) кривизна спадает
        c.BezierTo(At(eu + d, ev + cc), At(eu + d, ev + b + cc), At(eu + d, ev + a + b + cc), true, false);
        double tu = eu + d, tv = ev + a + b + cc;
        x = x0 + tu * ux + tv * vx;
        y = y0 + tu * uy + tv * vy;
    }

    // Контур прямоугольника w на h со сглаженными углами. waveA и waveB поднимают нижний край волной (жидкий остров).
    // ox, oy сдвигают контур (нужно для рамки).
    // ear > 0: режим notch. Верх фигуры лежит на краю экрана по всей ширине w, а по бокам вогнутые «уши» размером ear
    // плавно стекают в бока острова (он занимает середину шириной w - 2 * ear). Это одна фигура: остров и уши без шва,
    // одной заливкой и одной рамкой. openTop: контур без верхнего края (для рамки: по краю экрана линия не нужна).
    public static Geometry Shape(double w, double h, CornerRadius cr, double waveA = 0, double waveB = 0, double ox = 0, double oy = 0,
        double ear = 0, bool openTop = false)
    {
        if (w <= 0.5 || h <= 0.5) return Geometry.Empty;
        ear = Math.Clamp(ear, 0, Math.Min(w / 4, h * 0.75));
        bool ears = ear > 0.05;
        double bw = w - 2 * (ears ? ear : 0), bx = ox + (ears ? ear : 0);   // сам остров между ушами
        double lim = Math.Min(bw, h) / 2;
        double tl = ears ? 0 : Math.Clamp(cr.TopLeft, 0, lim), tr = ears ? 0 : Math.Clamp(cr.TopRight, 0, lim);
        double br = Math.Clamp(cr.BottomRight, 0, lim), bl = Math.Clamp(cr.BottomLeft, 0, lim);
        var ktl = Params(tl, Amount, Budget(tl, bw, tr, h, bl));
        var ktr = Params(tr, Amount, Budget(tr, bw, tl, h, br));
        var kbr = Params(br, Amount, Budget(br, h, tr, bw, bl));
        var kbl = Params(bl, Amount, Budget(bl, bw, br, h, tl));
        // ухо: тот же сглаженный угол, только поворачивает в другую сторону; его размер (P) ear, но не больше,
        // чем остаётся на боку над нижним углом (иначе ухо залезает на нижний угол и контур ломается, шаг 60).
        // Если ухо меньше ear, остаток поля уха это просто край экрана.
        double pe = ears ? Math.Min(ear, Math.Max(0, h - Math.Max(kbl.P, kbr.P))) : 0;
        var ke = ears ? Params(pe / (1 + Amount), Amount, pe * 4) : default;

        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            double x, y;
            if (ears)
            {
                // от кончика правого уха по часовой: правое ухо, правый бок, низ, левый бок, левое ухо, край экрана
                x = bx + bw + pe; y = oy;
                c.BeginFigure(new Point(x, y), !openTop, !openTop);
                DrawCorner(c, ref x, ref y, -1, 0, ke, left: true);
            }
            else
            {
                x = bx + bw - ktr.P; y = oy;
                c.BeginFigure(new Point(x, y), true, true);
                DrawCorner(c, ref x, ref y, 1, 0, ktr);
            }

            c.LineTo(new Point(bx + bw, oy + h - kbr.P), true, false);
            x = bx + bw; y = oy + h - kbr.P;
            DrawCorner(c, ref x, ref y, 0, 1, kbr);

            double x1 = bx + bw - kbr.P, x2 = bx + kbl.P;
            if (waveA > 0.3 || waveB > 0.3)
            {
                double span = Math.Max(1, x1 - x2);
                c.BezierTo(new Point(x1 - span * 0.33, oy + h - waveA), new Point(x2 + span * 0.33, oy + h - waveB),
                    new Point(x2, oy + h), true, false);
            }
            else c.LineTo(new Point(x2, oy + h), true, false);
            x = x2; y = oy + h;
            DrawCorner(c, ref x, ref y, -1, 0, kbl);

            if (ears)
            {
                c.LineTo(new Point(bx, oy + pe), true, false);
                x = bx; y = oy + pe;
                DrawCorner(c, ref x, ref y, 0, -1, ke, left: true);   // заканчивается в (bx - pe, oy): дальше край экрана
            }
            else
            {
                c.LineTo(new Point(bx, oy + ktl.P), true, false);
                x = bx; y = oy + ktl.P;
                DrawCorner(c, ref x, ref y, 0, -1, ktl);
            }
        }
        g.Freeze();
        return g;
    }

    // одна и та же форма запрашивается на каждом кадре (пружины, свечение): помним последнюю, пока размеры не меняются
    internal sealed class Cache
    {
        private Geometry? _g;
        private (double, double, CornerRadius, double, double, double, double, double, bool) _key;

        public Geometry Get(double w, double h, CornerRadius cr, double waveA = 0, double waveB = 0, double ox = 0, double oy = 0,
            double ear = 0, bool openTop = false)
        {
            var key = (Math.Round(w, 2), Math.Round(h, 2), cr, Math.Round(waveA, 2), Math.Round(waveB, 2), ox, oy, Math.Round(ear, 2), openTop);
            if (_g != null && key.Equals(_key)) return _g;
            _key = key;
            return _g = Shape(w, h, cr, waveA, waveB, ox, oy, ear, openTop);
        }
    }
}

// Border со сглаженными углами: рисует фон и рамку по контуру из Smooth.Shape.
// Всё остальное (Child, Padding, Background, CornerRadius и т.д.) работает как у обычного Border.
// Если углов нет или рамка с разной толщиной по сторонам, рисуется как обычный Border.
public class SBorder : Border
{
    // уши выреза (режим notch): элемент шире самого острова на 2 * Ears, уши рисуются в этих полях (см. Smooth.Shape)
    public static readonly DependencyProperty EarsProperty = DependencyProperty.Register(
        nameof(Ears), typeof(double), typeof(SBorder), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Ears
    {
        get => (double)GetValue(EarsProperty);
        set => SetValue(EarsProperty, value);
    }

    private readonly Smooth.Cache _bgShape = new(), _bdShape = new();

    private static CornerRadius Shrink(CornerRadius c, double d) =>
        new(Math.Max(0, c.TopLeft - d), Math.Max(0, c.TopRight - d), Math.Max(0, c.BottomRight - d), Math.Max(0, c.BottomLeft - d));

    protected override void OnRender(DrawingContext dc)
    {
        var cr = CornerRadius;
        var bt = BorderThickness;
        double ears = Ears;
        bool round = cr.TopLeft > 0 || cr.TopRight > 0 || cr.BottomRight > 0 || cr.BottomLeft > 0 || ears > 0.05;
        bool uniform = bt.Left == bt.Top && bt.Top == bt.Right && bt.Right == bt.Bottom;
        double w = ActualWidth, h = ActualHeight;
        if (!round || !uniform || w <= 0 || h <= 0)
        {
            base.OnRender(dc);
            return;
        }
        double t = bt.Left;
        if (ears > 0.05)
        {
            // вырез: фон до самого края экрана, рамка без верхней линии (по краю экрана она не нужна)
            if (Background != null) dc.DrawGeometry(Background, null, _bgShape.Get(w, h, cr, 0, 0, 0, 0, ears));
            if (t > 0 && BorderBrush != null && w > t && h > t)
                dc.DrawGeometry(null, new Pen(BorderBrush, t), _bdShape.Get(w - t, h - t / 2, Shrink(cr, t / 2), 0, 0, t / 2, 0, Math.Max(0, ears - t / 2), true));
            return;
        }
        if (Background != null && w > 2 * t && h > 2 * t)
        {
            // фон только внутри рамки, как у обычного Border
            dc.DrawGeometry(Background, null, _bgShape.Get(w - 2 * t, h - 2 * t, Shrink(cr, t), 0, 0, t, t));
        }
        if (t > 0 && BorderBrush != null && w > t && h > t)
        {
            // рамка: линия по середине полосы толщиной t
            dc.DrawGeometry(null, new Pen(BorderBrush, t), _bdShape.Get(w - t, h - t, Shrink(cr, t / 2), 0, 0, t / 2, t / 2));
        }
    }
}
