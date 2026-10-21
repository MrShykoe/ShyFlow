using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Island;

// Умный буфер обмена (шаг 64). Остров понимает, что скопировали, и показывает это сразу:
//   цвет (#FF375F, rgb(...), hsl(...)) — образец цвета в значке и HEX; в кармане карточка с большим образцом;
//   скриншот или картинка — миниатюра в значке плашки;
//   ссылка — сайт, а через секунду заголовок страницы и её значок (можно выключить: «Превью ссылок»);
//   код — язык и число строк; в кармане карточка с подсветкой синтаксиса.
// Клик по плашке «скопировано» закрепляет это в кармане (булавка), второй клик открепляет.
public partial class MainWindow
{
    // ---------- что скопировали ----------
    private static readonly Regex HexRx = new(@"^#?([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$");
    private static readonly Regex RgbRx = new(@"^rgba?\(\s*(\d{1,3})\s*[, ]\s*(\d{1,3})\s*[, ]\s*(\d{1,3})\s*(?:[,/]\s*[\d.]+%?\s*)?\)$", RegexOptions.IgnoreCase);
    private static readonly Regex HslRx = new(@"^hsla?\(\s*([\d.]+)(?:deg)?\s*[, ]\s*([\d.]+)%\s*[, ]\s*([\d.]+)%\s*(?:[,/]\s*[\d.]+%?\s*)?\)$", RegexOptions.IgnoreCase);

