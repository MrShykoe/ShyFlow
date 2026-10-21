using System.Runtime.InteropServices;

namespace Island;

// Список мониторов (в пикселях). Первым идёт основной.
internal static class Monitors
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data);

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFOEX mi);

    // WorkBottom: нижний край рабочей области (над панелью задач), шаг 76 — для дока снизу
    public sealed record Info(int Left, int Top, int Width, int Height, bool Primary, int WorkBottom = 0);

    public static List<Info> All()
    {
        var list = new List<Info>();
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref RECT rc, IntPtr __) =>
            {
                var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (GetMonitorInfo(h, ref mi))
                    list.Add(new Info(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right - mi.rcMonitor.Left,
                        mi.rcMonitor.Bottom - mi.rcMonitor.Top, (mi.dwFlags & 1) != 0, mi.rcWork.Bottom));
                return true;
            }, IntPtr.Zero);
        }
        catch { }
        if (list.Count == 0) list.Add(new Info(0, 0, (int)System.Windows.SystemParameters.PrimaryScreenWidth, (int)System.Windows.SystemParameters.PrimaryScreenHeight, true,
            (int)System.Windows.SystemParameters.WorkArea.Bottom));
        return list.OrderByDescending(m => m.Primary).ThenBy(m => m.Left).ToList();
    }
}
