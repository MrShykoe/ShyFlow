using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Island;

// Звуки интерфейса и упругость острова (шаг 49).
// Звуки синтезируются (Services\Sfx.cs, без файлов): раскрытие и закрытие, уведомление, свайп, смахивание, пузырь, нажатие.
// Шаг 63: вместо трёх ступеней «Упругости» ползунок «Желеобразность» (0..1, затухание пружин) и стиль морфинга:
//   жидкий — как было: пружины плюс жидкие растяжения (Fluid.cs);
//   стекло — чётко и плавно, пружины с критическим затуханием (без перехлёста), без жидкости и «желе»;
//   упругий — заметный отскок, остров сплющивается и отскакивает при каждой смене вида, без жидкости.
public partial class MainWindow
{
    private static bool IsPanel(Mode m) => m is Mode.Full or Mode.Pocket or Mode.Actions or Mode.Devices or Mode.Timer or Mode.Mixer or Mode.Center or Mode.Translate;

    private double JellyK => Math.Clamp(_cfg.Jelly, 0, 1);

    private void ModeSound(Mode prev, Mode m)
    {
        if (_chatWin != null) _chatWin.Opacity = IsPanel(m) ? 0 : 1;   // раскрытый остров не перекрываем чатом стрима (Streamer.cs)
        if (!_ready || prev == m) return;
        if (_cfg.MorphStyle == 2) Wobble();   // упругий стиль: «желе» на каждую смену вида
        if (IsPanel(m) && !IsPanel(prev)) Sfx.Ui("open");
        else if (IsPanel(prev) && m == Mode.Mini) Sfx.Ui("close");
    }

    private void ApplyVibeSettings()
    {
        Sfx.UiLevel = !_cfg.UiSounds ? 0 : _cfg.UiSoundVol switch { 0 => 0.12f, 2 => 0.4f, _ => 0.24f };
        double j = JellyK, k, damp;
        switch (_cfg.MorphStyle)
        {
            case 1:   // стекло: критическое затухание 2·√K, «желе» только чуть смягчает
                k = 190;
                damp = 2 * Math.Sqrt(k) * (1 - 0.2 * j);
                break;
            case 2:   // упругий: мало затухания, жёстче пружина
                k = 210 + 40 * j;
                damp = 12 - 7 * j;
                break;
            default:  // жидкий: меньше затухание = дольше колышется (0.45 ≈ прежнее «Обычно»)
                k = 165 + 30 * j;
                damp = 22 - 14 * j;
                break;
        }
        foreach (var s in new[] { _w, _h, _r }) { s.D = damp; s.K = k; }
    }

    // «желе»: остров чуть сплющивается и отскакивает обратно
    private void Wobble()
    {
        double j = JellyK;
        if (_cfg.MorphStyle == 1 || j < 0.05 || _eco || _hidden) return;
        var s = Get(Pill).S;
        double amp = (0.015 + 0.07 * j) * (_cfg.MorphStyle == 2 ? 1.5 : 1);
        var ease = new SpringEase(6.5 - 2.5 * j, 13);
        s.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1 + amp, 1, TimeSpan.FromMilliseconds(700)) { EasingFunction = ease });
        s.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1 - amp * 1.4, 1, TimeSpan.FromMilliseconds(700)) { EasingFunction = ease });
    }
}