    internal static bool TryColor(string s, out Color c)
    {
        c = default;
        s = s.Trim().TrimEnd(';');
        if (s.Length > 40) return false;
        var m = HexRx.Match(s);
        // без решётки только шесть знаков с буквами и цифрами: «123456» — число, «decade» — слово, а не цвет
        if (m.Success && (s.StartsWith('#') || s.Length == 6 && s.Any(char.IsLetter) && s.Any(char.IsDigit)))
        {
            string h = m.Groups[1].Value;
            if (h.Length == 3) h = string.Concat(h.Select(ch => new string(ch, 2)));
            uint v = uint.Parse(h, NumberStyles.HexNumber);
            c = h.Length == 8 ? Color.FromArgb((byte)v, (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8))
                              : Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
            return true;
        }
        m = RgbRx.Match(s);
        if (m.Success)
        {
            int r = int.Parse(m.Groups[1].Value), g = int.Parse(m.Groups[2].Value), b = int.Parse(m.Groups[3].Value);
            if (r > 255 || g > 255 || b > 255) return false;
            c = Color.FromRgb((byte)r, (byte)g, (byte)b);
            return true;
        }
        m = HslRx.Match(s);
        if (m.Success)
        {
            double hh = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) % 360 / 360,
                   ss = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 100,
                   ll = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) / 100;
            double q = ll < 0.5 ? ll * (1 + ss) : ll + ss - ll * ss, p = 2 * ll - q;
            static double Hue(double p, double q, double t)
            {
                t = (t + 1) % 1;
                return t < 1 / 6.0 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2 / 3.0 ? p + (q - p) * (2 / 3.0 - t) * 6 : p;
            }
            byte B(double x) => (byte)Math.Round(Math.Clamp(x, 0, 1) * 255);
            c = Color.FromRgb(B(Hue(p, q, hh + 1 / 3.0)), B(Hue(p, q, hh)), B(Hue(p, q, hh - 1 / 3.0)));
            return true;
        }
        return false;
    }

    internal static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract","async","await","bool","break","case","catch","class","const","continue","def","default","do","double","elif","else",
        "enum","export","extends","false","final","finally","fn","for","foreach","from","func","function","if","impl","import","in","int",
        "interface","is","let","match","mut","namespace","new","None","null","override","package","private","protected","pub","public",
        "readonly","return","self","static","string","struct","super","switch","this","throw","true","True","False","try","type","typeof",
        "using","var","void","while","with","yield","lambda","pass","raise","nil","end","select","from","where","SELECT","FROM","WHERE",
        "INSERT","UPDATE","DELETE","JOIN","AND","OR","NOT","ORDER","BY","GROUP","include","define","template","typename","auto","char",
    };

    // похоже на код: несколько строк со скобками и точками с запятой или ключевые слова
    internal static bool LooksLikeCode(string s, out string lang)
    {
        lang = "";
        if (s.Length < 12 || s.Length > 20000 || UrlOf(s) != null) return false;
        var lines = s.Replace("\r", "").Split('\n');
        int sym = s.Count(ch => ";{}()[]=<>".Contains(ch));
        int kw = Regex.Matches(s, @"\b[A-Za-z_]+\b").Count(m => Keywords.Contains(m.Value));
        int indented = lines.Count(l => l.StartsWith("    ") || l.StartsWith('\t'));
        double density = (double)sym / s.Length;
        bool code = lines.Length >= 2
            ? density > 0.035 && (kw >= 1 || indented >= 1) || kw >= 3 && indented >= 1
            : density > 0.06 && kw >= 1 && (s.Contains('(') || s.Contains('='))
              || Regex.IsMatch(s, @"^\s*(SELECT|INSERT|UPDATE|DELETE)\b.+\b(FROM|INTO|SET|WHERE)\b");   // SQL в одну строку, только заглавными
        if (!code) return false;
        lang = s.Contains("namespace ") || s.Contains("using System") || Regex.IsMatch(s, @"\bpublic (sealed |static )?(class|void|string|int)\b") ? "C#"
             : Regex.IsMatch(s, @"^\s*(def |import |from \S+ import )", RegexOptions.Multiline) && !s.Contains(';') ? "Python"
             : s.Contains("#include") || s.Contains("std::") ? "C++"
             : Regex.IsMatch(s, @"\b(fn|let mut|impl)\b") ? "Rust"
             : Regex.IsMatch(s, @"\b(SELECT|INSERT|UPDATE)\b.*\b(FROM|INTO|SET)\b", RegexOptions.IgnoreCase | RegexOptions.Singleline) ? "SQL"
             : Regex.IsMatch(s, @"^\s*<[a-zA-Z!]", RegexOptions.Multiline) ? "HTML"
             : Regex.IsMatch(s, @"\b(function|const|let|=>|console\.)") ? "JavaScript"
             : s.TrimStart().StartsWith('{') && s.Contains("\":") ? "JSON"
             : "";
        return true;
    }

    // ---------- подсветка синтаксиса (тёмная тема как в Xcode) ----------
    private static readonly Regex TokRx = new(
        @"(?<c>//[^\n]*|/\*[\s\S]*?\*/|(?<=^|\s)#(?!include)[^\n]*|--[^\n]*)|(?<s>""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\\n])*'|`[^`]*`)|(?<n>\b\d+(?:\.\d+)?[fFdDmMlL]?\b|\b0x[0-9a-fA-F]+\b)|(?<w>\b[A-Za-z_][A-Za-z0-9_]*\b)",
        RegexOptions.Multiline);
    private static readonly SolidColorBrush CodeKw = Frozen(0xFC, 0x5F, 0xA3), CodeStr = Frozen(0xFC, 0x6A, 0x5D), CodeCom = Frozen(0x7F, 0x8C, 0x98),
        CodeNum = Frozen(0xD0, 0xBF, 0x69), CodeFn = Frozen(0x67, 0xB7, 0xA4), CodeType = Frozen(0x9E, 0xF1, 0xDD), CodeTxt = Frozen(0xE6, 0xE6, 0xE6);

    private static SolidColorBrush Frozen(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

    internal static TextBlock CodeBlock(string code, int maxLines, double size = 10.5)
    {
        var lines = code.Replace("\r", "").Split('\n');
        // общий отступ убираем, табы в 4 пробела, только первые строки
        int indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        string text = string.Join("\n", lines.Take(maxLines + 1).Select(l => (l.Length >= indent ? l[indent..] : l.TrimStart()).Replace("\t", "    ")));
        var tb = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Code, Consolas"), FontSize = size, LineHeight = size + 4,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = maxLines * (size + 4), Foreground = CodeTxt
        };
        int pos = 0;
        foreach (Match m in TokRx.Matches(text))
        {
            if (m.Index > pos) tb.Inlines.Add(new Run(text[pos..m.Index]));
            Brush br = m.Groups["c"].Success ? CodeCom : m.Groups["s"].Success ? CodeStr : m.Groups["n"].Success ? CodeNum : CodeTxt;
            if (m.Groups["w"].Success)
            {
                string w = m.Value;
                int after = m.Index + m.Length;
                br = Keywords.Contains(w) ? CodeKw
                   : after < text.Length && text[after] == '(' ? CodeFn
                   : char.IsUpper(w[0]) ? CodeType : CodeTxt;
            }
            tb.Inlines.Add(new Run(m.Value) { Foreground = br });
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) tb.Inlines.Add(new Run(text[pos..]));
        return tb;
    }

    // ---------- превью ссылок: заголовок страницы и значок сайта ----------
    internal sealed record LinkMeta(string Title, string? Icon);
    private static readonly Dictionary<string, LinkMeta> _linkMeta = new();
    private static readonly HttpClient Http = MakeHttp();
    private static string LinkFile => Path.Combine(DataDir, "links.json");
    private static string LinkIconDir => Path.Combine(DataDir, "linkicons");
    private static bool _linksLoaded;

    private static HttpClient MakeHttp()
    {
        var h = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 4 }) { Timeout = TimeSpan.FromSeconds(5) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) ShyFlow");
        h.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru,en;q=0.8");
        return h;
    }

    private static void LoadLinks()
    {
        if (_linksLoaded) return;
        _linksLoaded = true;
        try
        {
            if (!File.Exists(LinkFile)) return;
            var d = JsonSerializer.Deserialize<Dictionary<string, LinkMeta>>(File.ReadAllText(LinkFile));
            if (d != null) foreach (var (k, v) in d) _linkMeta[k] = v;
        }
        catch (Exception ex) { App.LogOnce("Links.Load", ex); }
    }

    private static void SaveLinks()
    {
        try
        {
            // хранится 60 последних
            var keep = _linkMeta.TakeLast(60).ToDictionary(kv => kv.Key, kv => kv.Value);
            SafeFile.WriteAllText(LinkFile, JsonSerializer.Serialize(keep));
        }
        catch (Exception ex) { App.LogOnce("Links.Save", ex); }
    }

    internal static LinkMeta? CachedLink(string url) { LoadLinks(); return _linkMeta.TryGetValue(url, out var m) ? m : null; }

    // в локальную сеть и на сам компьютер не ходим: там бывают роутеры, NAS и внутренние сервисы
    private static bool PublicHost(Uri u)
    {
        if (u.IsLoopback || u.HostNameType == UriHostNameType.Basic) return false;
        string h = u.Host.ToLowerInvariant();
        if (h.EndsWith(".local") || h.EndsWith(".lan") || h.EndsWith(".internal") || !h.Contains('.')) return false;
        if (IPAddress.TryParse(h.Trim('[', ']'), out var ip))
        {
            var b = ip.GetAddressBytes();
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6) return false;
            if (b[0] == 10 || b[0] == 127 || b[0] == 169 && b[1] == 254 || b[0] == 172 && b[1] >= 16 && b[1] <= 31 || b[0] == 192 && b[1] == 168) return false;
        }
        return true;
    }

    private static readonly Regex TitleRx = new(@"<meta[^>]+property=[""']og:title[""'][^>]+content=[""']([^""']+)|<meta[^>]+content=[""']([^""']+)[""'][^>]+property=[""']og:title|<title[^>]*>([^<]+)</title>", RegexOptions.IgnoreCase);
    private static readonly Regex IconRx = new(@"<link[^>]+rel=[""'](?:shortcut )?icon[""'][^>]+href=[""']([^""']+)|<link[^>]+href=[""']([^""']+)[""'][^>]+rel=[""'](?:shortcut )?icon", RegexOptions.IgnoreCase);

    internal static async Task<LinkMeta?> FetchLink(string url)
    {
        LoadLinks();
        if (_linkMeta.TryGetValue(url, out var cached)) return cached;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || !PublicHost(u)) return null;
        try
        {
            string html;
            using (var resp = await Http.GetAsync(u, HttpCompletionOption.ResponseHeadersRead))
            {
                if (!resp.IsSuccessStatusCode || resp.Content.Headers.ContentType?.MediaType?.Contains("html") != true) return Miss(url);
                await using var st = await resp.Content.ReadAsStreamAsync();
                var buf = new byte[256 * 1024];   // заголовок всегда в начале страницы
                int n = 0, r;
                while (n < buf.Length && (r = await st.ReadAsync(buf.AsMemory(n))) > 0) n += r;
                html = System.Text.Encoding.UTF8.GetString(buf, 0, n);
            }
            var tm = TitleRx.Match(html);
            string title = tm.Success ? WebUtility.HtmlDecode((tm.Groups[1].Success ? tm.Groups[1] : tm.Groups[2].Success ? tm.Groups[2] : tm.Groups[3]).Value).Trim() : "";
            title = Regex.Replace(title, @"\s+", " ");
            if (title.Length > 120) title = title[..119] + "…";
            var im = IconRx.Match(html);
            string iconHref = im.Success ? WebUtility.HtmlDecode((im.Groups[1].Success ? im.Groups[1] : im.Groups[2]).Value) : "/favicon.ico";
            string? icon = await FetchIcon(new Uri(u, iconHref));
            var meta = new LinkMeta(title, icon);
            _linkMeta[url] = meta;
            SaveLinks();
            return meta;
        }
        catch (Exception ex) { App.LogOnce("Links.Fetch", ex); return Miss(url); }
    }

    // не получилось (не страница, нет сети, ошибка сайта): запоминаем пустое превью, чтобы не ходить туда на каждую перерисовку кармана
    private static LinkMeta? Miss(string url) { _linkMeta[url] = new LinkMeta("", null); return null; }

    private static async Task<string?> FetchIcon(Uri iu)
    {
        try
        {
            if (iu.Scheme != Uri.UriSchemeHttps && iu.Scheme != Uri.UriSchemeHttp || !PublicHost(iu)) return null;
            var bytes = await Http.GetByteArrayAsync(iu);
            if (bytes.Length == 0 || bytes.Length > 512 * 1024) return null;
            BitmapSource? bmp = null;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var dec = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    bmp = dec.Frames.OrderByDescending(f => f.PixelWidth).First();   // у .ico берём самый крупный размер
                }
                catch { /* svg и прочее, что WPF не читает: без значка */ }
            });
            if (bmp == null) return null;
            Directory.CreateDirectory(LinkIconDir);
            string path = Path.Combine(LinkIconDir, iu.Host.Replace(':', '_') + ".png");
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var fs = File.Create(path);
                enc.Save(fs);
            });
            return path;
        }
        catch { return null; }
    }

    internal static BitmapSource? LoadIcon(string? path) => path != null && File.Exists(path) ? LoadFull(path) : null;

    // ---------- плашка «скопировано» ----------
    private Action? _osdClick;   // клик по плашке (Pill_Click): здесь закрепить в кармане
    private int _clipToastId;

    private void ShowClipToast(string t)
    {
        int id = ++_clipToastId;
        string pinHint = Loc.T("клик — закрепить");
        if (TryColor(t, out var col))
        {
            ShowToast("droplet",Loc.T("Цвет") + ": " + Hex(col) + " · " + pinHint, null, "copy");
            Swatch(col);
        }
        else if (UrlOf(t) is { } url)
        {
            var u = new Uri(url);
            string host = u.Host.StartsWith("www.") ? u.Host[4..] : u.Host;
            var known = CachedLink(url);
            ShowToast("link", (known?.Title.Length > 0 ? Clean(known.Title) : Loc.T("Ссылка")) + ": " + host + " · " + pinHint, null, "copy", LoadIcon(known?.Icon));
            if (known == null && _cfg.LinkPreviews)
                _ = FetchLink(url).ContinueWith(task => Dispatcher.BeginInvoke(() =>
                {
                    // плашка всё ещё про эту ссылку: дописываем заголовок и значок сайта
                    if (task.Result is not { } meta || id != _clipToastId || _mode != Mode.Osd) return;
                    ShowToast("link", (meta.Title.Length > 0 ? Clean(meta.Title) : Loc.T("Ссылка")) + ": " + host + " · " + pinHint, null, "copy", LoadIcon(meta.Icon));
                    SetClipPin(t);
                }));
        }
        else if (LooksLikeCode(t, out var lang))
        {
            int lines = t.Replace("\r", "").Split('\n').Length;
            ShowToast("code", Loc.T("Код") + (lang.Length > 0 ? " · " + lang : "") + ": " + lines + " " + Loc.T(lines == 1 ? "строка" : lines < 5 ? "строки" : "строк") + " · " + pinHint, null, "copy");
        }
        else ShowToast("", Loc.T("Скопировано в буфер") + ": " + pinHint, null, "copy");
        SetClipPin(t);
    }

    // заголовок страницы без «: », иначе плашка разрежет его на заголовок и подпись
    private static string Clean(string title) => title.Replace(": ", " — ");

    private void SetClipPin(string item)
    {
        _osdClick = () =>
        {
            TogglePin(1, item);
            bool on = IsPinned(1, item);
            RenderPocket();
            ShowToast("pin", on ? "Закреплено в кармане" : "Откреплено", null, "copy");
            _osdClick = null;
        };
    }

    // образец цвета вместо значка: квадратик плашки (или пузыря событий) залит скопированным цветом
    private void Swatch(Color c)
    {
        var br = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        if (EventsToBubble) { EvIcon.Visibility = Visibility.Collapsed; EvArt.Visibility = Visibility.Collapsed; EvBadge.Background = br; return; }
        OsdIcon.Visibility = Visibility.Collapsed;
        OsdArt.Visibility = Visibility.Collapsed;
        OsdBadge.Background = br;
    }

    // миниатюра скриншота для значка плашки
    internal static BitmapSource? Thumb(BitmapSource? img, int size = 64)
    {
        if (img == null || img.PixelWidth < 2) return null;
        double s = (double)size / Math.Max(img.PixelWidth, img.PixelHeight);
        var t = new TransformedBitmap(img, new ScaleTransform(Math.Min(1, s), Math.Min(1, s)));
        t.Freeze();
        return t;
    }
}
