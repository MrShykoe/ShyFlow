using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Island;

// ============================================================
//  Время букв внутри строки песни
// ============================================================
// Раньше каждая буква получала одинаковый кусочек времени, поэтому волна шла «с постоянной скоростью».
// Теперь окно каждого слова (оно уже считается по слогам) делится между его буквами с весом:
// гласные тянутся дольше (на них поют), согласные короче, знаки препинания почти мгновенно.
// Так волна замедляется на протяжных слогах и ускоряется на быстрых, то есть идёт за голосом.
internal static class LetterTiming
{
    private const string VowelChars = "aeiouyаеёиоуыэюяáéíóúàèìòùâêîôûäöüæœ";

    public static List<string> Elements(string s)
    {
        var r = new List<string>();
        var en = StringInfo.GetTextElementEnumerator(s);
        while (en.MoveNext()) r.Add((string)en.Current);
        if (r.Count == 0) r.Add(s);
        return r;
    }

    public static double Weight(string el)
    {
        if (el.Length == 0) return 0.4;
        char c = char.ToLowerInvariant(el[0]);
        if (VowelChars.IndexOf(c) >= 0) return 2.2;
        if (char.IsLetterOrDigit(c)) return 1.0;
        return 0.4;
    }

    // Для каждого слова: время начала каждой его буквы.
    // smooth: сколько секунд буква плавно загорается и поднимается (чем быстрее поют, тем короче, но не рывком).
    public static double[][] Words(string[] parts, double t0, double win, out double smooth)
    {
        var wts = LyricsEngine.WordWeights(parts);
        double total = Math.Max(0.0001, wts.Sum()), acc = 0;
        var els = parts.Select(Elements).ToArray();
        int count = els.Sum(e => e.Count);
        smooth = Math.Clamp(win / Math.Max(1, count) * 6, 0.35, 0.8);
        var res = new double[parts.Length][];
        for (int i = 0; i < parts.Length; i++)
        {
            double dur = wts[i] / total * win;
            double sum = Math.Max(0.0001, els[i].Sum(Weight));
            var a = new double[els[i].Count];
            double at = t0 + acc;
            for (int k = 0; k < a.Length; k++)
            {
                a[k] = at;
                at += dur * Weight(els[i][k]) / sum;
            }
            res[i] = a;
            acc += dur;
        }
        return res;
    }

    // Точное время слов известно (NetEase, Kugou): буквы делят уже настоящее время своего слова, гласные дольше
    public static double[][] Within(string[] parts, double t0, WordTimes wt, out double smooth)
    {
        var res = new double[parts.Length][];
        int count = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            var els = Elements(parts[i]);
            count += els.Count;
            double start = t0 + wt.Start[i], dur = Math.Max(0.05, wt.End[i] - wt.Start[i]);
            double sum = Math.Max(0.0001, els.Sum(Weight));
            var a = new double[els.Count];
            double at = start;
            for (int k = 0; k < a.Length; k++) { a[k] = at; at += dur * Weight(els[k]) / sum; }
            res[i] = a;
        }
        double win = Math.Max(0.3, wt.End[^1]);
        smooth = Math.Clamp(win / Math.Max(1, count) * 6, 0.35, 0.8);
        return res;
    }

    // То же, но одним списком по всем буквам строки (без пробелов), в порядке на экране
    public static double[] Flat(string line, double t0, double win, out double smooth)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) { smooth = 0.5; return Array.Empty<double>(); }
        return Words(parts, t0, win, out smooth).SelectMany(x => x).ToArray();
    }
}

// ============================================================
//  Побуквенная смена текста
// ============================================================
// Старый текст: каждая буква по очереди взлетает, разлетается, крутится и растворяется.
// Новый текст: буквы по очереди опускаются сверху вниз на своё место.
//
// Если передать starts (время начала каждой буквы), то буквы остаются на экране и дальше загораются
// по очереди «волной» вслед за песней (Update(t) вызывается каждый кадр). Так работают слова песни.
//
// Сам TextBlock остаётся настоящим текстом (обрезка, раскладка, ширина острова), но на время анимации прозрачный;
// буквы рисуются поверх него на Canvas. Когда всё закончилось, буквы убираются и TextBlock снова виден.
// Подключение: TextBlock и Canvas лежат в одной ячейке Grid, Canvas сверху.
internal sealed class LetterSwap
{
    private sealed class Ltr
    {
        public TextBlock Tb = null!;
        public SolidColorBrush? Br;
        public TranslateTransform Wave = new();
        public double T0, P = -1;
    }

