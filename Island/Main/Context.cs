using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Act = (string kind, string text, double frac, System.Windows.Media.Color col, string icon, bool paused);

namespace Island;

// Остров понимает, что ты делаешь (шаг 65). Активность «контекст» в стеке пузыря (Activities.cs) по активной программе:
//   VS Code, Rider и другие IDE JetBrains, Visual Studio — проект, ветка git и сколько файлов изменено; клик: меню проекта.
//   Идёт сборка (dotnet build/run/test, MSBuild, cargo, npm/vite/next build, tsc...) — отдельная активность с волной и временем;
//   закончилась — «Сборка прошла» зелёным или «Сборка с ошибками» красным (по коду выхода, если его удалось узнать).
//   Discord — быстрые действия по клику: mute и deafen в самом Discord (его горячие клавиши Ctrl+Shift+M и Ctrl+Shift+D)
//   и микрофон Windows.
//   Браузер — название текущей вкладки; клик: скопировать его или открыть «Загрузки». Загрузки браузера идут живыми
//   активностями с волной и размером (SystemToasts.cs), конец — «Готово» со всплеском.
// Настройки, «Плашки», «Остров понимает, что ты делаешь».
public partial class MainWindow
{
    private enum CtxApp { None, Ide, Discord, Browser }
    private CtxApp _ctxApp;
    private IntPtr _ctxHwnd;
    private string _ctxProc = "", _ctxTitle = "";
    private string? _ctxRepo;                 // папка проекта (для git)
    private string _ctxProject = "", _ctxBranch = "";
    private int _ctxChanges = -1;
    private DateTime _gitAt;
    private static readonly Color CtxC = Color.FromRgb(0x64, 0xD2, 0xFF), BuildC = Color.FromRgb(0xFF, 0x9F, 0x0A),
        DiscordC = Color.FromRgb(0x58, 0x65, 0xF2), WebC = Color.FromRgb(0x7F, 0xBC, 0xFF), FailC = Color.FromRgb(0xFF, 0x45, 0x3A);

    private static readonly string[] IdeProcs = { "code", "code - insiders", "cursor", "windsurf", "devenv", "rider64", "idea64", "pycharm64", "webstorm64", "clion64", "goland64", "phpstorm64", "rustrover64" };
    private static readonly string[] BrowserProcs = { "chrome", "msedge", "firefox", "opera", "brave", "browser", "vivaldi", "arc", "zen" };
    private static readonly string[] TerminalProcs = { "windowsterminal", "cmd", "powershell", "pwsh", "conhost", "wezterm-gui", "alacritty" };

    private readonly DispatcherTimer _ctxTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _ctxInit;

    private void InitContext()
    {
        if (_ctxInit) return;
        _ctxInit = true;
        _ctxTimer.Tick += (_, _) => { _ = PollBuilds(); RefreshGit(false); };
    }

