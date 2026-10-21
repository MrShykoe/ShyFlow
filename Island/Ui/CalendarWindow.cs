using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Island;

// Календарь (шаг 54): месяц сеткой, праздники проставлены сами (Services\Holidays.cs), свои события — по клику на день.
// Справа: выбранный день, его праздники и события, поле «Добавить событие» (Enter), «каждый год» для дней рождения,
// ниже ближайшие праздники и события. Открывается кликом по пузырю с датой или временем и плиткой «Календарь».
public sealed class CalendarWindow : Window
{
    private static CalendarWindow? _open;

    public static void ShowCalendar()
    {
        if (_open != null) { _open.Activate(); return; }
        _open = new CalendarWindow();
        _open.Closed += (_, _) => _open = null;
        _open.Show();
    }

    private static readonly Color Accent = Color.FromRgb(0xFF, 0x45, 0x3A), HolC = Color.FromRgb(0xFF, 0x9F, 0x0A), EvC = Color.FromRgb(0x0A, 0x84, 0xFF);
    private static SolidColorBrush W(byte a) => new(Color.FromArgb(a, 255, 255, 255));

    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _sel = DateTime.Today;
    private readonly TextBlock _monthText = new();
    private readonly UniformGrid _grid = new() { Columns = 7, Rows = 6 };
    private readonly StackPanel _side = new();
    private readonly TextBox _input = new();
    private bool _yearly;

