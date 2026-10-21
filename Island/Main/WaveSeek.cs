using System.Windows;

namespace Island;

// Прогресс волной в острове вместо тонкой линии (настройка «Прогресс волной»).
public partial class MainWindow
{
    private void ApplyWaveSeek()
    {
        bool on = _cfg.WaveSeek;
        SeekWave.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        var o = on ? Visibility.Collapsed : Visibility.Visible;
        SeekTrack.Visibility = SeekFill.Visibility = SeekThumb.Visibility = o;
    }
}
