using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Island;

// Как выглядит обложка из коллекции (шаг 46: вместо «игральной карточки» просто обложка и панель с описанием).
//   Showcase: большая обложка (наклоняется за мышкой, по ней бегает блик, у голографической переливается радуга,
//             у редких и выше свечение цвета редкости) и рядом стеклянная панель: редкость, номер, название, исполнитель,
//             большая оценка X/10, пять характеристик полосками, дата и кнопки.
//   Tile:     для сетки коллекции: обложка с тонкой рамкой цвета редкости, оценка в углу, под ней название и исполнитель.
internal static class CollectibleCard
{
    // цвет редкости: светлый (рамки, текст) и тёмный (тень)
    public static readonly (Color a, Color b)[] Frames =
    {
        (Color.FromRgb(0xAE, 0xAE, 0xB2), Color.FromRgb(0x48, 0x48, 0x4A)),   // обычная
        (Color.FromRgb(0x4C, 0xE0, 0x6E), Color.FromRgb(0x1A, 0x6E, 0x33)),   // необычная
        (Color.FromRgb(0x40, 0xA0, 0xFF), Color.FromRgb(0x0A, 0x3A, 0x8C)),   // редкая
        (Color.FromRgb(0xD3, 0x7B, 0xFF), Color.FromRgb(0x5A, 0x24, 0x86)),   // эпическая
        (Color.FromRgb(0xFF, 0xD0, 0x4A), Color.FromRgb(0xB8, 0x7A, 0x0C)),   // легендарная
    };

    public static Color Accent(int rarity) => Frames[Math.Clamp(rarity, 0, 4)].a;

    private static SolidColorBrush B(byte a, Color c) => new(Color.FromArgb(a, c.R, c.G, c.B));
    private static SolidColorBrush W(byte a) => new(Color.FromArgb(a, 255, 255, 255));

    public static string RarityText(CardEntry e) =>
        Loc.T(CardCollection.RarityNames[Math.Clamp(e.Rarity, 0, 4)]).ToUpper(Loc.Culture) + (e.Foil ? "  ✦ " + Loc.T("ГОЛО") : "")
        + (e.Daily ? "  ☀ " + Loc.T("ДНЯ", "DAILY") : "");

    // шаг 73: картинка карточки для друзей: «Изображения\ShyFlow» и буфер обмена
    public static string? ShareImage(CardEntry e)
    {
        try { return StoryCards.Save(StoryCards.Card(e, CardCollection.Image(e)), "ShyFlow-card"); }
        catch (Exception ex) { App.Log("Card.Share", ex); return null; }
    }