    private CalendarWindow()
    {
        Title = Loc.T("Календарь");
        Width = 860; Height = 560;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var root = new SBorder
        {
            Margin = new Thickness(14), CornerRadius = new CornerRadius(26), Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1F)),
            BorderBrush = W(0x2E), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 34, Opacity = 0.6, ShadowDepth = 6, Direction = 270 }
        };
        var shell = new Grid();
        shell.ColumnDefinitions.Add(new ColumnDefinition());
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        root.Child = shell;
        shell.SizeChanged += (_, e) => shell.Clip = Smooth.Shape(e.NewSize.Width, e.NewSize.Height, new CornerRadius(25));

        // ---------- месяц ----------
        var left = new Grid { Margin = new Thickness(24, 18, 18, 22) };
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        left.RowDefinitions.Add(new RowDefinition());
        var head = new Grid { Background = Brushes.Transparent };
        head.MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } };
        _monthText.Foreground = Brushes.White; _monthText.FontSize = 22; _monthText.FontWeight = FontWeights.Bold; _monthText.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(_monthText);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        nav.Children.Add(Btn("chevron-left", null, () => { _month = _month.AddMonths(-1); Render(); }));
        nav.Children.Add(Btn(null, Loc.T("Сегодня"), () => { _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); _sel = DateTime.Today; Render(); }));
        nav.Children.Add(Btn("chevron-right", null, () => { _month = _month.AddMonths(1); Render(); }));
        head.Children.Add(nav);
        left.Children.Add(head);

        var dows = new UniformGrid { Columns = 7 };
        var names = Loc.Culture.DateTimeFormat.AbbreviatedDayNames;
        for (int i = 0; i < 7; i++)
            dows.Children.Add(new TextBlock { Text = names[(i + 1) % 7], Foreground = W(i >= 5 ? (byte)0x90 : (byte)0x70), FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetRow(dows, 1);
        left.Children.Add(dows);
        Grid.SetRow(_grid, 2);
        left.Children.Add(_grid);
        shell.Children.Add(left);

        // ---------- день ----------
        var sideBox = new Border { Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)) };
        Grid.SetColumn(sideBox, 1);
        shell.Children.Add(sideBox);
        var sideGrid = new Grid { Margin = new Thickness(20, 18, 20, 20) };
        sideGrid.RowDefinitions.Add(new RowDefinition());
        sideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(sideGrid, 1);
        var close = Btn("x", null, Close);
        close.HorizontalAlignment = HorizontalAlignment.Right; close.VerticalAlignment = VerticalAlignment.Top;
        sideGrid.Children.Add(new ScrollViewer { Content = _side, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Margin = new Thickness(0, 0, 0, 10) });
        sideGrid.Children.Add(close);

        // поле «добавить событие» и «каждый год»
        var add = new StackPanel();
        Grid.SetRow(add, 1);
        var box = new SBorder { CornerRadius = new CornerRadius(14), Background = W(0x1A), Height = 36 };
        var bg = new Grid();
        _input.Background = Brushes.Transparent; _input.BorderThickness = new Thickness(0); _input.Foreground = Brushes.White; _input.CaretBrush = Brushes.White;
        _input.FontSize = 13; _input.Padding = new Thickness(12, 0, 12, 0); _input.VerticalContentAlignment = VerticalAlignment.Center;
        var hint = new TextBlock { Text = Loc.T("Добавить событие и Enter"), Foreground = W(0x66), FontSize = 13, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        _input.TextChanged += (_, _) => hint.Visibility = _input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || _input.Text.Trim().Length == 0) return;
            Holidays.AddEvent(_sel, _input.Text, _yearly);
            _input.Text = "";
            Render();
            e.Handled = true;
        };
        bg.Children.Add(_input);
        bg.Children.Add(hint);
        box.Child = bg;
        add.Children.Add(box);
        var yr = new TextBlock { FontSize = 12, Margin = new Thickness(4, 8, 0, 0), Cursor = Cursors.Hand };
        void Yr() { yr.Text = (_yearly ? "☑ " : "☐ ") + Loc.T("каждый год (день рождения, годовщина)"); yr.Foreground = _yearly ? Brushes.White : W(0x90); }
        Yr();
        yr.MouseLeftButtonUp += (_, _) => { _yearly = !_yearly; Yr(); };
        add.Children.Add(yr);
        sideGrid.Children.Add(add);
        shell.Children.Add(sideGrid);

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

    private FrameworkElement Btn(string? icon, string? text, Action act)
    {
        UIElement child = icon != null
            ? new VIcon { Text = icon, FontSize = 13, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            : new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var b = new SBorder
        {
            Height = 32, MinWidth = 32, CornerRadius = new CornerRadius(16), Background = W(0x1A), Cursor = Cursors.Hand, Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(icon != null ? 0 : 14, 0, icon != null ? 0 : 14, 0), Child = child
        };
        b.MouseEnter += (_, _) => b.Background = W(0x30);
        b.MouseLeave += (_, _) => b.Background = W(0x1A);
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; act(); };
        return b;
    }

    private void Render()
    {
        string m = _month.ToString("MMMM yyyy", Loc.Culture);
        _monthText.Text = char.ToUpper(m[0], Loc.Culture) + m[1..];
        _grid.Children.Clear();
        int shift = ((int)_month.DayOfWeek + 6) % 7;   // неделя с понедельника
        var start = _month.AddDays(-shift);
        for (int i = 0; i < 42; i++)
        {
            var d = start.AddDays(i);
            _grid.Children.Add(DayCell(d, d.Month == _month.Month));
        }
        RenderSide();
    }

    private FrameworkElement DayCell(DateTime d, bool inMonth)
    {
        var marks = Holidays.For(d);
        bool today = d == DateTime.Today, sel = d == _sel;
        bool weekend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || marks.Any(x => x.Official);
        var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var num = new SBorder
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17), HorizontalAlignment = HorizontalAlignment.Center,
            Background = today ? new SolidColorBrush(Accent) : sel ? W(0x2A) : Brushes.Transparent,
            Child = new TextBlock
            {
                Text = d.Day.ToString(), FontSize = 14, FontWeight = today || sel ? FontWeights.Bold : FontWeights.Medium,
                Foreground = today ? Brushes.White : !inMonth ? W(0x40) : weekend ? new SolidColorBrush(Color.FromRgb(0xFF, 0x80, 0x78)) : Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        col.Children.Add(num);
        // точки: оранжевая — праздник, синяя — своё событие
        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Height = 8, Margin = new Thickness(0, 3, 0, 0) };
        if (marks.Any(x => x.Holiday)) dots.Children.Add(new Ellipse { Width = 5, Height = 5, Fill = new SolidColorBrush(HolC), Margin = new Thickness(1.5, 0, 1.5, 0), Opacity = inMonth ? 1 : 0.4 });
        if (marks.Any(x => !x.Holiday)) dots.Children.Add(new Ellipse { Width = 5, Height = 5, Fill = new SolidColorBrush(EvC), Margin = new Thickness(1.5, 0, 1.5, 0), Opacity = inMonth ? 1 : 0.4 });
        col.Children.Add(dots);
        var cell = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = col, ToolTip = marks.Count > 0 ? string.Join("\n", marks.Select(x => x.Text)) : null };
        cell.MouseEnter += (_, _) => { if (!today && !sel) num.Background = W(0x14); };
        cell.MouseLeave += (_, _) => { if (!today && !sel) num.Background = Brushes.Transparent; };
        cell.MouseLeftButtonUp += (_, _) =>
        {
            _sel = d;
            if (d.Month != _month.Month) _month = new DateTime(d.Year, d.Month, 1);
            Render();
            _input.Focus();
        };
        return cell;
    }

    private void RenderSide()
    {
        _side.Children.Clear();
        string day = _sel.ToString("d MMMM, dddd", Loc.Culture);
        _side.Children.Add(new TextBlock { Text = day, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 40, 4), TextWrapping = TextWrapping.Wrap });
        if (_sel == DateTime.Today) _side.Children.Add(new TextBlock { Text = Loc.T("сегодня"), Foreground = new SolidColorBrush(Accent), FontSize = 12.5, FontWeight = FontWeights.SemiBold });

        var hol = Holidays.For(_sel).Where(x => x.Holiday).ToList();
        var ev = Holidays.UserEventsWithKind(_sel);
        if (hol.Count == 0 && ev.Count == 0)
            _side.Children.Add(new TextBlock { Text = Loc.T("Ничего нет. Можно добавить своё событие ниже"), Foreground = W(0x70), FontSize = 12.5, Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap });
        foreach (var h in hol) _side.Children.Add(Row("sparkles", HolC, h.Text + (h.Official ? "  · " + Loc.T("выходной") : ""), null));
        foreach (var (text, yearly) in ev)
        {
            var t = text; var y = yearly;
            _side.Children.Add(Row(y ? "star" : "calendar", EvC, t + (y ? "  · " + Loc.T("каждый год") : ""), () => { Holidays.RemoveEvent(_sel, t, y); Render(); }));
        }

        _side.Children.Add(new TextBlock { Text = Loc.T("Ближайшее"), Foreground = W(0xA0), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 6) });
        foreach (var (d, mark) in Holidays.Upcoming(6))
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 7), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            int left = (d - DateTime.Today).Days;
            g.Children.Add(new TextBlock { Text = left == 0 ? Loc.T("сегодня") : left == 1 ? Loc.T("завтра") : d.ToString("d MMM", Loc.Culture), Foreground = left <= 1 ? new SolidColorBrush(Accent) : W(0x80), FontSize = 12 });
            var tx = new TextBlock { Text = mark.Text, Foreground = Brushes.White, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(tx, 1);
            g.Children.Add(tx);
            var day0 = d;
            g.MouseLeftButtonUp += (_, _) => { _sel = day0; _month = new DateTime(day0.Year, day0.Month, 1); Render(); };
            _side.Children.Add(g);
        }
    }

    private static FrameworkElement Row(string icon, Color c, string text, Action? remove)
    {
        var g = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new SBorder
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Color.FromArgb(0x33, c.R, c.G, c.B)), VerticalAlignment = VerticalAlignment.Top,
            Child = new VIcon { Text = icon, FontSize = 13, Foreground = new SolidColorBrush(c), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        });
        var t = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 5, 6, 0) };
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        if (remove != null)
        {
            var x = new SBorder
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = W(0x20), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0),
                ToolTip = Loc.T("Удалить"), Child = new VIcon { Text = "x", FontSize = 10, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            x.MouseLeftButtonUp += (_, e) => { e.Handled = true; remove(); };
            Grid.SetColumn(x, 2);
            g.Children.Add(x);
        }
        return g;
    }
}
