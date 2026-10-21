using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Island;

// Фокус (помодоро, шаг 54): 25 минут работы, 5 минут перерыв, по кругу (длительности в настройках).
// Пока идёт работа, уведомления не всплывают, а тихо копятся в шторке (временный «не беспокоить»);
// в конце работы остров говорит, сколько пришло. Время и кольцо — в пузыре рядом с островом (красный — работа, зелёный — перерыв).
// Включается плиткой «Фокус» в быстрых действиях. Клик по пузырю: пауза, пропустить этап, остановить.
public partial class MainWindow
{
    private static readonly Color FocusC = Color.FromRgb(0xFF, 0x5A, 0x4E), BreakC = Color.FromRgb(0x30, 0xD1, 0x58);
    private readonly DispatcherTimer _focusTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _focusOn, _focusBreak, _focusPaused;
    private DateTime _focusEnd;
    private double _focusLeft, _focusTotal;
    private int _focusRounds, _focusMuted;

    // работа сейчас и тишина включена: уведомления не всплывают
    private bool FocusQuiet => _focusOn && !_focusBreak && _cfg.FocusQuiet;

    private double FocusLeftNow => _focusPaused ? _focusLeft : Math.Max(0, (_focusEnd - DateTime.UtcNow).TotalSeconds);

    private void ToggleFocus()
    {
        if (_focusOn) StopFocus();
        else StartFocus();
    }

    private void StartFocus()
    {
        if (!_focusHooked) { _focusTimer.Tick += (_, _) => FocusTick(); _focusHooked = true; }
        _focusOn = true; _focusRounds = 0; _focusMuted = 0;
        BeginPhase(false);
        Sfx.Ui("open");
        if (_mode != Mode.Actions) ShowToast("zap", Loc.T("Фокус: ") + _cfg.FocusMin + " " + Loc.T("мин") + (_cfg.FocusQuiet ? Loc.T(", уведомления в шторку") : ""), null, "key");
    }
    private bool _focusHooked;

    private void BeginPhase(bool brk)
    {
        _focusBreak = brk;
        _focusPaused = false;
        _focusTotal = (brk ? Math.Clamp(_cfg.BreakMin, 1, 60) : Math.Clamp(_cfg.FocusMin, 1, 180)) * 60;
        _focusEnd = DateTime.UtcNow.AddSeconds(_focusTotal);
        _focusTimer.Start();
        UpdateBubble();
    }

    private void StopFocus()
    {
        _focusOn = false;
        _focusTimer.Stop();
        UpdateBubble();
        if (_mode != Mode.Actions) ShowToast("zap", Loc.T("Фокус выключен") + (_focusRounds > 0 ? ": " + _focusRounds + " " + Loc.T("кругов") : ""), null, "key");
    }

    private void FocusTick()
    {
        if (!_focusOn) { _focusTimer.Stop(); return; }
        if (!_focusPaused && FocusLeftNow <= 0)
        {
            if (!_focusBreak)
            {
                _focusRounds++;
                string extra = _focusMuted > 0 ? Loc.T("Пока ты работал, пришло уведомлений: ") + _focusMuted : Loc.T("Встань, разомнись, попей воды");
                _focusMuted = 0;
                BeginPhase(true);
                ShowNotif("Фокус", Loc.T("Перерыв ") + _cfg.BreakMin + " " + Loc.T("мин"), extra);
            }
            else
            {
                BeginPhase(false);
                ShowNotif("Фокус", Loc.T("Снова работаем: ") + _cfg.FocusMin + " " + Loc.T("мин"), Loc.T("Уведомления опять тихо копятся в шторке"));
            }
            Sfx.Reminder();
        }
        UpdateBubble();
    }

    private (string kind, string text, double frac, Color col, string icon, bool paused)? FocusState()
    {
        if (!_focusOn) return null;
        return ("focus", FmtTimer(FocusLeftNow), _focusTotal > 0 ? FocusLeftNow / _focusTotal : 0, _focusBreak ? BreakC : FocusC, _focusBreak ? "sun" : "zap", _focusPaused);
    }

    // меню по клику на пузырь фокуса
    private void FocusMenu()
    {
        var m = new ContextMenu();
        MenuItem It(string t, Action a) { var i = new MenuItem { Header = Loc.T(t) }; i.Click += (_, _) => a(); return i; }
        m.Items.Add(new MenuItem { Header = Loc.T(_focusBreak ? "Перерыв" : "Фокус") + " · " + Loc.T("круг ") + (_focusRounds + 1), IsEnabled = false });
        m.Items.Add(It(_focusPaused ? "Продолжить" : "Пауза", () =>
        {
            if (_focusPaused) { _focusEnd = DateTime.UtcNow.AddSeconds(_focusLeft); _focusPaused = false; }
            else { _focusLeft = FocusLeftNow; _focusPaused = true; }
            UpdateBubble();
        }));
        m.Items.Add(It(_focusBreak ? "Пропустить перерыв" : "Сразу на перерыв", () => { if (!_focusBreak) _focusRounds++; BeginPhase(!_focusBreak); }));
        m.Items.Add(It("Остановить фокус", StopFocus));
        m.PlacementTarget = Bubble;
        m.IsOpen = true;
    }
}
