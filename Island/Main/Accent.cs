using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Island;

// «Цвет из обложки везде»: полоски прогресса, эквалайзер, переключатели в настройках берут цвет обложки.
// Выключено: всё белое, как раньше.
public partial class MainWindow
{
    private readonly SolidColorBrush _uiBrush = new(Colors.White);
    private readonly SolidColorBrush _uiMiniBrush = new(Color.FromArgb(0x99, 255, 255, 255));

    internal Color UiColor => _cfg.AccentUi ? _accent : Colors.White;

    private void BindAccentUi()
    {
        SeekFill.Background = _uiBrush;
        SeekThumb.Fill = _uiBrush;
        SeekWave.Fill = _uiBrush;
        OsdFill.Background = _uiBrush;
        MiniProg.Background = _uiMiniBrush;
    }

    private void ApplyAccentUi()
    {
        var c = UiColor;
        var d = TimeSpan.FromMilliseconds(500);
        ColorTween.To(_uiBrush, SolidColorBrush.ColorProperty, new ColorAnimation(c, d));
        ColorTween.To(_uiMiniBrush, SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0x99, c.R, c.G, c.B), d));
        _settingsWin?.SetAccent(c);
        ApplyTheme();   // контур, свечение и «Аврора» красятся в цвет обложки
    }
}
