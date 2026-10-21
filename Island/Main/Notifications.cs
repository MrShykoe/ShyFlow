using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Xml.Linq;

namespace Island;

// Уведомления Windows: остров читает базу всплывашек (wpndatabase.db) и показывает новые.
public partial class MainWindow
{
    // ============================================================
    //  УВЕДОМЛЕНИЯ: чтение базы уведомлений Windows
    //  Windows записывает каждую всплывашку в wpndatabase.db. Мы читаем оттуда новые записи,
    //  это надёжнее, чем ловить окно всплывашки (его в новых Windows 11 просто нет среди окон).
    // ============================================================
    private DispatcherTimer? _wpnTimer;
    private long _wpnLast = -1;
    private DateTime _wpnStamp = DateTime.MinValue;
    private int _wpnBusy;
    private readonly HashSet<string> _wpnLogged = new();

    private static string WpnDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Notifications");

    private void LogOnce(string line)
    {
        lock (_wpnLogged) { if (!_wpnLogged.Add(line)) return; }
        Log($"{DateTime.Now:HH:mm:ss} {line}");
    }

    private void StartWpnReader()
    {
        if (_wpnTimer != null) return;
        _wpnTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _wpnTimer.Tick += (_, _) => { _ = Task.Run(WpnPoll); };
        _wpnTimer.Start();
    }

    private static bool CopyShared(string src, string dst)
    {
        try
        {
            using var i = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var o = new FileStream(dst, FileMode.Create, FileAccess.Write);
            i.CopyTo(o);
            return true;
        }
        catch { return false; }
    }

    private void WpnPoll()
    {
        if (Interlocked.Exchange(ref _wpnBusy, 1) == 1) return;
        try
        {
            if (!_cfg.Notifications) return;
            string db = Path.Combine(WpnDir, "wpndatabase.db"), wal = db + "-wal";
            if (!File.Exists(db)) { LogOnce("wpn: файл базы не найден: " + db); return; }

            var stamp = File.GetLastWriteTimeUtc(db);
            if (File.Exists(wal)) { var w = File.GetLastWriteTimeUtc(wal); if (w > stamp) stamp = w; }
            if (stamp == _wpnStamp && _wpnLast >= 0) return; // ничего не менялось
            _wpnStamp = stamp;

            string tmp = Path.Combine(Path.GetTempPath(), "IslandWpn");
            Directory.CreateDirectory(tmp);
            foreach (var f in Directory.GetFiles(tmp, "wpn.db*")) { try { File.Delete(f); } catch { } }
            string tdb = Path.Combine(tmp, "wpn.db");
            if (!CopyShared(db, tdb)) { LogOnce("wpn: не удалось скопировать базу (занята системой)"); return; }
            if (File.Exists(wal)) CopyShared(wal, tdb + "-wal");

            var (max, rows) = WinSqlite.Read(tdb, _wpnLast);
            if (_wpnLast < 0)
            {
                _wpnLast = max;
                LogOnce("wpn: чтение базы уведомлений работает");
                return;
            }
            _wpnLast = Math.Max(_wpnLast, max);

            NotifItem? last = null;
            foreach (var r in rows)
            {
                if (!string.Equals(r.Type, "toast", StringComparison.OrdinalIgnoreCase)) continue;
                if (r.AppId.Contains("Island", StringComparison.OrdinalIgnoreCase) || r.AppId.Contains("ShyFlow", StringComparison.OrdinalIgnoreCase)) continue;
                var p = ParseToast(r);
                if (p != null) last = p;
            }
            if (last is { } n)
            {
                Log($"{DateTime.Now:HH:mm:ss} wpn: новое уведомление от {n.App}");
                Dispatcher.BeginInvoke(() => { if (_ready) ShowNotifItem(n); });
            }
        }
        catch (Exception ex)
        {
            LogOnce("wpn ошибка: " + ex.GetType().Name + ": " + ex.Message);
        }
        finally { Volatile.Write(ref _wpnBusy, 0); }
    }

    private static string DecodePayload(byte[] b)
    {
        if (b.Length == 0) return "";
        string s = (b.Length > 1 && ((b[0] == 0xFF && b[1] == 0xFE) || b[1] == 0))
            ? Encoding.Unicode.GetString(b) : Encoding.UTF8.GetString(b);
        return s.TrimStart('\uFEFF');
    }