    // ---------- большая обложка + панель ----------
    public static FrameworkElement Showcase(CardEntry e, BitmapSource? img, double size, Action? onDelete, Action? onClose, bool share = true)
    {
        var acc = Accent(e.Rarity);
        var root = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });

        root.Children.Add(Cover(e, img, size));

        // панель с описанием
        var panel = new SBorder
        {
            CornerRadius = new CornerRadius(22), Padding = new Thickness(24, 22, 24, 22), VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x1A, 0x1A, 0x1D)), BorderBrush = W(0x22), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.45 }
        };
        Grid.SetColumn(panel, 2);
        var col = new StackPanel();
        panel.Child = col;

        var top = new Grid();
        top.Children.Add(new SBorder
        {
            CornerRadius = new CornerRadius(10), Background = B(0x2E, acc), Padding = new Thickness(10, 3, 10, 4), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = RarityText(e), Foreground = new SolidColorBrush(acc), FontSize = 11.5, FontWeight = FontWeights.Bold }
        });
        top.Children.Add(new TextBlock { Text = "#" + e.Number.ToString("000"), Foreground = W(0x80), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
        col.Children.Add(top);

        col.Children.Add(new TextBlock
        {
            Text = e.Title, Foreground = Brushes.White, FontSize = 22, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 60, Margin = new Thickness(0, 14, 0, 0)
        });
        col.Children.Add(new TextBlock { Text = e.Artist, Foreground = W(0xA8), FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });

        var score = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 2) };
        score.Children.Add(new TextBlock { Text = e.Score.ToString(), Foreground = new SolidColorBrush(acc), FontSize = 60, FontWeight = FontWeights.Black, LineHeight = 60, LineStackingStrategy = LineStackingStrategy.BlockLineHeight });
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 0, 8) };
        right.Children.Add(new TextBlock { Text = "/10", Foreground = W(0x99), FontSize = 20, FontWeight = FontWeights.SemiBold });
        right.Children.Add(new TextBlock { Text = Loc.T("общая оценка"), Foreground = W(0x70), FontSize = 11.5 });
        score.Children.Add(right);
        col.Children.Add(score);

        var stats = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var (name, v) in new[] { ("Краски", e.Colors), ("Контраст", e.Contrast), ("Детали", e.Detail), ("Гармония", e.Harmony), ("Удача", e.Luck) })
            stats.Children.Add(StatRow(Loc.T(name), v, acc));
        col.Children.Add(stats);

        col.Children.Add(new TextBlock
        {
            Text = Loc.T("В коллекции с ") + e.Added.ToString("d MMMM yyyy", Loc.Culture), Foreground = W(0x70), FontSize = 12, Margin = new Thickness(0, 14, 0, 0)
        });

        if (onClose != null)   // в коллекции (в плеере после ★ трек и так играет)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            row.Children.Add(new VIcon { Text = "play", FontSize = 13, Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            row.Children.Add(new TextBlock { Text = Loc.T("Слушать в Spotify"), Foreground = Brushes.Black, FontSize = 13.5, FontWeight = FontWeights.SemiBold });
            var spot = Pill(row, new SolidColorBrush(Color.FromRgb(0x1E, 0xD7, 0x60)));
            spot.Margin = new Thickness(0, 18, 0, 0);
            spot.ToolTip = Loc.T("Откроет Spotify с поиском этого трека");
            spot.MouseLeftButtonUp += (_, ev) => { ev.Handled = true; Spotify.OpenTrack(e.Title, e.Artist); };
            col.Children.Add(spot);
        }

        if (onDelete != null || onClose != null || share)
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, onClose != null ? 10 : 18, 0, 0) };
            if (share)
            {
                // шаг 73: «Поделиться» — картинка карточки 1080x1920 сразу в буфере, вставь в сторис или чат
                var shRow = new StackPanel { Orientation = Orientation.Horizontal };
                shRow.Children.Add(new VIcon { Text = "share", FontSize = 12, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
                var shTxt = new TextBlock { Text = Loc.T("Поделиться", "Share"), Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.Medium };
                shRow.Children.Add(shTxt);
                var sh = Pill(shRow, B(0x40, acc));
                sh.ToolTip = Loc.T("Картинка карточки: сохранится в «Изображения\\ShyFlow» и скопируется, вставь в сторис или чат",
                                   "Card picture: saved to Pictures\\ShyFlow and copied, paste it into a story or chat");
                sh.MouseLeftButtonDown += (_, ev) => ev.Handled = true;
                sh.MouseLeftButtonUp += (_, ev) =>
                {
                    ev.Handled = true;
                    var path = ShareImage(e);
                    shTxt.Text = path != null ? Loc.T("Скопировано ✓", "Copied ✓") : Loc.T("Не получилось", "Failed");
                    if (path != null) { try { Sfx.Ui("unlock"); } catch { } sh.ToolTip = path; }
                };
                buttons.Children.Add(sh);
            }
            if (onDelete != null)
            {
                bool armed = false;
                var txt = new TextBlock { Text = Loc.T("Удалить"), Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.Medium };
                var del = Pill(txt, W(0x22));
                del.MouseLeftButtonUp += (_, ev) =>
                {
                    ev.Handled = true;
                    if (!armed) { armed = true; txt.Text = Loc.T("Точно удалить?"); del.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)); return; }
                    onDelete();
                };
                buttons.Children.Add(del);
            }
            if (onClose != null)
            {
                var close = Pill(new TextBlock { Text = Loc.T("Закрыть"), Foreground = Brushes.Black, FontSize = 13, FontWeight = FontWeights.SemiBold }, Brushes.White);
                close.MouseLeftButtonUp += (_, ev) => { ev.Handled = true; onClose(); };
                buttons.Children.Add(close);
            }
            col.Children.Add(buttons);
        }
        root.Children.Add(panel);

        // панель выезжает сбоку, обложка появляется с пружиной
        var pt = new TranslateTransform(30, 0);
        panel.RenderTransform = pt;
        panel.Opacity = 0;
        panel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { BeginTime = TimeSpan.FromMilliseconds(120) });
        pt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(520))
        { BeginTime = TimeSpan.FromMilliseconds(120), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        return root;
    }

    private static SBorder Pill(UIElement content, Brush bg) => new()
    {
        CornerRadius = new CornerRadius(17), Background = bg, Padding = new Thickness(20, 8, 20, 8), Margin = new Thickness(0, 0, 10, 0),
        Cursor = Cursors.Hand, Child = content
    };

    private static FrameworkElement StatRow(string name, int v, Color c)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        g.Children.Add(new TextBlock { Text = name, Foreground = W(0xC0), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
        var track = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center };
        track.Children.Add(new SBorder { CornerRadius = new CornerRadius(3), Background = W(0x22) });
        var fill = new SBorder { CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(c), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        track.Children.Add(fill);
        track.SizeChanged += (_, e) =>
            fill.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(0, e.NewSize.Width * v / 10.0, TimeSpan.FromMilliseconds(700))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, BeginTime = TimeSpan.FromMilliseconds(250) });
        Grid.SetColumn(track, 1);
        g.Children.Add(track);
        var num = new TextBlock { Text = v.ToString(), Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(num, 2);
        g.Children.Add(num);
        return g;
    }

    // большая обложка: тень или свечение редкости, наклон за мышкой, блик, радуга у голографической
    private static FrameworkElement Cover(CardEntry e, BitmapSource? img, double size)
    {
        var acc = Accent(e.Rarity);
        var cover = new SBorder
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.06),
            Background = img != null ? new ImageBrush(img) { Stretch = Stretch.UniformToFill } : new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x30)),
            RenderTransformOrigin = new Point(0.5, 0.5), Cursor = Cursors.Hand,
            Effect = new DropShadowEffect
            {
                Color = e.Rarity >= 2 ? acc : Colors.Black, BlurRadius = 60, ShadowDepth = e.Rarity >= 2 ? 0 : 16, Direction = 270,
                Opacity = e.Rarity >= 2 ? 0.55 + 0.1 * (e.Rarity - 2) : 0.55
            }
        };
        if (cover.Background is ImageBrush ib) RenderOptions.SetBitmapScalingMode(ib, BitmapScalingMode.HighQuality);
        var layers = new Grid();
        layers.SizeChanged += (_, ev) => layers.Clip = Smooth.Shape(ev.NewSize.Width, ev.NewSize.Height, cover.CornerRadius);
        cover.Child = layers;

        LinearGradientBrush? foil = null;
        if (e.Foil)
        {
            foil = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1), Opacity = 0.2, RelativeTransform = new TranslateTransform() };
            foreach (var (c, o) in new[] { (0xFF5F8A, 0.0), (0xFFD60A, 0.2), (0x30D158, 0.4), (0x64D2FF, 0.6), (0xBF5AF2, 0.8), (0xFF5F8A, 1.0) })
                foil.GradientStops.Add(new GradientStop(Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c), o));
            layers.Children.Add(new Border { Background = foil, IsHitTestVisible = false });
            ((TranslateTransform)foil.RelativeTransform).BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-0.5, 0.5, TimeSpan.FromSeconds(4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        }
        var shine = new RadialGradientBrush { RadiusX = 0.8, RadiusY = 0.7, Opacity = 0 };
        shine.GradientStops.Add(new GradientStop(Color.FromArgb(0x45, 255, 255, 255), 0));
        shine.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        layers.Children.Add(new Border { Background = shine, IsHitTestVisible = false });

        var skew = new SkewTransform();
        var scale = new ScaleTransform(1, 1);
        cover.RenderTransform = new TransformGroup { Children = { scale, skew } };
        cover.MouseMove += (_, ev) =>
        {
            var p = ev.GetPosition(cover);
            double nx = Math.Clamp(p.X / size * 2 - 1, -1, 1), ny = Math.Clamp(p.Y / size * 2 - 1, -1, 1);
            skew.BeginAnimation(SkewTransform.AngleXProperty, null);
            skew.BeginAnimation(SkewTransform.AngleYProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            skew.AngleY = -nx * 3.5; skew.AngleX = ny * 2.5;
            scale.ScaleX = 1.02 - 0.03 * Math.Abs(nx); scale.ScaleY = 1.02 - 0.02 * Math.Abs(ny);
            shine.Center = shine.GradientOrigin = new Point(0.5 + nx * 0.5, 0.5 + ny * 0.5);
            shine.BeginAnimation(Brush.OpacityProperty, null);
            shine.Opacity = 1;
            if (foil?.RelativeTransform is TranslateTransform ft) { ft.BeginAnimation(TranslateTransform.XProperty, null); ft.X = nx * 0.6; foil.Opacity = 0.16 + 0.2 * Math.Abs(nx); }
        };
        cover.MouseLeave += (_, _) =>
        {
            var d = TimeSpan.FromMilliseconds(450);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            skew.BeginAnimation(SkewTransform.AngleXProperty, new DoubleAnimation(0, d) { EasingFunction = ease });
            skew.BeginAnimation(SkewTransform.AngleYProperty, new DoubleAnimation(0, d) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, d) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, d) { EasingFunction = ease });
            shine.BeginAnimation(Brush.OpacityProperty, new DoubleAnimation(0, d));
        };
        // обложка появляется с пружиной
        var pop = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(600)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        return cover;
    }

    // ---------- плитка для сетки коллекции ----------
    public static FrameworkElement Tile(CardEntry e, BitmapSource? img, double size)
    {
        var acc = Accent(e.Rarity);
        var col = new StackPanel { Width = size };
        var box = new Grid { Width = size, Height = size };
        box.Children.Add(new SBorder
        {
            CornerRadius = new CornerRadius(size * 0.08),
            Background = img != null ? new ImageBrush(img) { Stretch = Stretch.UniformToFill } : new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x30)),
            BorderBrush = e.Rarity == 0 ? W(0x26) : new SolidColorBrush(acc), BorderThickness = new Thickness(e.Rarity == 0 ? 1 : 2),
            Effect = new DropShadowEffect { Color = e.Rarity >= 3 ? acc : Colors.Black, BlurRadius = e.Rarity >= 3 ? 22 : 14, ShadowDepth = e.Rarity >= 3 ? 0 : 4, Direction = 270, Opacity = 0.5 }
        });
        // оценка в правом нижнем углу
        var badge = new SBorder
        {
            CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Color.FromArgb(0xC8, 0, 0, 0)), Padding = new Thickness(8, 2, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8)
        };
        var bt = new TextBlock();
        bt.Inlines.Add(new System.Windows.Documents.Run(e.Score.ToString()) { Foreground = new SolidColorBrush(acc), FontWeight = FontWeights.Black, FontSize = 14 });
        bt.Inlines.Add(new System.Windows.Documents.Run("/10") { Foreground = W(0x99), FontSize = 10.5 });
        badge.Child = bt;
        box.Children.Add(badge);
        if (e.Foil)
            box.Children.Add(new SBorder
            {
                CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Color.FromArgb(0xC8, 0, 0, 0)), Padding = new Thickness(7, 2, 7, 3),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8),
                Child = new TextBlock { Text = "✦ " + Loc.T("ГОЛО"), Foreground = Brushes.White, FontSize = 10.5, FontWeight = FontWeights.Bold }
            });
        // шаг 73: карточка дня — солнце в правом верхнем углу; карточка из собранного набора — плашка «Набор» слева внизу
        if (e.Daily)
            box.Children.Add(new SBorder
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(0xC8, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8), ToolTip = Loc.T("Карточка дня", "Card of the day"),
                Child = new VIcon { Text = "sun", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
        if (CardCollection.InSet(e))
            box.Children.Add(new SBorder
            {
                CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Color.FromArgb(0xC8, 0, 0, 0)), Padding = new Thickness(7, 2, 8, 3),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8),
                ToolTip = Loc.T("Набор собран: ", "Set complete: ") + e.Artist,
                Child = new TextBlock { Text = "◆ " + Loc.T("НАБОР", "SET"), Foreground = new SolidColorBrush(Frames[4].a), FontSize = 10.5, FontWeight = FontWeights.Bold }
            });
        col.Children.Add(box);
        col.Children.Add(new TextBlock { Text = e.Title, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 9, 2, 0) });
        col.Children.Add(new TextBlock { Text = e.Artist, Foreground = W(0x90), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 1, 2, 0) });
        return col;
    }
}
