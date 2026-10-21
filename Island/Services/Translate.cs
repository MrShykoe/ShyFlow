using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Island;

// Перевод строк песни (неофициальный бесплатный доступ Google Translate, ключ не нужен).
public static class TranslateEngine
{
    public static readonly string[] Codes = { "ru", "en", "de", "uk" };

    private static readonly HttpClient Web = Make();

    private static HttpClient Make()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        return c;
    }

    public static async Task<string[]?> TranslateAsync(IReadOnlyList<string> lines, string to)
    {
        var result = new string[lines.Count];
        int i = 0;
        while (i < lines.Count)
        {
            int start = i, len = 0;
            var batch = new List<string>();
            while (i < lines.Count && (batch.Count == 0 || len + lines[i].Length < 2500))
            {
                batch.Add(lines[i]);
                len += lines[i].Length + 1;
                i++;
            }
            var tr = await Batch(batch, to);
            if (tr == null) return null;
            for (int k = 0; k < batch.Count; k++) result[start + k] = tr[k];
        }
        return result;
    }

    // пачка строк одним запросом; если число строк на выходе не совпало, переводим по одной
    private static async Task<string[]?> Batch(List<string> batch, string to)
    {
        var joined = await Call(string.Join("\n", batch), to);
        if (joined == null) return null;
        var arr = joined.Split('\n');
        if (arr.Length == batch.Count) return arr.Select(x => x.Trim()).ToArray();

        var res = new string[batch.Count];
        bool failed = false;
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(batch.Select(async (s, k) =>
        {
            await gate.WaitAsync();
            try
            {
                var one = await Call(s, to);
                if (one == null) failed = true;
                res[k] = (one ?? "").Trim();
            }
            finally { gate.Release(); }
        }));
        return failed ? null : res;
    }

    // шаг 54, переводчик в острове: перевод одной фразы и язык, который определил Google
    public static async Task<(string text, string from)?> TranslateText(string text, string to)
    {
        try
        {
            string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&dt=t&tl=" + Uri.EscapeDataString(to);
            using var body = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("q", text) });
            using var resp = await Web.PostAsync(url, body);
            // часто отвечает 429 «слишком много запросов» (особенно через VPN): тогда запасной адрес
            if (!resp.IsSuccessStatusCode) return await Fallback(text, to);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var sb = new StringBuilder();
            foreach (var seg in doc.RootElement[0].EnumerateArray())
                if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() > 0 && seg[0].ValueKind == JsonValueKind.String)
                    sb.Append(seg[0].GetString());
            string from = doc.RootElement.GetArrayLength() > 2 && doc.RootElement[2].ValueKind == JsonValueKind.String ? doc.RootElement[2].GetString() ?? "" : "";
            return (sb.ToString(), from);
        }
        catch { return await Fallback(text, to); }
    }

    // запасной адрес Google (тот, что у расширения Chrome): ответ [["перевод","язык"]]
    private static async Task<(string text, string from)?> Fallback(string text, string to)
    {
        try
        {
            if (text.Length > 1500) text = text[..1500];   // всё уходит в адрес запроса
            string url = "https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl=auto&tl=" + Uri.EscapeDataString(to) + "&q=" + Uri.EscapeDataString(text);
            using var doc = JsonDocument.Parse(await Web.GetStringAsync(url));
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Array || r.GetArrayLength() == 0) return null;
            var first = r[0];
            if (first.ValueKind == JsonValueKind.String) return (first.GetString() ?? "", "");
            if (first.ValueKind == JsonValueKind.Array && first.GetArrayLength() > 0)
                return (first[0].GetString() ?? "", first.GetArrayLength() > 1 ? first[1].GetString() ?? "" : "");
            return null;
        }
        catch { return null; }
    }

    private static async Task<string?> Call(string text, string to)
    {
        try
        {
            string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&dt=t&tl=" + to;
            using var body = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("q", text) });
            using var resp = await Web.PostAsync(url, body);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var sb = new StringBuilder();
            foreach (var seg in doc.RootElement[0].EnumerateArray())
                if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() > 0 && seg[0].ValueKind == JsonValueKind.String)
                    sb.Append(seg[0].GetString());
            return sb.ToString();
        }
        catch { return null; }
    }
}

public partial class MainWindow
{
    private List<string>? _lyrTr;                          // перевод по индексам строк _lyr ("" = переводить нечего)
    private List<(double t, string s)>? _lyrTrFor;         // для какого списка строк он сделан
    private int _lyrTrLang = -1;
    private List<(double t, string s)>? _trBusy;
    private int _trBusyLang = -1;
    private readonly Dictionary<string, List<string>> _trCache = new();

    // плеер читает это; null, если перевод выключен или ещё не готов
    internal List<string>? TranslationNow =>
        _cfg.LyricsTranslate && _lyrTr != null && ReferenceEquals(_lyrTrFor, _lyr) && _lyrTrLang == _cfg.TranslateTo
            ? _lyrTr : null;

    private async void EnsureTranslation()
    {
        try
        {
            var lyr = _lyr;
            if (!_cfg.LyricsTranslate || lyr == null || lyr.Count == 0) return;
            int lang = Math.Clamp(_cfg.TranslateTo, 0, TranslateEngine.Codes.Length - 1);
            if (ReferenceEquals(_lyrTrFor, lyr) && _lyrTrLang == _cfg.TranslateTo) return;
            if (ReferenceEquals(_trBusy, lyr) && _trBusyLang == _cfg.TranslateTo) return;

            string key = _curArtist + "|" + _curTitle + "|" + lang + "|" + lyr.Count;   // число строк: текст могли подменить на пословный из другого источника
            if (_trCache.TryGetValue(key, out var cached))
            {
                _lyrTr = cached; _lyrTrFor = lyr; _lyrTrLang = _cfg.TranslateTo;
                return;
            }

            _trBusy = lyr; _trBusyLang = _cfg.TranslateTo;
            var idx = new List<int>();
            var src = new List<string>();
            for (int i = 0; i < lyr.Count; i++)
            {
                string s = Duet.Strip(lyr[i].s ?? "", _curArtist).Trim();   // шаг 76: метку голоса не переводим
                if (s.Length == 0 || LyricsEngine.IsInterlude(s)) continue;
                idx.Add(i); src.Add(s);
            }
            if (src.Count == 0) return;

            var res = await TranslateEngine.TranslateAsync(src, TranslateEngine.Codes[lang]);
            if (ReferenceEquals(_trBusy, lyr)) _trBusy = null;
            if (res == null) return;

            var full = Enumerable.Repeat("", lyr.Count).ToList();
            for (int k = 0; k < idx.Count; k++) full[idx[k]] = res[k];
            _trCache[key] = full;
            if (ReferenceEquals(_lyr, lyr) && _cfg.LyricsTranslate)
            {
                _lyrTr = full; _lyrTrFor = lyr; _lyrTrLang = _cfg.TranslateTo;
            }
        }
        catch (Exception ex) { App.LogOnce("Translate", ex); }
    }
}
