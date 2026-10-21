using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Island;

// «Что нового» после обновления (шаг 57), как в One UI после обновления телефона.
//   Первый слайд: «Что нового в 3.2», с какой версии обновился и сколько всего появилось.
//   Дальше по слайду на новинку: большой значок в цвете новинки со свечением, версия, название и пара строк.
//   Фон — два мягких цветных пятна, которые плавно перекрашиваются в цвет текущей новинки.
//   Последний слайд: все новинки плитками и «Начать».
// Листать: «Далее», стрелки, колесо мыши, точки внизу. Esc — закрыть.
public sealed class WhatsNewWindow : Window
{
    private static WhatsNewWindow? _open;

    public static void ShowSince(Version from, bool sounds)
    {
        var items = WhatsNew.Since(from);
        if (items.Count == 0) items = WhatsNew.Since(new Version(0, 0));
        if (_open != null) { _open.Activate(); return; }
        _open = new WhatsNewWindow(items, from, sounds);
        _open.Closed += (_, _) => _open = null;
        _open.Show();
    }

    private static SolidColorBrush W(byte a) => new(Color.FromArgb(a, 255, 255, 255));
    private static Color C(uint x) => Color.FromRgb((byte)(x >> 16), (byte)(x >> 8), (byte)x);
    private static readonly Color A1 = Color.FromRgb(0x7B, 0x61, 0xFF), A2 = Color.FromRgb(0xFF, 0x5F, 0xA2);

