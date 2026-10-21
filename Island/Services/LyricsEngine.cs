using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Island;

public sealed class LyricsResult
{
    public List<(double t, string s)> Lines { get; init; } = new();
    // время каждого слова строки (того же индекса), если источник его даёт; иначе null и время слов оценивается по слогам
    public List<WordTimes?> Words { get; init; } = new();
    public bool Synced { get; init; }
    public string Source { get; init; } = "";
    public bool HasWords => Words.Any(w => w != null);
}

// Точное время слов строки, в секундах от начала строки. Слова идут в том же порядке, что и text.Split(' ').
public sealed class WordTimes
{
    public double[] Start = Array.Empty<double>(), End = Array.Empty<double>();
    public int Count => Start.Length;
}

// Поиск слов песни. Источники: lrclib.net (с временем), NetEase и Kugou (с временем), потом обычный текст
// (lrclib и lyrics.ovh): время строк оценивается по длине трека.
// Каждый найденный вариант проверяется: название, исполнитель, версия (remix/cover/sped up...), длительность и язык.
// Лучше без текста, чем с чужим. Найденное хранится на диске.
public static class LyricsEngine
{
    private static readonly HttpClient Web = Make("ShyFlow/1.0 (https://github.com/MrShykoe/ShyFlow)");
    private static readonly HttpClient Browser = Make(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");

    private const RegexOptions RX = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static HttpClient Make(string ua)
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(7) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(ua);
        return c;
    }

    // ============================================================
    //  ПУБЛИЧНЫЙ ВХОД
    // ============================================================
    public static async Task<LyricsResult?> GetAsync(string title, string artist, string album, double dur,
        bool allowPlain, bool extra)
        => (await GetDetailedAsync(title, artist, album, dur, allowPlain, extra)).res;

    // failed = true: интернет или сервер не ответили, это НЕ «текста нет», результат не надо запоминать
    public static async Task<(LyricsResult? res, bool failed)> GetDetailedAsync(string title, string artist,
        string album, double dur, bool allowPlain, bool extra)
    {
        if (string.IsNullOrWhiteSpace(title)) return (null, false);

        var cached = LoadCache(title, artist, dur, allowPlain);
        if (cached.hit) return (cached.res, false);

        var parsed = ParseTitle(title);
        if (parsed.Tags.Contains("instr")) return (null, false); // инструментал или караоке: слов нет
        var cands = Candidates(parsed, artist);
        if (cands.Count == 0) return (null, false);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        var ctx = new Ctx { Dur = dur, Tags = parsed.Tags, Pairs = cands, Raw = title + " " + artist, Ct = cts.Token };
        // исполнители: полное имя («Король и Шут») и его части. Части, отделённые «и», «and», «&», считаются слабыми:
        // это может быть название группы, а не два разных человека
        var weakAll = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var strongAll = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddNames(string src)
        {
            var w = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var x in SplitArtists(src, w))
            {
                if (!ctx.Artists.Contains(x, StringComparer.OrdinalIgnoreCase)) ctx.Artists.Add(x);
                if (w.Contains(x)) weakAll.Add(x); else strongAll.Add(x);
            }
        }
        AddNames(artist);
        foreach (var c in cands) AddNames(c.a);
        if (parsed.Person.Length > 0) { ctx.Artists.Add(parsed.Person); strongAll.Add(parsed.Person); }
        foreach (var x in weakAll) if (!strongAll.Contains(x)) ctx.Weak.Add(x);

        // все источники стартуют сразу
        var tLr = Safe(() => Lrclib(ctx, cands));
        var tNe = extra ? Safe(() => NetEase(ctx, cands)) : Task.FromResult(new List<Hit>());
        var tKg = extra ? Safe(() => Kugou(ctx, cands)) : Task.FromResult(new List<Hit>());
        var tGe = allowPlain ? Safe(() => Genius(ctx, cands)) : Task.FromResult(new List<Hit>());   // Genius: много русских текстов, но без времени

        var lr = await tLr;
        LyricsResult? got = TrySave(ctx, title, artist, lr.Where(h => h.Synced), dur);
        if (got != null)
        {
            // Текст с временем строк уже есть, показываем сразу. NetEase и Kugou часто знают время каждого слова:
            // если они найдут ту же песню, текст тихо подменится на пословный (событие Upgraded), а в кэше останется он.
            if (extra && !got.HasWords)
            {
                var best = lr.Where(h => h.Synced).OrderByDescending(h => h.Score).First();
                _ = UpgradeLater(ctx, title, artist, dur, best, tNe, tKg);
            }
            return (got, false);
        }

        var ne = await tNe;
        var kg = await tKg;
        // при почти равной уверенности берём вариант со временем слов
        got = TrySave(ctx, title, artist, ne.Concat(kg).Where(h => h.Synced).OrderByDescending(h => h.Score + (h.Words ? 0.03 : 0)), dur, true);
        if (got != null) return (got, false);

        if (allowPlain)
        {
            var plains = lr.Where(h => !h.Synced).ToList();
            plains.AddRange(await tGe);
            // lyrics.ovh не отдаёт название и исполнителя найденного текста, проверить его нечем: не используем
            got = TrySave(ctx, title, artist, plains, dur);
            if (got != null) return (got, false);
        }