    private static readonly Color Dim = Color.FromArgb(0x55, 255, 255, 255);
    private static readonly Random Rnd = new();

    private readonly TextBlock _tb;
    private readonly Canvas _layer;
    private readonly List<Ltr> _live = new();
    private readonly List<UIElement> _olds = new();
    private DispatcherTimer? _timer;
    private int _token;
    private double _smooth = 0.5, _rise = 7;

    public LetterSwap(TextBlock tb, Canvas layer)
    {
        _tb = tb;
        _layer = layer;
    }

    // maxW: ширина, в которую должен влезть текст (<= 0: взять ширину TextBlock).
    // Возвращает false, если анимировать нечем (ещё нет раскладки): тогда вызывающий делает обычный переход.
    public bool Run(string oldText, string newText, int extraDelayMs = 0, double maxW = -1,
        double[]? starts = null, double smooth = 0.5, double rise = 7, bool animate = true)
    {
        if (_tb.FontSize <= 0) return false;
        double w = maxW > 0 ? maxW : _tb.ActualWidth;
        if (w < 20) return false;
        if (!animate && starts == null) { Cancel(); return false; }

        double dpi = VisualTreeHelper.GetDpi(_tb).PixelsPerDip;
        var origin = Origin();
        int token = ++_token;
        _timer?.Stop();
        _layer.Children.Clear();
        _live.Clear();
        _olds.Clear();
        _smooth = Math.Max(0.1, smooth);
        _rise = rise;
        _tb.BeginAnimation(UIElement.OpacityProperty, null);
        _tb.Opacity = 0;

        // строки песни меняются часто, поэтому для них всё в 2 раза короче
        double k = starts != null ? 0.55 : 1.0;

        // ---- старые буквы: взлетают и растворяются ----
        var old = animate ? Layout(oldText, w, dpi) : new List<(string el, double x)>();
        double oldSpread = Math.Min(old.Count * 22.0, 420) * k;
        for (int i = 0; i < old.Count; i++)
        {
            var (el, x) = old[i];
            var tr = new TranslateTransform();
            var rot = new RotateTransform();
            var sc = new ScaleTransform(1, 1);
            var l = MakeLetter(el, origin.X + x, origin.Y, tr, rot, sc, null);
            _layer.Children.Add(l);
            _olds.Add(l);

            double delay = extraDelayMs + (old.Count > 1 ? i * oldSpread / (old.Count - 1) : 0);
            var begin = TimeSpan.FromMilliseconds(delay);
            var dur = TimeSpan.FromMilliseconds(560 * k);

            tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -(22 + Rnd.NextDouble() * 34), dur)
            { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            tr.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, (Rnd.NextDouble() - 0.5) * 44, dur)
            { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, (Rnd.NextDouble() - 0.5) * 80, dur)
            { BeginTime = begin });
            var shrink = new DoubleAnimation(1, 0.45, dur) { BeginTime = begin };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
            l.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, dur)
            { BeginTime = begin, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
        }

        // ---- новые буквы: опускаются сверху вниз ----
        var fresh = Layout(newText, w, dpi);
        double startMs = extraDelayMs + (old.Count > 0 ? 200 * k : 0);
        double newSpread = Math.Min(fresh.Count * 30.0, 460) * k;
        double endMs = extraDelayMs + oldSpread + 560 * k;
        for (int i = 0; i < fresh.Count; i++)
        {
            var (el, x) = fresh[i];
            var tr = new TranslateTransform();
            var wave = new TranslateTransform();
            var l = MakeLetter(el, origin.X + x, origin.Y, tr, null, null, wave);
            if (starts != null)
            {
                var ltr = new Ltr { Tb = l, Wave = wave };
                ltr.Br = new SolidColorBrush(Dim);
                l.Foreground = ltr.Br;
                ltr.T0 = i < starts.Length ? starts[i] : (starts.Length > 0 ? starts[^1] : 0);
                wave.Y = rise;
                _live.Add(ltr);
            }
            _layer.Children.Add(l);

            if (!animate) continue;
            l.Opacity = 0; // до начала своей анимации буква не видна
            double delay = startMs + (fresh.Count > 1 ? i * newSpread / (fresh.Count - 1) : 0);
            var begin = TimeSpan.FromMilliseconds(delay);
            tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-20, 0, TimeSpan.FromMilliseconds(430 * k))
            { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            l.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300 * k))
            { BeginTime = begin });
            endMs = Math.Max(endMs, delay + 430 * k);
        }

        // когда всё закончилось: обычный текст возвращается, а у строк песни остаются только живые буквы
        bool keep = starts != null;
        var tm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(endMs + 60) };
        tm.Tick += (_, _) =>
        {
            tm.Stop();
            if (token != _token) return;
            if (keep)
            {
                foreach (var o in _olds) _layer.Children.Remove(o);
                _olds.Clear();
            }
            else
            {
                _layer.Children.Clear();
                _tb.BeginAnimation(UIElement.OpacityProperty, null);
                _tb.Opacity = 1;
            }
        };
        _timer = tm;
        tm.Start();
        return true;
    }

    // Каждый кадр: плавно зажигаем и приподнимаем буквы в такт песне.
    // Подъём идёт мягкой S-кривой, а окно растянуто на несколько соседних букв: получается волна.
    // Шаг 36: окно на треть длиннее и начинается чуть раньше буквы, «перелёт» вверх в 2 раза меньше: мягче и без подскоков.
    public void Update(double t)
    {
        double win = _smooth * 1.35;
        foreach (var l in _live)
        {
            double p = Math.Clamp((t - l.T0 + 0.06) / win, 0, 1);
            if (Math.Abs(p - l.P) < 0.004 && p > 0 && p < 1) continue;
            l.P = p;
            double s = p * p * p * (p * (p * 6 - 15) + 10);
            if (l.Br != null) l.Br.Color = Mix(Dim, Colors.White, s);
            l.Wave.Y = _rise * (1 - s) - _rise * 0.15 * Math.Sin(Math.PI * p);
        }
    }

    // Мгновенно вернуть обычный текст
    public void Cancel()
    {
        _token++;
        _timer?.Stop();
        _layer.Children.Clear();
        _live.Clear();
        _olds.Clear();
        _tb.BeginAnimation(UIElement.OpacityProperty, null);
        _tb.Opacity = 1;
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    // где у TextBlock левый верхний угол относительно слоя с буквами (подходит и для текста по центру, и для текста сверху)
    private Point Origin()
    {
        try { return _tb.TransformToVisual(_layer).Transform(new Point(0, 0)); }
        catch { return new Point(0, 0); }
    }

    private TextBlock MakeLetter(string el, double x, double y, TranslateTransform tr, RotateTransform? rot,
        ScaleTransform? sc, TranslateTransform? wave)
    {
        var g = new TransformGroup();
        if (sc != null) g.Children.Add(sc);
        if (rot != null) g.Children.Add(rot);
        g.Children.Add(tr);
        if (wave != null) g.Children.Add(wave);
        var l = new TextBlock
        {
            Text = el,
            FontFamily = _tb.FontFamily,
            FontSize = _tb.FontSize,
            FontWeight = _tb.FontWeight,
            FontStyle = _tb.FontStyle,
            Foreground = _tb.Foreground,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = g
        };
        Canvas.SetLeft(l, x);
        Canvas.SetTop(l, y);
        return l;
    }

    // Позиция каждой буквы = ширина всего, что стоит перед ней (так сохраняется кернинг и совпадает с обычным текстом).
    // Пробелы не рисуем. Буквы, не влезающие в ширину, отбрасываем.
    private List<(string el, double x)> Layout(string text, double maxW, double dpi)
    {
        var res = new List<(string el, double x)>();
        if (string.IsNullOrEmpty(text)) return res;
        var face = new Typeface(_tb.FontFamily, _tb.FontStyle, _tb.FontWeight, _tb.FontStretch);
        FormattedText Ft(string s) => new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face,
            _tb.FontSize, Brushes.White, dpi);

        var prefix = new StringBuilder();
        var en = StringInfo.GetTextElementEnumerator(text);
        while (en.MoveNext())
        {
            string el = (string)en.Current;
            if (el != " ")
            {
                double x = prefix.Length == 0 ? 0 : Ft(prefix.ToString()).WidthIncludingTrailingWhitespace;
                if (x + Ft(el).Width > maxW) break;
                res.Add((el, x));
            }
            prefix.Append(el);
        }
        return res;
    }
}
