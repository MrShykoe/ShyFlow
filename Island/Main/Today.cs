using System.Windows;
using System.Windows.Threading;

namespace Island;

// Праздник или своё событие сегодня (шаг 54): один раз в день остров говорит «Сегодня: …», клик открывает календарь.
// Проверка раз в минуту, чтобы сработать и после полуночи, если компьютер не выключали.
public partial class MainWindow
{
    private readonly DispatcherTimer _dayTimer = new() { Interval = TimeSpan.FromMinutes(1) };

    private void StartDayCheck()
    {
        _dayTimer.Tick += (_, _) => { DayCheck(); MonthCheck(); };
        _dayTimer.Start();
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };   // после приветствия и первых плашек
        first.Tick += (_, _) => { first.Stop(); DayCheck(); };
        first.Start();
        // шаг 73: итоги прошлого месяца — позже праздника, чтобы плашки не перебивали друг друга (Recap.cs)
        var month = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        month.Tick += (_, _) => { month.Stop(); MonthCheck(); };
        month.Start();
    }

    private void DayCheck()
    {
        if (!_ready || !_cfg.HolidayToasts) return;
        string today = DateTime.Today.ToString("yyyy-MM-dd");
        if (_cfg.HolidayShown == today) return;
        var marks = Holidays.For(DateTime.Today);
        _cfg.HolidayShown = today;
        _cfg.Save();
        if (marks.Count == 0) return;
        ShowNotif(Loc.T("Сегодня"), string.Join(", ", marks.Select(m => m.Text)), Loc.T("Клик: календарь и ближайшие праздники"));
        _notifClick = () => { CalendarWindow.ShowCalendar(); ApplyMode(Rest()); };
        Sfx.Ui("unlock");
    }

    private void Calendar_Click(object s, RoutedEventArgs e) => CalendarWindow.ShowCalendar();
    private void Translator_Click(object s, RoutedEventArgs e) => OpenTranslator(null, IntPtr.Zero);
}
