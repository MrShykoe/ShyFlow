using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Island;

// Диалог в стиле macOS вместо стандартного MessageBox: иконка ShyFlow, заголовок, текст, кнопки-капсулы.
// Enter = главная кнопка, Esc = отмена.
public static class MacDialog
{
    private static Brush Br(byte a, byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromArgb(a, r, g, b)); x.Freeze(); return x; }

    // сообщение с одной кнопкой «OK»
    public static void Info(Window? owner, string text) => Run(owner, text, Loc.T("OK", "OK"), null);

    // вопрос: true, если нажали главную кнопку
    public static bool Ask(Window? owner, string text, string okText) => Run(owner, text, okText, Loc.T("Отмена", "Cancel"));

    private static bool Run(Window? owner, string text, string okText, string? cancelText)
    {
        bool result = false;
        var win = new Window
        {
            Title = "ShyFlow", Width = 340, SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI")
        };
        if (owner != null && owner.IsLoaded) { win.Owner = owner; win.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        else win.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var scale = new ScaleTransform(1, 1);
        var card = new SBorder
        {
            Margin = new Thickness(16), CornerRadius = new CornerRadius(22), Padding = new Thickness(22, 22, 22, 18),
            Background = new SolidColorBrush(Color.FromRgb(40, 40, 43)),
            BorderBrush = Br(0x2E, 255, 255, 255), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.6 },
            RenderTransform = scale, RenderTransformOrigin = new Point(0.5, 0.5)
        };
        var col = new StackPanel();
        card.Child = col;

        var icon = new SBorder { Width = 58, Height = 58, CornerRadius = new CornerRadius(14), Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center };
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri("pack://application:,,,/ShyFlow.png", UriKind.Absolute);
            bi.DecodePixelWidth = 128;
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();
            icon.Background = new ImageBrush(bi);
        }
        catch { }
        col.Children.Add(icon);
        col.Children.Add(new TextBlock
        {
            Text = "ShyFlow", Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0)
        });
        col.Children.Add(new TextBlock
        {
            Text = text, Foreground = Br(0xC8, 255, 255, 255), FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 18)
        });

        Border Btn(string label, bool primary, Action click)
        {
            var normal = primary ? new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x2E, 255, 255, 255));
            var hover = primary ? new SolidColorBrush(Color.FromRgb(0x3B, 0x9B, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x42, 255, 255, 255));
            var b = new SBorder
            {
                Height = 32, CornerRadius = new CornerRadius(16), Background = normal, Cursor = Cursors.Hand, Margin = new Thickness(4, 0, 4, 0),
                Child = new TextBlock
                {
                    Text = label, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            b.MouseEnter += (_, _) => b.Background = hover;
            b.MouseLeave += (_, _) => b.Background = normal;
            // нажатие не должно доходить до карточки: там окно перетаскивается (DragMove), и отпускание кнопки терялось
            b.MouseLeftButtonDown += (_, e) => e.Handled = true;
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
            return b;
        }

        var row = new UniformGrid { Rows = 1 };
        if (cancelText != null) row.Children.Add(Btn(cancelText, false, () => { result = false; win.Close(); }));
        row.Children.Add(Btn(okText, true, () => { result = true; win.Close(); }));
        col.Children.Add(row);

        win.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { result = false; win.Close(); }
            else if (e.Key == Key.Enter) { result = true; win.Close(); }
        };
        card.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is not TextBlock) { try { win.DragMove(); } catch { } } };

        win.Content = card;
        Loc.ApplyTree(card);
        win.Loaded += (_, _) =>
        {
            win.Activate();
            var a = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(380))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        };
        win.ShowDialog();
        return result;
    }
}
