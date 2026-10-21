using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Island;

// Значки открытых приложений рядом с островом или в нём, как док или меню-бар на macOS (шаг 44).
// Настройки, «Внешний вид», «Значки приложений»: выкл, слева от острова, справа, в развёрнутом острове.
//
// Показываются программы с настоящими окнами (как на панели задач): без фоновых процессов, всплывашек и скрытых окон.
// Окна одной программы собираются в один значок. Активная программа отмечена точкой под значком.
//   Клик: переключиться на программу (свёрнутую разворачивает). Клик по уже активной сворачивает её.
//   Несколько окон: каждый клик переключает на следующее. Средний клик: закрыть окно (как крестик).
//   При наведении значки увеличиваются, как в доке macOS.
// Запустилась новая программа: её значок «впрыгивает», а остров показывает «Открыто: …» с её иконкой (можно выключить).
public partial class MainWindow
{
    private sealed class RunApp
    {
        public string Key = "", Name = "", Path = "";
        public readonly List<IntPtr> Wins = new();
        public BitmapSource? Icon;
        public int Cycle;
        public uint Pid;
    }

    private const int AppsMax = 10;
    private readonly Dictionary<string, RunApp> _apps = new();
    private readonly List<string> _appOrder = new();             // в порядке появления
    private readonly DispatcherTimer _appsTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private bool _appsFirstScan = true;
    private IntPtr _extFg;                                        // активное окно другой программы (не острова)
    private string _appsSig = "";
    private static readonly uint OwnPid = (uint)Environment.ProcessId;