    private static NotifItem? ParseToast(WpnRow r)
    {
        try
        {
            string xml = DecodePayload(r.Payload);
            if (string.IsNullOrWhiteSpace(xml)) return null;
            var doc = XDocument.Parse(xml);
            var texts = doc.Descendants("text")
                .Where(t => (string?)t.Attribute("placement") != "attribution")
                .Select(t => t.Value.Trim()).Where(t => t.Length > 0).ToList();
            if (texts.Count == 0) return null;
            // ссылка для перехода к источнику (шаг 49): только явная activationType="protocol", например tg:// или discord://
            var root = doc.Root;
            string launch = (string?)root?.Attribute("activationType") == "protocol" ? ((string?)root?.Attribute("launch") ?? "").Trim() : "";
            return new NotifItem
            {
                App = FriendlyApp(r.AppId), Title = texts[0], Body = string.Join(" ", texts.Skip(1)),
                AppId = r.AppId, Launch = launch, Keep = true
            };
        }
        catch { return null; }
    }

    // "TelegramMessengerLLP.TelegramDesktop_t4vj0pshhgkwm!Telegram.Desktop" -> "TelegramDesktop"
    private static string FriendlyApp(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "";
        string s = id;
        int bang = s.IndexOf('!');
        if (bang > 0) s = s[..bang];
        int us = s.IndexOf('_');
        if (us > 0) s = s[..us];
        var seg = s.Split('.', '\\', '/').Where(x => x.Length > 0).ToArray();
        return seg.Length == 0 ? id : seg[^1];
    }
}

// ============================================================
//  Чтение базы SQLite через системную библиотеку Windows (winsqlite3.dll), без внешних пакетов
// ============================================================
internal sealed record WpnRow(long Id, string Type, byte[] Payload, string AppId);

internal static class WinSqlite
{
    private const string Lib = "winsqlite3.dll";

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string file, out IntPtr db, int flags, IntPtr vfs);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int n, out IntPtr stmt, IntPtr tail);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr stmt);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_int64(IntPtr stmt, int idx, long v);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern long sqlite3_column_int64(IntPtr stmt, int col);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_blob(IntPtr stmt, int col);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_bytes(IntPtr stmt, int col);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_errmsg(IntPtr db);

    private static string Err(IntPtr db) => db == IntPtr.Zero ? "?" : Marshal.PtrToStringUTF8(sqlite3_errmsg(db)) ?? "?";

    private static void Run(IntPtr db, string sql, long? bind, Action<IntPtr> onRow)
    {
        if (sqlite3_prepare_v2(db, sql, -1, out var st, IntPtr.Zero) != 0)
            throw new InvalidOperationException("sql: " + Err(db));
        try
        {
            if (bind.HasValue) sqlite3_bind_int64(st, 1, bind.Value);
            while (sqlite3_step(st) == 100) onRow(st); // 100 = SQLITE_ROW
        }
        finally { sqlite3_finalize(st); }
    }

    private static string Text(IntPtr st, int col)
    {
        var p = sqlite3_column_text(st, col);
        return p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p) ?? "";
    }

    // after < 0: только узнать последний номер. Иначе вернуть и новые записи.
    public static (long maxId, List<WpnRow> rows) Read(string path, long after)
    {
        IntPtr db = IntPtr.Zero;
        try
        {
            if (sqlite3_open_v2(path, out db, 2 /* READWRITE */, IntPtr.Zero) != 0)
                throw new InvalidOperationException("open: " + Err(db));
            long max = 0;
            Run(db, "SELECT IFNULL(MAX(Id),0) FROM Notification", null, s => max = sqlite3_column_int64(s, 0));
            var rows = new List<WpnRow>();
            if (after >= 0)
            {
                Run(db,
                    "SELECT n.Id, n.Type, n.Payload, h.PrimaryId FROM Notification n " +
                    "LEFT JOIN NotificationHandler h ON n.HandlerId = h.RecordId WHERE n.Id > ?1 ORDER BY n.Id LIMIT 30",
                    after, s =>
                    {
                        int len = sqlite3_column_bytes(s, 2);
                        var b = new byte[len];
                        if (len > 0) Marshal.Copy(sqlite3_column_blob(s, 2), b, 0, len);
                        rows.Add(new WpnRow(sqlite3_column_int64(s, 0), Text(s, 1), b, Text(s, 3)));
                    });
            }
            return (max, rows);
        }
        finally { if (db != IntPtr.Zero) sqlite3_close(db); }
    }
}
