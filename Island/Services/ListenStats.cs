using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Island;

// Статистика прослушиваний (шаг 49). %AppData%\Island\stats: stats.json и маленькие обложки covers\{id}.jpg.
// Остров раз в 5 секунд добавляет 5 секунд играющему треку (Main\Stats.cs). «Прослушивание» засчитывается после 30 секунд трека.
// По дням хранится, сколько секунд играл каждый трек: из этого считаются неделя, месяц и всё время, дни подряд и график.
public sealed class TrackStat
{
    public string T { get; set; } = "";
    public string A { get; set; } = "";
    public int Sec { get; set; }
    public int Plays { get; set; }
    public DateTime First { get; set; }
    public DateTime Last { get; set; }
}

public sealed class StatsData
{
    public Dictionary<string, TrackStat> Tracks { get; set; } = new();
    public Dictionary<string, Dictionary<string, int>> Days { get; set; } = new();   // "2026-10-19" -> id трека -> секунды
    public int[] Hours { get; set; } = new int[24];                                  // секунды по часу дня, за всё время
}

internal sealed record TopTrack(string Id, string Title, string Artist, int Sec, int Plays);
internal sealed record TopArtist(string Name, int Sec, int Tracks);

internal sealed class StatsSummary
{
    public int Days;                 // 7, 30 или 0 (всё время)
    public int TotalSec, TrackCount, ArtistCount, Plays, Streak, BestStreak;
    public List<TopTrack> TopTracks = new();
    public List<TopArtist> TopArtists = new();
    public List<(DateTime day, int sec)> PerDay = new();
    public int[] Hours = new int[24];
    public int PeakHour = -1;
}

internal static class ListenStats
{
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island", "stats");
    private static string JsonPath => Path.Combine(Dir, "stats.json");
    public static string CoverPath(string id) => Path.Combine(Dir, "covers", id + ".jpg");

    private static StatsData? _data;
    private static bool _dirty;
    public static event Action? Changed;

    public static StatsData Data
    {
        get
        {
            if (_data != null) return _data;
            try
            {
                if (File.Exists(JsonPath)) _data = JsonSerializer.Deserialize<StatsData>(File.ReadAllText(JsonPath));
            }
            catch (Exception ex) { App.Log("Stats.Load", ex); }
            _data ??= new StatsData();
            if (_data.Hours.Length != 24) _data.Hours = new int[24];
            return _data;
        }
    }

    public static string Id(string title, string artist) => CardCollection.IdOf(title, artist);
    private static string DayKey(DateTime d) => d.ToString("yyyy-MM-dd");

    public static void Add(string title, string artist, int sec)
    {
        var d = Data;
        string id = Id(title, artist);
        var now = DateTime.Now;
        if (!d.Tracks.TryGetValue(id, out var t)) d.Tracks[id] = t = new TrackStat { T = title, A = artist, First = now };
        t.Sec += sec;
        t.Last = now;
        string dk = DayKey(now);
        if (!d.Days.TryGetValue(dk, out var day)) d.Days[dk] = day = new Dictionary<string, int>();
        day[id] = day.GetValueOrDefault(id) + sec;
        d.Hours[now.Hour] += sec;
        _dirty = true;
    }

    public static void CountPlay(string title, string artist)
    {
        if (Data.Tracks.TryGetValue(Id(title, artist), out var t)) { t.Plays++; _dirty = true; }
        Changed?.Invoke();
    }

