using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace Island;

// Окно настроек в стиле «Системных настроек» macOS: слева боковая панель (поиск, профиль, разделы),
// справа сгруппированные списки с переключателями, сегментами и выпадающими меню.
public sealed partial class SettingsWindow : Window
{
    // Шаг 72: настройки в стиле острова, а не копия Системных настроек macOS: чёрный фон, белое полупрозрачное,
    // «включено» белым (как плитки быстрых действий) или цветом обложки, если «Цвет из обложки везде» включён.
    private static readonly Color OffColor = Color.FromArgb(0x2E, 255, 255, 255);
    private static readonly Color SelWhite = Color.FromArgb(0x30, 255, 255, 255);   // выбранный раздел: как вкладка в кармане
    private static readonly Color IconTile = Color.FromArgb(0x1F, 255, 255, 255);   // кружок под значком раздела

    private Color _accent = Colors.White;                                 // цвет обложки (белый, если «Цвет из обложки везде» выключено)
    private readonly SolidColorBrush _selBrush = new(SelWhite);           // выбранный раздел, стрелки меню, выделение текста
    private readonly SolidColorBrush _fillBrush = new(Colors.White);      // заливка ползунков: белая или цвет обложки
    private readonly List<(SolidColorBrush b, Func<bool> on)> _thumbs = new();   // кружки переключателей: тёмные на белой дорожке
    private readonly List<(SolidColorBrush b, Func<bool> on)> _toggles = new();
    private Action? _refreshSel;
    public bool WantsReopen { get; private set; }
    private readonly AppSettings _s;
    private readonly Action _changed;

    // после смены языка окно закрывается и открывается заново уже на новом языке
    private void ReopenSoon() =>
        Dispatcher.BeginInvoke(new Action(() => { WantsReopen = true; Close(); }), System.Windows.Threading.DispatcherPriority.Background);

    private static bool IsWhite(Color c) => c.R > 245 && c.G > 245 && c.B > 245;
    private static Color ToggleCol(Color c) => IsWhite(c) ? Colors.White : c;   // белый акцент: включено белым, как на острове
    private static Color SelCol(Color c) => IsWhite(c) ? SelWhite : c;          // и полупрозрачный белый для выбранного раздела
    private static Color ThumbCol(Color track) => OnCol(track) == Colors.Black ? Color.FromRgb(0x1C, 0x1C, 0x1E) : Colors.White;
    private Color SelFg => IsWhite(_accent) ? Colors.White : OnCol(_accent);   // текст на выбранном разделе
    private static Color OnCol(Color bg) => (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) > 190 ? Colors.Black : Colors.White;

    // цвет обложки сменился или «Цвет из обложки везде» переключили
    public void SetAccent(Color c)
    {
        _accent = c;
        var d = TimeSpan.FromMilliseconds(400);
        ColorTween.To(_selBrush, SolidColorBrush.ColorProperty, new ColorAnimation(SelCol(c), d));
        ColorTween.To(_fillBrush, SolidColorBrush.ColorProperty, new ColorAnimation(ToggleCol(c), d));
        foreach (var (b, on) in _toggles)
            if (on()) ColorTween.To(b, SolidColorBrush.ColorProperty, new ColorAnimation(ToggleCol(c), d));
        foreach (var (b, on) in _thumbs)
            if (on()) ColorTween.To(b, SolidColorBrush.ColorProperty, new ColorAnimation(ThumbCol(ToggleCol(c)), d));
        _refreshSel?.Invoke();
    }

