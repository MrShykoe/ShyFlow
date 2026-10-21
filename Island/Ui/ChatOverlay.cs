using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Island;

// Чат стримера под островом (шаг 67): последние сообщения капсулами, новое снизу въезжает с пружиной,
// через 15 с сообщение тает. Окно прозрачное и пропускает мышь насквозь: на стриме и в играх ничего не перекрывает по кликам.
internal sealed class ChatOverlay : Window
{
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);

    private const int MaxShown = 6;
    private readonly StackPanel _list = new() { VerticalAlignment = VerticalAlignment.Top };

    public ChatOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Width = 380;
        Height = 330;
        Title = "ShyFlow chat";
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Content = _list;
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            // WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE: мышь насквозь, без фокуса и без значка в Alt+Tab
            SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x20 | 0x80000 | 0x80 | 0x8000000);
        };
    }

    // под островом по центру монитора; top — где кончается остров
    public void Place(Rect monitor, double top)
    {
        Left = monitor.Left + (monitor.Width - Width) / 2;
        Top = monitor.Top + top;
    }

    public void Add(TwitchChat.Msg m)
    {
        var name = new Run(m.Name) { FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(NameColor(m)) };
        var tb = new TextBlock { FontSize = 13, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxHeight = 56, TextTrimming = TextTrimming.CharacterEllipsis };
        tb.Inlines.Add(name);
        tb.Inlines.Add(new Run("  " + m.Text));
        var pill = new SBorder
        {
            CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Color.FromArgb(0xE0, 0, 0, 0)), Padding = new Thickness(13, 8, 13, 8),
            Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Center, Child = tb,
            RenderTransformOrigin = new Point(0.5, 0), RenderTransform = new ScaleTransform(0.85, 0.85), Opacity = 0
        };
        _list.Children.Add(pill);
        while (_list.Children.Count > MaxShown) _list.Children.RemoveAt(0);
        var grow = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(460)) { EasingFunction = new SpringEase(6.5, 11) };
        ((ScaleTransform)pill.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        ((ScaleTransform)pill.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        pill.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        var life = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        life.Tick += (_, _) =>
        {
            life.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(500));
            fade.Completed += (_, _) => _list.Children.Remove(pill);
            pill.BeginAnimation(OpacityProperty, fade);
        };
        life.Start();
    }

    public void System(string text) => Add(new TwitchChat.Msg("ShyFlow", "#A0A0A8", text));

    // цвет ника из Twitch; нет цвета — постоянный по имени; слишком тёмный осветляем, иначе на чёрном не читается
    private static Color NameColor(TwitchChat.Msg m)
    {
        Color c;
        if (m.Color.Length == 7 && m.Color[0] == '#')
            c = (Color)ColorConverter.ConvertFromString(m.Color);
        else
        {
            Color[] pal = { Color.FromRgb(0xFF, 0x6B, 0x9E), Color.FromRgb(0x64, 0xD2, 0xFF), Color.FromRgb(0x30, 0xD1, 0x58), Color.FromRgb(0xFF, 0xD6, 0x0A), Color.FromRgb(0xBF, 0x5A, 0xF2), Color.FromRgb(0xFF, 0x9F, 0x0A) };
            c = pal[(uint)m.Name.GetHashCode() % pal.Length];
        }
        double l = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        if (l < 110) c = Color.FromRgb((byte)(c.R + (255 - c.R) * 0.45), (byte)(c.G + (255 - c.G) * 0.45), (byte)(c.B + (255 - c.B) * 0.45));
        return c;
    }
}
