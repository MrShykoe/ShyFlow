using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Island;

// Часы в свёрнутом острове, когда музыка не играет.
public partial class MainWindow
{
    // ============================================================
    //  ЧАСЫ, когда музыка не играет
    // ============================================================
    private bool _clockMode;
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private void TickClock()
    {
        string t = DateTime.Now.ToString("HH:mm  ddd, d MMM", Loc.Culture);
        if (MiniTitle.Text != t)
        {
            MiniTitle.Text = t;
            UpdateWidths(t, "");
        }
    }

    private void EnterIdle()
    {
        SetLyricMode(false);
        _clockMode = _cfg.ShowClock;
        if (_clockMode)
        {
            CoverMini.Visibility = Visibility.Collapsed;
            MiniEq.Visibility = Visibility.Collapsed;
            MiniTitle.Text = "";
            TickClock();
        }
        else
        {
            CoverMini.Visibility = Visibility.Visible;
            MiniEq.Visibility = _cfg.Equalizer ? Visibility.Visible : Visibility.Collapsed;
            MiniTitle.Text = Loc.T("Ничего не играет");
            UpdateWidths("Ничего не играет", "");
        }
    }

    private void ExitIdle()
    {
        if (!_clockMode && CoverMini.Visibility == Visibility.Visible) return;
        _clockMode = false;
        CoverMini.Visibility = Visibility.Visible;
        MiniEq.Visibility = _cfg.Equalizer ? Visibility.Visible : Visibility.Collapsed;
    }
}
