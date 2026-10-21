using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace Island;

// Достижения коллекции (шаг 49). Считаются по карточкам и статистике прослушиваний; полученные хранятся
// в %AppData%\Island\collection\achievements.json с датой. Новое достижение: плашка в острове (или подсказка в плеере) и звук.
// Смотреть: кнопка «Достижения» в окне коллекции.
internal sealed record AchDef(string Id, string Icon, uint Color, string Title, string Desc, Func<AchCtx, (int cur, int max)> Progress);

internal sealed class AchCtx
{
    public List<CardEntry> Cards = new();
    public double Hours;
    public int BestStreak;
}

internal static class Achievements
{
    private static string JsonPath => Path.Combine(CardCollection.Dir, "achievements.json");
    private static Dictionary<string, DateTime>? _got;

    public static readonly List<AchDef> All = new()
    {
        new("first", "star", 0xFFD60A, "Первая карточка", "Забери в коллекцию первую обложку", c => (c.Cards.Count, 1)),
        new("ten", "layout-grid", 0x0A84FF, "Коллекционер", "10 карточек в коллекции", c => (c.Cards.Count, 10)),
        new("25", "layout-grid", 0x5E5CE6, "Хранитель", "25 карточек", c => (c.Cards.Count, 25)),
        new("50", "layout-grid", 0xBF5AF2, "Музей обложек", "50 карточек", c => (c.Cards.Count, 50)),
        new("100", "trophy", 0xFF9F0A, "Легенда коллекции", "100 карточек", c => (c.Cards.Count, 100)),
        new("rare", "sparkles", 0x40A0FF, "Редкая находка", "Карточка редкости «Редкая» или выше", c => (c.Cards.Any(x => x.Rarity >= 2) ? 1 : 0, 1)),
        new("epic", "sparkles", 0xD37BFF, "Эпично", "Эпическая карточка", c => (c.Cards.Any(x => x.Rarity >= 3) ? 1 : 0, 1)),
        new("legend", "trophy", 0xFFD04A, "Золото", "Легендарная карточка", c => (c.Cards.Any(x => x.Rarity >= 4) ? 1 : 0, 1)),
        new("rainbow", "sparkles", 0x30D158, "Вся радуга", "Карточки всех пяти редкостей", c => (c.Cards.Select(x => x.Rarity).Distinct().Count(), 5)),
        new("foil", "sparkles", 0x64D2FF, "Голограмма", "Голографическая карточка (примерно одна из двадцати)", c => (c.Cards.Any(x => x.Foil) ? 1 : 0, 1)),
        new("perfect", "star", 0xFF375F, "Идеал", "Обложка с оценкой 10/10", c => (c.Cards.Any(x => x.Score >= 10) ? 1 : 0, 1)),
        new("artists", "user", 0xFF9F0A, "Меломан", "Карточки 10 разных исполнителей", c => (c.Cards.Select(x => x.Artist.ToLowerInvariant()).Distinct().Count(), 10)),
        new("owl", "moon", 0x5E5CE6, "Ночная сова", "Забери карточку между полуночью и пятью утра", c => (c.Cards.Any(x => x.Added.Hour < 5) ? 1 : 0, 1)),
        new("h10", "headphones", 0x30D158, "В наушниках", "10 часов музыки (статистика)", c => ((int)c.Hours, 10)),
        new("h100", "headphones", 0xFF453A, "Марафон", "100 часов музыки", c => ((int)c.Hours, 100)),
        new("streak7", "flame", 0xFF6B2C, "Неделя без пауз", "Слушай музыку 7 дней подряд", c => (c.BestStreak, 7)),
        // шаг 73: наборы (3 карточки одного исполнителя) и карточки дня
        new("set1", "layout-grid", 0xFFD04A, "Первый набор", "3 карточки одного исполнителя", c => (Math.Min(1, CardCollection.SetsCount), 1)),
        new("set5", "trophy", 0xFFD04A, "Собиратель наборов", "5 собранных наборов", c => (CardCollection.SetsCount, 5)),
        new("fan", "user", 0xFF375F, "Фанат", "10 карточек одного исполнителя",
            c => (c.Cards.Where(x => x.Artist.Trim().Length > 0).GroupBy(x => x.Artist.Trim().ToLowerInvariant()).Select(g => g.Count()).DefaultIfEmpty(0).Max(), 10)),
        new("daily7", "sun", 0xFF9F0A, "Каждый день", "7 карточек дня", c => (c.Cards.Count(x => x.Daily), 7)),
    };

    public static Dictionary<string, DateTime> Got
    {
        get
        {
            if (_got != null) return _got;
            try
            {
                if (File.Exists(JsonPath)) _got = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(JsonPath));
            }
            catch (Exception ex) { App.Log("Ach.Load", ex); }
            return _got ??= new Dictionary<string, DateTime>();
        }
    }

    public static AchCtx Context()
    {
        var c = new AchCtx { Cards = CardCollection.All, Hours = ListenStats.TotalHours };
        c.BestStreak = ListenStats.Streaks(ListenStats.Data).best;
        return c;
    }

    public static Color ColorOf(AchDef a) => Color.FromRgb((byte)(a.Color >> 16), (byte)(a.Color >> 8), (byte)a.Color);

    // проверить все; вернуть только что полученные
    public static List<AchDef> Check()
    {
        var ctx = Context();
        var fresh = new List<AchDef>();
        foreach (var a in All)
        {
            if (Got.ContainsKey(a.Id)) continue;
            var (cur, max) = a.Progress(ctx);
            if (cur < max) continue;
            Got[a.Id] = DateTime.Now;
            fresh.Add(a);
        }
        if (fresh.Count > 0)
        {
            try { Directory.CreateDirectory(CardCollection.Dir); SafeFile.WriteAllText(JsonPath, JsonSerializer.Serialize(Got)); }
            catch (Exception ex) { App.Log("Ach.Save", ex); }
        }
        return fresh;
    }
}
