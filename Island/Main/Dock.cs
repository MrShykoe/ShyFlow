using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Island;

// Шаг 76: док программ снизу экрана (окно Ui\DockWindow.cs). Значки те же, что у капсулы рядом с островом (AppsBar.cs):
// сначала закреплённые, потом открытые. Настройки, «Внешний вид», «Значки приложений», «Док снизу»: выкл, остров, вырез.
public partial class MainWindow
{
    private const int DockMax = 14;
    private DockWindow? _dock;

    private bool DockWanted => _cfg.DockBottom != 0 && !_hidden && !_fsMini && !_fsHidden && _dock != null && _dock.Icons.Children.Count > 0;

    private void RenderDock(List<RunApp> list, RunApp? launched)
    {
        if (_cfg.DockBottom == 0) { _dock?.SetShown(false); return; }
        if (_dock == null)
        {
            var d = _dock = new DockWindow();
            d.Icons.MouseMove += (_, e) => Magnify(d.Icons, e.GetPosition(d.Icons).X, 0.9);
            d.Icons.MouseLeave += (_, _) => Magnify(d.Icons, double.NaN, 0.9);
            Closed += (_, _) => { try { _dock?.Close(); } catch { } };
        }
        _dock.Icons.Children.Clear();
        foreach (var app in list.Take(DockMax))
        {
            var el = AppIconEl(app, DockWindow.Cell, 30);
            _dock.Icons.Children.Add(el);
            if (ReferenceEquals(app, launched) && !_appsFirstScan)
            {
                Pop(el, 0.2, 650);   // новая программа «впрыгивает»
                Get(el).T.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                    new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(650)) { EasingFunction = Spr() });
            }
        }
        PlaceDock();
    }

    // монитор острова, вид и фон как у острова; показать или спрятать
    private void PlaceDock()
    {
        if (_dock == null) return;
        try
        {
            var list = Monitors.All();
            var m = list[Math.Clamp(CurMonitor, 0, list.Count - 1)];
            double sc = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var mon = new Rect(m.Left / sc, m.Top / sc, m.Width / sc, m.Height / sc);
            double workBottom = (m.WorkBottom > m.Top ? m.WorkBottom : m.Top + m.Height) / sc;
            var work = new Rect(mon.Left, mon.Top, mon.Width, Math.Max(0, workBottom - mon.Top));
            _dock.Configure(_cfg.DockBottom == 2, NotchEar, Pill.Background, Pill.BorderBrush, Pill.BorderThickness, mon, work);
        }
        catch (Exception ex) { App.LogOnce("Dock.Place", ex); }
        _dock.SetShown(DockWanted);
    }
}