    // маленькая обложка для списков и карточки «итогов»
    public static void SaveCover(string title, string artist, BitmapSource cover)
    {
        try
        {
            string path = CoverPath(Id(title, artist));
            if (File.Exists(path)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            double k = 160.0 / Math.Max(cover.PixelWidth, cover.PixelHeight);
            BitmapSource small = k < 1 ? new TransformedBitmap(cover, new ScaleTransform(k, k)) : cover;
            var enc = new JpegBitmapEncoder { QualityLevel = 88 };
            enc.Frames.Add(BitmapFrame.Create(small));
            using var fs = File.Create(path);
            enc.Save(fs);
        }
        catch (Exception ex) { App.LogOnce("Stats.Cover", ex); }
    }

    public static BitmapSource? Cover(string id)
    {
        try
        {
            string p = CoverPath(id);
            if (!File.Exists(p)) return null;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;   // файл не держим открытым
            bi.UriSource = new Uri(p);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    public static void Save()
    {
        if (!_dirty || _data == null) return;
        _dirty = false;
        // старше 400 дней по дням не нужно: общие суммы остаются в Tracks
        string cut = DayKey(DateTime.Today.AddDays(-400));
        foreach (var k in _data.Days.Keys.Where(k => string.CompareOrdinal(k, cut) < 0).ToList()) _data.Days.Remove(k);
        try { SafeFile.WriteAllText(JsonPath, JsonSerializer.Serialize(_data)); }
        catch (Exception ex) { App.Log("Stats.Save", ex); }
    }

    public static double TotalHours => Data.Tracks.Values.Sum(t => (double)t.Sec) / 3600;

    // days: 7, 30 или 0 (всё время)
    // шаг 73: итоги календарного месяца (для «Итоги месяца»): треки, исполнители и график по дням этого месяца
    public static StatsSummary QueryMonth(int year, int month)
    {
        var d = Data;
        var s = new StatsSummary { Days = 30 };
        var secs = new Dictionary<string, int>();
        var first = new DateTime(year, month, 1);
        for (int i = 0; i < DateTime.DaysInMonth(year, month); i++)
        {
            var day = first.AddDays(i);
            if (day > DateTime.Today) break;
            int daySec = 0;
            if (d.Days.TryGetValue(DayKey(day), out var m))
                foreach (var (id, sec) in m) { secs[id] = secs.GetValueOrDefault(id) + sec; daySec += sec; }
            s.PerDay.Add((day, daySec));
        }
        s.Hours = (int[])d.Hours.Clone();
        s.TotalSec = secs.Values.Sum();
        s.TrackCount = secs.Count(kv => kv.Value >= 30);
        var tracks = secs.Where(kv => d.Tracks.ContainsKey(kv.Key) && kv.Value >= 30)
            .Select(kv => (t: d.Tracks[kv.Key], id: kv.Key, sec: kv.Value)).ToList();
        s.TopTracks = tracks.OrderByDescending(x => x.sec).Take(5).Select(x => new TopTrack(x.id, x.t.T, x.t.A, x.sec, x.t.Plays)).ToList();
        var artists = tracks.Where(x => x.t.A.Length > 0).GroupBy(x => x.t.A, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TopArtist(g.First().t.A, g.Sum(x => x.sec), g.Count())).OrderByDescending(a => a.Sec).ToList();
        s.ArtistCount = artists.Count;
        s.TopArtists = artists.Take(5).ToList();
        int peak = Array.IndexOf(s.Hours, s.Hours.Max());
        s.PeakHour = s.Hours.Max() > 0 ? peak : -1;
        (s.Streak, s.BestStreak) = Streaks(d);
        return s;
    }

    public static StatsSummary Query(int days)
    {
        var d = Data;
        var s = new StatsSummary { Days = days };
        var secs = new Dictionary<string, int>();
        if (days == 0)
        {
            foreach (var (id, t) in d.Tracks) secs[id] = t.Sec;
            s.Hours = (int[])d.Hours.Clone();
        }
        else
        {
            for (int i = 0; i < days; i++)
                if (d.Days.TryGetValue(DayKey(DateTime.Today.AddDays(-i)), out var day))
                    foreach (var (id, sec) in day) secs[id] = secs.GetValueOrDefault(id) + sec;
            s.Hours = (int[])d.Hours.Clone();   // по часам считаем за всё время: за неделю картина слишком рваная
        }
        s.TotalSec = secs.Values.Sum();
        s.TrackCount = secs.Count(kv => kv.Value >= 30);
        var tracks = secs.Where(kv => d.Tracks.ContainsKey(kv.Key) && kv.Value >= 30)
            .Select(kv => (t: d.Tracks[kv.Key], id: kv.Key, sec: kv.Value)).ToList();
        s.Plays = days == 0 ? d.Tracks.Values.Sum(t => t.Plays) : 0;
        s.TopTracks = tracks.OrderByDescending(x => x.sec).Take(5).Select(x => new TopTrack(x.id, x.t.T, x.t.A, x.sec, x.t.Plays)).ToList();
        var artists = tracks.Where(x => x.t.A.Length > 0).GroupBy(x => x.t.A, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TopArtist(g.First().t.A, g.Sum(x => x.sec), g.Count())).OrderByDescending(a => a.Sec).ToList();
        s.ArtistCount = artists.Count;
        s.TopArtists = artists.Take(5).ToList();

        int chart = days == 0 ? 30 : days;
        for (int i = chart - 1; i >= 0; i--)
        {
            var day = DateTime.Today.AddDays(-i);
            s.PerDay.Add((day, d.Days.TryGetValue(DayKey(day), out var m) ? m.Values.Sum() : 0));
        }
        int peak = Array.IndexOf(s.Hours, s.Hours.Max());
        s.PeakHour = s.Hours.Max() > 0 ? peak : -1;
        (s.Streak, s.BestStreak) = Streaks(d);
        return s;
    }

    // дни подряд, когда слушал хотя бы минуту: текущая серия (сегодня или до вчера) и лучшая
    public static (int cur, int best) Streaks(StatsData d)
    {
        var days = d.Days.Where(kv => kv.Value.Values.Sum() >= 60)
            .Select(kv => DateTime.TryParse(kv.Key, out var t) ? t.Date : DateTime.MinValue)
            .Where(t => t != DateTime.MinValue).ToHashSet();
        int best = 0;
        foreach (var day in days)
        {
            if (days.Contains(day.AddDays(-1))) continue;   // не начало серии
            int n = 1;
            while (days.Contains(day.AddDays(n))) n++;
            best = Math.Max(best, n);
        }
        var start = days.Contains(DateTime.Today) ? DateTime.Today : DateTime.Today.AddDays(-1);
        int cur = 0;
        while (days.Contains(start.AddDays(-cur))) cur++;
        return (cur, best);
    }

    // «вайб» по часу, когда чаще всего слушаешь
    public static (string title, string sub) Vibe(int hour) => hour switch
    {
        < 0 => ("Новичок", "Послушай что-нибудь, и тут появится твой вайб"),
        >= 5 and < 11 => ("Жаворонок", "Больше всего музыки утром"),
        >= 11 and < 17 => ("Дневной поток", "Музыка весь день фоном"),
        >= 17 and < 23 => ("Вечерний вайб", "Главное начинается вечером"),
        _ => ("Ночная сова", "Самое громкое — после полуночи")
    };

    public static string FmtDur(int sec)
    {
        int h = sec / 3600, m = sec / 60 % 60;
        if (h > 0) return h + " " + Loc.T("ч") + " " + m + " " + Loc.T("мин");
        return Math.Max(m, sec > 0 ? 1 : 0) + " " + Loc.T("мин");
    }
}
