using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Island;

// Окно «Коллекция»: все собранные карточки сеткой, сверху счётчики по редкостям и сортировка (новые, оценка, редкость).
// Клик по карточке: она открывается крупно, её можно покрутить мышкой и удалить (со второго нажатия).
// Открывается кнопкой в плеере на весь экран и из меню острова (правый клик, «Коллекция карточек»).
public sealed class CollectionWindow : Window
{
    private static CollectionWindow? _open;

    public static void ShowCollection()
    {
        if (_open != null) { _open.Activate(); return; }
        _open = new CollectionWindow();
        _open.Closed += (_, _) => _open = null;
        _open.Show();
    }

    // сразу с открытыми достижениями (клик по плашке «Достижение: …»)
    public static void ShowAchievements()
    {
        ShowCollection();
        if (_open!.IsLoaded) _open.OpenAchievements();
        else _open.Loaded += (_, _) => _open?.OpenAchievements();
    }

    private readonly TextBlock _achText = new();

    private readonly WrapPanel _grid = new() { Margin = new Thickness(18, 4, 18, 24) };
    private readonly TextBlock _count = new();
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _overlay = new() { Visibility = Visibility.Collapsed, Background = new SolidColorBrush(Color.FromArgb(0xC8, 0, 0, 0)) };
    private readonly TextBlock _empty = new();
    private readonly List<TextBlock> _sortBtns = new();
    private int _sort;   // 0 новые, 1 оценка, 2 редкость, 3 исполнители (больше карточек — выше)

