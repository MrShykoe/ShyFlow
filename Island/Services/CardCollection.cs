using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Island;

// Коллекционные карточки из обложек (шаг 45). Хранятся в %AppData%\Island\collection: картинка {id}.png и cards.json.
// Одна карточка на трек (id из исполнителя и названия): тот же трек второй раз уже «в коллекции».
//
// Оценка считается по самой картинке, поэтому у одной обложки она всегда одна и та же:
//   Краски    насколько обложка цветная (метрика colorfulness Хаслера и Зюсструнка)
//   Контраст  разброс светлого и тёмного (стандартное отклонение яркости)
//   Детали    сколько на картинке мелких переходов (средний перепад яркости между соседними точками)
//   Гармония  насколько цвета держатся одной гаммы (доля самого частого участка цветового круга, с учётом насыщенности)
//   Удача     случай, привязанный к треку: у одного трека всегда одинаковая
// Итог: взвешенная сумма, X из 10. Редкость по итогу: обычная, необычная, редкая, эпическая, легендарная.
// Примерно одна карточка из двадцати голографическая (тоже по треку).
public sealed class CardEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string File { get; set; } = "";
    public DateTime Added { get; set; }
    public int Number { get; set; }
    public int Colors { get; set; }
    public int Contrast { get; set; }
    public int Detail { get; set; }
    public int Harmony { get; set; }
    public int Luck { get; set; }
    public int Score { get; set; }
    public int Rarity { get; set; }   // 0 обычная .. 4 легендарная
    public bool Foil { get; set; }
    public bool Daily { get; set; }   // шаг 73: карточка дня, первая за день, с повышенной удачей
}

internal static class CardCollection
{
    public static readonly string[] RarityNames = { "Обычная", "Необычная", "Редкая", "Эпическая", "Легендарная" };

    // ---------- шаг 73: карточка дня и наборы ----------
    // Карточка дня: первая забранная за день. Удача у неё от 7 до 10 (а не от 1 до 10), голограмма выпадает
    // примерно раз из шести, а не из двадцати. Так есть повод заходить каждый день.
    public static bool DailyAvailable => !All.Any(c => c.Added.Date == DateTime.Today);

    // Набор: SetSize карточек одного исполнителя. Собранный набор даёт золотую рамку этим карточкам в коллекции.
    public const int SetSize = 3;
    public static string ArtistKey(string artist) => artist.Trim().ToLowerInvariant();
    public static int ArtistCount(string artist)
    {
        string k = ArtistKey(artist);
        return k.Length == 0 ? 0 : All.Count(c => ArtistKey(c.Artist) == k);
    }
    public static bool InSet(CardEntry c) => ArtistCount(c.Artist) >= SetSize;
    public static int SetsCount => All.Where(c => ArtistKey(c.Artist).Length > 0)
        .GroupBy(c => ArtistKey(c.Artist)).Count(g => g.Count() >= SetSize);

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island", "collection");
    private static string JsonPath => Path.Combine(Dir, "cards.json");

    private static List<CardEntry>? _cards;
    public static event Action? Changed;

    public static List<CardEntry> All
    {
        get
        {
            if (_cards != null) return _cards;
            try
            {
                if (System.IO.File.Exists(JsonPath))
                    _cards = JsonSerializer.Deserialize<List<CardEntry>>(System.IO.File.ReadAllText(JsonPath));
            }
            catch (Exception ex) { App.Log("Collection.Load", ex); }
            return _cards ??= new List<CardEntry>();
        }
    }

