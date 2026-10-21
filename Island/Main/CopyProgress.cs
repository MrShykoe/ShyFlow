using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace Island;

// Прогресс копирования файлов в плашке. У Windows нет открытого API для этого, поэтому остров читает проценты
// из заголовка окна копирования Проводника («37% завершено»). Пока Проводник не показывает такое окно, плашки нет.
public partial class MainWindow
{
    private delegate bool CpEnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(CpEnumProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);

    private readonly DispatcherTimer _cpTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private static readonly Regex CpPct = new(@"(\d{1,3})\s*%", RegexOptions.Compiled);
    private int _cpLast = -1, _cpMiss;
    private DateTime _cpChanged = DateTime.MinValue;
    private bool _cpSeen;

    private void InitCopyProgress()
    {
        _cpTimer.Tick += (_, _) => PollCopy();
        _cpTimer.Start();
    }

    // -1: окна копирования нет; иначе проценты (0, если окно есть, а числа в заголовке пока нет)
    private static int ReadCopyPercent()
    {
        int found = -1;
        EnumWindows((h, _) =>
        {
            var cls = new StringBuilder(64);
            if (GetClassName(h, cls, 64) == 0 || cls.ToString() != "OperationStatusWindow") return true;
            // шаг 50: после копирования Проводник часто не закрывает это окно, а прячет его. Спрятанное не считается
            if (!IsWindowVisible(h)) return true;
            var title = new StringBuilder(256);
            GetWindowText(h, title, 256);
            var m = CpPct.Match(title.ToString());
            if (m.Success && int.TryParse(m.Groups[1].Value, out int p)) { found = Math.Clamp(p, 0, 100); return false; }
            found = Math.Max(found, 0);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private void PollCopy()
    {
        if (!_cfg.CopyProgress) { _cpSeen = false; _cpLast = -1; _cpMiss = 0; return; }
        int p;
        try { p = ReadCopyPercent(); } catch { return; }
        bool quiet = _mode == Mode.Full || _mode == Mode.Pocket || _mode == Mode.Actions;

        if (p < 0)
        {
            // окно пропало на пару проверок подряд: копирование закончилось
            if (_cpSeen && ++_cpMiss >= 2)
            {
                _cpSeen = false; _cpMiss = 0; _cpLast = -1;
                UpdateBubble();
                if (_ready && !quiet) ShowToast("\uE73E", "Копирование завершено", null, "dl");
            }
            return;
        }
        // 100% и больше не меняется: копирование закончилось, даже если окно ещё висит
        if (p >= 100 && _cpLast >= 100 && (DateTime.UtcNow - _cpChanged).TotalSeconds > 2.5)
        {
            bool wasSeen = _cpSeen;
            // с пузырём «готово» показывает он: волна выплёскивается и уходит в галочку (шаг 62, Activities.cs)
            bool inBubble = _cfg.BubbleSplit && _cfg.BubbleCopy;
            if (_cpSeen && _ready && !quiet && !inBubble) ShowToast("\uE73E", "Копирование завершено", null, "dl");
            _cpSeen = false; _cpMiss = 0; _cpLast = 1000;   // 1000: это окно уже «закрыто», пока оно не пропадёт или не начнёт новое
            if (wasSeen && inBubble && _ready) TriggerDone();
            UpdateBubble();
            return;
        }
        if (_cpLast == 1000 && p >= 100) return;
        _cpSeen = true; _cpMiss = 0;
        if (p == _cpLast) return;
        _cpLast = p;
        _cpChanged = DateTime.UtcNow;
        UpdateBubble();
        if (!_ready || quiet || (_cfg.BubbleSplit && _cfg.BubbleCopy)) return;   // с пузырём проценты в нём, а не плашками каждую секунду
        ShowToast("\uE8C8", null, p / 100.0, "copy");
        OsdValue.Text = p + "%";
    }
}