    private CollectionWindow()
    {
        Title = Loc.T("Коллекция карточек");
        Width = 1000; Height = 700;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var root = new SBorder
        {
            Margin = new Thickness(14), CornerRadius = new CornerRadius(26), Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1F)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 255, 255, 255)), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 34, Opacity = 0.65, ShadowDepth = 6, Direction = 270 }
        };
        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });
        shell.RowDefinitions.Add(new RowDefinition());
        root.Child = shell;
        shell.SizeChanged += (_, e) => shell.Clip = Smooth.Shape(e.NewSize.Width, e.NewSize.Height, new CornerRadius(25));

        // шапка: название и число, счётчики редкостей, сортировка, крестик
        var head = new Grid { Background = Brushes.Transparent, Margin = new Thickness(24, 0, 18, 0) };
        head.MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { /* окно уже закрывается */ } };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        titleRow.Children.Add(new SBorder
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x40)),
            Child = new VIcon { Text = "star", FontSize = 16, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        });
        titleRow.Children.Add(new TextBlock { Text = Loc.T("Коллекция"), Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(12, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        _count.Foreground = new SolidColorBrush(Color.FromRgb(0x98, 0x98, 0x9D));
        _count.FontSize = 14;
        _count.VerticalAlignment = VerticalAlignment.Center;
        titleRow.Children.Add(_count);
        _chips.Margin = new Thickness(18, 0, 0, 0);
        titleRow.Children.Add(_chips);
        head.Children.Add(titleRow);

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var sortBox = new SBorder { CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255)), Padding = new Thickness(3), Margin = new Thickness(0, 0, 12, 0) };
        var sorts = new StackPanel { Orientation = Orientation.Horizontal };
        string[] names = { "Новые", "Оценка", "Редкость", "Исполнители" };   // шаг 73: по исполнителям, наборы вместе
        for (int i = 0; i < names.Length; i++)
        {
            int idx = i;
            var tb = new TextBlock { Text = Loc.T(names[i]), Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.Medium, Margin = new Thickness(11, 4, 11, 4) };
            var b = new SBorder { CornerRadius = new CornerRadius(7), Child = tb, Cursor = Cursors.Hand, Background = Brushes.Transparent };
            // нажатие не должно уходить в шапку: там окно перетаскивается (DragMove), и отпускание кнопки терялось
            b.MouseLeftButtonDown += (_, e) => e.Handled = true;
            b.MouseLeftButtonUp += (_, _) => { _sort = idx; Render(); };
            _sortBtns.Add(tb);
            sorts.Children.Add(b);
        }
        sortBox.Child = sorts;
        // шаг 49: достижения (Services\Achievements.cs)
        var achRow = new StackPanel { Orientation = Orientation.Horizontal };
        achRow.Children.Add(new VIcon { Text = "trophy", FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x3A)), VerticalAlignment = VerticalAlignment.Center });
        _achText.Foreground = Brushes.White; _achText.FontSize = 12.5; _achText.FontWeight = FontWeights.Medium; _achText.Margin = new Thickness(7, 0, 0, 0); _achText.VerticalAlignment = VerticalAlignment.Center;
        achRow.Children.Add(_achText);
        var achBtn = new SBorder { CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255)), Padding = new Thickness(12, 7, 13, 7), Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand, Child = achRow };
        achBtn.MouseLeftButtonDown += (_, e) => e.Handled = true;
        achBtn.MouseLeftButtonUp += (_, _) => OpenAchievements();
        right.Children.Add(achBtn);
        right.Children.Add(sortBox);
        var close = new SBorder
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255)), Cursor = Cursors.Hand,
            Child = new VIcon { Text = "x", FontSize = 13, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        close.MouseLeftButtonDown += (_, e) => e.Handled = true;   // иначе шапка начинает перетаскивать окно, и крестик не срабатывал
        close.MouseLeftButtonUp += (_, _) => Close();
        right.Children.Add(close);
        head.Children.Add(right);
        shell.Children.Add(head);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _grid };
        Grid.SetRow(scroll, 1);
        shell.Children.Add(scroll);
        _empty.Text = Loc.T("Пока пусто. Включи музыку, раскрой остров и нажми ★, чтобы забрать обложку в коллекцию. Первая карточка за день — карточка дня, у неё больше шансов оказаться редкой",
                            "Nothing here yet. Play some music, open the island and press ★ to collect the cover. The first card each day is the card of the day, with better odds of being rare");
        _empty.Foreground = new SolidColorBrush(Color.FromRgb(0x98, 0x98, 0x9D));
        _empty.FontSize = 15; _empty.TextWrapping = TextWrapping.Wrap; _empty.TextAlignment = TextAlignment.Center;
        _empty.MaxWidth = 460; _empty.HorizontalAlignment = HorizontalAlignment.Center; _empty.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(_empty, 1);
        shell.Children.Add(_empty);

        Grid.SetRowSpan(_overlay, 2);
        _overlay.MouseLeftButtonUp += (_, e) => { if (ReferenceEquals(e.OriginalSource, _overlay)) CloseInspect(); };
        shell.Children.Add(_overlay);

        Content = root;
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (_overlay.Visibility == Visibility.Visible) CloseInspect(); else Close();
        };
        CardCollection.Changed += Render;
        Closed += (_, _) => CardCollection.Changed -= Render;
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

    private void Render()
    {
        var all = CardCollection.All;
        _count.Text = all.Count.ToString();
        try { Achievements.Check(); } catch (Exception ex) { App.LogOnce("Ach.Check", ex); }   // тихо: в окне и так всё видно
        _achText.Text = Loc.T("Достижения") + "  " + Achievements.Got.Count + "/" + Achievements.All.Count;
        _empty.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < _sortBtns.Count; i++) _sortBtns[i].Opacity = i == _sort ? 1 : 0.55;

        // счётчики: цветная точка редкости и сколько таких
        _chips.Children.Clear();
        // шаг 73: собранные наборы (3 карточки одного исполнителя)
        int sets = CardCollection.SetsCount;
        if (sets > 0)
        {
            var sc = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0), ToolTip = Loc.T("Собрано наборов: 3 карточки одного исполнителя", "Sets: 3 cards by the same artist") };
            sc.Children.Add(new TextBlock { Text = "◆", Foreground = new SolidColorBrush(CollectibleCard.Accent(4)), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            sc.Children.Add(new TextBlock { Text = sets.ToString(), Foreground = Brushes.White, FontSize = 12.5, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            _chips.Children.Add(sc);
        }
        for (int r = 4; r >= 0; r--)
        {
            int n = all.Count(c => c.Rarity == r);
            if (n == 0) continue;
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0), ToolTip = Loc.T(CardCollection.RarityNames[r]) };
            chip.Children.Add(new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(CollectibleCard.Accent(r)), VerticalAlignment = VerticalAlignment.Center });
            chip.Children.Add(new TextBlock { Text = n.ToString(), Foreground = Brushes.White, FontSize = 12.5, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            _chips.Children.Add(chip);
        }

        IEnumerable<CardEntry> sorted = _sort switch
        {
            1 => all.OrderByDescending(c => c.Score).ThenByDescending(c => c.Rarity),
            2 => all.OrderByDescending(c => c.Rarity).ThenByDescending(c => c.Foil).ThenByDescending(c => c.Score),
            3 => all.GroupBy(c => CardCollection.ArtistKey(c.Artist)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                    .SelectMany(g => g.OrderByDescending(c => c.Score)),
            _ => all.OrderByDescending(c => c.Added)
        };
        _grid.Children.Clear();
        int k = 0;
        foreach (var c in sorted)
        {
            var card = CollectibleCard.Tile(c, CardCollection.Image(c), 170);
            var holder = new Border { Margin = new Thickness(10), Child = card, Cursor = Cursors.Hand, Background = Brushes.Transparent, RenderTransformOrigin = new Point(0.5, 0.5) };
            var tf = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform() } };
            holder.RenderTransform = tf;
            void Lift(double s, double y, int ms)
            {
                var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
                ((ScaleTransform)tf.Children[0]).BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(s, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
                ((ScaleTransform)tf.Children[0]).BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(s, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
                ((TranslateTransform)tf.Children[1]).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
            }
            holder.MouseEnter += (_, _) => Lift(1.05, -6, 260);
            holder.MouseLeave += (_, _) => Lift(1, 0, 320);
            var entry = c;
            holder.MouseLeftButtonUp += (_, _) => Inspect(entry);
            var menu = new ContextMenu();
            var listen = new MenuItem { Header = Loc.T("Слушать в Spotify") };
            listen.Click += (_, _) => Spotify.OpenTrack(entry.Title, entry.Artist);
            var open = new MenuItem { Header = Loc.T("Открыть карточку") };
            open.Click += (_, _) => Inspect(entry);
            menu.Items.Add(listen);
            menu.Items.Add(open);
            holder.ContextMenu = menu;
            // карточки выкладываются по очереди
            holder.Opacity = 0;
            var delay = TimeSpan.FromMilliseconds(Math.Min(k, 24) * 28);
            holder.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = delay });
            ((TranslateTransform)tf.Children[1]).BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = delay, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            _grid.Children.Add(holder);
            k++;
        }
    }

    // ---------- осмотр: большая обложка и панель с описанием ----------
    private void Inspect(CardEntry c)
    {
        _overlay.Children.Clear();
        var show = CollectibleCard.Showcase(c, CardCollection.Image(c), 400,
            onDelete: () => { CardCollection.Remove(c); CloseInspect(); },
            onClose: CloseInspect);
        _overlay.Children.Add(show);
        _overlay.Visibility = Visibility.Visible;
        _overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
    }
    // ---------- достижения: значки сеткой, полученные цветные с датой, остальные серые с прогрессом ----------
    private void OpenAchievements()
    {
        _overlay.Children.Clear();
        var ctx = Achievements.Context();
        var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        int k = 0;
        foreach (var a in Achievements.All.OrderByDescending(x => Achievements.Got.ContainsKey(x.Id)))
        {
            var badge = AchBadge(a, ctx);
            badge.Opacity = 0;
            var tt = new TranslateTransform(0, 14);
            badge.RenderTransform = tt;
            var delay = TimeSpan.FromMilliseconds(k++ * 30);
            badge.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { BeginTime = delay });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = delay, EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } });
            wrap.Children.Add(badge);
        }
        var col = new StackPanel();
        var top = new Grid { Margin = new Thickness(8, 0, 8, 14) };
        top.Children.Add(new TextBlock { Text = Loc.T("Достижения") + "  " + Achievements.Got.Count + "/" + Achievements.All.Count, Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.Bold });
        var x = new SBorder
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255)), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right,
            Child = new VIcon { Text = "x", FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        x.MouseLeftButtonUp += (_, e) => { e.Handled = true; CloseInspect(); };
        top.Children.Add(x);
        col.Children.Add(top);
        col.Children.Add(new ScrollViewer { Content = wrap, MaxHeight = 500, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var panel = new SBorder
        {
            Width = 820, CornerRadius = new CornerRadius(24), Padding = new Thickness(18, 20, 18, 18), Child = col,
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x1E, 0x1E, 0x22)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        _overlay.Children.Add(panel);
        _overlay.Visibility = Visibility.Visible;
        _overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
    }

    private static FrameworkElement AchBadge(AchDef a, AchCtx ctx)
    {
        bool got = Achievements.Got.TryGetValue(a.Id, out var when);
        var (cur, max) = a.Progress(ctx);
        var c = Achievements.ColorOf(a);
        var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var circle = new SBorder
        {
            Width = 58, Height = 58, CornerRadius = new CornerRadius(29), HorizontalAlignment = HorizontalAlignment.Center,
            Background = got ? new LinearGradientBrush(Color.FromRgb((byte)Math.Min(255, c.R + 40), (byte)Math.Min(255, c.G + 40), (byte)Math.Min(255, c.B + 40)), c, 45)
                             : new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255)),
            Effect = got ? new DropShadowEffect { Color = c, BlurRadius = 22, ShadowDepth = 0, Opacity = 0.55 } : null,
            Child = new VIcon { Text = got ? a.Icon : "lock", FontSize = 22, Foreground = got ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x70, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        col.Children.Add(circle);
        col.Children.Add(new TextBlock { Text = Loc.T(a.Title), Foreground = got ? Brushes.White : new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255)), FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        col.Children.Add(new TextBlock { Text = Loc.T(a.Desc), Foreground = new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255)), FontSize = 11.5, Margin = new Thickness(0, 3, 0, 0), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center });
        if (got)
            col.Children.Add(new TextBlock { Text = when.ToString("d MMM yyyy", Loc.Culture), Foreground = new SolidColorBrush(c), FontSize = 11, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        else if (max > 1)
        {
            var bar = new Grid { Width = 110, Height = 5, Margin = new Thickness(0, 8, 0, 0) };
            bar.Children.Add(new SBorder { CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)) });
            bar.Children.Add(new SBorder { CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(c), HorizontalAlignment = HorizontalAlignment.Left, Width = 110.0 * Math.Clamp((double)cur / max, 0, 1) });
            col.Children.Add(bar);
            col.Children.Add(new TextBlock { Text = Math.Min(cur, max) + " / " + max, Foreground = new SolidColorBrush(Color.FromArgb(0x70, 255, 255, 255)), FontSize = 10.5, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        }
        return new SBorder
        {
            Width = 180, Height = 196, Margin = new Thickness(7), CornerRadius = new CornerRadius(18), Padding = new Thickness(12, 16, 12, 12),
            Background = new SolidColorBrush(Color.FromArgb(got ? (byte)0x14 : (byte)0x0A, 255, 255, 255)), Child = col
        };
    }

    private void CloseInspect()
    {
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        a.Completed += (_, _) => { _overlay.Visibility = Visibility.Collapsed; _overlay.Children.Clear(); };
        _overlay.BeginAnimation(OpacityProperty, a);
    }
}
