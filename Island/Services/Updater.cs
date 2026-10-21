using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Island;

// Автообновление (шаг 55). Новые версии лежат в GitHub Releases репозитория MrShykoe/ShyFlow: установщик ShyFlow-Setup.exe.
//   Проверка: последний релиз через api.github.com, тег вида «3.1.0» (или «v3.1.0») сравнивается с версией сборки.
//   Скачивание: только с github.com этого же репозитория, в %TEMP%\ShyFlow-Update; если GitHub знает SHA-256 файла, он сверяется.
//   Установка: тихо (/VERYSILENT), установщик сам закрывает ShyFlow, ставит поверх (настройки в %AppData% не трогаются)
//   и запускает новую версию (/relaunch=1, см. ShyFlow.iss).
internal sealed record UpdateInfo(Version Ver, string Tag, string Name, string Notes, string Url, long Size, string? Sha256);

internal static class Updater
{
    public const string Repo = "MrShykoe/ShyFlow";
    private const string AssetName = "ShyFlow-Setup.exe";

    private static readonly HttpClient Web = Make();

    private static HttpClient Make()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("ShyFlow-Updater/" + AppInfo.CurrentVersion);   // без него api.github.com отвечает 403
        return c;
    }

    public static Version Current => Version.TryParse(AppInfo.CurrentVersion, out var v) ? v : new Version(0, 0, 0);

    private static Version? ParseTag(string tag)
    {
        var m = Regex.Match(tag, @"(\d+)\.(\d+)(?:\.(\d+))?");
        if (!m.Success) return null;
        return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
    }

    // null: обновлений нет или GitHub недоступен
    public static async Task<UpdateInfo?> Check()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var resp = await Web.SendAsync(req, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var r = doc.RootElement;
            if (r.TryGetProperty("draft", out var dr) && dr.GetBoolean()) return null;
            if (r.TryGetProperty("prerelease", out var pr) && pr.GetBoolean()) return null;
            string tag = r.GetProperty("tag_name").GetString() ?? "";
            var ver = ParseTag(tag);
            if (ver == null || ver <= Current) return null;
            foreach (var a in r.GetProperty("assets").EnumerateArray())
            {
                if (!string.Equals(a.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                string url = a.GetProperty("browser_download_url").GetString() ?? "";
                // качаем только из этого репозитория на github.com
                if (!url.StartsWith($"https://github.com/{Repo}/releases/download/", StringComparison.OrdinalIgnoreCase)) return null;
                string? sha = a.TryGetProperty("digest", out var dg) && dg.GetString() is string d && d.StartsWith("sha256:") ? d[7..] : null;
                return new UpdateInfo(ver, tag, r.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag,
                    r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "", url, a.GetProperty("size").GetInt64(), sha);
            }
            return null;
        }
        catch (Exception ex) { App.LogOnce("Update.Check", ex); return null; }
    }

    public static async Task<string?> Download(UpdateInfo u, IProgress<double> progress)
    {
        string dir = Path.Combine(Path.GetTempPath(), "ShyFlow-Update");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "ShyFlow-Setup-" + u.Ver + ".exe");
        try
        {
            using (var resp = await Web.GetAsync(u.Url, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? u.Size;
                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var dst = File.Create(path);
                var buf = new byte[81920];
                long done = 0;
                int n;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n));
                    done += n;
                    if (total > 0) progress.Report((double)done / total);
                }
            }
            if (u.Sha256 != null)
            {
                await using var fs = File.OpenRead(path);
                string got = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();
                if (got != u.Sha256.ToLowerInvariant()) { App.Log("Update", new Exception("SHA-256 не совпал: " + got)); File.Delete(path); return null; }
            }
            return path;
        }
        catch (Exception ex) { App.Log("Update.Download", ex); try { File.Delete(path); } catch { } return null; }
    }

    public static bool Install(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /relaunch=1") { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) { App.Log("Update.Install", ex); return false; }
    }

    // «Что нового» из описания релиза: без разметки Markdown и без английской части
    public static string PlainNotes(string body, int max = 700)
    {
        string s = body.Replace("\r", "");
        int en = s.IndexOf("**EN:**", StringComparison.Ordinal);
        if (en > 0) s = s[..en];
        s = Regex.Replace(s, @"^#+\s*", "", RegexOptions.Multiline);
        s = s.Replace("**", "").Replace("`", "").Replace("---", "").Trim();
        s = Regex.Replace(s, @"\n{3,}", "\n\n");
        return s.Length > max ? s[..max].TrimEnd() + "…" : s;
    }
}
