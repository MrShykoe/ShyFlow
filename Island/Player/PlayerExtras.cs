using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Windows.Media.Control;

namespace Island;

// Плеер на весь экран: визуализатор по низу и свечение за обложкой (оба включаются в настройках отдельно
// от эквалайзера в самом острове).
public sealed partial class PlayerWindow
{
    private const int VizN = 56;
    private readonly Canvas _viz = new();
    private readonly System.Windows.Shapes.Rectangle[] _vizBars = new System.Windows.Shapes.Rectangle[VizN];
    private readonly LinearGradientBrush _vizBrush = new(Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0x77, 255, 255, 255), 90);
    private readonly Border _halo = new();
    private readonly RadialGradientBrush _haloBrush = new();
    private readonly ScaleTransform _haloScale = new(1, 1);
    private double _haloLevel;
    private Color _accentShown = Colors.White, _uiShown = Colors.White;

    private void BuildExtras()
    {
        // визуализатор: полоски поднимаются снизу в такт музыке
        _viz.Height = 170;
        _viz.VerticalAlignment = VerticalAlignment.Bottom;
        _viz.IsHitTestVisible = false;
        Panel.SetZIndex(_viz, 1);
        for (int i = 0; i < VizN; i++)
        {
            var r = new System.Windows.Shapes.Rectangle { Fill = _vizBrush, Height = 3 };
            _viz.Children.Add(r);
            _vizBars[i] = r;
        }
        _root.Children.Add(_viz);

        // мягкое свечение за обложкой, дышит с басами
        _haloBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x80, 255, 255, 255), 0));
        _haloBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        _halo.Background = _haloBrush;
        _halo.IsHitTestVisible = false;
        _halo.HorizontalAlignment = HorizontalAlignment.Center;
        _halo.VerticalAlignment = VerticalAlignment.Center;
        _halo.RenderTransformOrigin = new Point(0.5, 0.5);
        _halo.RenderTransform = _haloScale;
        _halo.Opacity = 0.4;
        _coverHost.Children.Insert(0, _halo);
    }

    private void ExtrasLayout(double S, double W)
    {
        _viz.Width = W;
        _halo.Width = _halo.Height = S * 1.75;
    }

    // каждый кадр: полоски визуализатора и свечение
    private void UpdateViz()
    {
        bool vizOn = _host.PlayerViz, glowOn = _host.PlayerGlow;
        _viz.Visibility = vizOn ? Visibility.Visible : Visibility.Collapsed;
        _halo.Visibility = glowOn ? Visibility.Visible : Visibility.Collapsed;
        if (!vizOn && !glowOn) return;

        var lv = _host.EqLevels; // 7 полос; между ними значения плавно интерполируются
        if (vizOn)
        {
            double W = _viz.Width;
            if (double.IsNaN(W) || W < 10) return;
            double slot = W / VizN, bw = slot * 0.5, maxH = _viz.Height;
            double t = Environment.TickCount64 / 1000.0;
            for (int i = 0; i < VizN; i++)
            {
                double x = (double)i / (VizN - 1) * (lv.Length - 1);
                int a = (int)Math.Floor(x), b = Math.Min(lv.Length - 1, a + 1);
                double f = (1 - Math.Cos((x - a) * Math.PI)) / 2;
                double v = lv[a] * (1 - f) + lv[b] * f;
                v *= 0.8 + 0.2 * Math.Sin(t * 3.2 + i * 0.85);
                double h = Math.Max(3, Math.Min(1.1, v) * maxH * 0.92);
                var bar = _vizBars[i];
                bar.Width = bw;
                bar.RadiusX = bar.RadiusY = bw / 2;
                bar.Height = h;
                Canvas.SetLeft(bar, i * slot + (slot - bw) / 2);
                Canvas.SetTop(bar, maxH - h);
            }
        }
        if (glowOn)
        {
            double bass = Math.Min(1.2, (lv[0] + lv[1]) / 2);
            _haloLevel += (bass - _haloLevel) * 0.25;
            _halo.Opacity = Math.Clamp(0.35 + _haloLevel * 0.7, 0, 1);
            _haloScale.ScaleX = _haloScale.ScaleY = 1 + _haloLevel * 0.12;
        }
    }

    // каждые 80 мс: цвет по обложке
    private void ExtrasTick(GlobalSystemMediaTransportControlsSession? s, bool has)
    {
        var ui = _host.UiColor;
        if (ui != _uiShown) { _uiShown = ui; _seek.SetAccent(ui); _vol.SetAccent(ui); }
        var acc = _host.AccentNow;
        if (acc == _accentShown) return;
        _accentShown = acc;
        _vizBrush.GradientStops[0].Color = Color.FromArgb(0, acc.R, acc.G, acc.B);
        _vizBrush.GradientStops[1].Color = Color.FromArgb(0x77, acc.R, acc.G, acc.B);
        _haloBrush.GradientStops[0].Color = Color.FromArgb(0x80, acc.R, acc.G, acc.B);
        _haloBrush.GradientStops[1].Color = Color.FromArgb(0, acc.R, acc.G, acc.B);
    }
}
