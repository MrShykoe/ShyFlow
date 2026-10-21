using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Island;

// Окно «Статистика» (шаг 49): сколько слушал за неделю, месяц или всё время, график по дням,
// топ треков с обложками и топ исполнителей, дни подряд и «твой вайб» по часам, когда чаще всего слушаешь.
// «Поделиться» делает картинку итогов 1080x1920 для сторис (Ui\StoryCards.cs).
// Открывается из меню острова, плиткой в быстрых действиях и кнопкой в плеере.
public sealed class StatsWindow : Window
{
    private static StatsWindow? _open;

    public static void ShowStats()
    {
        if (_open != null) { _open.Activate(); return; }
        ListenStats.Save();
        _open = new StatsWindow();
        _open.Closed += (_, _) => _open = null;
        _open.Show();
    }

    private static readonly Color A1 = Color.FromRgb(0x7B, 0x61, 0xFF), A2 = Color.FromRgb(0xFF, 0x5F, 0xA2);
    private static SolidColorBrush W(byte a) => new(Color.FromArgb(a, 255, 255, 255));

    private readonly StackPanel _body = new() { Margin = new Thickness(26, 4, 26, 26) };
    private readonly List<TextBlock> _periodBtns = new();
    private int _period = 7;
    private StatsSummary? _sum;

