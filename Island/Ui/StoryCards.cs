using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Island;

// Картинки для сторис 1080x1920 (шаг 49). Рисуются вне экрана обычными элементами WPF и сохраняются в PNG:
// «Изображения\ShyFlow», плюс копия в буфер обмена, чтобы сразу вставить в Telegram, Discord или сторис.
//   NowPlaying: размытая обложка на фоне, большая обложка с тенью, название, исполнитель, строчка текста песни,
//               полоска «эквалайзера» цветом обложки и подпись ShyFlow.
//   Wrapped:    итоги из статистики: сколько часов, топ-5 треков с обложками, любимый исполнитель, вайб, дни подряд.
internal static class StoryCards
{
    public const int W = 1080, H = 1920;
    private static SolidColorBrush Wb(byte a) => new(Color.FromArgb(a, 255, 255, 255));
    private static readonly FontFamily Font = new("Segoe UI Variable Display, Segoe UI");

    // шаг 73: остров ставит сюда «не замечать свою картинку в буфере» (окна коллекции и статистики иначе
    // вызывали плашку «картинка в буфере» и клали её в карман)
    public static Action? BeforeClipboard;

    public static string Folder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ShyFlow");

    public static BitmapSource NowPlaying(BitmapSource? cover, string title, string artist, string? lyric, Color accent, double progress)
    {
        var root = new Grid { Width = W, Height = H, ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x14)) };
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Ideal);

        // фон: обложка во весь экран, сильно размытая, сверху затемнение
        if (cover != null)
        {
            root.Children.Add(new Rectangle
            {
                Fill = new ImageBrush(cover) { Stretch = Stretch.UniformToFill }, Margin = new Thickness(-260),
                Effect = new BlurEffect { Radius = 110, KernelType = KernelType.Gaussian }
            });
        }
        else root.Children.Add(new Rectangle { Fill = new LinearGradientBrush(accent, Color.FromRgb(0x10, 0x10, 0x14), 90) });
        root.Children.Add(new Rectangle
        {
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0x55, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0x30, 0, 0, 0), 0.45), new GradientStop(Color.FromArgb(0xD8, 0, 0, 0), 1)
            }, 90)
        });

        var col = new StackPanel { Margin = new Thickness(110, 130, 110, 0) };
        root.Children.Add(col);

        // плашка сверху
        var chip = new StackPanel { Orientation = Orientation.Horizontal };
        chip.Children.Add(new VIcon { Text = "music", FontSize = 30, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        chip.Children.Add(new TextBlock { Text = Loc.T("Сейчас играет"), Foreground = Brushes.White, FontFamily = Font, FontSize = 34, FontWeight = FontWeights.SemiBold, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        col.Children.Add(new SBorder
        {
            CornerRadius = new CornerRadius(40), Background = Wb(0x2A), BorderBrush = Wb(0x30), BorderThickness = new Thickness(2),
            Padding = new Thickness(30, 16, 34, 18), HorizontalAlignment = HorizontalAlignment.Center, Child = chip
        });

        // обложка
        var art = new SBorder
        {
            Width = 820, Height = 820, CornerRadius = new CornerRadius(64), Margin = new Thickness(0, 80, 0, 0),
            Background = cover != null ? new ImageBrush(cover) { Stretch = Stretch.UniformToFill } : new LinearGradientBrush(accent, Colors.Black, 45),
            Effect = new DropShadowEffect { BlurRadius = 120, ShadowDepth = 30, Direction = 270, Opacity = 0.65 }
        };
        RenderOptions.SetBitmapScalingMode(art, BitmapScalingMode.HighQuality);
        col.Children.Add(art);

        col.Children.Add(new TextBlock
        {
            Text = title, Foreground = Brushes.White, FontFamily = Font, FontSize = 74, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 76, 0, 0),
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 190, LineHeight = 88
        });
        col.Children.Add(new TextBlock { Text = artist, Foreground = Wb(0xC0), FontFamily = Font, FontSize = 46, Margin = new Thickness(0, 10, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });

        // прогресс и «эквалайзер» цветом обложки
        var bar = new Grid { Height = 12, Margin = new Thickness(0, 54, 0, 0) };
        bar.Children.Add(new SBorder { CornerRadius = new CornerRadius(6), Background = Wb(0x30) });
        bar.Children.Add(new SBorder { CornerRadius = new CornerRadius(6), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(12, 860 * Math.Clamp(progress, 0, 1)) });
        col.Children.Add(bar);

        if (!string.IsNullOrWhiteSpace(lyric))
        {
            var q = new Grid();
            q.Children.Add(new TextBlock { Text = "“", Foreground = new SolidColorBrush(Color.FromArgb(0x90, accent.R, accent.G, accent.B)), FontFamily = new FontFamily("Georgia"), FontSize = 150, Margin = new Thickness(-6, -64, 0, 0) });
            q.Children.Add(new TextBlock
            {
                Text = lyric, Foreground = Brushes.White, FontFamily = Font, FontSize = 46, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 170, LineHeight = 58, Margin = new Thickness(0, 26, 0, 0)
            });
            col.Children.Add(new SBorder
            {
                CornerRadius = new CornerRadius(40), Background = Wb(0x1C), BorderBrush = Wb(0x26), BorderThickness = new Thickness(2),
                Padding = new Thickness(44, 34, 44, 40), Margin = new Thickness(0, 60, 0, 0), Child = q
            });
        }

        root.Children.Add(Footer());
        return Render(root);
    }

    public static BitmapSource Wrapped(StatsSummary s, string period, CardEntry? card = null)
    {
        Color a1 = Color.FromRgb(0x7B, 0x61, 0xFF), a2 = Color.FromRgb(0xFF, 0x5F, 0xA2);
        var root = new Grid { Width = W, Height = H, ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(0x12, 0x0E, 0x22)) };
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Ideal);
        root.Children.Add(new Ellipse { Width = 1500, Height = 1500, Margin = new Thickness(-700, -800, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Fill = new RadialGradientBrush(Color.FromArgb(0xB0, a1.R, a1.G, a1.B), Color.FromArgb(0, a1.R, a1.G, a1.B)) });
        root.Children.Add(new Ellipse { Width = 1400, Height = 1400, Margin = new Thickness(0, 0, -700, -600), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Fill = new RadialGradientBrush(Color.FromArgb(0xA0, a2.R, a2.G, a2.B), Color.FromArgb(0, a2.R, a2.G, a2.B)) });

        var col = new StackPanel { Margin = new Thickness(100, 120, 100, 0) };
        root.Children.Add(col);
        col.Children.Add(new TextBlock { Text = Loc.T("Мой ShyFlow") + " · " + period, Foreground = Wb(0xD0), FontFamily = Font, FontSize = 44, FontWeight = FontWeights.SemiBold });
        col.Children.Add(new TextBlock { Text = ListenStats.FmtDur(s.TotalSec), Foreground = Brushes.White, FontFamily = Font, FontSize = 132, FontWeight = FontWeights.Black, Margin = new Thickness(0, 20, 0, 0) });
        col.Children.Add(new TextBlock { Text = Loc.T("музыки, ") + s.TrackCount + " " + Loc.T("треков, ") + s.ArtistCount + " " + Loc.T("исполнителей"), Foreground = Wb(0xC0), FontFamily = Font, FontSize = 40, TextWrapping = TextWrapping.Wrap });

        col.Children.Add(new TextBlock { Text = Loc.T("Топ треков"), Foreground = Wb(0xA0), FontFamily = Font, FontSize = 36, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 64, 0, 24) });
        int i = 1;
        foreach (var t in s.TopTracks)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 24) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = i.ToString(), Foreground = i == 1 ? new LinearGradientBrush(a1, a2, 0) : Wb(0x90), FontFamily = Font, FontSize = 56, FontWeight = FontWeights.Black, VerticalAlignment = VerticalAlignment.Center });
            var cov = ListenStats.Cover(t.Id);
            var box = new SBorder
            {
                Width = 130, Height = 130, CornerRadius = new CornerRadius(26), Margin = new Thickness(0, 0, 34, 0),
                Background = cov != null ? new ImageBrush(cov) { Stretch = Stretch.UniformToFill } : new LinearGradientBrush(Color.FromArgb(0x50, a1.R, a1.G, a1.B), Color.FromArgb(0x50, a2.R, a2.G, a2.B), 45),
                Child = cov == null ? new VIcon { Text = "music", FontSize = 48, Foreground = Wb(0xC0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null
            };
            Grid.SetColumn(box, 1);
            row.Children.Add(box);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock { Text = t.Title, Foreground = Brushes.White, FontFamily = Font, FontSize = 46, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
            texts.Children.Add(new TextBlock { Text = t.Artist, Foreground = Wb(0xB0), FontFamily = Font, FontSize = 34, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(texts, 2);
            row.Children.Add(texts);
            col.Children.Add(row);
            i++;
        }

        // любимый исполнитель и вайб
        var duo = new Grid { Margin = new Thickness(0, 36, 0, 0) };
        duo.ColumnDefinitions.Add(new ColumnDefinition());
        duo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        duo.ColumnDefinitions.Add(new ColumnDefinition());
        var (vibe, _) = ListenStats.Vibe(s.PeakHour);
        duo.Children.Add(Tile(Loc.T("Любимый исполнитель"), s.TopArtists.FirstOrDefault()?.Name ?? "—"));
        // шаг 73: в итогах месяца вместо вайба самая редкая карточка месяца
        var vt = card != null
            ? Tile(Loc.T("Карточка месяца", "Card of the month"), card.Title + "\n" + Loc.T(CardCollection.RarityNames[card.Rarity]) + " · " + card.Score + "/10")
            : Tile(Loc.T("Вайб"), Loc.T(vibe) + (s.Streak > 1 ? "\n" + s.Streak + " " + Loc.T("дн. подряд") : ""));
        Grid.SetColumn(vt, 2);
        duo.Children.Add(vt);
        col.Children.Add(duo);

        root.Children.Add(Footer());
        return Render(root);
    }

    // ---------- шаг 73: карточка из коллекции, чтобы показать друзьям ----------
    // Размытая обложка на фоне и свечение цвета редкости, крупная обложка с рамкой, редкость, название, исполнитель,
    // большая оценка и пять характеристик полосками, номер в коллекции и подпись ShyFlow.
    public static BitmapSource Card(CardEntry e, BitmapSource? img)
    {
        var acc = CollectibleCard.Accent(e.Rarity);
        var root = new Grid { Width = W, Height = H, ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0C)) };
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Ideal);
        if (img != null)
            root.Children.Add(new Rectangle
            {
                Fill = new ImageBrush(img) { Stretch = Stretch.UniformToFill }, Margin = new Thickness(-260), Opacity = 0.55,
                Effect = new BlurEffect { Radius = 120, KernelType = KernelType.Gaussian }
            });
        root.Children.Add(new Ellipse
        {
            Width = 1500, Height = 1500, Margin = new Thickness(0, 120, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Fill = new RadialGradientBrush(Color.FromArgb(0x70, acc.R, acc.G, acc.B), Color.FromArgb(0, acc.R, acc.G, acc.B))
        });
        root.Children.Add(new Rectangle
        {
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0x40, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0x10, 0, 0, 0), 0.4), new GradientStop(Color.FromArgb(0xE0, 0, 0, 0), 1)
            }, 90)
        });

        var col = new StackPanel { Margin = new Thickness(110, 120, 110, 0) };
        root.Children.Add(col);

        // плашка сверху
        var chip = new StackPanel { Orientation = Orientation.Horizontal };
        chip.Children.Add(new VIcon { Text = e.Daily ? "sun" : "star", FontSize = 30, Foreground = new SolidColorBrush(acc), VerticalAlignment = VerticalAlignment.Center });
        chip.Children.Add(new TextBlock
        {
            Text = Loc.T(e.Daily ? "Карточка дня" : "Из моей коллекции", e.Daily ? "Card of the day" : "From my collection"),
            Foreground = Brushes.White, FontFamily = Font, FontSize = 34, FontWeight = FontWeights.SemiBold, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
        });
        col.Children.Add(new SBorder
        {
            CornerRadius = new CornerRadius(40), Background = Wb(0x22), BorderBrush = Wb(0x30), BorderThickness = new Thickness(2),
            Padding = new Thickness(30, 16, 34, 18), HorizontalAlignment = HorizontalAlignment.Center, Child = chip
        });

        // обложка с рамкой и свечением редкости, у голографической радуга
        var coverBox = new Grid { Width = 720, Height = 720, Margin = new Thickness(0, 70, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        coverBox.Children.Add(new SBorder
        {
            CornerRadius = new CornerRadius(56),
            Background = img != null ? new ImageBrush(img) { Stretch = Stretch.UniformToFill } : new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x30)),
            BorderBrush = e.Rarity == 0 ? Wb(0x40) : new SolidColorBrush(acc), BorderThickness = new Thickness(e.Rarity == 0 ? 3 : 6),
            Effect = new DropShadowEffect { Color = e.Rarity >= 2 ? acc : Colors.Black, BlurRadius = 90, ShadowDepth = 0, Opacity = 0.75 }
        });
        if (e.Foil)
        {
            var foil = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1), Opacity = 0.28 };
            foreach (var (c, o) in new[] { (0xFF5F8A, 0.0), (0xFFD60A, 0.2), (0x30D158, 0.4), (0x64D2FF, 0.6), (0xBF5AF2, 0.8), (0xFF5F8A, 1.0) })
                foil.GradientStops.Add(new GradientStop(Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c), o));
            coverBox.Children.Add(new SBorder { CornerRadius = new CornerRadius(56), Background = foil, Margin = new Thickness(6) });
        }
        col.Children.Add(coverBox);

        // редкость и номер
        col.Children.Add(new SBorder
        {
            CornerRadius = new CornerRadius(30), Background = new SolidColorBrush(Color.FromArgb(0x38, acc.R, acc.G, acc.B)),
            Padding = new Thickness(28, 10, 28, 12), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 60, 0, 0),
            Child = new TextBlock { Text = CollectibleCard.RarityText(e) + "   #" + e.Number.ToString("000"), Foreground = new SolidColorBrush(acc), FontFamily = Font, FontSize = 32, FontWeight = FontWeights.Bold }
        });
        col.Children.Add(new TextBlock
        {
            Text = e.Title, Foreground = Brushes.White, FontFamily = Font, FontSize = 66, FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 26, 0, 0)
        });
        col.Children.Add(new TextBlock
        {
            Text = e.Artist, Foreground = Wb(0xB8), FontFamily = Font, FontSize = 42,
            TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0)
        });

        // оценка слева, характеристики справа
        var bottom = new Grid { Margin = new Thickness(0, 50, 0, 0) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition());
        var score = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var big = new TextBlock { FontFamily = Font };
        big.Inlines.Add(new System.Windows.Documents.Run(e.Score.ToString()) { Foreground = new SolidColorBrush(acc), FontSize = 150, FontWeight = FontWeights.Black });
        big.Inlines.Add(new System.Windows.Documents.Run("/10") { Foreground = Wb(0x99), FontSize = 50, FontWeight = FontWeights.SemiBold });
        score.Children.Add(big);
        bottom.Children.Add(score);
        var stats = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var (name, v) in new[] { ("Краски", e.Colors), ("Контраст", e.Contrast), ("Детали", e.Detail), ("Гармония", e.Harmony), ("Удача", e.Luck) })
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(new TextBlock { Text = Loc.T(name), Foreground = Wb(0xC8), FontFamily = Font, FontSize = 28, VerticalAlignment = VerticalAlignment.Center });
            var track = new Grid { Height = 14, VerticalAlignment = VerticalAlignment.Center };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(v, GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10 - v, GridUnitType.Star) });
            var bg = new SBorder { CornerRadius = new CornerRadius(7), Background = Wb(0x26) };
            Grid.SetColumnSpan(bg, 2);
            track.Children.Add(bg);
            track.Children.Add(new SBorder { CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(acc) });
            Grid.SetColumn(track, 1);
            g.Children.Add(track);
            stats.Children.Add(g);
        }
        Grid.SetColumn(stats, 2);
        bottom.Children.Add(stats);
        col.Children.Add(bottom);

        root.Children.Add(Footer());
        return Render(root);
    }

    private static FrameworkElement Tile(string label, string value)
    {
        var c = new StackPanel();
        c.Children.Add(new TextBlock { Text = label, Foreground = Wb(0xA0), FontFamily = Font, FontSize = 30 });
        c.Children.Add(new TextBlock { Text = value, Foreground = Brushes.White, FontFamily = Font, FontSize = 48, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 200, LineHeight = 60, Margin = new Thickness(0, 8, 0, 0) });
        return new SBorder { CornerRadius = new CornerRadius(40), Background = Wb(0x1A), BorderBrush = Wb(0x24), BorderThickness = new Thickness(2), Padding = new Thickness(40, 34, 40, 38), Child = c };
    }

    private static FrameworkElement Footer()
    {
        var f = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 90) };
        f.Children.Add(new SBorder { Width = 96, Height = 30, CornerRadius = new CornerRadius(15), Background = Brushes.Black, BorderBrush = Wb(0x50), BorderThickness = new Thickness(2), VerticalAlignment = VerticalAlignment.Center });
        f.Children.Add(new TextBlock { Text = "ShyFlow", Foreground = Wb(0xE0), FontFamily = Font, FontSize = 38, FontWeight = FontWeights.SemiBold, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return f;
    }

    private static BitmapSource Render(FrameworkElement root)
    {
        root.Measure(new Size(W, H));
        root.Arrange(new Rect(0, 0, W, H));
        root.UpdateLayout();
        var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);
        rtb.Freeze();
        return rtb;
    }

    // сохранить в «Изображения\ShyFlow» и положить в буфер обмена; вернуть путь
    public static string Save(BitmapSource bmp, string prefix)
    {
        Directory.CreateDirectory(Folder);
        string path = System.IO.Path.Combine(Folder, prefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(path)) enc.Save(fs);
        BeforeClipboard?.Invoke();
        try { Clipboard.SetImage(bmp); } catch (Exception ex) { App.LogOnce("Story.Clipboard", ex); }
        return path;
    }

    public static void ShowInFolder(string path)
    {
        try { Process.Start("explorer.exe", "/select,\"" + path + "\""); }
        catch (Exception ex) { App.LogOnce("Story.Folder", ex); }
    }
}
