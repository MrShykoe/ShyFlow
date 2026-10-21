using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace Island;

// Шаг 74: полноэкранные приложения и несколько мониторов.
//
// АВТО-СКРЫТИЕ. Раньше остров нарочно поднимался поверх всего, и игра в безрамочном режиме или видео на весь экран
// оказывались с чёрной капсулой сверху. Теперь, если активное окно занимает весь монитор острова и у него нет
// заголовка (игра, F11 в браузере, плеер на весь экран), остров уезжает вверх, как по Ctrl+Alt+I, и возвращается,
// когда это окно перестало быть на весь экран или активным. Развёрнутое окно с заголовком (обычный браузер на весь
// экран) не считается. Ctrl+Alt+I в игре всё равно показывает остров — до выхода из полноэкранного режима.
// Настройки, «Основное», «Прятать в играх и видео на весь экран» (вкл).
//
// МОНИТОР «ГДЕ АКТИВНОЕ ОКНО». Остров переезжает на монитор, где сейчас активное окно. Настройки, «Основное», «Монитор».
public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)] private struct WRECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONINFO
    {
        public int cbSize;
        public WRECT rcMonitor, rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }
    // GetForegroundWindow (Translator.cs), GetClassName (CopyProgress.cs), IsWindowVisible и IsIconic (AppsBar.cs) уже объявлены
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out WRECT r);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonInfo(IntPtr hMon, ref MONINFO mi);

    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    // окна оболочки: рабочий стол, панель задач, Пуск, поиск, Alt+Tab — это не «приложение на весь экран»
    private static readonly string[] ShellClasses =
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow", "MultitaskingViewFrame", "ForegroundStaging", "TaskListThumbnailWnd", "NotifyIconOverflowWindow"
    };

    private readonly DispatcherTimer _scrTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private bool _fsHidden;      // остров спрятан, потому что сейчас полноэкранное приложение
    private bool _fsOverride;    // в полноэкранном его показали Ctrl+Alt+I: не прятать снова, пока полноэкранное не кончится
    private bool _autoToggling;  // ToggleHidden вызвал сам остров, а не человек
    private int _followMon;      // монитор с активным окном (когда Monitor = -1)

    private void InitScreens()
    {
        _scrTimer.Tick += (_, _) => ScreensCheck();
        _scrTimer.Start();
    }

    // вызывается при смене активного окна (Performance.cs) и по таймеру: F11 в браузере не меняет активное окно
    private void ScreensCheck()
    {
        if (_hwnd == IntPtr.Zero) return;
        IntPtr fg;
        try { fg = GetForegroundWindow(); } catch { return; }
        FollowMonitor(fg);
        FullscreenCheck(fg);
    }

    // ---------- монитор с активным окном ----------
    private int CurMonitor => _cfg.Monitor >= 0 ? _cfg.Monitor : _followMon;

    private void FollowMonitor(IntPtr fg)
    {
        if (_cfg.Monitor >= 0 || fg == IntPtr.Zero || IsShellOrOwn(fg)) return;
        int idx = MonitorIndexOf(fg);
        if (idx < 0 || idx == _followMon) return;
        _followMon = idx;
        PlaceOnMonitor();
        NotchRetarget();
        PlaceDock();   // шаг 76: док снизу едет на тот же монитор
    }

    private static int MonitorIndexOf(IntPtr h)
    {
        try
        {
            var mi = new MONINFO { cbSize = Marshal.SizeOf<MONINFO>() };
            if (!GetMonInfo(MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST), ref mi)) return -1;
            var list = Monitors.All();
            return list.FindIndex(m => m.Left == mi.rcMonitor.Left && m.Top == mi.rcMonitor.Top);
        }
        catch { return -1; }
    }

    private bool IsShellOrOwn(IntPtr h)
    {
        GetWindowThreadProcessId(h, out uint pid);
        if (pid == OwnPid) return true;
        var sb = new StringBuilder(128);
        GetClassName(h, sb, sb.Capacity);
        string cls = sb.ToString();
        return ShellClasses.Contains(cls);
    }

    // ---------- авто-скрытие ----------
    // Шаг 76: два вида. «Тонкая полоска» (по умолчанию): свёрнутый остров сжимается в короткую полупрозрачную черту
    // и не закрывает игру, но плашки (трек, громкость, уведомления) всё равно показываются и потом снова сжимаются в черту.
    // «Прятать совсем»: как раньше, остров уезжает вверх.
    private bool _fsMini;   // сейчас полноэкранное приложение и выбран вид «тонкая полоска»

    private void FullscreenCheck(IntPtr fg)
    {
        bool fs = _cfg.HideFullscreen && IsFullscreen(fg);
        bool strip = _cfg.FsStyle == 0;
        if (!fs || !strip) SetFsMini(false);
        if (!fs)
        {
            _fsOverride = false;
            if (_fsHidden) { _fsHidden = false; if (_hidden) AutoToggle(); }
            return;
        }
        if (strip)
        {
            if (_fsHidden) { _fsHidden = false; if (_hidden) AutoToggle(); }   // вид сменили прямо в игре
            SetFsMini(!_fsOverride);
            return;
        }
        if (_fsOverride || _fsHidden || _hidden) return;
        _fsHidden = true;
        AutoToggle();
    }

    private void SetFsMini(bool on)
    {
        if (_fsMini == on) return;
        _fsMini = on;
        if (_mode == Mode.Mini) ApplyMode(Mode.Mini);   // сжаться в черту или вернуться
        Pill.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(
            on && _mode == Mode.Mini ? 0.45 : 1, TimeSpan.FromMilliseconds(300)));
        AppsHiddenChanged();   // пузыри и капсула значков в игре не нужны
    }

    private void AutoToggle()
    {
        _autoToggling = true;
        try { ToggleHidden(); }
        finally { _autoToggling = false; }
    }

    // человек сам нажал Ctrl+Alt+I или «Скрыть / показать» (из ToggleHidden)
    private void OnManualToggle()
    {
        if (_autoToggling) return;
        if (_fsHidden) { _fsHidden = false; _fsOverride = true; }
        if (_fsMini) { _fsOverride = true; SetFsMini(false); }   // в игре показали остров целиком
    }

    private bool IsFullscreen(IntPtr h)
    {
        try
        {
            if (h == IntPtr.Zero || !IsWindowVisible(h) || IsIconic(h) || IsShellOrOwn(h)) return false;
            if ((GetWindowLong(h, GWL_STYLE) & WS_CAPTION) == WS_CAPTION) return false;   // обычное развёрнутое окно с заголовком
            if (!GetWindowRect(h, out var r)) return false;
            var list = Monitors.All();
            var m = list[Math.Clamp(CurMonitor, 0, list.Count - 1)];
            // окно накрывает весь монитор острова (пару пикселей запаса на рамки)
            return r.Left <= m.Left + 2 && r.Top <= m.Top + 2 && r.Right >= m.Left + m.Width - 2 && r.Bottom >= m.Top + m.Height - 2;
        }
        catch { return false; }
    }
}
