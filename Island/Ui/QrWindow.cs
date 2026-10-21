using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QRCoder;

namespace Island;

// Окно «На телефон» (шаг 66): QR со ссылкой на файл в локальной сети (Services\LanShare.cs), имя и размер, сколько ещё
// работает ссылка, «Скопировать ссылку» и «Готово». Телефон скачал файл — галочка. Закрыли окно — раздача остановлена.
internal sealed class QrWindow : Window
{
    private readonly string _token;
    private readonly TextBlock _status;
    private readonly DateTime _until;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _got;

    public QrWindow(string url, LanShare.Item item, Rect area)
    {
        _token = item.Token;
        _until = item.Until;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "ShyFlow";
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var col = new StackPanel { Width = 280 };
        col.Children.Add(new TextBlock { Text = Loc.T("На телефон"), Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.Bold });
        col.Children.Add(new TextBlock
        {
            Text = item.Name + " · " + Size(item.Size), Foreground = Gray, FontSize = 12.5, Margin = new Thickness(0, 3, 0, 16),
            TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = item.Path
        });
        var qr = new Image { Source = Qr(url), Width = 236, Height = 236, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(qr, BitmapScalingMode.NearestNeighbor);   // QR без размытия
        col.Children.Add(new SBorder { CornerRadius = new CornerRadius(20), Background = Brushes.White, Padding = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Center, Child = qr });
        col.Children.Add(new TextBlock
        {
            Text = Loc.T("Наведи камеру телефона на код. Телефон и компьютер должны быть в одной Wi-Fi сети. Если Windows спросит про брандмауэр, разреши «Частные сети»"),
            Foreground = Gray, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0)
        });
        _status = new TextBlock { Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) };
        col.Children.Add(_status);
        var btns = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        btns.ColumnDefinitions.Add(new ColumnDefinition());
        btns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        btns.ColumnDefinitions.Add(new ColumnDefinition());
        var copy = Btn(Loc.T("Скопировать ссылку"), false, () =>
        {
            try { Clipboard.SetText(url); _status.Text = Loc.T("Ссылка скопирована"); } catch { }
        });
        var done = Btn(Loc.T("Готово"), true, Close);
        Grid.SetColumn(done, 2);
        btns.Children.Add(copy);
        btns.Children.Add(done);
        col.Children.Add(btns);

        var card = new SBorder
        {
            CornerRadius = new CornerRadius(28), Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E)), Padding = new Thickness(22, 20, 22, 20),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)), BorderThickness = new Thickness(1), Margin = new Thickness(24),
            Effect = new DropShadowEffect { BlurRadius = 30, Opacity = 0.6, ShadowDepth = 6, Direction = 270 }, Child = col,
            RenderTransformOrigin = new Point(0.5, 0), RenderTransform = new ScaleTransform(0.9, 0.9)
        };
        Content = card;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) =>
        {
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Top + 56;
            var a = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(460)) { EasingFunction = new SpringEase(6.5, 11) };
            ((ScaleTransform)card.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, a);
            ((ScaleTransform)card.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, a);
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        };
        _tick.Tick += (_, _) => Tick();
        Tick();
        _tick.Start();
        Closed += (_, _) => { _tick.Stop(); LanShare.Stop(_token); };
    }

    private static readonly Brush Gray = new SolidColorBrush(Color.FromRgb(0x98, 0x98, 0x9D));

    public string Token => _token;

    // телефон скачал файл
    public void Downloaded()
    {
        _got = true;
        _status.Foreground = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
        _status.Text = "✓ " + Loc.T("Скачано на телефон");
    }

    private void Tick()
    {
        var left = _until - DateTime.UtcNow;
        if (left <= TimeSpan.Zero) { Close(); return; }
        if (!_got) _status.Text = Loc.T("Ссылка работает ещё ") + $"{(int)left.TotalMinutes}:{left.Seconds:00}";
    }

    private static BitmapSource Qr(string url)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8, new byte[] { 0, 0, 0 }, new byte[] { 255, 255, 255 }, drawQuietZones: false);
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = new MemoryStream(png);
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    private static string Size(long b) => b >= 1_000_000_000 ? (b / 1e9).ToString("0.0", Loc.Culture) + Loc.T(" ГБ") : b >= 1_000_000 ? (b / 1e6).ToString("0.0", Loc.Culture) + Loc.T(" МБ") : Math.Max(1, b / 1000) + Loc.T(" КБ");

    private static Border Btn(string text, bool primary, Action click)
    {
        var bg = new SolidColorBrush(primary ? Colors.White : Color.FromArgb(0x24, 255, 255, 255));
        var b = new SBorder
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 10, 10, 10), Cursor = Cursors.Hand, Background = bg,
            Child = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = primary ? Brushes.Black : Brushes.White, HorizontalAlignment = HorizontalAlignment.Center }
        };
        b.MouseEnter += (_, _) => bg.Color = primary ? Color.FromRgb(0xE6, 0xE6, 0xE6) : Color.FromArgb(0x38, 255, 255, 255);
        b.MouseLeave += (_, _) => bg.Color = primary ? Colors.White : Color.FromArgb(0x24, 255, 255, 255);
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return b;
    }
}