    private readonly List<(Version ver, Feature f)> _items;
    private readonly Version _from;
    private readonly bool _sounds;
    private int _idx;
    private readonly Grid _stage = new();
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly SolidColorBrush _blobA = new(A1), _blobB = new(A2);
    private readonly TextBlock _nextText = new() { Foreground = Brushes.Black, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly FrameworkElement _back;
    private int Count => _items.Count + 2;   // вступление + новинки + итог

    private WhatsNewWindow(List<(Version, Feature)> items, Version from, bool sounds)
    {
        _items = items; _from = from; _sounds = sounds;
        Title = Loc.T("Что нового");
        Width = 860; Height = 580;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");

        var root = new SBorder
        {
            Margin = new Thickness(16), CornerRadius = new CornerRadius(32), Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x0D, 0x12)),
            BorderBrush = W(0x22), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 40, Opacity = 0.6, ShadowDepth = 8, Direction = 270 }
        };
        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition());
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(84) });
        root.Child = shell;
        shell.SizeChanged += (_, e) => shell.Clip = Smooth.Shape(e.NewSize.Width, e.NewSize.Height, new CornerRadius(31));

        // живой фон: два пятна цвета новинки медленно плывут
        var blobs = new Canvas { IsHitTestVisible = false };
        Grid.SetRowSpan(blobs, 2);
        var e1 = new Ellipse { Width = 620, Height = 620, Opacity = 0.42, Fill = Blob(_blobA) };
        var e2 = new Ellipse { Width = 560, Height = 560, Opacity = 0.34, Fill = Blob(_blobB) };
        Canvas.SetLeft(e1, -260); Canvas.SetTop(e1, -300);
        Canvas.SetLeft(e2, 430); Canvas.SetTop(e2, 160);
        blobs.Children.Add(e1);
        blobs.Children.Add(e2);
        Drift(e1, 70, 50, 9);
        Drift(e2, -60, -40, 11);
        shell.Children.Add(blobs);

        shell.Children.Add(_stage);

        // низ: «Назад», точки, «Далее»
        var bottom = new Grid { Margin = new Thickness(36, 0, 36, 24) };
        Grid.SetRow(bottom, 1);
        _back = Btn(false, new TextBlock { Text = Loc.T("Назад"), Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold }, () => Go(_idx - 1));
        _back.HorizontalAlignment = HorizontalAlignment.Left;
        bottom.Children.Add(_back);
        bottom.Children.Add(_dots);
        var nextRow = new StackPanel { Orientation = Orientation.Horizontal };
        nextRow.Children.Add(_nextText);
        var next = Btn(true, nextRow, () => { if (_idx >= Count - 1) Close(); else Go(_idx + 1); });
        next.HorizontalAlignment = HorizontalAlignment.Right;
        bottom.Children.Add(next);
        shell.Children.Add(bottom);

        // закрыть
        var x = new SBorder
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = W(0x1A), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 20, 22, 0),
            Child = new VIcon { Text = "x", FontSize = 13, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        x.MouseLeftButtonUp += (_, e) => { e.Handled = true; Close(); };
        shell.Children.Add(x);

        root.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is Grid or Canvas or SBorder) try { DragMove(); } catch { } };
        Content = root;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key is Key.Right or Key.Enter or Key.Space) { if (_idx >= Count - 1) Close(); else Go(_idx + 1); }
            else if (e.Key == Key.Left) Go(_idx - 1);
        };
        DateTime wheelAt = DateTime.MinValue;
        MouseWheel += (_, e) =>
        {
            if ((DateTime.UtcNow - wheelAt).TotalMilliseconds < 350) return;
            wheelAt = DateTime.UtcNow;
            Go(_idx + (e.Delta < 0 ? 1 : -1));
        };
        Loaded += (_, _) =>
        {
            Show(0, 0);
            root.RenderTransformOrigin = new Point(0.5, 0.5);
            var sc = new ScaleTransform(0.92, 0.92);
            root.RenderTransform = sc;
            var a = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(600)) { EasingFunction = new SpringEase(6, 11) };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
            if (_sounds) Sfx.Intro();
        };
    }

    private static Brush Blob(SolidColorBrush c)
    {
        // радиальный градиент, центр которого следует за цветом кисти c
        var g = new RadialGradientBrush();
        var s0 = new GradientStop(c.Color, 0);
        var s1 = new GradientStop(Color.FromArgb(0, c.Color.R, c.Color.G, c.Color.B), 1);
        g.GradientStops.Add(s0);
        g.GradientStops.Add(s1);
        c.Changed += (_, _) => { s0.Color = c.Color; s1.Color = Color.FromArgb(0, c.Color.R, c.Color.G, c.Color.B); };
        return g;
    }

    private static void Drift(UIElement e, double dx, double dy, double sec)
    {
        var t = new TranslateTransform();
        e.RenderTransform = t;
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dx, TimeSpan.FromSeconds(sec)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dy, TimeSpan.FromSeconds(sec * 1.3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
    }

    private FrameworkElement Btn(bool primary, UIElement content, Action act)
    {
        var b = new SBorder
        {
            Height = 44, MinWidth = 110, CornerRadius = new CornerRadius(22), Padding = new Thickness(24, 0, 24, 0), Cursor = Cursors.Hand,
            Background = primary ? Brushes.White : W(0x1C), Child = content
        };
        if (content is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Center; fe.VerticalAlignment = VerticalAlignment.Center; }
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var sc = new ScaleTransform(0.92, 0.92);
            b.RenderTransformOrigin = new Point(0.5, 0.5);
            b.RenderTransform = sc;
            var a = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = new SpringEase(6, 12) };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            act();
        };
        return b;
    }

    private void Go(int i)
    {
        i = Math.Clamp(i, 0, Count - 1);
        if (i == _idx) return;
        int dir = i > _idx ? 1 : -1;
        if (_sounds) { if (dir > 0) Sfx.Next(); else Sfx.Back(); }
        Show(i, dir);
    }

    private void Show(int i, int dir)
    {
        _idx = i;
        // старый слайд уезжает
        foreach (var old in _stage.Children.OfType<FrameworkElement>().ToList())
        {
            var ot = new TranslateTransform();
            old.RenderTransform = ot;
            old.IsHitTestVisible = false;
            ot.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -dir * 120, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            var f = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            f.Completed += (_, _) => _stage.Children.Remove(old);
            old.BeginAnimation(OpacityProperty, f);
        }
        Color acc = i == 0 || i == Count - 1 ? A1 : C(_items[i - 1].f.Color);
        Color acc2 = i == 0 || i == Count - 1 ? A2 : Shift(acc);
        ColorTween.To(_blobA, SolidColorBrush.ColorProperty, new ColorAnimation(acc, TimeSpan.FromMilliseconds(700)));
        ColorTween.To(_blobB, SolidColorBrush.ColorProperty, new ColorAnimation(acc2, TimeSpan.FromMilliseconds(900)));

        FrameworkElement slide = i == 0 ? Intro() : i == Count - 1 ? Summary() : FeatureSlide(_items[i - 1].ver, _items[i - 1].f, i);
        var tt = new TranslateTransform();
        slide.RenderTransform = tt;
        _stage.Children.Add(slide);
        if (dir != 0)
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dir * 160, 0, TimeSpan.FromMilliseconds(620)) { BeginTime = TimeSpan.FromMilliseconds(80), EasingFunction = new SpringEase(6.5, 11) });
        slide.Opacity = 0;
        slide.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(80) });

        _nextText.Text = Loc.T(i == 0 ? "Посмотреть" : i >= Count - 1 ? "Начать" : "Далее");
        _back.Visibility = i == 0 ? Visibility.Hidden : Visibility.Visible;
        RenderDots();
    }

    private static Color Shift(Color c) => Color.FromRgb((byte)((c.R + 0xFF) / 2), (byte)(c.G * 0.6), (byte)Math.Min(255, c.B + 60));

    private void RenderDots()
    {
        _dots.Children.Clear();
        int n = Count;
        // много слайдов: точек не больше 15, текущая — длинная капсула
        for (int k = 0; k < n; k++)
        {
            bool cur = k == _idx;
            var d = new SBorder { Height = 7, Width = cur ? 24 : 7, CornerRadius = new CornerRadius(3.5), Margin = new Thickness(3, 0, 3, 0), Background = cur ? Brushes.White : W(0x40), Cursor = Cursors.Hand };
            int target = k;
            d.MouseLeftButtonUp += (_, e) => { e.Handled = true; Go(target); };
            _dots.Children.Add(d);
        }
    }

    private FrameworkElement Intro()
    {
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(60, 0, 60, 0) };
        col.Children.Add(new SBorder
        {
            Width = 86, Height = 86, CornerRadius = new CornerRadius(26), HorizontalAlignment = HorizontalAlignment.Center,
            Background = new LinearGradientBrush(A1, A2, 45), Effect = new DropShadowEffect { Color = A1, BlurRadius = 40, ShadowDepth = 0, Opacity = 0.7 },
            Child = new VIcon { Text = "sparkles", FontSize = 40, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        });
        col.Children.Add(new TextBlock { Text = "ShyFlow " + Updater.Current.ToString(2), Foreground = W(0xB0), FontSize = 17, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 0) });
        col.Children.Add(new TextBlock
        {
            Text = Loc.T("Что нового"), FontSize = 58, FontWeight = FontWeights.Black, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0),
            Foreground = new LinearGradientBrush(Color.FromRgb(0xC8, 0xB8, 0xFF), Color.FromRgb(0xFF, 0xB0, 0xD0), 0)
        });
        string from = _from.Major > 0 ? Loc.T("Ты обновился с ") + _from.ToString(2) + Loc.T(" до ") + Updater.Current.ToString(2) + ". " : "";
        col.Children.Add(new TextBlock
        {
            Text = from + Loc.T("Новинок: ") + _items.Count + Loc.T(". Листай стрелками, колесом или кнопкой «Далее»"),
            Foreground = W(0xB8), FontSize = 16, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 560, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Center
        });
        Pop(col.Children[0]);
        return col;
    }

    private FrameworkElement FeatureSlide(Version ver, Feature f, int i)
    {
        var c = C(f.Color);
        var g = new Grid { Margin = new Thickness(70, 40, 70, 10), VerticalAlignment = VerticalAlignment.Center };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = new SBorder
        {
            Width = 176, Height = 176, CornerRadius = new CornerRadius(50), HorizontalAlignment = HorizontalAlignment.Left,
            Background = new LinearGradientBrush(Color.FromRgb((byte)Math.Min(255, c.R + 50), (byte)Math.Min(255, c.G + 50), (byte)Math.Min(255, c.B + 50)), c, 45),
            Effect = new DropShadowEffect { Color = c, BlurRadius = 60, ShadowDepth = 0, Opacity = 0.75 },
            Child = new VIcon { Text = f.Icon, FontSize = 80, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        g.Children.Add(icon);
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(col, 1);
        var chip = new SBorder
        {
            CornerRadius = new CornerRadius(11), Padding = new Thickness(11, 4, 11, 5), HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromArgb(0x33, c.R, c.G, c.B)),
            Child = new TextBlock { Text = Loc.T("Версия ") + ver.ToString(ver.Build > 0 ? 3 : 2) + "  ·  " + (i) + " / " + _items.Count, Foreground = new SolidColorBrush(Color.FromRgb((byte)Math.Min(255, c.R + 70), (byte)Math.Min(255, c.G + 70), (byte)Math.Min(255, c.B + 70))), FontSize = 12.5, FontWeight = FontWeights.SemiBold }
        };
        col.Children.Add(chip);
        col.Children.Add(new TextBlock { Text = Loc.T(f.Title), Foreground = Brushes.White, FontSize = 38, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), LineHeight = 44 });
        col.Children.Add(new TextBlock { Text = Loc.T(f.Text), Foreground = W(0xC0), FontSize = 17, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), LineHeight = 25 });
        g.Children.Add(col);
        Pop(icon);
        return g;
    }

    private FrameworkElement Summary()
    {
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(56, 30, 56, 0) };
        col.Children.Add(new TextBlock { Text = Loc.T("Всё готово"), Foreground = Brushes.White, FontSize = 40, FontWeight = FontWeights.Black, HorizontalAlignment = HorizontalAlignment.Center });
        col.Children.Add(new TextBlock { Text = Loc.T("Всё новое уже работает. Найти и настроить: правый клик по острову или Ctrl+Alt+S"), Foreground = W(0xB0), FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 22), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        int k = 0;
        foreach (var (_, f) in _items)
        {
            var c = C(f.Color);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new VIcon { Text = f.Icon, FontSize = 14, Foreground = new SolidColorBrush(c), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = Loc.T(f.Title), Foreground = Brushes.White, FontSize = 13.5, FontWeight = FontWeights.Medium, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var chip = new SBorder { Child = row, CornerRadius = new CornerRadius(16), Padding = new Thickness(13, 7, 14, 8), Margin = new Thickness(5), Background = new SolidColorBrush(Color.FromArgb(0x26, c.R, c.G, c.B)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, c.R, c.G, c.B)), BorderThickness = new Thickness(1) };
            int target = ++k;
            chip.Cursor = Cursors.Hand;
            chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; Go(target); };
            chip.Opacity = 0;
            chip.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(120 + k * 35) });
            wrap.Children.Add(chip);
        }
        col.Children.Add(wrap);
        return col;
    }

    // значок появляется с пружиной и лёгким поворотом
    private static void Pop(UIElement e)
    {
        var st = new ScaleTransform(0.5, 0.5);
        var rt = new RotateTransform(-12);
        e.RenderTransformOrigin = new Point(0.5, 0.5);
        e.RenderTransform = new TransformGroup { Children = { st, rt } };
        var a = new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(800)) { BeginTime = TimeSpan.FromMilliseconds(120), EasingFunction = new SpringEase(5, 11) };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        rt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-12, 0, TimeSpan.FromMilliseconds(800)) { BeginTime = TimeSpan.FromMilliseconds(120), EasingFunction = new SpringEase(5, 11) });
    }
}
