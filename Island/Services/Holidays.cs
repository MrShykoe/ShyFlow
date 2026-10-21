using System.IO;
using System.Text.Json;

namespace Island;

// Праздники и свои события для календаря (шаг 54).
// Праздники считаются сами на любой год: государственные праздники России, популярные (Валентин, Хэллоуин, День смеха...)
// и подвижные: православная Пасха, Масленица, День программиста (256-й день года).
// Свои события — %AppData%\Island\calendar.json: ключ «ГГГГ-ММ-ДД» (один раз) или «ММ-ДД» (каждый год, например дни рождения).
internal sealed record DayMark(string Text, bool Holiday, bool Official);

internal static class Holidays
{
    private static readonly (int m, int d, string name, bool off)[] Fixed =
    {
        (1, 1, "Новый год", true), (1, 7, "Рождество Христово", true), (1, 25, "Татьянин день (День студента)", false),
        (2, 14, "День святого Валентина", false), (2, 23, "День защитника Отечества", true),
        (3, 8, "Международный женский день", true), (4, 1, "День смеха", false), (4, 12, "День космонавтики", false),
        (5, 1, "Праздник Весны и Труда", true), (5, 9, "День Победы", true), (6, 1, "День защиты детей", false),
        (6, 12, "День России", true), (9, 1, "День знаний", false), (10, 31, "Хэллоуин", false),
        (11, 4, "День народного единства", true), (12, 31, "Канун Нового года", false),
    };

    // православная Пасха (по юлианскому алгоритму Гаусса и перевод в григорианский календарь)
    public static DateTime OrthodoxEaster(int year)
    {
        int a = year % 4, b = year % 7, c = year % 19;
        int d = (19 * c + 15) % 30;
        int e = (2 * a + 4 * b - d + 34) % 7;
        int month = (d + e + 114) / 31, day = (d + e + 114) % 31 + 1;
        return new DateTime(year, month, day).AddDays(13);   // юлианская дата -> григорианская (1900–2099)
    }

    public static List<DayMark> For(DateTime date)
    {
        date = date.Date;
        var list = new List<DayMark>();
        foreach (var f in Fixed) if (f.m == date.Month && f.d == date.Day) list.Add(new DayMark(Loc.T(f.name), true, f.off));
        var easter = OrthodoxEaster(date.Year);
        if (date == easter) list.Add(new DayMark(Loc.T("Пасха"), true, false));
        if (date >= easter.AddDays(-55) && date <= easter.AddDays(-49)) list.Add(new DayMark(Loc.T("Масленица"), true, false));
        if (date.DayOfYear == 256) list.Add(new DayMark(Loc.T("День программиста"), true, false));
        foreach (var e in UserEvents(date)) list.Add(new DayMark(e, false, false));
        return list;
    }

    // ближайшие отметки начиная с сегодня
    public static List<(DateTime day, DayMark mark)> Upcoming(int count)
    {
        var res = new List<(DateTime, DayMark)>();
        for (int i = 0; i < 400 && res.Count < count; i++)
        {
            var d = DateTime.Today.AddDays(i);
            foreach (var m in For(d)) { res.Add((d, m)); if (res.Count >= count) break; }
        }
        return res;
    }

    // ---------- свои события ----------
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island", "calendar.json");
    private static Dictionary<string, List<string>>? _user;

    private static Dictionary<string, List<string>> User
    {
        get
        {
            if (_user != null) return _user;
            try
            {
                if (File.Exists(FilePath)) _user = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(FilePath));
            }
            catch (Exception ex) { App.Log("Calendar.Load", ex); }
            return _user ??= new Dictionary<string, List<string>>();
        }
    }

    public static IEnumerable<string> UserEvents(DateTime d)
    {
        if (User.TryGetValue(d.ToString("yyyy-MM-dd"), out var once)) foreach (var e in once) yield return e;
        if (User.TryGetValue(d.ToString("MM-dd"), out var yearly)) foreach (var e in yearly) yield return e;
    }

    // события дня с пометкой «каждый год», чтобы их можно было удалить
    public static List<(string text, bool yearly)> UserEventsWithKind(DateTime d)
    {
        var l = new List<(string, bool)>();
        if (User.TryGetValue(d.ToString("yyyy-MM-dd"), out var once)) l.AddRange(once.Select(e => (e, false)));
        if (User.TryGetValue(d.ToString("MM-dd"), out var yearly)) l.AddRange(yearly.Select(e => (e, true)));
        return l;
    }

    public static void AddEvent(DateTime d, string text, bool yearly)
    {
        string k = yearly ? d.ToString("MM-dd") : d.ToString("yyyy-MM-dd");
        if (!User.TryGetValue(k, out var l)) User[k] = l = new List<string>();
        l.Add(text.Trim());
        Save();
    }

    public static void RemoveEvent(DateTime d, string text, bool yearly)
    {
        string k = yearly ? d.ToString("MM-dd") : d.ToString("yyyy-MM-dd");
        if (User.TryGetValue(k, out var l)) { l.Remove(text); if (l.Count == 0) User.Remove(k); }
        Save();
    }

    private static void Save()
    {
        try { SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(User, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { App.Log("Calendar.Save", ex); }
    }
}
