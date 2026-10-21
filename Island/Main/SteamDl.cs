using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Act = (string kind, string text, double frac, System.Windows.Media.Color col, string icon, bool paused);

namespace Island;

// Загрузка игры в Steam (шаг 68): активность с волной в стеке пузыря — «37% · BeamNG.drive · 12 МБ/с».
// Steam сам прогресс наружу не отдаёт и манифест (appmanifest_ID.acf) обновляет редко, а файлы в steamapps\downloading
// заранее занимают полный размер. Поэтому так:
//   сколько всего распаковать — BytesToStage из манифеста (или BytesToDownload, если без распаковки);
//   сколько уже — BytesStaged из манифеста плюс то, что steam.exe записал на диск с тех пор (счётчик записи процесса);
//   манифест обновился — считаем заново от него. Скорость сети — последняя строка «Current download rate» в content_log.
// Если ShyFlow запустили посреди загрузки, процент сначала чуть занижен, пока Steam не обновит манифест.
// Кончилась (папка в downloading пропала) — «Установлено» со всплеском. Нет записи 15 с — пауза (пузырь бледнее).
public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [DllImport("kernel32.dll")] private static extern bool GetProcessIoCounters(IntPtr h, out IO_COUNTERS c);   // OpenProcess и CloseHandle объявлены в Mixer.cs

    private sealed class SteamJob
    {
        public string Id = "", Name = "", Acf = "";
        public long Total, Base;          // всего записать; уже записано по манифесту
        public DateTime AcfTime;
        public ulong Writes0;             // счётчик записи steam.exe в момент Base
        public double Frac;
        public DateTime LastGrow = DateTime.UtcNow;
        public ulong LastWrites;
    }

    private static readonly Color SteamC = Color.FromRgb(0x66, 0xC0, 0xF4);
    private readonly DispatcherTimer _steamTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private SteamJob? _steam;
    private string _steamRate = "";
    private List<string> _steamLibs = new();
    private DateTime _steamLibsAt;
    private bool _steamBusy;

    private void InitSteam()
    {
        _steamTimer.Tick += async (_, _) => await PollSteam();
        _steamTimer.Start();
    }

    private Act? SteamState()
    {
        var j = _steam;
        if (j == null || !_cfg.SteamDownloads) return null;
        string name = j.Name.Length > 16 ? j.Name[..15] + "…" : j.Name;
        bool paused = (DateTime.UtcNow - j.LastGrow).TotalSeconds > 15;
        string t = (int)(j.Frac * 100) + "% · " + name + (!paused && _steamRate.Length > 0 ? " · " + _steamRate : paused ? " · " + Loc.T("пауза") : "");
        return ("steam", t, Math.Max(0.03, j.Frac), SteamC, "download", paused);
    }

    private async Task PollSteam()
    {
        if (_steamBusy || !_cfg.SteamDownloads) { if (!_cfg.SteamDownloads && _steam != null) { _steam = null; UpdateBubble(); } return; }
        _steamBusy = true;
        try
        {
            var prev = _steam;
            var res = await Task.Run(() => ReadSteam(prev));
            if (res.done != null)
            {
                _steam = null;
                TriggerDone(Loc.T("Установлено: ") + (res.done.Length > 18 ? res.done[..17] + "…" : res.done), SteamC);
                if (_ready && _cfg.Downloads) ShowToast("download", Loc.T("Steam: установлено") + ": " + res.done, null, "dl");
                UpdateBubble();
                return;
            }
            bool had = _steam != null;
            _steam = res.job;
            _steamRate = res.rate;
            if (_steam != null || had) UpdateBubble();
        }
        catch (Exception ex) { App.LogOnce("Steam", ex); }
        finally { _steamBusy = false; }
    }

    // в фоне: какая игра качается и сколько готово; done — имя, если загрузка только что закончилась
    private (SteamJob? job, string rate, string? done) ReadSteam(SteamJob? prev)
    {
        var steamProc = Process.GetProcessesByName("steam");
        if (steamProc.Length == 0) return (null, "", null);
        int pid = steamProc[0].Id;
        foreach (var p in steamProc) p.Dispose();
        string? root = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (root == null) return (null, "", null);
        root = root.Replace('/', '\\');
        if ((DateTime.UtcNow - _steamLibsAt).TotalSeconds > 60 || _steamLibs.Count == 0)
        {
            _steamLibsAt = DateTime.UtcNow;
            var libs = new List<string> { root };
            try
            {
                string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    {
                        string l = m.Groups[1].Value.Replace(@"\\", @"\");
                        if (!libs.Any(x => string.Equals(x.TrimEnd('\\'), l.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) libs.Add(l);
                    }
            }
            catch { }
            _steamLibs = libs;
        }

        // качается та, у которой есть папка в downloading и свежее всех изменён манифест (остальные в очереди)
        (string id, string acf, DateTime t)? best = null;
        foreach (var lib in _steamLibs)
        {
            string dl = Path.Combine(lib, "steamapps", "downloading");
            if (!Directory.Exists(dl)) continue;
            foreach (var d in Directory.EnumerateDirectories(dl))
            {
                string id = Path.GetFileName(d);
                if (!id.All(char.IsDigit)) continue;
                string acf = Path.Combine(lib, "steamapps", $"appmanifest_{id}.acf");
                if (!File.Exists(acf)) continue;
                var t = File.GetLastWriteTimeUtc(acf);
                if (best == null || t > best.Value.t) best = (id, acf, t);
            }
        }

        string rate = SteamRate(root);
        if (best == null)
        {
            // папки больше нет: если что-то качалось и Steam отметил игру установленной — готово
            if (prev != null && File.Exists(prev.Acf) && Field(File.ReadAllText(prev.Acf), "StateFlags") == 4) return (null, "", prev.Name);
            return (null, "", null);
        }
        ulong writes = Writes(pid);
        var (bid, bacf, btime) = best.Value;
        var job = prev != null && prev.Id == bid ? prev : null;
        if (job == null || btime != job.AcfTime)
        {
            // новая загрузка или Steam обновил манифест: начинаем счёт от него
            string txt = File.ReadAllText(bacf);
            long toStage = Field(txt, "BytesToStage"), staged = Field(txt, "BytesStaged");
            long toDl = Field(txt, "BytesToDownload"), dled = Field(txt, "BytesDownloaded");
            job ??= new SteamJob { Id = bid, Acf = bacf, Name = Regex.Match(txt, "\"name\"\\s+\"([^\"]*)\"").Groups[1].Value };
            bool fresh = job.Writes0 == 0;
            job.AcfTime = btime;
            job.Total = toStage > 0 ? toStage : toDl;
            job.Base = toStage > 0 ? staged : dled;
            // ShyFlow запустили посреди загрузки: что скачано после сохранения манифеста, берём из лога Steam
            // (скорость раз в минуту), и переводим в долю от распаковки
            if (fresh && toDl > 0)
            {
                long since = LoggedBytes(root, btime.ToLocalTime());
                if (since > 0)
                {
                    double f = Math.Clamp((double)(dled + since) / toDl, 0, 0.99);
                    job.Base = Math.Max(job.Base, (long)(f * job.Total));
                }
            }
            job.Writes0 = writes;
            job.LastWrites = writes;
        }
        if (job.Total <= 0) return (null, rate, null);
        if (writes > job.LastWrites + 1_000_000) { job.LastGrow = DateTime.UtcNow; job.LastWrites = writes; }
        long done = job.Base + (long)(writes - Math.Min(writes, job.Writes0));
        job.Frac = Math.Clamp((double)done / job.Total, 0, 0.99);
        return (job, rate, null);
    }

    private static long Field(string acf, string key)
    {
        var m = Regex.Match(acf, "\"" + key + "\"\\s+\"(\\d+)\"");
        return m.Success && long.TryParse(m.Groups[1].Value, out long v) ? v : 0;
    }

    // сколько steam.exe записал на диск с момента запуска
    private static ulong Writes(int pid)
    {
        IntPtr h = OpenProcess(0x1000 /*PROCESS_QUERY_LIMITED_INFORMATION*/, false, (uint)pid);
        if (h == IntPtr.Zero) return 0;
        try { return GetProcessIoCounters(h, out var c) ? c.WriteBytes : 0; }
        finally { CloseHandle(h); }
    }

    // сколько примерно скачано после момента from: сумма «Current download rate» (каждая строка — прошедшая минута)
    private static long LoggedBytes(string root, DateTime from)
    {
        try
        {
            string log = Path.Combine(root, "logs", "content_log.txt");
            if (!File.Exists(log)) return 0;
            using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long len = fs.Length;
            fs.Seek(Math.Max(0, len - 1024 * 1024), SeekOrigin.Begin);   // последний мегабайт лога — это много часов
            var buf = new byte[Math.Min(1024 * 1024, len)];
            int n = fs.Read(buf, 0, buf.Length);
            double bytes = 0;
            DateTime? prev = null;
            foreach (Match m in Regex.Matches(Encoding.UTF8.GetString(buf, 0, n), @"^\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)\] Current download rate: ([\d.]+) Mbps", RegexOptions.Multiline))
            {
                if (!DateTime.TryParse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t) || t <= from) continue;
                double secs = Math.Clamp((t - (prev ?? from)).TotalSeconds, 0, 90);
                bytes += double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) / 8 * 1e6 * secs;
                prev = t;
            }
            return (long)bytes;
        }
        catch { return 0; }
    }

    // «Current download rate: 106.280 Mbps» — последняя строка лога (Steam пишет её раз в минуту)
    private static string SteamRate(string root)
    {
        try
        {
            string log = Path.Combine(root, "logs", "content_log.txt");
            if (!File.Exists(log) || (DateTime.Now - File.GetLastWriteTime(log)).TotalMinutes > 3) return "";
            using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long len = fs.Length;
            fs.Seek(Math.Max(0, len - 8192), SeekOrigin.Begin);
            var buf = new byte[Math.Min(8192, len)];
            int n = fs.Read(buf, 0, buf.Length);
            var ms = Regex.Matches(Encoding.UTF8.GetString(buf, 0, n), @"Current download rate: ([\d.]+) Mbps");
            if (ms.Count == 0) return "";
            double mbps = double.Parse(ms[^1].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            return (mbps / 8).ToString(mbps >= 80 ? "0" : "0.0", Loc.Culture) + Loc.T(" МБ/с");
        }
        catch { return ""; }
    }
}
