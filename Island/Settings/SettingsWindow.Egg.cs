using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Island;

// Пасхалка: пять кликов по версии (в «О программе» или в карточке ShyFlow слева).
// На третьем и четвёртом клике надпись подмигивает «ещё 2…», «ещё 1…», как «Вы почти разработчик» в Android.
// На пятом вместо версии «Island какашечки 💩», а по окну настроек идёт дождь из 💩.
public sealed partial class SettingsWindow
{
    private Grid? _eggHost;
    private int _eggClicks;
    private bool _eggOn;
    private readonly List<TextBlock> _eggLabels = new();
    private static readonly Random EggRnd = new();
    private const string EggText = "Island какашечки 💩";

    private void AttachEgg(TextBlock version)
    {
        _eggLabels.Add(version);
        version.Background = Brushes.Transparent;   // клик ловится по всей строке, а не только по буквам
        version.Cursor = Cursors.Hand;
        version.RenderTransformOrigin = new Point(0.5, 0.5);
        var sc = new ScaleTransform(1, 1);
        version.RenderTransform = sc;
        version.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;   // карточка ShyFlow слева иначе переключила бы раздел
            var pop = new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.8 } };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            if (_eggOn) { PoopRain(12); return; }   // уже открыта: ещё немного дождя
            _eggClicks++;
            if (_eggClicks >= 5) { EggOn(); return; }
            if (_eggClicks >= 3) Wink(version, Loc.En ? $"{5 - _eggClicks} more…" : $"ещё {5 - _eggClicks}…");
        };
    }

    // ненадолго подменяет надпись и возвращает обратно
    private static void Wink(TextBlock tb, string text)
    {
        string back = tb.Text;
        tb.Text = text;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        t.Tick += (_, _) => { t.Stop(); if (tb.Text == text) tb.Text = back; };
        t.Start();
    }

    private void EggOn()
    {
        _eggOn = true;
        foreach (var tb in _eggLabels)
        {
            tb.Text = EggText;
            tb.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Segoe UI Emoji");
            tb.Foreground = new LinearGradientBrush(Color.FromRgb(0xC8, 0x8A, 0x4E), Color.FromRgb(0xFF, 0xD2, 0x8A), 0);
            tb.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
        }
        try { Sfx.Done(); } catch { /* нет звука: дождь и без него смешной */ }
        PoopRain(34);
    }

    // 💩 падают сверху с разной скоростью, крутятся и тают у низа окна
    private void PoopRain(int count)
    {
        if (_eggHost == null) return;
        var layer = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        Grid.SetColumnSpan(layer, Math.Max(1, _eggHost.ColumnDefinitions.Count));
        Grid.SetRowSpan(layer, Math.Max(1, _eggHost.RowDefinitions.Count));
        Panel.SetZIndex(layer, 1000);
        _eggHost.Children.Add(layer);
        double w = Math.Max(200, _eggHost.ActualWidth), h = Math.Max(200, _eggHost.ActualHeight);
        double last = 0;
        for (int i = 0; i < count; i++)
        {
            double size = 18 + EggRnd.NextDouble() * 26;
            var tb = new TextBlock { Text = "💩", FontSize = size, FontFamily = new FontFamily("Segoe UI Emoji") };
            var tr = new TranslateTransform(EggRnd.NextDouble() * (w - size), -size - 10);
            var rot = new RotateTransform(EggRnd.NextDouble() * 60 - 30, size / 2, size / 2);
            tb.RenderTransform = new TransformGroup { Children = { rot, tr } };
            layer.Children.Add(tb);
            double delay = EggRnd.NextDouble() * 900, dur = 1300 + EggRnd.NextDouble() * 1200;
            last = Math.Max(last, delay + dur);
            var begin = TimeSpan.FromMilliseconds(delay);
            tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-size - 10, h + 10, TimeSpan.FromMilliseconds(dur))
            { BeginTime = begin, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
            tr.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(tr.X, tr.X + (EggRnd.NextDouble() - 0.5) * 120, TimeSpan.FromMilliseconds(dur))
            { BeginTime = begin });
            rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(rot.Angle, rot.Angle + (EggRnd.NextDouble() - 0.5) * 540, TimeSpan.FromMilliseconds(dur))
            { BeginTime = begin });
            tb.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300))
            { BeginTime = TimeSpan.FromMilliseconds(delay + dur - 300) });
        }
        var done = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(last + 100) };
        done.Tick += (_, _) => { done.Stop(); _eggHost.Children.Remove(layer); };
        done.Start();
    }
}
