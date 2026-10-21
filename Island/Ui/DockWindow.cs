using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Island;

// Шаг 76: док программ снизу экрана, в стиле острова.
// Те же значки, что у капсулы рядом с островом (открытые и закреплённые программы, AppsBar.cs), но крупнее и отдельной
// капсулой внизу по центру монитора острова. Два вида (Настройки, «Внешний вид», «Док снизу»):
//   остров — чёрная капсула над панелью задач;
//   вырез — прилипает к нижнему краю экрана, углы перетекают в рамку, как вырез сверху (те же уши, перевёрнутые вниз).
// Окно не забирает фокус (WS_EX_NOACTIVATE): клик по значку ведёт себя как на панели задач. Прячется вместе с островом
// (Ctrl+Alt+I) и в играх и видео на весь экран.
internal sealed class DockWindow : Window
{
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);

    public const double Cell = 44;           // ячейка значка
    private const double BarH = 54;          // высота капсулы
    private const double Gap = 10;           // отступ капсулы-острова от панели задач

    private readonly SBorder _bg = new() { RenderTransformOrigin = new Point(0.5, 0.5) };
    private readonly ScaleTransform _flip = new(1, 1);
    public readonly StackPanel Icons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _root = new() { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TranslateTransform _slide = new();
    private bool _notch, _shown;
    private double _ear;
    private Rect _mon, _work;

    public DockWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Title = "ShyFlow Dock";
        Width = 200; Height = BarH + 30;   // запас сверху под увеличение значков при наведении
        _bg.RenderTransform = _flip;
        _root.Children.Add(_bg);
        _root.Children.Add(Icons);
        _root.RenderTransform = _slide;
        Content = _root;
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/);
        };
        Icons.SizeChanged += (_, _) => Layout();
    }

    // вид, фон как у острова, монитор (в единицах WPF): весь и рабочая область без панели задач
    public void Configure(bool notch, double ear, Brush? bg, Brush? border, Thickness bt, Rect mon, Rect work)
    {
        _notch = notch; _ear = notch ? ear : 0;
        _mon = mon; _work = work;
        _bg.Background = bg ?? Brushes.Black;
        _bg.BorderBrush = border;
        _bg.BorderThickness = bt;
        _bg.Ears = _ear;
        // вырез: фигура острова с ушами сверху, перевёрнутая вниз — уши у нижнего края экрана
        _flip.ScaleY = notch ? -1 : 1;
        _bg.CornerRadius = notch ? new CornerRadius(0, 0, 18, 18) : new CornerRadius(BarH / 2);
        Icons.Margin = new Thickness(12 + _ear, 0, 12 + _ear, 0);
        Layout();
    }

    private void Layout()
    {
        double w = Math.Max(BarH, Icons.ActualWidth + Icons.Margin.Left + Icons.Margin.Right);
        _bg.Width = w; _bg.Height = BarH;
        Icons.Height = BarH;
        Width = w + 40;   // поля под увеличение крайних значков
        Left = _mon.Left + Math.Round((_mon.Width - Width) / 2);
        double bottom = _notch ? _mon.Bottom : _work.Bottom - Gap;
        Top = bottom - Height;
    }

    public void SetShown(bool on)
    {
        if (on == _shown && IsVisible == on) return;
        _shown = on;
        if (on)
        {
            if (!IsVisible) Show();
            _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(BarH + 20, 0, TimeSpan.FromMilliseconds(520))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 } });
        }
        else if (IsVisible)
        {
            var a = new DoubleAnimation(BarH + 20, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += (_, _) => { if (!_shown) Hide(); };
            _slide.BeginAnimation(TranslateTransform.YProperty, a);
        }
    }
}
