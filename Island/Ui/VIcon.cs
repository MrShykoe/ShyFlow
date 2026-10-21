using System.Windows.Documents;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Island;

// Векторная иконка (набор Lucide, ISC) с морфингом: при смене Text контур плавно перетекает
// в новую иконку на пружине (play в pause, звук в mute). Заменяет глифы Segoe MDL2 Assets.
// Text принимает как старый код глифа (\uE102), так и имя иконки ("play").
// Размер и цвет берутся как у текста: FontSize и Foreground (наследуются, как у TextBlock).
public sealed class VIcon : FrameworkElement
{
    // ---------- старые глифы MDL2 в имена иконок ----------
    private static readonly Dictionary<string, string> Map = new()
    {
        ["\uE102"] = "play", ["\uE103"] = "pause", ["\uE100"] = "skip-back", ["\uE101"] = "skip-forward",
        ["\uE767"] = "volume-2", ["\uE74F"] = "volume-x", ["\uE73E"] = "check", ["\uE8C8"] = "copy",
        ["\uE916"] = "mic-vocal", ["\uE720"] = "mic", ["\uE722"] = "camera", ["\uE701"] = "wifi",
        ["\uE839"] = "network", ["\uE702"] = "bluetooth", ["\uE714"] = "video", ["\uE896"] = "download",
        ["\uE8A5"] = "file", ["\uE706"] = "sun", ["\uEB9F"] = "image", ["\uE8D6"] = "music",
        ["\uE768"] = "circle-play", ["\uE7E7"] = "bell", ["\uE713"] = "settings",
        ["\uE70E"] = "chevron-up", ["\uE70D"] = "chevron-down", ["\uE721"] = "search", ["\uE711"] = "x",
        ["\uE740"] = "maximize-2",
    };

    // закрашенные иконки (кнопки плеера): контур и заливка одним цветом
    private static readonly HashSet<string> Solid = new() { "play", "pause", "skip-back", "skip-forward" };

    private const int N = 128;          // точек в одном контуре при морфинге
    private const double K = 1.15;      // размер иконки относительно FontSize

    private sealed class Fig
    {
        public Point[] P = Array.Empty<Point>();
        public bool Closed;
    }

    private static string Resolve(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (Map.TryGetValue(s, out var n)) return n;
        return IconData.Paths.ContainsKey(s) ? s : "";
    }

