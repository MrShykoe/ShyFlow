using System.Text.RegularExpressions;

namespace Island;

// Шаг 76: дуэты. Когда в песне поют двое (или больше), в плеере на весь экран строки второго голоса стоят справа,
// как в Apple Music.
// Кто поёт, берётся из самого текста:
//   «v1: …», «v2: …» — так помечают голоса в расширенных LRC;
//   «Имя: …» — если имя есть среди исполнителей трека (поле «исполнитель»: «Drake, Rihanna», «A feat. B», «A & B», «A x B»);
//   в текстах без времени (Genius) заголовки «[Verse 2: Rihanna]» — LyricsEngine.Estimate превращает их в «Rihanna: …».
// Метка действует до следующей. Если за песню встретился только один голос, всё остаётся слева, как раньше.
// Сама метка на экране не показывается ни в плеере, ни в острове (Strip).
internal static class Duet
{
    private static readonly Regex Tag = new(@"^\s*(v\d{1,2}|[^:：\(\)\[\]]{1,40}?)\s*[:：]\s+(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Voice = new(@"^v\d{1,2}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Splitter = new(@"\s*(?:,|&|\+|/|;|\bfeat\.?|\bft\.?|\bfeaturing\b|\bx\b|\bи\b|\band\b|\bwith\b|\bvs\.?)\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string[] Names(string? artist) =>
        Splitter.Split(artist ?? "").Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

    // «кто» — это голос (v1, v2) или исполнители трека (все имена, если их несколько: «Drake & Rihanna»)
    private static bool IsSpeaker(string who, string[] names)
    {
        if (Voice.IsMatch(who)) return true;
        var parts = Names(who);
        if (parts.Length == 0 || names.Length == 0) return false;
        return parts.All(p => names.Any(n =>
            string.Equals(n, p, StringComparison.OrdinalIgnoreCase) ||
            (p.Length >= 3 && n.Contains(p, StringComparison.OrdinalIgnoreCase)) ||
            (n.Length >= 3 && p.Contains(n, StringComparison.OrdinalIgnoreCase))));
    }

    // метка голоса в начале строки, если она есть и это действительно голос
    public static bool TrySplit(string line, string[] names, out string who, out string text)
    {
        who = ""; text = line;
        var m = Tag.Match(line);
        if (!m.Success) return false;
        string w = m.Groups[1].Value.Trim();
        if (!IsSpeaker(w, names)) return false;
        who = w.ToLowerInvariant();
        text = m.Groups[2].Value.Trim();
        return true;
    }

    // строка без метки голоса (для острова и всего, что показывает текст)
    public static string Strip(string line, string? artist) =>
        TrySplit(line, Names(artist), out _, out var text) ? text : line;

    // сторона каждой строки: 0 слева, 1 справа; side = null, если голос один. text — строки без меток
    public static (int[]? side, string[] text) Analyze(IReadOnlyList<string> lines, string? artist)
    {
        var names = Names(artist);
        int n = lines.Count;
        var text = new string[n];
        var key = new string?[n];
        string? cur = null;
        for (int i = 0; i < n; i++)
        {
            if (TrySplit(lines[i], names, out var who, out var t)) { cur = who; text[i] = t; }
            else text[i] = lines[i];
            key[i] = cur;
        }
        var order = new List<string>();
        foreach (var k in key)
            if (k != null && !order.Contains(k)) order.Add(k);
        if (order.Count < 2) return (null, text);
        var side = new int[n];
        for (int i = 0; i < n; i++)
            side[i] = key[i] == null ? 0 : order.IndexOf(key[i]!) % 2;
        return (side, text);
    }
}