    private StatsWindow()
    {
        Title = Loc.T("Статистика");
        Width = 980; Height = 720;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var root = new SBorder
        {
            Margin = new Thickness(14), CornerRadius = new CornerRadius(26), Background = new SolidColorBrush(Color.FromRgb(0x17, 0x16, 0x1C)),
            BorderBrush = W(0x2E), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 34, Opacity = 0.65, ShadowDepth = 6, Direction = 270 }
        };
        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });
        shell.RowDefinitions.Add(new RowDefinition());
        root.Child = shell;
        shell.SizeChanged += (_, e) => shell.Clip = Smooth.Shape(e.NewSize.Width, e.NewSize.Height, new CornerRadius(25));

        // мягкое цветное свечение в углу: «вайб» окна
        var glow = new Ellipse
        {
            Width = 620, Height = 620, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -330, -260, 0), IsHitTestVisible = false,
            Fill = new RadialGradientBrush(Color.FromArgb(0x55, A1.R, A1.G, A1.B), Color.FromArgb(0, A1.R, A1.G, A1.B))
        };
        Grid.SetRowSpan(glow, 2);
        shell.Children.Add(glow);
        var glow2 = new Ellipse
        {
            Width = 520, Height = 520, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(-260, 0, 0, -300), IsHitTestVisible = false,
            Fill = new RadialGradientBrush(Color.FromArgb(0x40, A2.R, A2.G, A2.B), Color.FromArgb(0, A2.R, A2.G, A2.B))
        };
        Grid.SetRowSpan(glow2, 2);
        shell.Children.Add(glow2);

        // шапка
        var head = new Grid { Background = Brushes.Transparent, Margin = new Thickness(24, 0, 18, 0) };
        head.MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { /* окно уже закрывается */ } };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        titleRow.Children.Add(new SBorder
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(9), Background = new LinearGradientBrush(A1, A2, 45),
            Child = new VIcon { Text = "chart-column", FontSize = 16, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        });
        titleRow.Children.Add(new TextBlock { Text = Loc.T("Статистика"), Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(titleRow);

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var seg = new SBorder { CornerRadius = new CornerRadius(9), Background = W(0x1F), Padding = new Thickness(3), Margin = new Thickness(0, 0, 10, 0) };
        var segRow = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, days) in new[] { ("Неделя", 7), ("Месяц", 30), ("Всё время", 0) })
        {
            int d = days;
            var tb = new TextBlock { Text = Loc.T(name), Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.Medium, Margin = new Thickness(12, 4, 12, 4), Tag = d };
            var b = new SBorder { CornerRadius = new CornerRadius(7), Child = tb, Cursor = Cursors.Hand, Background = Brushes.Transparent };
            b.MouseLeftButtonDown += (_, e) => e.Handled = true;   // не перетаскивать окно
            b.MouseLeftButtonUp += (_, _) => { _period = d; Render(); };
            _periodBtns.Add(tb);
            segRow.Children.Add(b);
        }
        seg.Child = segRow;
        right.Children.Add(seg);
        right.Children.Add(HeadBtn("share", Loc.T("Поделиться"), true, Share));
        right.Children.Add(HeadBtn("x", null, false, Close));
        head.Children.Add(right);
        shell.Children.Add(head);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = _body };   // колесо работает, серой системной полосы нет
        Grid.SetRow(scroll, 1);
        shell.Children.Add(scroll);

        Content = root;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) =>
        {
            Render();
            root.RenderTransformOrigin = new Point(0.5, 0.5);
            var sc = new ScaleTransform(0.95, 0.95);
            root.RenderTransform = sc;
            var a = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        };
    }

    private FrameworkElement HeadBtn(string icon, string? text, bool primary, Action act)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new VIcon { Text = icon, FontSize = 13, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        if (text != null) row.Children.Add(new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var b = new SBorder
        {
            Height = 32, MinWidth = 32, CornerRadius = new CornerRadius(16), Cursor = Cursors.Hand, Child = row, Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(text != null ? 14 : 0, 0, text != null ? 15 : 0, 0),
            Background = primary ? new LinearGradientBrush(A1, A2, 0) : W(0x1F)
        };
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;
        b.MouseLeftButtonUp += (_, _) => act();
        return b;
    }

    private void Render()
    {
        foreach (var tb in _periodBtns) tb.Opacity = (int)tb.Tag == _period ? 1 : 0.5;
        var s = _sum = ListenStats.Query(_period);
        _body.Children.Clear();

        if (ListenStats.Data.Tracks.Count == 0)
        {
            _body.Children.Add(new TextBlock
            {
                Text = Loc.T("Пока пусто. Включи музыку: ShyFlow сам посчитает, что и сколько ты слушаешь"),
                Foreground = W(0x99), FontSize = 15, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                MaxWidth = 460, Margin = new Thickness(0, 200, 0, 0)
            });
            return;
        }

        // четыре больших числа
        var hero = new UniformGrid { Columns = 4, Margin = new Thickness(-6, 0, -6, 0) };
        hero.Children.Add(BigTile("headphones", Loc.T("Слушал"), ListenStats.FmtDur(s.TotalSec), A1, true));
        hero.Children.Add(BigTile("music", Loc.T("Треков"), s.TrackCount.ToString(), Color.FromRgb(0x0A, 0x84, 0xFF), false));
        hero.Children.Add(BigTile("user", Loc.T("Исполнителей"), s.ArtistCount.ToString(), Color.FromRgb(0x30, 0xD1, 0x58), false));
        hero.Children.Add(BigTile("flame", Loc.T("Дней подряд"), s.Streak + (s.BestStreak > s.Streak ? "  · " + Loc.T("рекорд ") + s.BestStreak : ""), Color.FromRgb(0xFF, 0x6B, 0x2C), false));
        _body.Children.Add(hero);

        _body.Children.Add(Card(Loc.T("По дням"), DayChart(s), new Thickness(0, 12, 0, 0)));

        var cols = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        cols.ColumnDefinitions.Add(new ColumnDefinition());
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        cols.ColumnDefinitions.Add(new ColumnDefinition());
        var tracks = new StackPanel();
        int i = 1;
        foreach (var t in s.TopTracks) tracks.Children.Add(TrackRow(i++, t));
        if (s.TopTracks.Count == 0) tracks.Children.Add(Muted(Loc.T("За это время ничего не играло")));
        cols.Children.Add(Card(Loc.T("Топ треков"), tracks, new Thickness(0)));
        var artists = new StackPanel();
        i = 1;
        int maxA = s.TopArtists.Count > 0 ? s.TopArtists.Max(a => a.Sec) : 1;
        foreach (var a in s.TopArtists) artists.Children.Add(ArtistRow(i++, a, maxA));
        if (s.TopArtists.Count == 0) artists.Children.Add(Muted(Loc.T("Пока никого")));
        var ac = Card(Loc.T("Топ исполнителей"), artists, new Thickness(0));
        Grid.SetColumn(ac, 2);
        cols.Children.Add(ac);
        _body.Children.Add(cols);

        _body.Children.Add(Card(Loc.T("Твой вайб"), VibeBlock(s), new Thickness(0, 12, 0, 0)));

        // блоки выезжают по очереди
        int k = 0;
        foreach (var el in _body.Children.OfType<FrameworkElement>())
        {
            var tt = new TranslateTransform(0, 18);
            el.RenderTransform = tt;
            el.Opacity = 0;
            var delay = TimeSpan.FromMilliseconds(k++ * 70);
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = delay });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(460)) { BeginTime = delay, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }

    private static TextBlock Muted(string t) => new() { Text = t, Foreground = W(0x80), FontSize = 13, Margin = new Thickness(0, 6, 0, 6) };

    private static FrameworkElement Card(string title, UIElement content, Thickness margin)
    {
        var col = new StackPanel();
        col.Children.Add(new TextBlock { Text = title, Foreground = W(0xB0), FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        col.Children.Add(content);
        return new SBorder { CornerRadius = new CornerRadius(20), Background = W(0x0D), BorderBrush = W(0x14), BorderThickness = new Thickness(1), Padding = new Thickness(18, 16, 18, 16), Margin = margin, Child = col };
    }

    private static FrameworkElement BigTile(string icon, string label, string value, Color c, bool gradient)
    {
        var col = new StackPanel();
        col.Children.Add(new SBorder
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromArgb(0x33, c.R, c.G, c.B)),
            Child = new VIcon { Text = icon, FontSize = 14, Foreground = new SolidColorBrush(c), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        });
        col.Children.Add(new TextBlock
        {
            Text = value, FontSize = 26, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = gradient ? new LinearGradientBrush(A1, A2, 0) : Brushes.White
        });
        col.Children.Add(new TextBlock { Text = label, Foreground = W(0x90), FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0) });
        return new SBorder { CornerRadius = new CornerRadius(20), Background = W(0x0D), BorderBrush = W(0x14), BorderThickness = new Thickness(1), Padding = new Thickness(18, 16, 18, 16), Margin = new Thickness(6, 0, 6, 0), Child = col };
    }

    // столбики по дням; высота растёт с пружинкой
    private static FrameworkElement DayChart(StatsSummary s)
    {
        int max = Math.Max(60, s.PerDay.Count > 0 ? s.PerDay.Max(d => d.sec) : 60);
        var grid = new Grid { Height = 150 };
        int n = s.PerDay.Count;
        for (int i = 0; i < n; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < n; i++)
        {
            var (day, sec) = s.PerDay[i];
            var cell = new Grid { Background = Brushes.Transparent, ToolTip = day.ToString("d MMMM", Loc.Culture) + ": " + ListenStats.FmtDur(sec) };
            cell.RowDefinitions.Add(new RowDefinition());
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
            double h = sec <= 0 ? 3 : Math.Max(5, 118.0 * sec / max);
            bool today = day == DateTime.Today;
            var bar = new SBorder
            {
                Height = h, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(n > 14 ? 3 : 7),
                Margin = new Thickness(n > 14 ? 2 : 8, 0, n > 14 ? 2 : 8, 0),
                Background = sec <= 0 ? W(0x14) : new LinearGradientBrush(A2, A1, 90) { Opacity = today ? 1 : 0.75 },
                RenderTransformOrigin = new Point(0.5, 1)
            };
            var st = new ScaleTransform(1, 0);
            bar.RenderTransform = st;
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700))
            { BeginTime = TimeSpan.FromMilliseconds(150 + i * (n > 14 ? 12 : 40)), EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 } });
            cell.Children.Add(bar);
            bool label = n <= 14 || i % 5 == 4 || today;
            if (label)
            {
                var lb = new TextBlock
                {
                    Text = n <= 7 ? day.ToString("ddd", Loc.Culture) : day.Day.ToString(), Foreground = today ? Brushes.White : W(0x70),
                    FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom
                };
                Grid.SetRow(lb, 1);
                cell.Children.Add(lb);
            }
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        return grid;
    }

    private static FrameworkElement TrackRow(int rank, TopTrack t)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock { Text = rank.ToString(), Foreground = rank == 1 ? new LinearGradientBrush(A1, A2, 0) : W(0x70), FontSize = 15, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        var cov = ListenStats.Cover(t.Id);
        var art = new SBorder
        {
            Width = 44, Height = 44, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 12, 0),
            Background = cov != null ? new ImageBrush(cov) { Stretch = Stretch.UniformToFill } : W(0x14),
            Child = cov == null ? new VIcon { Text = "music", FontSize = 16, Foreground = W(0x80), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null
        };
        Grid.SetColumn(art, 1);
        g.Children.Add(art);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = t.Title, Foreground = Brushes.White, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        texts.Children.Add(new TextBlock { Text = t.Artist, Foreground = W(0x90), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(texts, 2);
        g.Children.Add(texts);
        var time = new TextBlock { Text = ListenStats.FmtDur(t.Sec), Foreground = W(0x90), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        Grid.SetColumn(time, 3);
        g.Children.Add(time);
        g.ToolTip = Loc.T("Прослушиваний: ") + t.Plays;
        return g;
    }

    private static FrameworkElement ArtistRow(int rank, TopArtist a, int max)
    {
        var col = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var top = new Grid();
        top.Children.Add(new TextBlock { Text = rank + "   " + a.Name, Foreground = Brushes.White, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 80, 0) });
        top.Children.Add(new TextBlock { Text = ListenStats.FmtDur(a.Sec), Foreground = W(0x90), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right });
        col.Children.Add(top);
        var track = new Grid { Height = 6, Margin = new Thickness(0, 7, 0, 0) };
        track.Children.Add(new SBorder { CornerRadius = new CornerRadius(3), Background = W(0x14) });
        var fill = new SBorder { CornerRadius = new CornerRadius(3), Background = new LinearGradientBrush(A1, A2, 0), HorizontalAlignment = HorizontalAlignment.Left, RenderTransformOrigin = new Point(0, 0.5) };
        track.SizeChanged += (_, e) => fill.Width = Math.Max(6, e.NewSize.Width * a.Sec / Math.Max(1, max));
        var st = new ScaleTransform(0, 1);
        fill.RenderTransform = st;
        st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(800)) { BeginTime = TimeSpan.FromMilliseconds(250 + rank * 60), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        track.Children.Add(fill);
        col.Children.Add(track);
        return col;
    }

    private static FrameworkElement VibeBlock(StatsSummary s)
    {
        var (title, sub) = ListenStats.Vibe(s.PeakHour);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock { Text = Loc.T(title), FontSize = 30, FontWeight = FontWeights.Black, Foreground = new LinearGradientBrush(A1, A2, 0) });
        left.Children.Add(new TextBlock { Text = Loc.T(sub), Foreground = W(0xB0), FontSize = 13.5, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        if (s.PeakHour >= 0)
            left.Children.Add(new TextBlock { Text = Loc.T("Чаще всего в ") + s.PeakHour.ToString("00") + ":00", Foreground = W(0x80), FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0) });
        g.Children.Add(left);

        // 24 столбика по часам дня
        var hours = new Grid { Height = 90 };
        int max = Math.Max(1, s.Hours.Max());
        for (int h = 0; h < 24; h++)
        {
            hours.ColumnDefinitions.Add(new ColumnDefinition());
            var cell = new Grid { ToolTip = h.ToString("00") + ":00  " + ListenStats.FmtDur(s.Hours[h]), Background = Brushes.Transparent };
            cell.RowDefinitions.Add(new RowDefinition());
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            cell.Children.Add(new SBorder
            {
                Height = Math.Max(3, 66.0 * s.Hours[h] / max), VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(3), Margin = new Thickness(2, 0, 2, 0),
                Background = h == s.PeakHour ? new LinearGradientBrush(A2, A1, 90) : W(0x26)
            });
            if (h % 6 == 0)
            {
                var lb = new TextBlock { Text = h.ToString("00"), Foreground = W(0x70), FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Left };
                Grid.SetRow(lb, 1);
                cell.Children.Add(lb);
            }
            Grid.SetColumn(cell, h);
            hours.Children.Add(cell);
        }
        Grid.SetColumn(hours, 1);
        g.Children.Add(hours);
        return g;
    }

    private void Share()
    {
        if (_sum == null) return;
        try
        {
            string period = _period switch { 7 => Loc.T("за неделю"), 30 => Loc.T("за месяц"), _ => Loc.T("за всё время") };
            var bmp = StoryCards.Wrapped(_sum, period);
            string path = StoryCards.Save(bmp, "ShyFlow-итоги");
            MacDialog.Info(this, Loc.T("Картинка итогов сохранена и скопирована в буфер: её можно сразу вставить в сторис или чат.") + "\n\n" + path);
            StoryCards.ShowInFolder(path);
        }
        catch (Exception ex) { App.Log("Stats.Share", ex); MacDialog.Info(this, ex.Message); }
    }
}