    // ---------- свойства ----------
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(VIcon), new PropertyMetadata("", OnTextChanged));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(
        typeof(VIcon), new FrameworkPropertyMetadata(16.0,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(VIcon), new FrameworkPropertyMetadata(Brushes.White,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    // толщина линии: 1 = стандарт (2 на сетке 24)
    public static readonly DependencyProperty WeightProperty = DependencyProperty.Register(
        nameof(Weight), typeof(double), typeof(VIcon),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    // ход морфинга 0..1 (с перелётом на пружине)
    private static readonly DependencyProperty MorphProperty = DependencyProperty.Register(
        "Morph", typeof(double), typeof(VIcon),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double Weight { get => (double)GetValue(WeightProperty); set => SetValue(WeightProperty, value); }

    // ---------- состояние ----------
    private string _name = "";
    private List<Fig> _from = new(), _to = new();
    private double _fillFrom, _fillTo;
    private bool _morphing;

    private static readonly Dictionary<string, List<Fig>> SampleCache = new();
    private static readonly Dictionary<string, Geometry> ExactCache = new();

    public VIcon()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((VIcon)d).SetIcon(Resolve(e.NewValue as string));

    private void SetIcon(string name)
    {
        if (name == _name) return;
        bool animate = IsLoaded && (_name != "" || name != "");
        var oldFigs = CurrentFigs();
        double oldFill = CurrentFill();
        _name = name;
        var newFigs = name == "" ? new List<Fig>() : Sampled(name);
        double newFill = name != "" && Solid.Contains(name) ? 1 : 0;

        if (!animate || (oldFigs.Count == 0 && newFigs.Count == 0))
        {
            BeginAnimation(MorphProperty, null);
            _morphing = false;
            _from = _to = newFigs;
            _fillFrom = _fillTo = newFill;
            SetValue(MorphProperty, 1.0);
            InvalidateVisual();
            return;
        }

        _from = oldFigs;
        _to = newFigs;
        _fillFrom = oldFill;
        _fillTo = newFill;
        _morphing = true;
        BuildPairs();
        SetValue(MorphProperty, 1.0);
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600)) { EasingFunction = new SpringEase(6.5, 10) };
        anim.Completed += (_, _) =>
        {
            if (!_morphing) return;
            // Completed от старой анимации может прийти после запуска новой: проверяем по ходу
            if (Math.Abs((double)GetValue(MorphProperty) - 1.0) > 1e-6) return;
            _morphing = false;
            _from = _to;
            _fillFrom = _fillTo;
            InvalidateVisual();
        };
        BeginAnimation(MorphProperty, anim);
    }

    // ---------- выборка контуров ----------
    private static List<Fig> Sampled(string name)
    {
        if (SampleCache.TryGetValue(name, out var c)) return c;
        var list = new List<Fig>();
        foreach (var d in IconData.Paths[name])
        {
            try
            {
                var pg = PathGeometry.CreateFromGeometry(Geometry.Parse(d)).GetFlattenedPathGeometry(0.04, ToleranceType.Absolute);
                foreach (var f in pg.Figures)
                {
                    var pts = new List<Point> { f.StartPoint };
                    foreach (var seg in f.Segments)
                    {
                        if (seg is PolyLineSegment pl) pts.AddRange(pl.Points);
                        else if (seg is LineSegment ls) pts.Add(ls.Point);
                    }
                    if (f.IsClosed && pts.Count > 1 && (pts[^1] - pts[0]).Length > 1e-6) pts.Add(pts[0]);
                    var clean = new List<Point> { pts[0] };
                    for (int i = 1; i < pts.Count; i++)
                        if ((pts[i] - clean[^1]).Length > 1e-6) clean.Add(pts[i]);
                    bool closed = f.IsClosed && clean.Count > 2 && (clean[^1] - clean[0]).Length < 1e-6;
                    if (closed) clean.RemoveAt(clean.Count - 1);
                    list.Add(new Fig { P = Resample(clean, closed), Closed = closed });
                }
            }
            catch { }
        }
        // порядок слева направо, сверху вниз: так фигуры двух иконок сопоставляются естественнее
        list.Sort((a, b) =>
        {
            var ca = Centroid(a.P); var cb = Centroid(b.P);
            int r = Math.Round(ca.X, 1).CompareTo(Math.Round(cb.X, 1));
            return r != 0 ? r : ca.Y.CompareTo(cb.Y);
        });
        SampleCache[name] = list;
        return list;
    }

    private static Point Centroid(Point[] p)
    {
        double x = 0, y = 0;
        foreach (var q in p) { x += q.X; y += q.Y; }
        return new Point(x / Math.Max(1, p.Length), y / Math.Max(1, p.Length));
    }

    // N точек по контуру; углы (вершины) сохраняются, если их не больше N
    private static Point[] Resample(List<Point> v, bool closed)
    {
        var pts = new List<Point>(v);
        if (closed) pts.Add(pts[0]);
        int m = pts.Count - 1;
        var res = new List<Point>(N + 1);
        if (m < 1)
        {
            for (int i = 0; i < N; i++) res.Add(pts[0]);
            return res.ToArray();
        }
        var len = new double[m];
        double total = 0;
        for (int i = 0; i < m; i++) { len[i] = (pts[i + 1] - pts[i]).Length; total += len[i]; }
        const int need = N;
        if (total < 1e-9)
        {
            for (int i = 0; i < N; i++) res.Add(pts[0]);
            return res.ToArray();
        }

        if (m + 1 <= need + (closed ? 1 : 0))
        {
            // углы остаются: вершин m+1 (у замкнутого последняя совпадает с первой), остальное раздаём по длине
            int keep = closed ? m : m + 1;
            int extra = need - keep;
            var add = new int[m];
            var rem = new (double r, int i)[m];
            int used = 0;
            for (int i = 0; i < m; i++)
            {
                double ideal = len[i] / total * extra;
                add[i] = (int)Math.Floor(ideal);
                used += add[i];
                rem[i] = (ideal - add[i], i);
            }
            Array.Sort(rem, (a, b) => b.r.CompareTo(a.r));
            for (int k = 0; k < extra - used && k < m; k++) add[rem[k].i]++;
            for (int i = 0; i < m; i++)
            {
                res.Add(pts[i]);
                for (int j = 1; j <= add[i]; j++)
                {
                    double t = j / (double)(add[i] + 1);
                    res.Add(new Point(pts[i].X + (pts[i + 1].X - pts[i].X) * t, pts[i].Y + (pts[i + 1].Y - pts[i].Y) * t));
                }
            }
            if (!closed) res.Add(pts[m]);
        }
        else
        {
            // слишком много вершин (круглая форма): равномерно по длине
            const int cnt = N;
            double step = total / (closed ? cnt : cnt - 1);
            int seg = 0; double acc = 0;
            for (int k = 0; k < cnt; k++)
            {
                double dist = k * step;
                while (seg < m - 1 && acc + len[seg] < dist) { acc += len[seg]; seg++; }
                double t = len[seg] < 1e-12 ? 0 : Math.Clamp((dist - acc) / len[seg], 0, 1);
                res.Add(new Point(pts[seg].X + (pts[seg + 1].X - pts[seg].X) * t, pts[seg].Y + (pts[seg + 1].Y - pts[seg].Y) * t));
            }
        }
        while (res.Count > N) res.RemoveAt(res.Count - 1);
        while (res.Count < N) res.Add(res[^1]);
        return res.ToArray();
    }

    // ---------- пары контуров для морфинга ----------
    private sealed class Pair { public Point[] A = Array.Empty<Point>(), B = Array.Empty<Point>(); public bool CA, CB; }
    private List<Pair> _pairs = new();

    private void BuildPairs()
    {
        _pairs = new List<Pair>();
        int cnt = Math.Max(_from.Count, _to.Count);
        for (int i = 0; i < cnt; i++)
        {
            Fig? a = i < _from.Count ? _from[i] : null;
            Fig? b = i < _to.Count ? _to[i] : null;
            if (a == null && b == null) continue;
            if (a == null) a = new Fig { P = Enumerable.Repeat(Centroid(b!.P), N).ToArray(), Closed = b!.Closed };
            if (b == null) b = new Fig { P = Enumerable.Repeat(Centroid(a.P), N).ToArray(), Closed = a.Closed };
            _pairs.Add(Align(a, b));
        }
    }

    private static double Cost(Point[] a, Point[] b, int shift, bool rev)
    {
        double s = 0;
        for (int i = 0; i < N; i++)
        {
            int j = rev ? ((shift - i) % N + N) % N : (shift + i) % N;
            var d = a[i] - b[j];
            s += d.X * d.X + d.Y * d.Y;
        }
        return s;
    }

    private static Pair Align(Fig a, Fig b)
    {
        var p = new Pair { A = a.P, B = b.P, CA = a.Closed, CB = b.Closed };
        if (a.Closed && b.Closed)
        {
            double best = double.MaxValue; int bs = 0; bool br = false;
            for (int rev = 0; rev < 2; rev++)
                for (int s = 0; s < N; s++)
                {
                    double c = Cost(a.P, b.P, s, rev == 1);
                    if (c < best) { best = c; bs = s; br = rev == 1; }
                }
            p.B = Reorder(b.P, bs, br);
        }
        else if (a.Closed != b.Closed)
        {
            // сдвигаем замкнутый, чтобы его начало легло ближе к открытому
            var closedFig = a.Closed ? a.P : b.P;
            var openFig = a.Closed ? b.P : a.P;
            double best = double.MaxValue; int bs = 0;
            for (int s = 0; s < N; s++)
            {
                double c = Cost(openFig, closedFig, s, false);
                if (c < best) { best = c; bs = s; }
            }
            var shifted = Reorder(closedFig, bs, false);
            if (a.Closed) p.A = shifted; else p.B = shifted;
        }
        else
        {
            // оба открытые: прямо или в обратную сторону
            double f = Cost(a.P, b.P, 0, false);
            double r = 0;
            for (int i = 0; i < N; i++) { var d = a.P[i] - b.P[N - 1 - i]; r += d.X * d.X + d.Y * d.Y; }
            if (r < f) { var rv = (Point[])b.P.Clone(); Array.Reverse(rv); p.B = rv; }
        }
        return p;
    }

    private static Point[] Reorder(Point[] src, int shift, bool rev)
    {
        var r = new Point[N];
        for (int i = 0; i < N; i++)
        {
            int j = rev ? ((shift - i) % N + N) % N : (shift + i) % N;
            r[i] = src[j];
        }
        return r;
    }

    // ---------- текущий кадр ----------
    private List<Fig> CurrentFigs()
    {
        if (!_morphing) return _to;
        double t = (double)GetValue(MorphProperty);
        var res = new List<Fig>();
        foreach (var p in _pairs)
        {
            var pts = new Point[N];
            for (int i = 0; i < N; i++)
                pts[i] = new Point(p.A[i].X + (p.B[i].X - p.A[i].X) * t, p.A[i].Y + (p.B[i].Y - p.A[i].Y) * t);
            res.Add(new Fig { P = pts, Closed = t < 0.5 ? p.CA : p.CB });
        }
        return res;
    }

    private double CurrentFill()
    {
        if (!_morphing) return _fillTo;
        double t = Math.Clamp((double)GetValue(MorphProperty), 0, 1);
        return _fillFrom + (_fillTo - _fillFrom) * t;
    }

    // ---------- вёрстка и рисование ----------
    private double Px => Math.Max(1, FontSize * K);

    protected override Size MeasureOverride(Size availableSize) => new Size(Px, Px);

    private static Geometry Exact(string name)
    {
        if (ExactCache.TryGetValue(name, out var g)) return g;
        var grp = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var d in IconData.Paths[name])
        {
            try { grp.Children.Add(Geometry.Parse(d)); } catch { }
        }
        grp.Freeze();
        ExactCache[name] = grp;
        return grp;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_name == "" && !_morphing) return;
        double size = Px;
        double s = size / 24.0;
        double th = Math.Max(1.15, 2.0 * s * Weight);   // толщина линии в пикселях
        var pen = new Pen(Foreground, th / s) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var fg = Foreground;

        dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        dc.PushTransform(new ScaleTransform(s, s));

        if (!_morphing && _name != "")
        {
            var g = Exact(_name);
            if (_fillTo > 0) dc.DrawGeometry(fg, null, g);
            dc.DrawGeometry(null, pen, g);
        }
        else
        {
            var figs = CurrentFigs();
            double fill = Math.Clamp(CurrentFill(), 0, 1);
            var sg = new StreamGeometry();
            using (var ctx = sg.Open())
            {
                foreach (var f in figs)
                {
                    if (f.P.Length < 2) continue;
                    ctx.BeginFigure(f.P[0], f.Closed, f.Closed);
                    var rest = new List<Point>(f.P.Skip(1));
                    ctx.PolyLineTo(rest, true, true);
                }
            }
            sg.Freeze();
            if (fill > 0.01)
            {
                dc.PushOpacity(fill);
                dc.DrawGeometry(fg, null, sg);
                dc.Pop();
            }
            dc.DrawGeometry(null, pen, sg);
        }

        dc.Pop();
        dc.Pop();
    }
}