        // не успели за отведённое время: это не «текста нет», запоминать нельзя
        bool failed = (ctx.Ok == 0 && ctx.Fail > 0) || ctx.Ct.IsCancellationRequested;
        if (!failed) SaveMiss(title, artist);
        return (null, failed);
    }

    private static async Task<List<Hit>> Safe(Func<Task<List<Hit>>> f)
    {
        try { return await f(); }
        catch { return new List<Hit>(); }
    }

    // текст нашёлся, а пословный вариант той же песни пришёл позже (или не пришёл)
    public static event Action<string, string, LyricsResult>? Upgraded;

    private static async Task UpgradeLater(Ctx ctx, string title, string artist, double dur, Hit lineHit, Task<List<Hit>> tNe, Task<List<Hit>> tKg)
    {
        try
        {
            var all = (await tNe).Concat(await tKg).Where(h => h.Synced && h.Words);
            // та же песня с той же уверенностью: иначе лучше оставить проверенный текст по строкам
            var ok = all.Where(h => h.Score >= lineHit.Score - 0.04 && h.Ident >= Math.Min(0.85, lineHit.Ident) && h.Scale == lineHit.Scale);
            var r = TrySave(ctx, title, artist, ok, dur, true);
            if (r != null && r.HasWords) Upgraded?.Invoke(title, artist, r);
        }
        catch (Exception ex) { App.LogOnce("Lyrics.Upgrade", ex); }
    }

    private static LyricsResult? TrySave(Ctx ctx, string title, string artist, IEnumerable<Hit> hits, double dur, bool keepOrder = false)
    {
        var ordered = keepOrder ? hits : hits.OrderByDescending(x => x.Score).ThenByDescending(x => x.Ident);
        foreach (var h in ordered)
        {
            // текст на другом алфавите (китайский под английское название) берём только при точном совпадении исполнителя
            if (ScriptMismatch(ctx.Raw, h.Text) && h.Ident < 0.9) continue;
            var r = Save(title, artist, h.Text, h.Synced, h.Src, dur, h.Scale);
            if (r != null) return r;
        }
        return null;
    }

    private sealed class Hit
    {
        public string Text = "";
        public bool Synced;
        public double Score;
        public double Ident;      // насколько уверены в исполнителе (0..1)
        public double Scale = 1;  // растяжение времени (для sped up / slowed, найденных по оригиналу)
        public string Src = "";
        public bool Words => Synced && WordTagCap.IsMatch(Text);   // есть время каждого слова
    }

    private sealed class Ctx
    {
        public double Dur;
        public HashSet<string> Tags = new();
        public List<(string a, string t)> Pairs = new();
        public List<string> Artists = new();
        public HashSet<string> Weak = new(StringComparer.OrdinalIgnoreCase);
        public string Raw = "";
        public CancellationToken Ct;
        public int Ok, Fail;
    }

    // запрос в сеть; считаем, отвечал ли главный сервер (для отличия «нет текста» от «нет интернета»)
    private static async Task<string?> Fetch(Ctx ctx, HttpClient c, string url, bool primary)
    {
        try
        {
            using var r = await c.GetAsync(url, ctx.Ct);
            int code = (int)r.StatusCode;
            if (primary)
            {
                if (code >= 500 || code == 429) Interlocked.Increment(ref ctx.Fail);
                else Interlocked.Increment(ref ctx.Ok);
            }
            if (!r.IsSuccessStatusCode) return null;
            return await r.Content.ReadAsStringAsync(ctx.Ct);
        }
        catch
        {
            if (primary) Interlocked.Increment(ref ctx.Fail);
            return null;
        }
    }

    // ============================================================
    //  НАЗВАНИЯ: «Artist - Song (Official Video)», версии (remix, cover...) и мусор
    // ============================================================
    private sealed class Parsed
    {
        public List<string> Parts = new();      // название (или «исполнитель», «название»)
        public HashSet<string> Tags = new();    // версия: remix, cover, sped, slowed, acoustic, live, instr
        public string Person = "";              // кто сделал ремикс или кавер, если указано
    }

    private static readonly Regex BracketGroup = new(@"[\(\[\{【（]([^\)\]\}】）]*)[\)\]\}】）]", RegexOptions.Compiled);
    private static readonly Regex DashSplit = new(@"\s+[-–—|]\s+", RegexOptions.Compiled);
    private static readonly Regex Feat = new(@"\s+(feat\.?|ft\.?|featuring|при уч\.?|при участии)\s.*$", RX | RegexOptions.Compiled);
    private static readonly Regex Junk = new(
        @"\b(official\s*(music\s*)?(video|audio|lyric video|clip)|lyric(s)? video|lyrics?|audio|hd|hq|4k|премьера клипа|" +
        @"новинка|clip officiel|videoclip|video oficial|mv)\b", RX | RegexOptions.Compiled);
    private static readonly Regex JunkWhole = new(
        @"^(remaster(ed)?(\s*\d{4})?|\d{4}\s*(digital\s*)?remaster(ed)?|mono|stereo|radio edit|extended.*|original mix|" +
        @"official.*|lyrics?|lyric video|audio|video|clip|клип|слова|текст|hd|hq|4k|prod\..*|version.*|explicit|clean|" +
        @"deluxe.*|single|album version|ost.*|vevo|topic)$", RX | RegexOptions.Compiled);

    private static readonly Regex RxRemix = new(@"\b(remix|rmx|ремикс|bootleg|mashup|flip|rework|vip)\b", RX | RegexOptions.Compiled);
    private static readonly Regex RxCover = new(@"\b(c[oa]ver(ed)?|кавер)\b", RX | RegexOptions.Compiled);
    private static readonly Regex RxSped = new(@"\b(sped\s*up|speed(ed)?\s*up|spedup|nightcore|ускорен\w*)", RX | RegexOptions.Compiled);
    private static readonly Regex RxSlow = new(@"\b(slowed|reverb|замедлен\w*)", RX | RegexOptions.Compiled);
    private static readonly Regex RxAcou = new(@"\b(acoustic|акустик\w*|unplugged)", RX | RegexOptions.Compiled);
    private static readonly Regex RxLive = new(@"\b(live|concert|концерт\w*)\b", RX | RegexOptions.Compiled);
    private static readonly Regex RxInstr = new(@"\b(instrumental|karaoke|караоке|минус|backing\s*track|без\s*слов)\b", RX | RegexOptions.Compiled);
    private static readonly Regex PersonBy = new(@"\bby\s+(.{2,40})$", RX | RegexOptions.Compiled);
    private static readonly Regex PersonOf = new(@"^(.{2,40}?)(?:'s|’s)?\s+(?:remix|rmx|cover|bootleg|flip|rework|vip)\b", RX | RegexOptions.Compiled);

    // ищет в тексте скобки или хвоста признаки версии; возвращает true, если это была пометка версии
    private static bool Scan(string text, Parsed p)
    {
        bool any = false;
        if (RxRemix.IsMatch(text)) { p.Tags.Add("remix"); any = true; }
        if (RxCover.IsMatch(text)) { p.Tags.Add("cover"); any = true; }
        if (RxSped.IsMatch(text)) { p.Tags.Add("sped"); any = true; }
        if (RxSlow.IsMatch(text)) { p.Tags.Add("slowed"); any = true; }
        if (RxAcou.IsMatch(text)) { p.Tags.Add("acoustic"); any = true; }
        if (RxLive.IsMatch(text)) { p.Tags.Add("live"); any = true; }
        if (RxInstr.IsMatch(text)) { p.Tags.Add("instr"); any = true; }
        if (any && p.Person.Length == 0)
        {
            string tx = text.Trim();
            var pm = PersonBy.Match(tx);
            if (!pm.Success) pm = PersonOf.Match(tx);
            if (pm.Success)
            {
                string who = pm.Groups[1].Value.Trim();
                if (!Regex.IsMatch(who, @"\b(official|extended|radio|club|original|version|mix|the)\b", RX)) p.Person = who;
            }
        }
        return any;
    }

    // часть названия после « - »: это пометка версии/мусор или часть настоящего названия?
    private static bool VariantPart(string part, Parsed p)
    {
        string s = part.Trim();
        if (s.Length == 0) return true;
        if (JunkWhole.IsMatch(s)) return true;
        if (s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 5) return false;
        var tmp = new Parsed();
        if (!Scan(s, tmp)) return false;
        // «Live Forever»: live в начале названия это название, а не версия
        if (tmp.Tags.Count == 1 && tmp.Tags.Contains("live") &&
            !Regex.IsMatch(s, @"^live(\s+(at|in|from|on|version|session|performance|acoustic)\b.*)?$|\blive$", RX))
            return false;
        foreach (var t in tmp.Tags) p.Tags.Add(t);
        if (tmp.Person.Length > 0 && p.Person.Length == 0) p.Person = tmp.Person;
        return true;
    }

    private static Parsed ParseTitle(string raw)
    {
        var p = new Parsed();
        string s = raw ?? "";
        // скобки: пометку версии запоминаем, остальное выбрасываем
        s = BracketGroup.Replace(s, m => { Scan(m.Groups[1].Value, p); return " "; });
        var parts = DashSplit.Split(s).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0 && VariantPart(parts[i], p)) continue;
            string n = Feat.Replace(parts[i], "");
            n = Junk.Replace(n, "");
            n = n.Replace("\"", "").Replace("«", "").Replace("»", "").Replace("“", "").Replace("”", "");
            n = Regex.Replace(n, @"[\(\)\[\]\{\}【】（）]", " ");
            n = Regex.Replace(n, @"\s{2,}", " ").Trim(' ', '-', '–', '—', '|', '.', ',');
            if (n.Length > 0) p.Parts.Add(n);
        }
        return p;
    }

    public static string StripJunk(string t) => string.Join(" - ", ParseTitle(t).Parts);

    private static List<string> NamesOf(Parsed p)
    {
        var l = new List<string>(p.Parts);
        if (p.Parts.Count > 1) l.Add(string.Join(" ", p.Parts));
        return l;
    }

    private static readonly Regex StrongSep = new(@"\s*(?:,|;|/|×|\bfeat\.?\s|\bft\.?\s|\bx\b)\s*", RX | RegexOptions.Compiled);
    private static readonly Regex WeakSep = new(@"\s*(?:&|\band\b|\bи\b)\s*", RX | RegexOptions.Compiled);

    private static string CleanArtist(string a)
    {
        if (string.IsNullOrWhiteSpace(a)) return "";
        a = a.Replace(" - Topic", "", StringComparison.OrdinalIgnoreCase);
        a = Regex.Replace(a, "VEVO", "", RegexOptions.IgnoreCase);
        return a.Trim();
    }

    // Первым идёт ПОЛНОЕ имя («Король и Шут»), дальше его части. Части, найденные по «и», «and», «&», кладутся в weak.
    private static List<string> SplitArtists(string a, HashSet<string>? weak = null)
    {
        var res = new List<string>();
        a = CleanArtist(a);
        if (a.Length == 0) return res;
        res.Add(a);
        foreach (var sp in StrongSep.Split(a))
        {
            string strong = sp.Trim();
            if (strong.Length == 0) continue;
            if (!res.Contains(strong, StringComparer.OrdinalIgnoreCase)) res.Add(strong);
            foreach (var wp in WeakSep.Split(strong))
            {
                string t = wp.Trim();
                if (t.Length == 0 || t.Equals(strong, StringComparison.OrdinalIgnoreCase)) continue;
                if (!res.Contains(t, StringComparer.OrdinalIgnoreCase)) { res.Add(t); weak?.Add(t); }
            }
        }
        return res;
    }

    private static string TagWords(HashSet<string> tags) =>
        string.Join(" ", tags.Select(t => t == "sped" ? "sped up" : t));

    private static List<(string a, string t)> Candidates(Parsed p, string artist)
    {
        var res = new List<(string a, string t)>();
        var wk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var arts = SplitArtists(artist, wk);
        string a = arts.Count > 0 ? arts[0] : "";                       // полное имя: «Король и Шут»
        string alt = arts.Skip(1).FirstOrDefault(x => !wk.Contains(x)) ?? "";   // первый из нескольких исполнителей: «A, B»
        string na = Norm(a);
        var names = p.Parts;
        if (names.Count == 0) return res;
        if (names.Count >= 2)
        {
            string p0 = names[0], p1 = names[1];
            if (na.Length > 0 && Norm(p0).Contains(na)) res.Add((a, p1));
            if (na.Length > 0 && Norm(p1).Contains(na)) res.Add((a, p0));
            res.Add((p0, p1));
            res.Add((a, p0));
        }
        string whole = names.Count == 1 ? names[0] : string.Join(" - ", names);
        res.Add((a, whole));
        if (alt.Length > 0) res.Add((alt, whole));
        if (a.Length > 0) res.Add(("", whole));
        return res.Where(x => x.t.Length > 0)
                  .GroupBy(x => Norm(x.a) + "|" + Norm(x.t)).Select(g => g.First())
                  .Take(5).ToList();
    }

    private static List<string> SearchStrings(Ctx ctx, List<(string a, string t)> cands, int maxCands)
    {
        var list = new List<string>();
        foreach (var (a, t) in cands.Take(maxCands))
        {
            string q = (a + " " + t).Trim();
            if (!list.Contains(q)) list.Add(q);
            if (ctx.Tags.Count > 0)
            {
                string q2 = q + " " + TagWords(ctx.Tags);
                if (!list.Contains(q2)) list.Add(q2);
            }
        }
        return list;
    }

    // ============================================================
    //  СХОДСТВО И ОЦЕНКА НАЙДЕННОГО ТРЕКА
    // ============================================================
    private static string Norm(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch == 'ё' ? 'е' : ch);
        }
        return sb.ToString();
    }

    // Кириллица -> латиница, а потом «скелет» звучания: Скриптонит и Skryptonit, Деготь и Degot, Кино и Kino становятся почти равны
    private static readonly Dictionary<char, string> Tr = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e", ['ж'] = "zh", ['з'] = "z",
        ['и'] = "i", ['й'] = "i", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r",
        ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sh",
        ['ъ'] = "", ['ы'] = "i", ['ь'] = "", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya", ['і'] = "i", ['ї'] = "yi", ['є'] = "e", ['ґ'] = "g"
    };

    private static bool HasCyr(string s)
    {
        foreach (var ch in s) if (ch >= 0x0400 && ch <= 0x04FF) return true;
        return false;
    }

    private static bool HasLat(string s)
    {
        foreach (var ch in s) if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z')) return true;
        return false;
    }

    private static string Skeleton(string norm)
    {
        var sb = new StringBuilder();
        foreach (var ch in norm)
            if (Tr.TryGetValue(ch, out var l)) sb.Append(l); else sb.Append(ch);
        string t = sb.ToString();
        t = t.Replace("ph", "f").Replace("kh", "h").Replace("ch", "4").Replace("sh", "6").Replace("zh", "7").Replace("ts", "8")
             .Replace("ya", "9").Replace("ia", "9").Replace("ja", "9").Replace("yu", "0").Replace("iu", "0").Replace("ju", "0")
             .Replace("ck", "k").Replace("x", "ks").Replace("c", "k").Replace("q", "k").Replace("w", "v").Replace("j", "i").Replace("y", "i")
             .Replace("h", "");
        var o = new StringBuilder();
        foreach (var ch in t)
            if (o.Length == 0 || o[^1] != ch) o.Append(ch);   // двойные буквы -> одна
        return o.ToString();
    }

    private static double Sim(string a, string b, bool contain = true)
    {
        a = Norm(a); b = Norm(b);
        if (a.Length == 0 || b.Length == 0) return 0;
        double s = SimCore(a, b, contain);
        if (s < 1 && (HasCyr(a) || HasCyr(b)) && (HasLat(a) || HasLat(b)))
            s = Math.Max(s, SimCore(Skeleton(a), Skeleton(b), contain) * 0.96);
        return s;
    }

    private static double SimCore(string a, string b, bool contain)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;
        if (contain && (a.Contains(b) || b.Contains(a)))
        {
            double ratio = (double)Math.Min(a.Length, b.Length) / Math.Max(a.Length, b.Length);
            if (ratio >= 0.6) return 0.82 + 0.15 * ratio;
        }
        int[] prev = new int[b.Length + 1], cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return 1.0 - (double)prev[b.Length] / Math.Max(a.Length, b.Length);
    }

    private static double ArtistSim(string cand, Ctx ctx)
    {
        double best = 0;
        var cw = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in SplitArtists(cand, cw))
            foreach (var w in ctx.Artists)
            {
                string nc = Norm(c), nw = Norm(w);
                if (nc.Length == 0 || nw.Length == 0) continue;
                double s;
                if (nc == nw) s = 1;
                else if (Math.Min(nc.Length, nw.Length) >= 3 && (nc.Contains(nw) || nw.Contains(nc)))
                    s = (double)Math.Min(nc.Length, nw.Length) / Math.Max(nc.Length, nw.Length) >= 0.6 ? 0.9 : 0.55;   // «Шут» внутри «Шутов» это не тот же исполнитель
                else s = Sim(c, w, false);
                // совпала только часть названия группы («Король» из «Король и Шут»): не уверены
                if (cw.Contains(c) || ctx.Weak.Contains(w)) s = Math.Min(s, 0.75);
                if (s > best) best = s;
            }
        return best;
    }

    private static bool TagsCompatible(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0) return true;
        if (a.Count == 0 || b.Count == 0) return false;
        return a.Overlaps(b);
    }

    // 0 = не подходит. Иначе оценка 0.68..1.
    // ident: уверенность в исполнителе. scale: во сколько раз растянуть время (sped up / slowed по тексту оригинала).
    private static double Score(Ctx ctx, string candTitle, string candArtist, double candDur, out double ident, out double scale, bool lenient = false)
    {
        ident = 0; scale = 1;
        var ct = ParseTitle(candTitle);
        if (ct.Parts.Count == 0) return 0;
        if (ct.Tags.Contains("instr")) return 0;

        // название
        double ts = 0;
        foreach (var cn in NamesOf(ct))
            foreach (var pr in ctx.Pairs)
                ts = Math.Max(ts, Sim(cn, pr.t));
        if (ts < 0.78) return 0;

        // исполнитель
        double asim = 0.5;
        bool haveArtists = ctx.Artists.Count > 0 && !string.IsNullOrWhiteSpace(candArtist);
        if (haveArtists) asim = ArtistSim(candArtist, ctx);
        ident = asim;

        bool durKnown = !lenient && ctx.Dur > 1 && candDur > 1;   // lenient: длину не сравниваем (для обычного текста без времени)
        double dd = durKnown ? Math.Abs(candDur - ctx.Dur) : 0;

        // исполнитель и длина совпали: это он, даже если в названии нет пометки remix/cover
        bool strong = haveArtists && asim >= 0.85 && durKnown && dd <= 3;

        // версия: оригинал не подходит ремиксу или каверу и наоборот
        bool needScale = false;
        if (!TagsCompatible(ctx.Tags, ct.Tags) && !strong)
        {
            bool speedOnly = ct.Tags.Count == 0 && ctx.Tags.Count > 0 && ctx.Tags.All(x => x == "sped" || x == "slowed");
            if (!speedOnly) return 0;
            needScale = true;
        }

        if (needScale)
        {
            if (!durKnown) return 0;
            if (dd <= 3) needScale = false;
            else
            {
                double ratio = ctx.Dur / candDur;
                if (ratio < 0.5 || ratio > 2.0) return 0;
                scale = ratio;
            }
        }
        else if (durKnown && dd > 6) return 0; // другая версия трека

        if (haveArtists && asim < 0.45)
        {
            // другой исполнитель: годится только транслитерация (имя на другом алфавите: Кино и Kino, 李荣浩 и Li Ronghao)
            // при точном названии и совпавшей длине. Тёзка-песня другого исполнителя на том же алфавите не подходит.
            if (!(ts >= 0.95 && durKnown && dd <= 2.0 && !needScale && scale == 1 &&
                  ScriptClass(candArtist) != ScriptClass(string.Join(" ", ctx.Artists)))) return 0;
            asim = 0.45;
        }
        // без длины проверить нечем: нужно почти идеальное совпадение
        if (!durKnown && !(ts >= 0.9 && ((haveArtists && asim >= 0.6) || (ctx.Artists.Count == 0 && ts >= 0.97)))) return 0;

        double score = ts * 0.55 + asim * 0.35 + (durKnown ? Math.Max(0, 0.10 - dd * 0.015) : 0.03);
        if (scale != 1) score -= 0.08;
        return score >= 0.68 ? score : 0;
    }

    // ============================================================
    //  ЯЗЫК ТЕКСТА (против китайского под английское название)
    // ============================================================
    private static bool IsNonLatinLetter(char c) =>
        (c >= 0x0E00 && c <= 0x0E7F) || (c >= 0x0600 && c <= 0x06FF) || (c >= 0x0900 && c <= 0x097F) ||
        (c >= 0x3040 && c <= 0x30FF) || (c >= 0x3400 && c <= 0x9FFF) || (c >= 0xAC00 && c <= 0xD7AF);

    // 0 = латиница, 1 = кириллица, 2 = китайский/японский/корейский/арабский и т.п.
    private static int ScriptClass(string s)
    {
        int lat = 0, cyr = 0, oth = 0;
        foreach (var ch in s)
        {
            if (!char.IsLetter(ch)) continue;
            if (ch >= 0x0400 && ch <= 0x04FF) cyr++;
            else if (IsNonLatinLetter(ch)) oth++;
            else lat++;
        }
        if (oth > 0 && oth >= lat && oth >= cyr) return 2;
        return cyr > lat ? 1 : 0;
    }

    private static bool ScriptMismatch(string wanted, string text)
    {
        foreach (var ch in wanted) if (IsNonLatinLetter(ch)) return false;
        int letters = 0, other = 0;
        foreach (var ch in text)
        {
            if (!char.IsLetter(ch)) continue;
            letters++;
            if (IsNonLatinLetter(ch)) other++;
        }
        return letters >= 20 && other > letters * 0.5;
    }

    // ============================================================
    //  LRCLIB
    // ============================================================
    private static string Q(string s) => Uri.EscapeDataString(s);

    private static string? Str(JsonElement el, string name)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
        {
            var s = p.GetString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        return null;
    }

    private static async Task<List<Hit>> Lrclib(Ctx ctx, List<(string a, string t)> cands)
    {
        var urls = new List<string>();
        int i = 0;
        foreach (var (a, t) in cands)
        {
            if (i < 2 && a.Length > 0)
                urls.Add($"https://lrclib.net/api/get?artist_name={Q(a)}&track_name={Q(t)}" +
                         (ctx.Dur > 1 ? $"&duration={(int)Math.Round(ctx.Dur)}" : ""));
            if (i < 3)
            {
                if (a.Length > 0) urls.Add($"https://lrclib.net/api/search?track_name={Q(t)}&artist_name={Q(a)}");
                urls.Add("https://lrclib.net/api/search?q=" + Q((a + " " + t).Trim()));
                if (ctx.Tags.Count > 0)
                    urls.Add("https://lrclib.net/api/search?q=" + Q((a + " " + t + " " + TagWords(ctx.Tags)).Trim()));
            }
            i++;
        }
        if (cands.Count > 0 && cands[0].a.Length > 0) urls.Add("https://lrclib.net/api/search?q=" + Q(cands[0].t));
        urls = urls.Distinct().Take(10).ToList();

        var sem = new SemaphoreSlim(4);
        Func<string, Task<string?>> run = async u =>
        {
            try { await sem.WaitAsync(ctx.Ct); } catch { return null; }
            try { return await Fetch(ctx, Web, u, true); }
            finally { sem.Release(); }
        };
        var bodies = await Task.WhenAll(urls.Select(run));

        var hits = new List<Hit>();
        foreach (var body in bodies)
        {
            if (body == null) continue;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                    foreach (var el in root.EnumerateArray()) AddLrclib(ctx, el, hits);
                else if (root.ValueKind == JsonValueKind.Object)
                    AddLrclib(ctx, root, hits);
            }
            catch { }
        }
        return hits;
    }

    private static void AddLrclib(Ctx ctx, JsonElement el, List<Hit> hits)
    {
        if (el.ValueKind != JsonValueKind.Object) return;
        if (el.TryGetProperty("instrumental", out var ins) && ins.ValueKind == JsonValueKind.True) return;
        string name = Str(el, "trackName") ?? Str(el, "name") ?? "";
        string art = Str(el, "artistName") ?? "";
        double cd = el.TryGetProperty("duration", out var du) && du.ValueKind == JsonValueKind.Number ? du.GetDouble() : 0;
        double sc = Score(ctx, name, art, cd, out var ident, out var scale);
        bool len = false;
        if (sc <= 0)
        {
            // длина не сошлась (клип с вступлением, другая версия): обычный текст всё равно подходит, если название и исполнитель те же
            sc = Score(ctx, name, art, cd, out ident, out scale, true);
            len = true;
        }
        if (sc <= 0) return;
        var syn = Str(el, "syncedLyrics");
        var pl = Str(el, "plainLyrics");
        if (syn != null && !len) hits.Add(new Hit { Text = syn, Synced = true, Score = sc, Ident = ident, Scale = scale, Src = "lrclib" });
        if (pl != null) hits.Add(new Hit { Text = pl, Synced = false, Score = sc, Ident = ident, Scale = 1, Src = "plain" });
    }

    // ============================================================
    //  NETEASE
    // ============================================================
    private static async Task<List<Hit>> NetEase(Ctx ctx, List<(string a, string t)> cands)
    {
        var hits = new List<Hit>();
        var tried = new HashSet<long>();
        foreach (var q in SearchStrings(ctx, cands, 2).Take(3))
        {
            try
            {
                // старый поиск (search/get/web) теперь отвечает зашифрованной строкой; cloudsearch пока отдаёт обычный JSON
                var body = await Fetch(ctx, Browser,
                    "https://music.163.com/api/cloudsearch/pc?type=1&offset=0&limit=10&s=" + Q(q), false);
                if (body == null) continue;
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("result", out var res) || res.ValueKind != JsonValueKind.Object ||
                    !res.TryGetProperty("songs", out var songs) || songs.ValueKind != JsonValueKind.Array) continue;

                var scored = new List<(long id, double sc, double ident, double scale)>();
                foreach (var song in songs.EnumerateArray())
                {
                    string name = Str(song, "name") ?? "";
                    var arts = new List<string>();
                    // cloudsearch: исполнители в "ar", длина в "dt"; старый формат: "artists" и "duration"
                    foreach (var key in new[] { "ar", "artists" })
                        if (song.TryGetProperty(key, out var ar) && ar.ValueKind == JsonValueKind.Array)
                            foreach (var x in ar.EnumerateArray())
                            {
                                var an = Str(x, "name");
                                if (an != null && !arts.Contains(an)) arts.Add(an);
                            }
                    double cd = 0;
                    foreach (var key in new[] { "dt", "duration" })
                        if (cd == 0 && song.TryGetProperty(key, out var du) && du.ValueKind == JsonValueKind.Number)
                            cd = du.GetDouble() / 1000.0;
                    double sc = Score(ctx, name, string.Join(", ", arts), cd, out var ident, out var scale);
                    if (sc > 0 && song.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
                        scored.Add((idEl.GetInt64(), sc, ident, scale));
                }

                foreach (var c in scored.OrderByDescending(x => x.sc).Take(2))
                {
                    if (!tried.Add(c.id)) continue;
                    // lyric/v1 с yv=-1 отдаёт ещё и yrc: время каждого слова. Нет его: обычный LRC
                    var lb = await Fetch(ctx, Browser, $"https://music.163.com/api/song/lyric/v1?id={c.id}&lv=-1&kv=-1&tv=-1&yv=-1", false)
                             ?? await Fetch(ctx, Browser, $"https://music.163.com/api/song/lyric?id={c.id}&lv=-1&kv=-1&tv=-1", false);
                    if (lb == null) continue;
                    using var ld = JsonDocument.Parse(lb);
                    string? words = ld.RootElement.TryGetProperty("yrc", out var yrc) ? YrcToLrc(Str(yrc, "lyric")) : null;
                    string? txt = words ?? (ld.RootElement.TryGetProperty("lrc", out var lrc) ? Str(lrc, "lyric") : null);
                    if (txt != null && Stamp.IsMatch(txt))
                        hits.Add(new Hit { Text = txt, Synced = true, Score = c.sc, Ident = c.ident, Scale = c.scale, Src = words != null ? "netease-words" : "netease" });
                }
            }
            catch { }
        }
        return hits;
    }

    // ============================================================
    //  KUGOU
    // ============================================================
    private static async Task<List<Hit>> Kugou(Ctx ctx, List<(string a, string t)> cands)
    {
        var hits = new List<Hit>();
        var tried = new HashSet<string>();
        foreach (var kw in SearchStrings(ctx, cands, 2).Take(2))
        {
            try
            {
                var body = await Fetch(ctx, Browser,
                    "https://mobileservice.kugou.com/api/v3/search/song?format=json&page=1&pagesize=8&showtype=1&keyword=" + Q(kw), false);
                if (body == null) continue;
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
                    !data.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Array) continue;

                var scored = new List<(string hash, int secs, double sc, double ident, double scale)>();
                foreach (var it in info.EnumerateArray())
                {
                    string name = Str(it, "songname") ?? "";
                    string singer = Str(it, "singername") ?? "";
                    int du = it.TryGetProperty("duration", out var dEl) && dEl.ValueKind == JsonValueKind.Number
                        ? (int)dEl.GetDouble() : 0;
                    double sc = Score(ctx, name, singer, du, out var ident, out var scale);
                    string? hash = Str(it, "hash");
                    if (sc > 0 && hash != null) scored.Add((hash, du, sc, ident, scale));
                }

                foreach (var c in scored.OrderByDescending(x => x.sc).Take(2))
                {
                    if (!tried.Add(c.hash)) continue;
                    var b2 = await Fetch(ctx, Browser,
                        $"https://lyrics.kugou.com/search?ver=1&man=yes&client=pc&keyword={Q(kw)}&duration={c.secs * 1000}&hash={c.hash}", false);
                    if (b2 == null) continue;
                    using var d2 = JsonDocument.Parse(b2);
                    if (!d2.RootElement.TryGetProperty("candidates", out var cs) || cs.ValueKind != JsonValueKind.Array) continue;
                    int n = 0;
                    foreach (var cd in cs.EnumerateArray())
                    {
                        if (n++ >= 2) break;
                        string? id = cd.TryGetProperty("id", out var idEl) ? idEl.ToString() : null;
                        string? key = Str(cd, "accesskey");
                        if (id == null || key == null) continue;
                        // \u0441\u043D\u0430\u0447\u0430\u043B\u0430 KRC (\u0437\u0430\u0448\u0438\u0444\u0440\u043E\u0432\u0430\u043D\u043D\u044B\u0439 \u0444\u043E\u0440\u043C\u0430\u0442 Kugou \u0441\u043E \u0432\u0440\u0435\u043C\u0435\u043D\u0435\u043C \u043A\u0430\u0436\u0434\u043E\u0433\u043E \u0441\u043B\u043E\u0432\u0430), \u043D\u0435 \u0432\u044B\u0448\u043B\u043E: \u043E\u0431\u044B\u0447\u043D\u044B\u0439 LRC
                        string? txt = KrcToLrc(await KugouContent(ctx, id, key, "krc"));
                        bool words = txt != null;
                        if (txt == null)
                        {
                            var b64 = await KugouContent(ctx, id, key, "lrc");
                            if (b64 != null) txt = Encoding.UTF8.GetString(Convert.FromBase64String(b64)).TrimStart('\uFEFF');
                        }
                        if (txt != null && Stamp.IsMatch(txt))
                        {
                            hits.Add(new Hit { Text = txt, Synced = true, Score = c.sc, Ident = c.ident, Scale = c.scale, Src = words ? "kugou-words" : "kugou" });
                            break;
                        }
                    }
                }
            }
            catch { }
        }
        return hits;
    }

    private static async Task<string?> KugouContent(Ctx ctx, string id, string key, string fmt)
    {
        var b = await Fetch(ctx, Browser, $"https://lyrics.kugou.com/download?ver=1&client=pc&id={id}&accesskey={key}&fmt={fmt}&charset=utf8", false);
        if (b == null) return null;
        using var d = JsonDocument.Parse(b);
        return Str(d.RootElement, "content");
    }

    // ============================================================
    //  ВРЕМЯ СЛОВ: yrc (NetEase) и KRC (Kugou) -> расширенный LRC
    // ============================================================
    // Оба формата переводим в расширенный LRC: [mm:ss.xxx]<mm:ss.xxx>слово<mm:ss.xxx> ...
    // Так текст хранится в кэше как обычно, а ParseLrc достаёт из тегов <...> время каждого слова.
    private static readonly Regex YrcLine = new(@"^\[(\d+),(\d+)\](.*)$", RegexOptions.Compiled);
    private static readonly Regex YrcWord = new(@"\((\d+),(\d+),\d+\)([^(]*)", RegexOptions.Compiled);
    private static readonly Regex KrcWord = new(@"<(\d+),(\d+),\d+>([^<]*)", RegexOptions.Compiled);
    private static readonly byte[] KrcKey = { 64, 71, 97, 119, 94, 50, 116, 71, 81, 54, 49, 45, 206, 210, 110, 105 };

    private static string Ts(double ms)
    {
        ms = Math.Max(0, ms);
        int m = (int)(ms / 60000);
        return m.ToString("00", CultureInfo.InvariantCulture) + ":" + (ms / 1000 - m * 60).ToString("00.000", CultureInfo.InvariantCulture);
    }

    // слова с абсолютным временем: yrc (start,dur,0)слово
    private static string? YrcToLrc(string? yrc) => WordsToLrc(yrc, YrcWord, relative: false);

    // KRC: base64 -> "krc1" + XOR с ключом -> zlib -> текст, слова <смещение от начала строки,длительность,0>слово
    private static string? KrcToLrc(string? b64)
    {
        if (string.IsNullOrEmpty(b64)) return null;
        try
        {
            var raw = Convert.FromBase64String(b64);
            if (raw.Length < 8 || Encoding.ASCII.GetString(raw, 0, 4) != "krc1") return null;
            var x = new byte[raw.Length - 4];
            for (int i = 0; i < x.Length; i++) x[i] = (byte)(raw[i + 4] ^ KrcKey[i % KrcKey.Length]);
            using var z = new System.IO.Compression.ZLibStream(new MemoryStream(x), System.IO.Compression.CompressionMode.Decompress);
            using var sr = new StreamReader(z, Encoding.UTF8);
            return WordsToLrc(sr.ReadToEnd(), KrcWord, relative: true);
        }
        catch { return null; }   // другой формат или битый файл: возьмём обычный LRC
    }

    private static string? WordsToLrc(string? src, Regex word, bool relative)
    {
        if (string.IsNullOrWhiteSpace(src)) return null;
        var sb = new StringBuilder();
        int withWords = 0;
        foreach (var rawLine in src.Replace("\r", "").Split('\n'))
        {
            var lm = YrcLine.Match(rawLine.Trim());
            if (!lm.Success) continue;
            double ls = double.Parse(lm.Groups[1].Value, CultureInfo.InvariantCulture);
            double ld = double.Parse(lm.Groups[2].Value, CultureInfo.InvariantCulture);
            sb.Append('[').Append(Ts(ls)).Append(']');
            var ws = word.Matches(lm.Groups[3].Value);
            if (ws.Count == 0) { sb.Append(lm.Groups[3].Value).Append('\n'); continue; }
            withWords++;
            foreach (Match w in ws)
            {
                double s = double.Parse(w.Groups[1].Value, CultureInfo.InvariantCulture) + (relative ? ls : 0);
                double d = double.Parse(w.Groups[2].Value, CultureInfo.InvariantCulture);
                sb.Append('<').Append(Ts(s)).Append('>').Append(w.Groups[3].Value).Append('<').Append(Ts(s + d)).Append('>');
            }
            sb.Append('\n');
        }
        return withWords >= 3 ? sb.ToString() : null;
    }

    // ============================================================
    //  GENIUS (обычный текст, очень много русских песен)
    // ============================================================
    private static async Task<List<Hit>> Genius(Ctx ctx, List<(string a, string t)> cands)
    {
        var hits = new List<Hit>();
        var scored = new List<(string url, double sc, double ident)>();
        foreach (var q in SearchStrings(ctx, cands, 2).Take(2))
        {
            try
            {
                var body = await Fetch(ctx, Browser, "https://genius.com/api/search/multi?per_page=6&q=" + Q(q), false);
                if (body == null) continue;
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("response", out var resp) || resp.ValueKind != JsonValueKind.Object ||
                    !resp.TryGetProperty("sections", out var secs) || secs.ValueKind != JsonValueKind.Array) continue;
                foreach (var sec in secs.EnumerateArray())
                {
                    if (sec.ValueKind != JsonValueKind.Object || !sec.TryGetProperty("hits", out var hs) || hs.ValueKind != JsonValueKind.Array) continue;
                    foreach (var h in hs.EnumerateArray())
                    {
                        if (h.ValueKind != JsonValueKind.Object || !h.TryGetProperty("result", out var r) || r.ValueKind != JsonValueKind.Object) continue;
                        string? url = Str(r, "url");
                        string title = Str(r, "title") ?? "";
                        string art = Str(r, "artist_names") ?? "";
                        if (art.Length == 0 && r.TryGetProperty("primary_artist", out var pa)) art = Str(pa, "name") ?? "";
                        if (url == null || !url.StartsWith("https://genius.com/", StringComparison.OrdinalIgnoreCase) || title.Length == 0) continue;
                        if (art.StartsWith("Genius", StringComparison.OrdinalIgnoreCase)) continue;   // переводы
                        if (Regex.IsMatch(title, @"translat|перевод|traducc|übersetz|romaniz", RX)) continue;
                        double sc = Score(ctx, title, art, 0, out var ident, out var scale);
                        if (sc <= 0 || scale != 1) continue;
                        if (!scored.Any(x => x.url == url)) scored.Add((url, sc, ident));
                    }
                }
                if (scored.Count > 0) break;
            }
            catch { }
        }
        foreach (var c in scored.OrderByDescending(x => x.sc).Take(2))
        {
            try
            {
                var html = await Fetch(ctx, Browser, c.url, false);
                if (html == null) continue;
                var txt = GeniusLyrics(html);
                if (txt != null)
                    hits.Add(new Hit { Text = txt, Synced = false, Score = c.sc, Ident = c.ident, Src = "plain" });
            }
            catch { }
        }
        return hits;
    }

    // вырезает <div ...marker...> ... </div> с учётом вложенных div
    private static string CutDivs(string s, string marker)
    {
        while (true)
        {
            int k = s.IndexOf(marker, StringComparison.Ordinal);
            if (k < 0) return s;
            int st = s.LastIndexOf("<div", k, StringComparison.Ordinal);
            if (st < 0) return s.Remove(k, marker.Length);
            int depth = 0, i = st, end = -1;
            while (i < s.Length)
            {
                int o = s.IndexOf("<div", i, StringComparison.Ordinal);
                int c = s.IndexOf("</div", i, StringComparison.Ordinal);
                if (c < 0) break;
                if (o >= 0 && o < c) { depth++; i = o + 4; continue; }
                depth--;
                int gt = s.IndexOf('>', c);
                i = gt < 0 ? s.Length : gt + 1;
                if (depth <= 0) { end = i; break; }
            }
            s = end < 0 ? s.Remove(st) : s.Remove(st, end - st);
        }
    }

    private static string? GeniusLyrics(string html)
    {
        var sb = new StringBuilder();
        int pos = 0;
        while (true)
        {
            int k = html.IndexOf("data-lyrics-container=\"true\"", pos, StringComparison.Ordinal);
            if (k < 0) break;
            int gt = html.IndexOf('>', k);
            if (gt < 0) break;
            int start = gt + 1, depth = 1, i = start, end = -1;
            while (i < html.Length)
            {
                int o = html.IndexOf("<div", i, StringComparison.Ordinal);
                int c = html.IndexOf("</div", i, StringComparison.Ordinal);
                if (c < 0) break;
                if (o >= 0 && o < c) { depth++; i = o + 4; continue; }
                depth--;
                if (depth == 0) { end = c; break; }
                i = c + 5;
            }
            if (end < 0) break;
            string inner = html.Substring(start, end - start);
            inner = CutDivs(inner, "data-exclude-from-selection");
            inner = Regex.Replace(inner, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            inner = Regex.Replace(inner, @"<[^>]+>", "");
            sb.Append(System.Net.WebUtility.HtmlDecode(inner)).Append("\n\n");
            pos = end;
        }
        if (sb.Length == 0) return null;
        var lines = sb.ToString().Replace("\r", "").Split('\n').Select(l => l.Trim()).ToList();
        lines = lines.Where(l => !Regex.IsMatch(l, @"^\d*\s*Embed$|^You might also like$|^Вам (также )?может понравиться$|^Get tickets as low as", RX)).ToList();
        while (lines.Count > 0 && lines[0].Length < 120 && Regex.IsMatch(lines[0], @"^\d*\s*contributors?|lyrics$|текст песни$|translations?$|переводы?$|^read more", RX))
            lines.RemoveAt(0);
        string text = string.Join("\n", lines).Trim();
        if (text.Length < 80) return null;
        if (Regex.IsMatch(text, @"lyrics for this song have yet to be released|текст этой песни ещё не|we do not have the lyrics", RX)) return null;
        return text;
    }

    // ============================================================
    //  LYRICS.OVH (обычный текст, только точное название и исполнитель)
    // ============================================================
    private static async Task<List<Hit>> LyricsOvh(Ctx ctx, List<(string a, string t)> cands)
    {
        var hits = new List<Hit>();
        foreach (var (a, t) in cands.Where(c => c.a.Length > 0).Take(2))
        {
            var body = await Fetch(ctx, Web, $"https://api.lyrics.ovh/v1/{Q(a)}/{Q(t)}", false);
            if (body == null) continue;
            try
            {
                using var d = JsonDocument.Parse(body);
                var txt = Str(d.RootElement, "lyrics");
                if (txt != null && txt.Length > 40)
                {
                    hits.Add(new Hit { Text = txt, Synced = false, Score = 0.6, Ident = 0.5, Src = "plain" });
                    break;
                }
            }
            catch { }
        }
        return hits;
    }

    // ============================================================
    //  РАЗБОР LRC
    // ============================================================
    private static readonly Regex Stamp = new(@"\[(\d+):(\d+(?:[.:]\d+)?)\]", RegexOptions.Compiled);
    private static readonly Regex WordTag = new(@"<\d+:\d+(?:[.:]\d+)?>", RegexOptions.Compiled);
    private static readonly Regex CreditColon = new(
        @"^(作词|作曲|编曲|制作人?|混音|母带|歌手|词|曲|作詞|编辑|录音|监制|出品|OP|SP|Lyrics\s*by|Lyricist|Music\s*by|" +
        @"Composed\s*by|Composer|Arranged\s*by|Produced\s*by|Producer|Written\s*by|Mixed\s*by|Mastered\s*by)\s*[:：]",
        RX | RegexOptions.Compiled);
    private static readonly Regex CreditPlain = new(
        @"^(Lyrics\s*by|Composed\s*by|Arranged\s*by|Produced\s*by|Written\s*by|Mixed\s*by|Mastered\s*by)\s+\S",
        RX | RegexOptions.Compiled);

    private static readonly Regex WordTagCap = new(@"<(\d+):(\d+(?:[.:]\d+)?)>", RegexOptions.Compiled);

    private static double StampSec(Match m) =>
        int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60 +
        double.Parse(m.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture);

    public static List<(double t, string s)> ParseLrc(string lrc) => ParseLrcFull(lrc).Select(x => (x.t, x.s)).ToList();

    private static List<(double t, string s, WordTimes? w)> ParseLrcFull(string lrc)
    {
        var raw = new List<(double t, string s, WordTimes? w)>();
        double offset = 0;
        var om = Regex.Match(lrc, @"\[offset:\s*([+-]?\d+)\]", RegexOptions.IgnoreCase);
        if (om.Success) offset = int.Parse(om.Groups[1].Value, CultureInfo.InvariantCulture) / 1000.0;

        foreach (var line in lrc.Split('\n'))
        {
            var ms = Stamp.Matches(line);
            if (ms.Count == 0) continue;
            string body = Stamp.Replace(line, "").TrimEnd('\r');
            string text = WordTag.Replace(body, "").Trim();
            text = Regex.Replace(text, @"\s{2,}", " ");
            if (CreditColon.IsMatch(text) || CreditPlain.IsMatch(text)) continue;
            double first = StampSec(ms[0]) - offset;
            var wt = WordTagCap.IsMatch(body) ? WordsOf(body, first, offset, text) : null;
            foreach (Match m in ms)
                raw.Add((Math.Max(0, StampSec(m) - offset), text, wt));   // повтор припева [01:10][02:30]: то же время слов от начала строки
        }

        // стабильная сортировка (List.Sort перемешивает строки с одинаковым временем)
        var sorted = raw.OrderBy(x => x.t).ToList();
        // строки с одним и тем же временем (оригинал и перевод): оставляем первую, иначе текст мелькает
        var res = new List<(double t, string s, WordTimes? w)>();
        foreach (var x in sorted)
        {
            if (res.Count > 0 && Math.Abs(res[^1].t - x.t) < 0.01)
            {
                if (string.IsNullOrWhiteSpace(res[^1].s) && !string.IsNullOrWhiteSpace(x.s)) res[^1] = x;
                continue;
            }
            res.Add(x);
        }
        return res;
    }

    // Время слов из тегов <mm:ss.xx>: текст между двумя тегами звучит от первого до второго.
    // Слог может быть куском слова (yrc), поэтому время ставится каждой букве, а слово берёт начало первой и конец последней.
    private static WordTimes? WordsOf(string body, double lineT, double offset, string text)
    {
        var chars = new StringBuilder();
        var cs = new List<double>();
        var ce = new List<double>();
        void Put(string s, double a, double b)
        {
            foreach (char ch in s) { chars.Append(ch); cs.Add(a); ce.Add(Math.Max(b, a)); }
        }
        double cur = lineT;
        int pos = 0;
        foreach (Match m in WordTagCap.Matches(body))
        {
            double tag = StampSec(m) - offset;
            if (m.Index > pos) Put(body[pos..m.Index], cur, tag);
            cur = tag;
            pos = m.Index + m.Length;
        }
        if (pos < body.Length)
        {
            string tail = body[pos..];
            Put(tail, cur, cur + Math.Max(0.3, 0.08 * tail.Trim().Length));
        }

        var st = new List<double>();
        var en = new List<double>();
        int i = 0;
        string all = chars.ToString();
        while (i < all.Length)
        {
            if (char.IsWhiteSpace(all[i])) { i++; continue; }
            int a = i;
            while (i < all.Length && !char.IsWhiteSpace(all[i])) i++;
            st.Add(cs[a] - lineT);
            en.Add(Math.Max(ce[i - 1], cs[a] + 0.05) - lineT);
        }
        // слова должны совпасть с тем, как строку делят на экране; не совпали: пусть время слов оценивается как раньше
        if (st.Count == 0 || st.Count != text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length) return null;
        return new WordTimes { Start = st.ToArray(), End = en.ToArray() };
    }

    // Время подходит? Отсеивает чужую версию трека (другая длина) и битые файлы, где всё в одну секунду.
    private static bool TimingOk(List<(double t, string s)> lines, double dur)
    {
        var real = lines.Where(l => !string.IsNullOrWhiteSpace(l.s)).ToList();
        if (real.Count < 3) return false;
        int close = 0;
        for (int i = 1; i < real.Count; i++)
            if (real[i].t - real[i - 1].t < 0.15) close++;
        if (close > real.Count * 0.3) return false;
        if (real[^1].t - real[0].t < 5) return false;
        if (dur > 20)
        {
            if (real[^1].t > dur + 8) return false;   // текст длиннее трека
            if (real[0].t > dur * 0.8) return false;
            if (real[^1].t < dur * 0.15) return false; // текст кончается слишком рано
        }
        return true;
    }

    // Обычный текст без времени: время строк оцениваем по слогам и длине трека.
    public static List<(double t, string s)> Estimate(string plain, double dur)
    {
        var rows = new List<string>();
        // шаг 76: заголовки Genius «[Verse 2: Rihanna]» говорят, кто поёт. Каждый новый голос — метка «v1:», «v2:»…
        // на первой строке после заголовка (Services\Duet.cs ставит строки второго голоса справа и прячет метку)
        var voices = new List<string>();
        string? pendingVoice = null;
        foreach (var raw in plain.Replace("\r", "").Split('\n'))
        {
            string l = raw.Trim();
            var head = Regex.Match(l, @"^\[[^\]:]*:\s*([^\]]+)\]$");
            if (head.Success)
            {
                string who = head.Groups[1].Value.Trim().ToLowerInvariant();
                if (!voices.Contains(who)) voices.Add(who);
                pendingVoice = "v" + (voices.IndexOf(who) + 1);
                continue;
            }
            if (Regex.IsMatch(l, @"^\[.*\]$") || Regex.IsMatch(l, @"^\(?(chorus|verse|bridge|intro|outro|припев|куплет|бридж)[^)]*\)?:?$", RegexOptions.IgnoreCase))
                continue;
            if (l.Length == 0 && (rows.Count == 0 || rows[^1].Length == 0)) continue;
            if (l.Length > 0 && pendingVoice != null) { l = pendingVoice + ": " + l; pendingVoice = null; }
            rows.Add(l);
        }
        while (rows.Count > 0 && rows[^1].Length == 0) rows.RemoveAt(rows.Count - 1);
        if (rows.Count < 3) return new();

        var w = rows.Select(r => r.Length == 0 ? 2.5 : Syl(r) + 1.0).ToArray();
        double total = w.Sum();
        double length = dur > 20 ? dur : rows.Count * 3.4;
        double start = Math.Min(length * 0.07, 14);
        double end = length * 0.94;
        double span = Math.Max(10, end - start);
        var res = new List<(double, string)>();
        double acc = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            res.Add((start + acc / total * span, rows[i]));
            acc += w[i];
        }
        return res;
    }

    // ============================================================
    //  ВРЕМЯ СЛОВ: слоги вместо длины слов, паузы на запятых
    // ============================================================
    private static readonly Regex Vowels = new(
        @"[aeiouyаеёиоуыэюяáéíóúàèìòùâêîôûäöüæœ]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static int Syl(string s)
    {
        int n = Vowels.Matches(s).Count;
        if (n == 0)
        {
            int cjk = s.Count(c => c >= 0x3040);
            n = cjk > 0 ? cjk : (s.Any(char.IsLetterOrDigit) ? 1 : 0);
        }
        return Math.Max(n, s.Length == 0 ? 0 : 1);
    }

    public static bool IsInterlude(string s) => s.StartsWith("●");

    // Растянутые буквы: «бляяяядь», «ooooh», «нееет». Каждая лишняя буква в серии из трёх и больше даёт 1.
    // Слог из одинаковых гласных Syl считает за один, а поют его долго: этим и поправляем время слова.
    public static double Stretch(string word)
    {
        double extra = 0;
        int run = 1;
        for (int i = 1; i <= word.Length; i++)
        {
            if (i < word.Length && char.IsLetter(word[i]) && char.ToLowerInvariant(word[i]) == char.ToLowerInvariant(word[i - 1])) { run++; continue; }
            if (run >= 3) extra += run - 2;
            run = 1;
        }
        return extra;
    }

    private const double StretchWeight = 0.35;   // сколько «слога» весит одна лишняя растянутая буква

    public static double[] WordWeights(string[] parts)
    {
        var w = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i];
            double v = Math.Max(1, Syl(p)) + Math.Min(3, Stretch(p) * StretchWeight);
            if (p.Length > 0 && ",.!?;:—…".IndexOf(p[^1]) >= 0 && i < parts.Length - 1) v += 0.7;
            w[i] = v;
        }
        return w;
    }

    // сколько секунд строка реально поётся (остальное до следующей строки держим подсвеченной)
    public static double LineWindow(string text, double t0, double t1)
    {
        double gap = Math.Max(0.3, t1 - t0);
        if (IsInterlude(text)) return Math.Clamp(gap * 0.92, 0.8, 14);
        double stretch = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Sum(p => Math.Min(3, Stretch(p) * StretchWeight));
        double sing = 0.5 + (Syl(text) + stretch) * 0.42;
        return Math.Clamp(Math.Min(gap * 0.9, Math.Max(sing, 0.8)), 0.5, 9);
    }

    // точки ● ● ● в длинных проигрышах (как в Apple Music): заполняются, пока нет слов
    public static List<(double t, string s)> AddInterludes(List<(double t, string s)> lines, double dur) =>
        AddInterludes(lines, null, out _);

    // то же, но и список времени слов (words, той же длины) идёт параллельно и получает null на месте точек
    private static List<(double t, string s)> AddInterludes(List<(double t, string s)> lines, List<WordTimes?>? words, out List<WordTimes?> wordsOut)
    {
        var res = new List<(double, string)>();
        wordsOut = new List<WordTimes?>();
        if (lines.Count == 0) return res;
        if (lines[0].t > 7) { res.Add((1.0, "● ● ●")); wordsOut.Add(null); }
        for (int i = 0; i < lines.Count; i++)
        {
            res.Add(lines[i]);
            wordsOut.Add(words?[i]);
            if (string.IsNullOrWhiteSpace(lines[i].s) && i + 1 < lines.Count && lines[i + 1].t - lines[i].t > 6)
            {
                res[^1] = (lines[i].t + 0.5, "● ● ●");
                wordsOut[^1] = null;
                continue;
            }
            if (i + 1 < lines.Count)
            {
                // со временем слов конец строки известен точно, без оценки по слогам
                double end = words?[i] is { Count: > 0 } wt ? wt.End[^1] : LineWindow(lines[i].s, lines[i].t, lines[i + 1].t);
                double sungEnd = lines[i].t + end + 0.4;
                if (lines[i + 1].t - sungEnd > 6.5 && !string.IsNullOrWhiteSpace(lines[i].s))
                {
                    res.Add((sungEnd, "● ● ●"));
                    wordsOut.Add(null);
                }
            }
        }
        return res;
    }

    // ============================================================
    //  ДИСКОВЫЙ КЭШ
    // ============================================================
    // Версия кэша: старые записи (до проверки версии, исполнителя и длины) могли хранить чужой текст, их не читаем.
    // 4: исправлено разбиение исполнителей («Король и Шут»), старые тексты могли быть чужими
    // 5: тексты со временем слов (NetEase yrc, Kugou KRC); старые записи перекачиваются один раз, чтобы найти пословные
    private const int CacheVer = 5;

    internal sealed class CacheFile
    {
        public int V { get; set; }
        public string Src { get; set; } = "";
        public bool Synced { get; set; }
        public bool Miss { get; set; }
        public long At { get; set; }
        public string Raw { get; set; } = "";
        public double Scale { get; set; } = 1;
    }

    private static string CacheDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island", "lyrics");

    private static string CachePath(string title, string artist)
    {
        var h = System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes((artist + "|" + title).ToLowerInvariant()));
        return Path.Combine(CacheDir, Convert.ToHexString(h)[..20] + ".json");
    }

    private static LyricsResult? Build(string raw, bool synced, string src, double dur, double scale = 1)
    {
        List<(double t, string s)> lines;
        List<WordTimes?>? words = null;
        if (synced)
        {
            var full = ParseLrcFull(raw);
            bool sc = scale > 0 && Math.Abs(scale - 1) > 0.001;
            lines = full.Select(x => (sc ? x.t * scale : x.t, x.s)).ToList();
            if (full.Any(x => x.w != null))
                words = full.Select(x => x.w == null || !sc ? x.w
                    : new WordTimes { Start = x.w.Start.Select(v => v * scale).ToArray(), End = x.w.End.Select(v => v * scale).ToArray() }).ToList();
            if (!TimingOk(lines, dur)) return null;
        }
        else lines = Estimate(raw, dur);
        if (lines.Count < 3) return null;
        var withDots = AddInterludes(lines, words, out var wordsOut);
        return new LyricsResult { Lines = withDots, Words = words != null ? wordsOut : new List<WordTimes?>(), Synced = synced, Source = src };
    }

    private static (bool hit, LyricsResult? res) LoadCache(string title, string artist, double dur, bool allowPlain)
    {
        try
        {
            string p = CachePath(title, artist);
            if (!File.Exists(p)) return (false, null);
            var c = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(p));
            if (c == null || c.V < CacheVer) return (false, null);
            double ageDays = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - c.At) / 86400.0;
            if (c.Miss) return ageDays < 0.25 ? (true, null) : (false, null); // «нет текста» помним 6 часов
            if (!c.Synced && (ageDays > 7 || !allowPlain)) return (false, null); // вдруг уже появился текст со временем
            var r = Build(c.Raw, c.Synced, c.Src, dur, c.Scale <= 0 ? 1 : c.Scale);
            return r == null ? (false, null) : (true, r);
        }
        catch { return (false, null); }
    }

    // мгновенно из кэша, без ожидания: только текст со временем (он уже проверялся при сохранении)
    public static LyricsResult? PeekSynced(string title, string artist)
    {
        try
        {
            string p = CachePath(title, artist);
            if (!File.Exists(p)) return null;
            var c = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(p));
            if (c == null || c.V < CacheVer || c.Miss || !c.Synced) return null;
            return Build(c.Raw, true, c.Src, 0, c.Scale <= 0 ? 1 : c.Scale);
        }
        catch { return null; }
    }

    private static LyricsResult? Save(string title, string artist, string raw, bool synced, string src, double dur, double scale = 1)
    {
        var r = Build(raw, synced, src, dur, scale);
        if (r == null) return null;
        try
        {
            SafeFile.WriteAllText(CachePath(title, artist), JsonSerializer.Serialize(new CacheFile
            {
                V = CacheVer, Src = src, Synced = synced, Raw = raw, Scale = scale,
                At = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }));
        }
        catch (Exception ex) { App.Log("Lyrics.Cache", ex); }
        return r;
    }

    private static void SaveMiss(string title, string artist)
    {
        try
        {
            SafeFile.WriteAllText(CachePath(title, artist), JsonSerializer.Serialize(new CacheFile
            {
                V = CacheVer, Miss = true, At = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }));
        }
        catch (Exception ex) { App.Log("Lyrics.Cache", ex); }
    }
}