    private static void Save()
    {
        try { SafeFile.WriteAllText(JsonPath, JsonSerializer.Serialize(All, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { App.Log("Collection.Save", ex); }
        Changed?.Invoke();
    }

    public static string IdOf(string title, string artist)
    {
        uint h = 2166136261;
        foreach (char ch in (artist + "|" + title).ToLowerInvariant()) h = (h ^ ch) * 16777619;
        return h.ToString("x8");
    }

    public static CardEntry? Find(string title, string artist)
    {
        string id = IdOf(title, artist);
        return All.FirstOrDefault(c => c.Id == id);
    }

    // добавить обложку играющего трека; isNew = false, если такая карточка уже есть
    public static (CardEntry card, bool isNew) Add(BitmapSource cover, string title, string artist)
    {
        var old = Find(title, artist);
        if (old != null) return (old, false);
        string id = IdOf(title, artist);
        Directory.CreateDirectory(Dir);
        string file = Path.Combine(Dir, id + ".png");
        var img = Shrink(cover, 512);
        using (var fs = System.IO.File.Create(file))
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(img));
            enc.Save(fs);
        }
        bool daily = DailyAvailable;
        var card = Rate(img, id);
        if (daily)
        {
            uint h = Convert.ToUInt32(id, 16) ^ (uint)DateTime.Today.DayOfYear * 2654435761u;
            card.Daily = true;
            card.Luck = Math.Max(card.Luck, 7 + (int)(h % 4));
            card.Foil = card.Foil || h / 4 % 6 == 1;
            ScoreOf(card);
        }
        card.Title = title; card.Artist = artist; card.File = file;
        card.Added = DateTime.Now;
        card.Number = All.Count == 0 ? 1 : All.Max(c => c.Number) + 1;
        All.Add(card);
        Save();
        return (card, true);
    }

    public static void Remove(CardEntry c)
    {
        All.Remove(c);
        try { if (System.IO.File.Exists(c.File)) System.IO.File.Delete(c.File); }
        catch (Exception ex) { App.Log("Collection.Delete", ex); }
        Save();
    }

    public static BitmapSource? Image(CardEntry c)
    {
        try
        {
            if (!System.IO.File.Exists(c.File)) return null;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(c.File);
            bi.CacheOption = BitmapCacheOption.OnLoad;   // файл не держим открытым: карточку можно удалить
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    private static BitmapSource Shrink(BitmapSource src, int max)
    {
        double sc = Math.Min(1.0, (double)max / Math.Max(src.PixelWidth, src.PixelHeight));
        BitmapSource s = sc < 1 ? new TransformedBitmap(src, new ScaleTransform(sc, sc)) : src;
        var conv = new FormatConvertedBitmap(s, PixelFormats.Bgra32, null, 0);
        conv.Freeze();
        return conv;
    }

    // ---------- оценка обложки ----------
    public static CardEntry Rate(BitmapSource img, string id)
    {
        const int N = 64;
        var small = new FormatConvertedBitmap(
            new TransformedBitmap(img, new ScaleTransform((double)N / img.PixelWidth, (double)N / img.PixelHeight)), PixelFormats.Bgra32, null, 0);
        int w = small.PixelWidth, h = small.PixelHeight;
        var px = new byte[w * h * 4];
        small.CopyPixels(px, w * 4, 0);
        int n = w * h;
        var L = new double[n];
        double sRg = 0, sYb = 0, sRg2 = 0, sYb2 = 0, sL = 0, sL2 = 0;
        var hue = new double[12];
        double satSum = 0;
        for (int i = 0; i < n; i++)
        {
            double b = px[i * 4], g = px[i * 4 + 1], r = px[i * 4 + 2];
            double rg = r - g, yb = 0.5 * (r + g) - b;
            sRg += rg; sYb += yb; sRg2 += rg * rg; sYb2 += yb * yb;
            double l = 0.299 * r + 0.587 * g + 0.114 * b;
            L[i] = l; sL += l; sL2 += l * l;
            // оттенок и насыщенность (HSV)
            double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), d = mx - mn;
            double sat = mx <= 0 ? 0 : d / mx;
            if (d > 0 && sat > 0.15)
            {
                double hh = mx == r ? (g - b) / d % 6 : mx == g ? (b - r) / d + 2 : (r - g) / d + 4;
                if (hh < 0) hh += 6;
                hue[(int)(hh * 2) % 12] += sat;
                satSum += sat;
            }
        }
        double mRg = sRg / n, mYb = sYb / n;
        double colorful = Math.Sqrt(Math.Max(0, sRg2 / n - mRg * mRg) + Math.Max(0, sYb2 / n - mYb * mYb)) + 0.3 * Math.Sqrt(mRg * mRg + mYb * mYb);
        double mL = sL / n, sdL = Math.Sqrt(Math.Max(0, sL2 / n - mL * mL));
        double grad = 0;
        for (int y = 0; y < h - 1; y++)
            for (int x = 0; x < w - 1; x++)
            {
                int i = y * w + x;
                grad += (Math.Abs(L[i + 1] - L[i]) + Math.Abs(L[i + w] - L[i])) / 2;
            }
        grad /= (w - 1) * (h - 1);
        // гармония: самый плотный участок круга в 3 соседних деления; почти бесцветные обложки спокойные, у них гармония средняя
        double best = 0;
        for (int k = 0; k < 12; k++) best = Math.Max(best, hue[k] + hue[(k + 1) % 12] + hue[(k + 11) % 12]);
        double harmonyRaw = satSum < n * 0.05 ? 0.6 : best / satSum;

        int Clamp10(double v) => (int)Math.Round(Math.Clamp(v, 1, 10));
        uint hsh = Convert.ToUInt32(id, 16);
        var c = new CardEntry
        {
            Id = id,
            Colors = Clamp10(colorful / 9),
            Contrast = Clamp10(sdL / 7),
            Detail = Clamp10(grad / 2.4),
            Harmony = Clamp10(1 + harmonyRaw * 9.5),
            Luck = (int)(hsh % 10) + 1,
            Foil = hsh / 10 % 20 == 7
        };
        ScoreOf(c);
        return c;
    }

    // итог и редкость по характеристикам
    private static void ScoreOf(CardEntry c)
    {
        double total = 0.25 * c.Colors + 0.2 * c.Contrast + 0.2 * c.Detail + 0.2 * c.Harmony + 0.15 * c.Luck;
        c.Score = (int)Math.Round(Math.Clamp(total, 1, 10));
        c.Rarity = total >= 8.6 ? 4 : total >= 7.6 ? 3 : total >= 6.4 ? 2 : total >= 5.0 ? 1 : 0;
    }
}