    private static void CopySettings(AppSettings from, AppSettings to)
    {
        foreach (var p in typeof(AppSettings).GetProperties())
            if (p.CanRead && p.CanWrite && p.Name != nameof(AppSettings.HideWelcome)) p.SetValue(to, p.GetValue(from));
    }
    private static Brush Br(byte a, byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromArgb(a, r, g, b)); x.Freeze(); return x; }
    private static readonly Brush Gray = Br(255, 0x98, 0x98, 0x9D);
    private static readonly Brush CardBg = Br(0x12, 255, 255, 255);
    private static readonly Brush Line = Br(0x1A, 255, 255, 255);
    private static readonly Brush HoverBg = Br(0x0A, 255, 255, 255);
    private static readonly Brush ChipBg = Br(0x1F, 255, 255, 255);
    private static readonly Brush BtnBg = Br(0x26, 255, 255, 255);
    private static readonly Brush BtnHover = Br(0x38, 255, 255, 255);
    private static readonly Brush SegSel = Br(0x4A, 255, 255, 255);

    private static int _lastTab;

    // плавная прокрутка колесом: анимируем это свойство, оно двигает ScrollViewer
    private static readonly DependencyProperty SmoothOffsetProp = DependencyProperty.RegisterAttached(
        "SmoothOffset", typeof(double), typeof(SettingsWindow),
        new PropertyMetadata(0.0, (d, e) => { if (d is ScrollViewer sv) sv.ScrollToVerticalOffset((double)e.NewValue); }));

    private static readonly Color Gray2 = Color.FromRgb(0x98, 0x98, 0x9D);
    private static Color Clear(Color c) => Color.FromArgb(0, c.R, c.G, c.B);

    // «ГОРЯЧИЕ КЛАВИШИ: CTRL + ALT + …» -> «Горячие клавиши: Ctrl + Alt + …»
    private static string Sent(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = s.ToLower(Loc.Culture);
        s = char.ToUpper(s[0], Loc.Culture) + s[1..];
        return s.Replace("ctrl", "Ctrl").Replace("alt", "Alt");
    }

    private static string TextOf(DependencyObject root)
    {
        var sb = new StringBuilder();
        void Walk(DependencyObject o)
        {
            if (o is TextBlock tb) sb.Append(tb.Text).Append(' ');
            foreach (var ch in LogicalTreeHelper.GetChildren(o))
                if (ch is DependencyObject co) Walk(co);
        }
        Walk(root);
        return sb.ToString().ToLowerInvariant();
    }

    public SettingsWindow(AppSettings s, Action changed, Color accent)
    {
        _s = s;
        _changed = changed;
        _accent = accent;
        _selBrush.Color = SelCol(accent);
        _fillBrush.Color = ToggleCol(accent);
        Title = Loc.T("Настройки ShyFlow");
        Width = 900;
        Height = 660;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        // шаг 43: разделы по смыслу (порядок важен: «Система» последняя, на неё ведёт карточка ShyFlow слева)
        string[] tabNames = { "Основное", "Внешний вид", "Музыка", "Плеер", "Плашки", "Управление", "Система" };
        var navIcons = new (string glyph, Color col)[]
        {
            ("settings", IconTile),             // основное
            ("image", IconTile),                // внешний вид
            ("music", IconTile),                // музыка
            ("maximize-2", IconTile),           // плеер
            ("bell", IconTile),                 // плашки
            ("sliders-horizontal", IconTile),   // управление
            ("gauge", IconTile),                // система
        };

        // ---------- каркас окна ----------
        var scale = new ScaleTransform(1, 1);
        var root = new SBorder
        {
            Margin = new Thickness(14),
            CornerRadius = new CornerRadius(26),
            Background = new SolidColorBrush(Color.FromRgb(0x08, 0x08, 0x09)),   // почти чёрный, как остров
            BorderBrush = Br(0x24, 255, 255, 255),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 34, Opacity = 0.65, ShadowDepth = 6, Direction = 270 },
            RenderTransform = scale,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        var shell = new Grid();
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(256) });
        shell.ColumnDefinitions.Add(new ColumnDefinition());
        root.Child = shell;
        _eggHost = shell;

        // ---------- правая часть: заголовок + прокручиваемые страницы ----------
        var contentPane = new Grid();
        contentPane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
        contentPane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        contentPane.SizeChanged += (_, e) =>
            contentPane.Clip = Smooth.Shape(e.NewSize.Width + 60, e.NewSize.Height, new CornerRadius(0, 25, 25, 0), 0, 0, -60, 0);
        Grid.SetColumn(contentPane, 1);
        shell.Children.Add(contentPane);

        // мягкое цветное свечение под заголовком: цвет раздела
        var glowA = new GradientStop(Color.FromArgb(0x14, navIcons[0].col.R, navIcons[0].col.G, navIcons[0].col.B), 0);
        var glowB = new GradientStop(Clear(navIcons[0].col), 1);
        var glow = new Border
        {
            Height = 190, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
            Background = new LinearGradientBrush(new GradientStopCollection { glowA, glowB }, new Point(0, 0), new Point(0, 1))
        };
        Grid.SetRowSpan(glow, 2);
        contentPane.Children.Add(glow);

        var pageTitle = new TextBlock
        {
            Foreground = Brushes.White, FontSize = 19, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new TranslateTransform()
        };
        var titleTileBrush = new SolidColorBrush(navIcons[0].col);
        var titleGlyph = new VIcon
        {
            Foreground = Brushes.White, FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        var titleScale = new ScaleTransform(1, 1);
        var titleTile = new SBorder
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = titleTileBrush, Child = titleGlyph,
            Margin = new Thickness(22, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = titleScale, RenderTransformOrigin = new Point(0.5, 0.5)
        };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(titleTile);
        titleRow.Children.Add(pageTitle);
        var titleBar = new Border { Background = Brushes.Transparent, Child = titleRow };
        titleBar.MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } };
        Grid.SetRow(titleBar, 0);
        contentPane.Children.Add(titleBar);

        // тонкая линия под заголовком появляется, когда страница прокручена
        var headLine = new Border { Height = 1, Background = Line, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0, IsHitTestVisible = false };
        Grid.SetRow(headLine, 0);
        contentPane.Children.Add(headLine);

        void SetHeader(string text, string glyph, Color col, bool animate)
        {
            pageTitle.Text = text;
            titleGlyph.Text = glyph;
            var dur = TimeSpan.FromMilliseconds(animate ? 420 : 1);
            ColorTween.To(titleTileBrush, SolidColorBrush.ColorProperty, new ColorAnimation(col, dur));
            ColorTween.To(glowA, GradientStop.ColorProperty, new ColorAnimation(Color.FromArgb(0x14, col.R, col.G, col.B), dur));
            ColorTween.To(glowB, GradientStop.ColorProperty, new ColorAnimation(Clear(col), dur));
            if (!animate) return;
            var popA = new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(460)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.9 } };
            titleScale.BeginAnimation(ScaleTransform.ScaleXProperty, popA);
            titleScale.BeginAnimation(ScaleTransform.ScaleYProperty, popA);
            pageTitle.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
            ((TranslateTransform)pageTitle.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false
        };
        var host = new Border();
        var hostMove = new TranslateTransform();
        host.RenderTransform = hostMove;
        scroll.Content = host;
        Grid.SetRow(scroll, 1);
        contentPane.Children.Add(scroll);

        var pages = new StackPanel[tabNames.Length];
        for (int i = 0; i < pages.Length; i++) pages[i] = new StackPanel { Margin = new Thickness(20, 0, 10, 22) };

        // плавная прокрутка колесом и линия под заголовком
        double smoothTarget = 0; bool smoothOn = false; int smoothId = 0; bool lineOn = false;
        void StopSmooth() { smoothId++; smoothOn = false; scroll.BeginAnimation(SmoothOffsetProp, null); }
        scroll.PreviewMouseWheel += (_, e) =>
        {
            if (scroll.ScrollableHeight <= 0) return;
            e.Handled = true;
            double fromV = scroll.VerticalOffset;
            double start = smoothOn ? smoothTarget : fromV;
            smoothTarget = Math.Clamp(start - e.Delta * 0.9, 0, scroll.ScrollableHeight);
            int id = ++smoothId; smoothOn = true;
            var a = new DoubleAnimation(fromV, smoothTarget, TimeSpan.FromMilliseconds(340))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            a.Completed += (_, _) => { if (id == smoothId) smoothOn = false; };
            scroll.BeginAnimation(SmoothOffsetProp, a);
        };
        scroll.ScrollChanged += (_, e) =>
        {
            if (e.VerticalChange == 0) return;
            bool down = scroll.VerticalOffset > 4;
            if (down == lineOn) return;
            lineOn = down;
            headLine.BeginAnimation(OpacityProperty, new DoubleAnimation(down ? 1 : 0, TimeSpan.FromMilliseconds(180)));
        };

        BuildGeneralPage(pages[0]);
        BuildLookPage(pages[1]);
        BuildMusicPage(pages[2]);
        BuildPlayerPage(pages[3]);
        BuildToastsPage(pages[4]);
        BuildControlPage(pages[5]);
        BuildSystemPage(pages[6]);

        // ---------- состояние: раздел, поиск ----------
        int cur = -1;
        bool searching = false;
        var navBg = new List<Border>();
        var navFg = new List<SolidColorBrush>();
        var navIcoScale = new List<ScaleTransform>();
        Border? navPill = null;
        var navPillMove = new TranslateTransform();
        bool pillShown = false;
        var searchStack = new StackPanel { Margin = new Thickness(20, 0, 10, 22) };
        var noResults = new TextBlock
        {
            Text = Loc.T("Ничего не найдено", "Nothing found"), Foreground = Gray, FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 60, 0, 0), Visibility = Visibility.Collapsed
        };
        var parked = new List<UIElement>[pages.Length];

        void RefreshSel()
        {
            var fgCol = SelFg;
            bool showPill = !searching && cur >= 0;
            for (int i = 0; i < navFg.Count; i++)
                ColorTween.To(navFg[i], SolidColorBrush.ColorProperty,
                    new ColorAnimation(showPill && cur == i ? fgCol : Colors.White, TimeSpan.FromMilliseconds(200)));
            if (navPill == null) return;
            if (showPill)
            {
                double y = cur * 42;
                if (!pillShown)
                {
                    navPillMove.BeginAnimation(TranslateTransform.YProperty, null);
                    navPillMove.Y = y;
                    navPill.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
                    pillShown = true;
                }
                else
                    navPillMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(420))
                    { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } });
            }
            else
            {
                pillShown = false;
                navPill.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)));
            }
        }
        _refreshSel = RefreshSel;

        void Park()
        {
            searchStack.Children.Clear();
            searchStack.Children.Add(noResults);
            for (int i = 0; i < pages.Length; i++)
            {
                parked[i] = pages[i].Children.Cast<UIElement>().ToList();
                pages[i].Children.Clear();
                // в результатах видно, в каком разделе нашлось: значок и название раздела над его карточками
                searchStack.Children.Add(PageLabel(Loc.T(tabNames[i]), navIcons[i].glyph, navIcons[i].col));
                foreach (var u in parked[i]) searchStack.Children.Add(u);
            }
        }

        void Unpark()
        {
            searchStack.Children.Clear();
            for (int i = 0; i < pages.Length; i++)
            {
                if (parked[i] == null) continue;
                foreach (var u in parked[i])
                {
                    u.Visibility = Visibility.Visible;
                    if (u is Border bd && bd.Child is StackPanel rows)
                        foreach (UIElement r in rows.Children) r.Visibility = Visibility.Visible;
                    pages[i].Children.Add(u);
                }
            }
        }

        void Filter(string q)
        {
            bool any = false;
            TextBlock? header = null;
            FrameworkElement? pageLabel = null;
            var terms = SearchTerms(q);
            foreach (UIElement u in searchStack.Children)
            {
                if (ReferenceEquals(u, noResults)) continue;
                if (u is FrameworkElement pl && pl.Tag as string == "page") { pageLabel = pl; pl.Visibility = Visibility.Collapsed; continue; }
                if (u is TextBlock ib && ib.Tag as string == "intro") { ib.Visibility = Visibility.Collapsed; continue; }
                if (u is TextBlock hb) { header = hb; hb.Visibility = Visibility.Collapsed; continue; }
                if (u is Border bd && bd.Child is StackPanel rows)
                {
                    bool headerHit = header != null && Hit(header.Text.ToLowerInvariant(), terms);
                    bool cardAny = false, shown = false;
                    UIElement? pending = null;
                    foreach (UIElement r in rows.Children)
                    {
                        if (r is Border dv && dv.Tag as string == "div") { dv.Visibility = Visibility.Collapsed; pending = dv; continue; }
                        bool hit = headerHit || Hit(TextOf(r), terms);
                        r.Visibility = hit ? Visibility.Visible : Visibility.Collapsed;
                        if (hit)
                        {
                            if (shown && pending != null) pending.Visibility = Visibility.Visible;
                            shown = true; cardAny = true;
                        }
                        pending = null;
                    }
                    bd.Visibility = cardAny ? Visibility.Visible : Visibility.Collapsed;
                    if (header != null) header.Visibility = cardAny ? Visibility.Visible : Visibility.Collapsed;
                    if (cardAny) { any = true; if (pageLabel != null) pageLabel.Visibility = Visibility.Visible; }
                    header = null;
                }
            }
            noResults.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        }

        TextBox searchBox = new TextBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White,
            CaretBrush = Brushes.White, SelectionBrush = _selBrush, FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center
        };

        // карточки страницы выезжают снизу по очереди
        void Stagger(Panel page)
        {
            int n = 0;
            foreach (UIElement u in page.Children)
            {
                var tt = new TranslateTransform(0, 18);
                u.RenderTransform = tt;
                u.BeginAnimation(OpacityProperty, null);
                u.Opacity = 0;
                var delay = TimeSpan.FromMilliseconds(30 + Math.Min(n, 9) * 45);
                u.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)) { BeginTime = delay });
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(380))
                { BeginTime = delay, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                n++;
            }
        }

        void SelectTab(int idx, bool animate)
        {
            if (searching) searchBox.Text = "";            // сбрасывает поиск и возвращает страницу
            if (idx == cur) return;
            int prev = cur;
            cur = idx; _lastTab = idx;
            host.Child = pages[idx];
            StopSmooth();
            scroll.ScrollToVerticalOffset(0);
            SetHeader(Loc.T(tabNames[idx]), navIcons[idx].glyph, navIcons[idx].col, animate);
            RefreshSel();
            Stagger(pages[idx]);
            if (animate)
            {
                double dir = prev < 0 || idx > prev ? 1 : -1;
                hostMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(26 * dir, 0, TimeSpan.FromMilliseconds(380))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                if (idx < navIcoScale.Count)
                {
                    var bounce = new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 1.1 } };
                    navIcoScale[idx].BeginAnimation(ScaleTransform.ScaleXProperty, bounce);
                    navIcoScale[idx].BeginAnimation(ScaleTransform.ScaleYProperty, bounce);
                }
            }
        }

        // ---------- боковая панель ----------
        var sidebar = new SBorder
        {
            Margin = new Thickness(8), CornerRadius = new CornerRadius(20),
            Background = Br(0x0C, 255, 255, 255),
            BorderBrush = Br(0x12, 255, 255, 255), BorderThickness = new Thickness(1)
        };
        var sideGrid = new Grid();
        sideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sideGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        sidebar.Child = sideGrid;
        shell.Children.Add(sidebar);

        // Шаг 72: вместо светофора macOS — стеклянные кружки, как кнопки острова: закрыть и свернуть
        var lightsRow = new Grid { Height = 50, Background = Brushes.Transparent };
        var lights = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0), Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left
        };
        SBorder Glass(string icon, string tip, Action click)
        {
            var bg = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255));
            var sc = new ScaleTransform(1, 1);
            var b = new SBorder
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = bg, Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand, ToolTip = tip, RenderTransform = sc, RenderTransformOrigin = new Point(0.5, 0.5),
                Child = new VIcon
                {
                    Text = icon, FontSize = 12, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            void To(double v, int ms)
            {
                var an = new DoubleAnimation(v, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } };
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, an);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, an);
            }
            b.MouseEnter += (_, _) => ColorTween.To(bg, SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0x38, 255, 255, 255), TimeSpan.FromMilliseconds(140)));
            b.MouseLeave += (_, _) => { ColorTween.To(bg, SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0x1A, 255, 255, 255), TimeSpan.FromMilliseconds(220))); To(1, 220); };
            b.MouseLeftButtonDown += (_, e) => { e.Handled = true; To(0.88, 110); };
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; To(1, 200); click(); };
            return b;
        }
        lights.Children.Add(Glass("x", Loc.T("Закрыть (Esc)", "Close (Esc)"), () => Close()));
        lights.Children.Add(Glass("minus", Loc.T("Свернуть", "Minimize"), () => WindowState = WindowState.Minimized));
        lightsRow.Children.Add(lights);
        lightsRow.MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } };
        Grid.SetRow(lightsRow, 0);
        sideGrid.Children.Add(lightsRow);

        // поиск
        var searchGrid = new Grid();
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition());
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchGrid.Children.Add(new VIcon
        {
            Text = "search", Foreground = Gray, FontSize = 12, Margin = new Thickness(11, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        var searchHost = new Grid();
        var placeholder = new TextBlock
        {
            Text = Loc.T("Поиск", "Search"), Foreground = Gray, FontSize = 13, IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center
        };
        searchHost.Children.Add(searchBox);
        searchHost.Children.Add(placeholder);
        Grid.SetColumn(searchHost, 1);
        searchGrid.Children.Add(searchHost);
        var clearBtn = new SBorder
        {
            Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Br(0x40, 255, 255, 255),
            Margin = new Thickness(4, 0, 9, 0), Cursor = Cursors.Hand, Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new VIcon
            {
                Text = "x", FontSize = 7, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        clearBtn.MouseLeftButtonUp += (_, e) => { searchBox.Text = ""; searchBox.Focus(); e.Handled = true; };
        Grid.SetColumn(clearBtn, 2);
        searchGrid.Children.Add(clearBtn);
        var searchFrame = new SBorder
        {
            Height = 34, CornerRadius = new CornerRadius(17), Background = Br(0x14, 255, 255, 255), Margin = new Thickness(12, 0, 12, 10),
            BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1.5), Child = searchGrid
        };
        searchBox.GotKeyboardFocus += (_, _) => searchFrame.BorderBrush = _selBrush;
        searchBox.LostKeyboardFocus += (_, _) => searchFrame.BorderBrush = Brushes.Transparent;
        Grid.SetRow(searchFrame, 1);
        sideGrid.Children.Add(searchFrame);

        searchBox.TextChanged += (_, _) =>
        {
            string q = searchBox.Text.Trim();
            placeholder.Visibility = searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            clearBtn.Visibility = searchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (q.Length == 0)
            {
                if (!searching) return;
                searching = false;
                Unpark();
                int back = Math.Max(cur, 0);
                host.Child = pages[back];
                SetHeader(Loc.T(tabNames[back]), navIcons[back].glyph, navIcons[back].col, true);
                Stagger(pages[back]);
                RefreshSel();
                return;
            }
            if (!searching)
            {
                searching = true;
                Park();
                host.Child = searchStack;
                SetHeader(Loc.T("Результаты поиска", "Search results"), "\uE721", Gray2, true);
                scroll.ScrollToVerticalOffset(0);
                RefreshSel();
            }
            Filter(q.ToLowerInvariant());
        };

        // поиск без мыши: Ctrl+F, или просто начни печатать, где бы ни был фокус
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { searchBox.Focus(); searchBox.SelectAll(); e.Handled = true; }
        };
        PreviewTextInput += (_, e) =>
        {
            if (Keyboard.FocusedElement is TextBox || string.IsNullOrEmpty(e.Text) || !char.IsLetterOrDigit(e.Text[0])) return;
            searchBox.Focus();
            searchBox.Text += e.Text;
            searchBox.CaretIndex = searchBox.Text.Length;
            e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (searchBox.Text.Length > 0) { searchBox.Text = ""; e.Handled = true; }
            else Close();
        };

        // карточка профиля ShyFlow
        var profBg = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255));
        var profCard = new SBorder
        {
            Margin = new Thickness(12, 0, 12, 12), Padding = new Thickness(10), CornerRadius = new CornerRadius(18),
            Background = profBg
        };
        profCard.MouseEnter += (_, _) => ColorTween.To(profBg, SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0x26, 255, 255, 255), TimeSpan.FromMilliseconds(160)));
        profCard.MouseLeave += (_, _) => ColorTween.To(profBg, SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0x14, 255, 255, 255), TimeSpan.FromMilliseconds(220)));
        var profGrid = new Grid();
        profGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        profGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var avatar = new SBorder { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Background = Brushes.Black };
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri("pack://application:,,,/ShyFlow.png", UriKind.Absolute);
            bi.DecodePixelWidth = 128;
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();
            avatar.Background = new ImageBrush(bi);
        }
        catch { }
        profGrid.Children.Add(avatar);
        var profText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 0, 0) };
        profText.Children.Add(new TextBlock { Text = "ShyFlow", Foreground = Brushes.White, FontSize = 14.5, FontWeight = FontWeights.SemiBold });
        var profVer = new TextBlock { Text = "Версия " + AppInfo.CurrentVersion, Foreground = Gray, FontSize = 11.5, Margin = new Thickness(0, 1, 0, 0) };
        AttachEgg(profVer);
        profText.Children.Add(profVer);
        Grid.SetColumn(profText, 1);
        profGrid.Children.Add(profText);
        profCard.Child = profGrid;
        profCard.MouseLeftButtonUp += (_, _) => SelectTab(tabNames.Length - 1, true);
        profCard.Cursor = Cursors.Hand;
        Grid.SetRow(profCard, 2);
        sideGrid.Children.Add(profCard);

        // разделы
        var navHost = new Grid { Margin = new Thickness(8, 0, 8, 10) };
        navPill = new SBorder
        {
            Height = 40, CornerRadius = new CornerRadius(20), Background = _selBrush, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 0, 0), Opacity = 0, IsHitTestVisible = false, RenderTransform = navPillMove
        };
        navHost.Children.Add(navPill);
        var nav = new StackPanel();
        navHost.Children.Add(nav);
        for (int i = 0; i < tabNames.Length; i++)
        {
            int idx = i;
            var icoScale = new ScaleTransform(1, 1);
            navIcoScale.Add(icoScale);
            var ico = new SBorder
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = new SolidColorBrush(navIcons[i].col),
                VerticalAlignment = VerticalAlignment.Center, RenderTransform = icoScale, RenderTransformOrigin = new Point(0.5, 0.5),
                Child = new VIcon
                {
                    Text = navIcons[i].glyph, Foreground = Brushes.White, FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            var txBrush = new SolidColorBrush(Colors.White);
            navFg.Add(txBrush);
            var tx = new TextBlock
            {
                Text = tabNames[i], Foreground = txBrush, FontSize = 13.5, FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 0, 0)
            };
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(ico);
            line.Children.Add(tx);
            var item = new SBorder
            {
                Height = 40, CornerRadius = new CornerRadius(20), Padding = new Thickness(7, 0, 9, 0), Margin = new Thickness(0, 1, 0, 1),
                Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = line
            };
            item.MouseEnter += (_, _) => { if (searching || cur != idx) item.Background = HoverBg; };
            item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
            item.MouseLeftButtonDown += (_, e) => { SelectTab(idx, true); e.Handled = true; };
            navBg.Add(item);
            nav.Children.Add(item);
        }
        Grid.SetRow(navHost, 3);
        sideGrid.Children.Add(navHost);

        Content = root;
        Loc.ApplyTree(root);
        foreach (var pg in pages) Loc.ApplyTree(pg); // страницы, которых сейчас нет на экране, тоже переводим
        Loaded += (_, _) =>
        {
            SelectTab(Math.Clamp(_lastTab, 0, tabNames.Length - 1), false);
            var a = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(480))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        };
    }
}