    // EnumWindows и делегат CpEnumProc объявлены в CopyProgress.cs
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    // GetWindowText и GetClassName объявлены в CopyProgress.cs
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int val, int size);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr SendMessageTimeoutPtr(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr res);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static extern IntPtr GetClassLongPtr(IntPtr h, int idx);

    private static readonly HashSet<string> SkipClasses = new(StringComparer.OrdinalIgnoreCase)
        { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow" };

    private void StartApps()
    {
        _appsTimer.Tick += (_, _) => ScanApps();
        _appsTimer.Start();
        AppsList.MouseMove += (_, e) => Magnify(AppsList, e.GetPosition(AppsList).X);
        AppsList.MouseLeave += (_, _) => Magnify(AppsList, double.NaN);
        AppsFullRow.MouseMove += (_, e) => Magnify(AppsFullRow, e.GetPosition(AppsFullRow).X);
        AppsFullRow.MouseLeave += (_, _) => Magnify(AppsFullRow, double.NaN);
        AppsBar.SizeChanged += (_, _) => PlaceAppsBar();
        // фон острова под значками обрезается по сглаженному контуру капсулы, как у самого острова (в режиме notch с ушами, Notch.cs)
        AppsBgLayers.SizeChanged += (_, _) => ClipCapsuleBg(AppsBar, AppsBgLayers);
        ScanApps();
    }

    // окно похоже на те, что видны на панели задач
    private static bool IsAppWindow(IntPtr h)
    {
        if (!IsWindowVisible(h) || GetWindow(h, 4 /*GW_OWNER*/) != IntPtr.Zero) return false;
        int ex = GetWindowLong(h, -20 /*GWL_EXSTYLE*/);
        if ((ex & 0x80 /*WS_EX_TOOLWINDOW*/) != 0 && (ex & 0x40000 /*WS_EX_APPWINDOW*/) == 0) return false;
        var sb = new StringBuilder(256);
        if (GetWindowText(h, sb, 256) == 0) return false;
        sb.Clear();
        GetClassName(h, sb, 256);
        if (SkipClasses.Contains(sb.ToString())) return false;
        // «спрятанные» окна приложений из Магазина и других рабочих столов
        return DwmGetWindowAttribute(h, 14 /*DWMWA_CLOAKED*/, out int cloaked, 4) != 0 || cloaked == 0;
    }

    private static string WinTitle(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetWindowText(h, sb, 256);
        return sb.ToString();
    }

    private void ScanApps()
    {
        // шаг 76: значки нужны и капсуле у острова, и доку снизу (Dock.cs)
        if (_cfg.AppsPlace == 0 && _cfg.DockBottom == 0) { if (_apps.Count > 0 || AppsBar.Visibility == Visibility.Visible) ClearApps(); _dock?.SetShown(false); return; }
        var found = new Dictionary<string, (string path, uint pid, List<IntPtr> wins)>();
        var order = new List<string>();
        try
        {
            EnumWindows((CpEnumProc)((h, _) =>
            {
                if (!IsAppWindow(h)) return true;
                GetWindowThreadProcessId(h, out uint pid);
                if (pid == OwnPid) return true;
                string path = ProcessPath(pid);
                bool uwp = path.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
                string key = uwp ? "uwp:" + WinTitle(h) : path.Length > 0 ? path.ToLowerInvariant() : "pid:" + pid;
                if (!found.TryGetValue(key, out var f)) { f = (path, pid, new List<IntPtr>()); found[key] = f; order.Add(key); }
                f.wins.Add(h);
                return true;
            }), IntPtr.Zero);
        }
        catch (Exception ex) { App.LogOnce("Apps.Scan", ex); return; }

        RunApp? launched = null;
        foreach (var key in order)
        {
            var (path, pid, wins) = found[key];
            if (!_apps.TryGetValue(key, out var app))
            {
                bool uwp = key.StartsWith("uwp:");
                app = new RunApp
                {
                    Key = key, Path = path,
                    Name = uwp ? key[4..] : AppName(pid, path, null),
                    Icon = (uwp ? WindowIcon(wins[0]) : null) ?? (path.Length > 0 ? AppIcon(path) : null) ?? WindowIcon(wins[0])
                };
                _apps[key] = app;
                _appOrder.Add(key);
                launched ??= app;
            }
            app.Wins.Clear();
            app.Wins.AddRange(wins);
            app.Pid = pid;
        }
        RunApp? closed = null;
        foreach (var gone in _apps.Keys.Where(k => !found.ContainsKey(k)).ToList())
        {
            closed ??= _apps[gone];
            _apps.Remove(gone);
            _appOrder.Remove(gone);
        }

        // «Открыто: …» и «Закрыто: …» только для программ, запущенных и закрытых уже при работающем острове
        bool toast = !_appsFirstScan && _ready && _cfg.AppLaunchToast && !_hidden && _mode is Mode.Mini or Mode.Osd;
        if (toast && launched != null) ShowToast("app-window", Loc.T("Открыто: ") + launched.Name, null, "app", launched.Icon);
        else if (toast && closed != null) ShowToast("x", Loc.T("Закрыто: ") + closed.Name, null, "app", closed.Icon);
        _appsFirstScan = false;

        string sig = string.Join("|", _appOrder) + "#" + _cfg.AppsPlace + "#" + _cfg.DockBottom + "#" + _extFg + "#" + string.Join("|", _cfg.PinnedApps);
        if (sig != _appsSig) { _appsSig = sig; RenderApps(launched); }
    }

    private void ClearApps()
    {
        _apps.Clear();
        _appOrder.Clear();
        _appsSig = "";
        AppsList.Children.Clear();
        AppsFullRow.Children.Clear();
        AppsBar.Visibility = Visibility.Collapsed;
        if (AppsFullRow.Visibility == Visibility.Visible) { AppsFullRow.Visibility = Visibility.Collapsed; Retarget(); }
    }

    // активное окно сменилось (хук из Performance.cs): двигаем точку «активно»
    private void OnForeground(IntPtr h)
    {
        if (h == IntPtr.Zero) return;
        GetWindowThreadProcessId(h, out uint pid);
        if (pid == OwnPid) return;   // клик по острову не считается сменой программы
        _extFg = h;
        if (_cfg.AppsPlace != 0) ScanApps();
        ContextForeground(h, pid);   // шаг 65: IDE, Discord, браузер (Context.cs)
    }

    private bool AppsInIsland => _cfg.AppsPlace == 3;
    private double AppsFullH => AppsInIsland && _apps.Count > 0 ? 38 : 0;

    private void RenderApps(RunApp? launched)
    {
        bool inIsland = AppsInIsland;
        var host = inIsland ? AppsFullRow : AppsList;
        (inIsland ? AppsList : AppsFullRow).Children.Clear();
        host.Children.Clear();
        int shown = 0;
        // шаг 53: сначала закреплённые (запущенные или нет), потом остальные запущенные
        var list = new List<RunApp>();
        foreach (var p in _cfg.PinnedApps)
        {
            string k = p.ToLowerInvariant();
            list.Add(_apps.TryGetValue(k, out var run) ? run : PinnedStub(p));
        }
        list.AddRange(_appOrder.Where(k => !IsPinned(_apps[k])).Select(k => _apps[k]));
        int total = list.Count;
        RenderDock(list, launched);   // шаг 76: док снизу (Dock.cs)
        bool top = _cfg.AppsPlace != 0;
        if (!top) total = 0;          // у острова значков нет, только в доке
        foreach (var app in top ? list : new List<RunApp>())
        {
            if (shown == AppsMax) break;
            var el = AppIconEl(app);
            host.Children.Add(el);
            if (ReferenceEquals(app, launched) && !_appsFirstScan)
            {
                Pop(el, 0.2, 650);   // новая программа «впрыгивает»
                Get(el).T.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(-12, 0, TimeSpan.FromMilliseconds(650)) { EasingFunction = Spr() });
            }
            shown++;
        }
        if (total > AppsMax)
            host.Children.Add(new TextBlock
            {
                Text = "+" + (total - AppsMax), Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255)),
                FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0)
            });

        bool any = total > 0;
        AppsBar.Visibility = !inIsland && any && !_hidden && !_fsMini ? Visibility.Visible : Visibility.Collapsed;
        var full = inIsland && any ? Visibility.Visible : Visibility.Collapsed;
        if (AppsFullRow.Visibility != full) { AppsFullRow.Visibility = full; Retarget(); }
        PlaceAppsBar();
    }

    // cell и icon: размер ячейки и значка (у дока снизу крупнее, шаг 76)
    private FrameworkElement AppIconEl(RunApp app, double cell = 30, double icon = 20)
    {
        bool active = app.Wins.Contains(_extFg);
        bool running = app.Wins.Count > 0;
        var g = new Grid { Width = cell, Height = cell, Background = Brushes.Transparent, Cursor = Cursors.Hand, Tag = app.Key };
        if (app.Icon != null)
        {
            // закреплённая, но не запущенная: значок чуть прозрачнее, без точки
            var img = new Image { Source = app.Icon, Width = icon, Height = icon, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = running ? 1 : 0.55 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            g.Children.Add(img);
        }
        else
            g.Children.Add(new VIcon { Text = "app-window", FontSize = 16, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        // точка под активной программой, маленькие точки, если окон несколько
        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 0) };
        int n = Math.Min(3, app.Wins.Count);
        if (active || n > 1)
            for (int i = 0; i < Math.Max(1, n); i++)
                dots.Children.Add(new Ellipse { Width = 3, Height = 3, Margin = new Thickness(1, 0, 1, 0), Fill = active ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255)) });
        g.Children.Add(dots);
        string title = app.Wins.Count > 0 ? WinTitle(app.Wins[0]) : "";
        g.ToolTip = app.Name + (title.Length > 0 && title != app.Name ? "\n" + title : "") +
                    (app.Wins.Count > 1 ? "\n" + Loc.T("Окон: ") + app.Wins.Count : "") +
                    (IsPinned(app) ? "\n" + Loc.T(running ? "Закреплено" : "Закреплено: клик, чтобы открыть") : "");
        g.MouseLeftButtonUp += (_, e) => { e.Handled = true; Pop(g, 0.75, 420); ActivateApp(app); };
        g.MouseUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            e.Handled = true;
            var h = app.Wins.FirstOrDefault();
            if (h != IntPtr.Zero) PostMessage(h, 0x0010 /*WM_CLOSE*/, IntPtr.Zero, IntPtr.Zero);
        };
        g.MouseLeftButtonDown += (_, e) => e.Handled = true;   // не раскрывать остров и не начинать жест
        g.ContextMenu = AppMenu(app);
        return g;
    }

    private void ActivateApp(RunApp app)
    {
        if (app.Wins.Count == 0)
        {
            // закреплённая и не запущена: запускаем
            if (app.Path.Length == 0 || !File.Exists(app.Path)) { ShowToast("triangle-alert", Loc.T("Файл программы не найден: ") + app.Name, null, "key"); return; }
            try { Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(app.Path) ?? "" }); }
            catch (Exception ex) { App.LogOnce("Apps.Launch", ex); }
            return;
        }
        try
        {
            IntPtr h;
            if (app.Wins.Contains(_extFg) && app.Wins.Count > 1)
            {
                app.Cycle = (app.Wins.IndexOf(_extFg) + 1) % app.Wins.Count;   // несколько окон: следующее
                h = app.Wins[app.Cycle];
            }
            else h = app.Wins.Contains(_extFg) ? _extFg : app.Wins[0];
            // уже активная и единственное окно: сворачиваем, как панель задач
            if (h == _extFg && app.Wins.Count == 1 && !IsIconic(h)) { ShowWindow(h, 6 /*SW_MINIMIZE*/); return; }
            if (IsIconic(h)) ShowWindow(h, 9 /*SW_RESTORE*/);
            SetForegroundWindow(h);
        }
        catch (Exception ex) { App.LogOnce("Apps.Activate", ex); }
    }

    // ---------- закреплённые (шаг 53): путь к exe в настройках, значок виден, даже когда программа закрыта ----------
    private bool IsPinned(RunApp app) => app.Path.Length > 0 && _cfg.PinnedApps.Any(p => string.Equals(p, app.Path, StringComparison.OrdinalIgnoreCase));

    private readonly Dictionary<string, RunApp> _pinStubs = new(StringComparer.OrdinalIgnoreCase);

    private RunApp PinnedStub(string path)
    {
        if (_pinStubs.TryGetValue(path, out var s)) return s;
        s = new RunApp { Key = path.ToLowerInvariant(), Path = path, Name = AppName(0, path, null), Icon = File.Exists(path) ? AppIcon(path) : null };
        _pinStubs[path] = s;
        return s;
    }

    private void TogglePin(RunApp app)
    {
        if (app.Path.Length == 0) return;
        if (IsPinned(app)) _cfg.PinnedApps.RemoveAll(p => string.Equals(p, app.Path, StringComparison.OrdinalIgnoreCase));
        else _cfg.PinnedApps.Add(app.Path);
        _cfg.Save();
        _appsSig = "";
        ScanApps();
        Sfx.Ui("tap");
    }

    // увеличение как в доке: ближе к курсору больше
    // originY: откуда растёт значок (у дока снизу — от низа, вверх; шаг 76)
    private void Magnify(Panel host, double x, double originY = 0.25)
    {
        foreach (var el in host.Children.OfType<FrameworkElement>())
        {
            var t = Get(el);
            el.RenderTransformOrigin = new Point(0.5, originY);
            double s = 1;
            if (!double.IsNaN(x))
            {
                double cx = el.TranslatePoint(new Point(el.ActualWidth / 2, 0), host).X;
                s = 1 + 0.38 * Math.Max(0, 1 - Math.Abs(x - cx) / 46);
            }
            var a = new DoubleAnimation(s, TimeSpan.FromMilliseconds(double.IsNaN(x) ? 260 : 90));
            t.S.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            t.S.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
    }

    // капсула слева или справа от острова, на одной высоте с ним; едет вместе с его шириной
    private void PlaceAppsBar()
    {
        PlaceStage();    // шаг 74: остров слева или справа (Display.cs)
        PlaceBubble();   // пузырь таймера справа от острова (Bubble.cs); капсула справа встаёт за ним
        PlaceEvent();    // и пузырь событий (EventBubble.cs)
        FitWindow();     // окно шире, если остров и капсулы перестали влезать (Display.cs)
        if (AppsBar.Visibility != Visibility.Visible) return;
        double side = AppsLeft ? -1 : 1;
        AppsBarMove.X = side * (Pill.Width / 2 + 8 + AppsBar.ActualWidth / 2 + ((side > 0) == (!BubbleLeft) ? BubbleSpace + EvSpace : 0));   // пузырь с той же стороны: капсула за ним
        AppsBgLayers.Visibility = _cfg.AppsIslandBg ? Visibility.Visible : Visibility.Collapsed;   // фон как у острова можно выключить
        double top = Pill.Margin.Top + 2 * (1 - NotchK);   // в режиме notch тоже у самого края экрана
        if (Math.Abs(AppsBar.Margin.Top - top) > 0.1) AppsBar.Margin = new Thickness(0, top, 0, 0);
    }

    // остров спрятан (Ctrl+Alt+I, плеер на весь экран): капсула тоже
    private void AppsHiddenChanged()
    {
        if (_cfg.AppsPlace is 1 or 2 && (_apps.Count > 0 || _cfg.PinnedApps.Count > 0))
            AppsBar.Visibility = _hidden || _fsMini ? Visibility.Collapsed : Visibility.Visible;
        PlaceDock();   // шаг 76: док снизу прячется вместе с островом
        UpdateBubble();
        if (_hidden || _fsMini) HideEvent();
        PlaceAppsBar();
    }

    // иконка окна (нужна приложениям из Магазина: у них exe общий, ApplicationFrameHost)
    private static BitmapSource? WindowIcon(IntPtr h)
    {
        try
        {
            IntPtr hi = IntPtr.Zero;
            foreach (var kind in new IntPtr[] { (IntPtr)1, (IntPtr)2, IntPtr.Zero })   // ICON_BIG, ICON_SMALL2, ICON_SMALL
            {
                SendMessageTimeoutPtr(h, 0x007F /*WM_GETICON*/, kind, IntPtr.Zero, 2 /*SMTO_ABORTIFHUNG*/, 100, out hi);
                if (hi != IntPtr.Zero) break;
            }
            if (hi == IntPtr.Zero) hi = GetClassLongPtr(h, -14 /*GCLP_HICON*/);
            if (hi == IntPtr.Zero) hi = GetClassLongPtr(h, -34 /*GCLP_HICONSM*/);
            if (hi == IntPtr.Zero) return null;
            // иконка принадлежит окну: не уничтожаем её, только копируем картинку
            var bs = Imaging.CreateBitmapSourceFromHIcon(hi, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bs.Freeze();
            return bs;
        }
        catch { return null; }
    }
}