    // из OnForeground (AppsBar.cs): сменилась активная программа
    private void ContextForeground(IntPtr h, uint pid)
    {
        if (!_cfg.ContextAware) { if (_ctxApp != CtxApp.None) { _ctxApp = CtxApp.None; UpdateBubble(); } return; }
        InitContext();
        string proc;
        try { proc = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant(); } catch { return; }
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, 512);
        string title = sb.ToString();
        var app = IdeProcs.Contains(proc) ? CtxApp.Ide : proc == "discord" ? CtxApp.Discord : BrowserProcs.Contains(proc) ? CtxApp.Browser : CtxApp.None;
        bool changed = app != _ctxApp || title != _ctxTitle;
        _ctxApp = app; _ctxHwnd = h; _ctxProc = proc; _ctxTitle = title;
        if (app == CtxApp.Ide) FindProject();
        // сборки смотрим, пока на виду IDE или терминал, или пока сборка ещё идёт
        bool watch = app == CtxApp.Ide || TerminalProcs.Contains(proc) || _builds.Count > 0;
        if (watch && !_ctxTimer.IsEnabled) { _ctxTimer.Start(); _ = PollBuilds(); }
        else if (!watch) _ctxTimer.Stop();
        if (changed) UpdateBubble();
    }

    // ---------- активности для стека ----------
    private IEnumerable<Act> ContextStates()
    {
        if (!_cfg.ContextAware) yield break;
        foreach (var b in _builds.Values)
        {
            var el = DateTime.UtcNow - b.Start;
            // сколько длится сборка, не знаем: волна медленно поднимается и не доходит до верха
            double frac = 1 - Math.Exp(-el.TotalSeconds / 40);
            yield return ("build", Loc.T(b.What.Contains("test", StringComparison.OrdinalIgnoreCase) ? "Тесты" : "Сборка") + " " +(int)el.TotalMinutes + ":" + el.Seconds.ToString("00"), Math.Min(0.92, frac), BuildC, "code", false);
            break;   // одна строка на все сборки сразу
        }
        foreach (var d in _webDls.Values.Take(3))
            yield return ("web:" + d.Name, FmtSize(d.Size), d.Frac, WebC, "download", false);
        switch (_ctxApp)
        {
            case CtxApp.Ide when _ctxProject.Length > 0:
                string t = _ctxBranch.Length > 0 ? _ctxBranch : _ctxProject;
                if (t.Length > 18) t = t[..17] + "…";
                if (_ctxChanges > 0) t += " · " + _ctxChanges;
                yield return ("ctx", t, 0, CtxC, _ctxBranch.Length > 0 ? "git-branch" : "code", false);
                break;
            case CtxApp.Discord:
                yield return ("ctx", "Discord", 0, DiscordC, "headphones", false);
                break;
            case CtxApp.Browser:
                string tab = TabTitle();
                if (tab.Length > 0) yield return ("ctx", tab.Length > 22 ? tab[..21] + "…" : tab, 0, WebC, "globe", false);
                break;
        }
    }

    // «Название — Google Chrome» → «Название»
    private string TabTitle()
    {
        string t = _ctxTitle;
        int i = Math.Max(t.LastIndexOf(" - ", StringComparison.Ordinal), t.LastIndexOf(" — ", StringComparison.Ordinal));
        if (i > 0) t = t[..i];
        return t.Trim();
    }

    private void ContextClick()
    {
        var m = new ContextMenu();
        MenuItem It(string t, Action a) { var i = new MenuItem { Header = Loc.T(t) }; i.Click += (_, _) => a(); return i; }
        switch (_ctxApp)
        {
            case CtxApp.Ide:
                m.Items.Add(new MenuItem { Header = _ctxProject + (_ctxBranch.Length > 0 ? " · " + _ctxBranch : "") + (_ctxChanges >= 0 ? " · " + _ctxChanges + " " + Loc.T("изм.") : ""), IsEnabled = false });
                if (_ctxRepo != null)
                {
                    m.Items.Add(It("Открыть папку проекта", () => Launch(_ctxRepo!)));
                    if (_ctxBranch.Length > 0) m.Items.Add(It("Скопировать ветку", () => CopyBack(_ctxBranch)));
                    m.Items.Add(It("Обновить git", () => RefreshGit(true)));
                }
                break;
            case CtxApp.Discord:
                m.Items.Add(It("Mute в Discord (Ctrl+Shift+M)", () => DiscordKey(0x4D)));
                m.Items.Add(It("Deafen в Discord (Ctrl+Shift+D)", () => DiscordKey(0x44)));
                m.Items.Add(It("Микрофон Windows (Ctrl+Alt+V)", ToggleMic));
                break;
            case CtxApp.Browser:
                m.Items.Add(It("Скопировать название вкладки", () => CopyBack(TabTitle())));
                m.Items.Add(It("Открыть «Загрузки»", () => Launch(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"))));
                break;
            default: return;
        }
        m.PlacementTarget = Bubble;
        m.IsOpen = true;
    }

    // горячая клавиша Discord: окно Discord на передний план, затем Ctrl+Shift+клавиша
    private async void DiscordKey(byte vk)
    {
        if (_ctxHwnd == IntPtr.Zero) return;
        SetForegroundWindow(_ctxHwnd);
        await Task.Delay(120);
        const uint up = 0x2;
        keybd_event(0x11, 0, 0, UIntPtr.Zero); keybd_event(0x10, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, up, UIntPtr.Zero); keybd_event(0x10, 0, up, UIntPtr.Zero); keybd_event(0x11, 0, up, UIntPtr.Zero);
        Sfx.Ui("tap");
    }

    // ---------- проект и git ----------
    private void FindProject()
    {
        // VS Code: «● файл - Папка - Visual Studio Code»; JetBrains: «Проект – файл»; Visual Studio: «Решение - Microsoft Visual Studio»
        string t = _ctxTitle.TrimStart('●', ' ');
        var parts = t.Split(new[] { " - ", " – ", " — " }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string name = _ctxProc.StartsWith("code") || _ctxProc is "cursor" or "windsurf" ? (parts.Length >= 3 ? parts[^2] : parts.Length == 2 ? parts[0] : "")
                    : _ctxProc == "devenv" ? (parts.Length >= 2 ? parts[0] : "")
                    : parts.Length >= 1 ? parts[0] : "";
        name = Regex.Replace(name, @"\s*\[.*\]$", "");   // «Проект [путь]» у JetBrains
        if (name.Length == 0 || name == _ctxProject && _ctxRepo != null) { _ctxProject = name.Length > 0 ? name : _ctxProject; return; }
        _ctxProject = name;
        _ctxRepo = RepoFor(name);
        _ctxBranch = ""; _ctxChanges = -1;
        RefreshGit(true);
    }

    // где лежит проект с таким именем: последние папки VS Code и проекты JetBrains
    private static string? RepoFor(string name)
    {
        try
        {
            string app = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            foreach (var ed in new[] { "Code", "Code - Insiders", "Cursor", "Windsurf" })
            {
                string f = Path.Combine(app, ed, "User", "globalStorage", "storage.json");
                if (!File.Exists(f)) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(f), @"""(?:folder|folderUri)""\s*:\s*""file:///([^""]+)"""))
                {
                    string p = Uri.UnescapeDataString(m.Groups[1].Value).Replace('/', '\\');
                    if (string.Equals(Path.GetFileName(p.TrimEnd('\\')), name, StringComparison.OrdinalIgnoreCase) && Directory.Exists(p)) return p;
                }
            }
            string jb = Path.Combine(app, "JetBrains");
            if (Directory.Exists(jb))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                foreach (var f in Directory.GetDirectories(jb).Select(d => Path.Combine(d, "options", "recentProjects.xml")).Where(File.Exists))
                    foreach (Match m in Regex.Matches(File.ReadAllText(f), @"key=""([^""]+)"""))
                    {
                        string p = m.Groups[1].Value.Replace("$USER_HOME$", home).Replace('/', '\\');
                        if (File.Exists(p)) p = Path.GetDirectoryName(p)!;   // путь к .sln
                        if (string.Equals(Path.GetFileName(p.TrimEnd('\\')), name, StringComparison.OrdinalIgnoreCase) && Directory.Exists(p)) return p;
                    }
            }
        }
        catch (Exception ex) { App.LogOnce("Context.Repo", ex); }
        return null;
    }

    private bool _gitBusy;

    private async void RefreshGit(bool force)
    {
        if (_ctxApp != CtxApp.Ide || _ctxRepo == null || _gitBusy) return;
        if (!force && (DateTime.UtcNow - _gitAt).TotalSeconds < 6) return;
        _gitBusy = true;
        _gitAt = DateTime.UtcNow;
        string repo = _ctxRepo;
        var (branch, changes) = await Task.Run(() => GitInfo(repo));
        _gitBusy = false;
        if (repo != _ctxRepo) return;
        if (branch != _ctxBranch || changes != _ctxChanges) { _ctxBranch = branch; _ctxChanges = changes; UpdateBubble(); }
    }

    // ветка из .git/HEAD (git не нужен); число изменённых файлов — через git status, если git установлен
    private static (string branch, int changes) GitInfo(string dir)
    {
        string branch = "";
        try
        {
            string? d = dir, gitDir = null;
            while (d != null)
            {
                string g = Path.Combine(d, ".git");
                if (Directory.Exists(g)) { gitDir = g; break; }
                if (File.Exists(g))   // worktree или submodule: «gitdir: путь»
                {
                    string line = File.ReadAllText(g).Trim();
                    if (line.StartsWith("gitdir:")) { gitDir = Path.GetFullPath(Path.Combine(d, line[7..].Trim())); break; }
                }
                d = Path.GetDirectoryName(d);
            }
            if (gitDir == null) return ("", -1);
            string head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
            branch = head.StartsWith("ref: refs/heads/") ? head[16..] : head.Length >= 7 ? head[..7] : head;
        }
        catch { return ("", -1); }
        int changes = -1;
        try
        {
            var psi = new ProcessStartInfo("git", "status --porcelain")
            {
                WorkingDirectory = dir, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                string outp = p.StandardOutput.ReadToEnd();
                if (p.WaitForExit(3000) && p.ExitCode == 0) changes = outp.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            }
        }
        catch { /* git не установлен: только ветка */ }
        return (branch, changes);
    }

    // ---------- сборки ----------
    private sealed class BuildProc { public required Process P; public DateTime Start; public string What = ""; }
    private readonly Dictionary<int, BuildProc> _builds = new();
    private bool _buildPolling;
    private static readonly Regex BuildCmd = new(
        // run и watch не берём: это запущенная программа или дев-сервер, они живут долго и сборкой не являются
        @"\b(dotnet(\.exe)?""?\s+(build|test|publish|pack)|msbuild|cargo(\.exe)?""?\s+(build|test|check)|((npm|pnpm|yarn|bun)(\.cmd)?|npm-cli\.js|pnpm\.cjs|yarn\.js)""?\s+(run\s+)?(build|test)|vite(\.js)?""?\s+build|next\s+build|\btsc\b|webpack|gradlew?(\.bat)?""?\s+\w*(build|assemble)|go(\.exe)?""?\s+build|cmake\s+--build|ninja)\b",
        RegexOptions.IgnoreCase);

    private async Task PollBuilds()
    {
        if (_buildPolling || !_cfg.ContextAware) return;
        _buildPolling = true;
        try
        {
            var found = await Task.Run(() =>
            {
                var list = new List<(int pid, string what)>();
                using var q = new ManagementObjectSearcher("SELECT ProcessId, Name, CommandLine FROM Win32_Process WHERE Name='dotnet.exe' OR Name='MSBuild.exe' OR Name='cargo.exe' OR Name='node.exe' OR Name='go.exe' OR Name='cmake.exe' OR Name='java.exe' OR Name='ninja.exe'");
                foreach (ManagementObject o in q.Get())
                {
                    string cmd = o["CommandLine"] as string ?? "";
                    // служебные процессы, которые живут всегда: сервер сборки и языковые серверы
                    // рабочие узлы MSBuild (-nodemode) после сборки остаются ждать следующую, их не считаем
                    if (Regex.IsMatch(cmd, @"nodemode|VBCSCompiler|languageserver|GradleDaemon|tsserver|--stdio|--watch", RegexOptions.IgnoreCase)) continue;
                    var m = BuildCmd.Match(cmd);
                    if (m.Success) list.Add((Convert.ToInt32(o["ProcessId"]), m.Value.Replace("\"", "").Replace(".exe", "")));
                }
                return list;
            });
            bool changed = false;
            foreach (var (pid, what) in found)
            {
                if (_builds.ContainsKey(pid)) continue;
                try
                {
                    var p = Process.GetProcessById(pid);
                    _ = p.Handle;   // открываем сразу, чтобы потом узнать код выхода
                    p.EnableRaisingEvents = true;
                    var b = new BuildProc { P = p, Start = DateTime.UtcNow, What = what };
                    p.Exited += (_, _) => Dispatcher.BeginInvoke(() => BuildEnded(pid, b));
                    _builds[pid] = b;
                    changed = true;
                }
                catch { /* уже закончился или нет доступа */ }
            }
            if (changed) { if (!_ctxTimer.IsEnabled) _ctxTimer.Start(); UpdateBubble(); }
            else if (_builds.Count > 0) UpdateBubble();   // время сборки в пузыре
        }
        catch (Exception ex) { App.LogOnce("Context.Builds", ex); }
        finally { _buildPolling = false; }
    }

    private void BuildEnded(int pid, BuildProc b)
    {
        if (!_builds.Remove(pid)) return;
        if (_builds.Count > 0) { UpdateBubble(); return; }   // ещё идут другие: итог по последней
        int code = 0;
        try { code = b.P.ExitCode; } catch { }
        b.P.Dispose();
        // короткое (быстрые служебные запуски) не показываем
        if ((DateTime.UtcNow - b.Start).TotalSeconds < 2) { UpdateBubble(); return; }
        if (code == 0) TriggerDone(Loc.T("Сборка прошла"), DoneC);
        else { TriggerDone(Loc.T("Сборка с ошибками"), FailC, "x"); Sfx.Ui("notif"); }
    }

    // ---------- загрузки браузера: живые активности (SystemToasts.cs сообщает о начале и конце) ----------
    private sealed class WebDl { public string Name = "", Path = ""; public long Size, LastSize; public DateTime Seen; public double Frac = 0.1; }
    private readonly Dictionary<string, WebDl> _webDls = new();
    private readonly DispatcherTimer _webTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private void WebDlStart(string name, string partPath)
    {
        if (!_cfg.ContextAware) return;
        _webDls[name] = new WebDl { Name = name, Path = partPath, Seen = DateTime.UtcNow };
        if (!_webTimer.IsEnabled)
        {
            _webTimer.Tick -= WebTick;
            _webTimer.Tick += WebTick;
            _webTimer.Start();
        }
        UpdateBubble();
    }

    private void WebTick(object? s, EventArgs e)
    {
        foreach (var d in _webDls.Values.ToList())
        {
            long size = -1;
            try { if (File.Exists(d.Path)) size = new FileInfo(d.Path).Length; } catch { }
            if (size < 0 && (DateTime.UtcNow - d.Seen).TotalSeconds > 5) { _webDls.Remove(d.Name); continue; }   // отменили
            if (size >= 0) { d.LastSize = d.Size; d.Size = size; }
            // общий размер браузер не сообщает: волна растёт по мере скачанного и не доходит до верха до конца
            d.Frac = Math.Min(0.9, 0.08 + 0.82 * (1 - Math.Exp(-d.Size / 60e6)));
        }
        if (_webDls.Count == 0) _webTimer.Stop();
        UpdateBubble();
    }

    private void WebDlDone(string name)
    {
        string key = _webDls.Keys.FirstOrDefault(k => name.StartsWith(k, StringComparison.OrdinalIgnoreCase)) ?? name;
        if (!_webDls.Remove(key)) return;
        if (_cfg.BubbleSplit) TriggerDone();
        UpdateBubble();
    }
}
