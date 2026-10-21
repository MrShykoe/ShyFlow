using System.Windows;

namespace Island;

// Шаг 73: итоги месяца, как Spotify Wrapped.
// В начале месяца остров сам предлагает итоги прошлого: «Итоги месяца готовы: сентябрь 2026», клик — картинка 1080x1920
// (сколько часов, топ-5 треков с обложками, любимый исполнитель и самая редкая карточка месяца) сохраняется
// в «Изображения\ShyFlow» и сразу лежит в буфере. Предлагается один раз и только если за месяц набралось полчаса музыки.
// Итоги текущего месяца на сегодня — плитка «Итоги месяца» в быстрых действиях.
public partial class MainWindow
{
    private void MonthCheck()
    {
        if (!_ready) return;
        var prev = DateTime.Today.AddMonths(-1);
        string key = prev.ToString("yyyy-MM");
        if (_cfg.RecapShown == key) return;
        _cfg.RecapShown = key;
        _cfg.Save();
        StatsSummary sum;
        try { sum = ListenStats.QueryMonth(prev.Year, prev.Month); }
        catch (Exception ex) { App.Log("Recap.Check", ex); return; }
        if (sum.TotalSec < 30 * 60) return;   // почти ничего не слушали: итоги не предлагаем
        ShowNotif("ShyFlow", Loc.T("Итоги месяца готовы", "Your month in music") + ": " + MonthName(prev),
                  Loc.T("Клик: картинка с топом треков и карточкой месяца", "Click: a picture with your top tracks and card of the month"));
        _notifClick = () => { ApplyMode(Rest()); MakeRecap(prev.Year, prev.Month); };
        Sfx.Ui("unlock");
    }

    private static string MonthName(DateTime d) => d.ToString("MMMM yyyy", Loc.Culture);

    internal void MakeRecap(int year, int month)
    {
        try
        {
            var sum = ListenStats.QueryMonth(year, month);
            if (sum.TotalSec < 60) { Tell("chart-column", Loc.T("За этот месяц музыки ещё нет", "No music this month yet")); return; }
            // карточка месяца: самая редкая из забранных в этом месяце
            var best = CardCollection.All.Where(c => c.Added.Year == year && c.Added.Month == month)
                .OrderByDescending(c => c.Rarity).ThenByDescending(c => c.Foil).ThenByDescending(c => c.Score).FirstOrDefault();
            var name = MonthName(new DateTime(year, month, 1));
            var bmp = StoryCards.Wrapped(sum, name, best);
            string path = StoryCards.Save(bmp, "ShyFlow-month");
            Sfx.Ui("unlock");
            ShowNotif("ShyFlow", Loc.T("Итоги готовы", "Recap ready") + ": " + name,
                      Loc.T("Уже в буфере: вставь в сторис или чат. Клик: показать файл", "Already copied: paste it into a story or chat. Click: show the file"));
            _notifClick = () => { StoryCards.ShowInFolder(path); ApplyMode(Rest()); };
        }
        catch (Exception ex) { App.Log("Recap", ex); Tell("triangle-alert", Loc.T("Не получилось сделать итоги", "Couldn't make the recap")); }
    }
}
