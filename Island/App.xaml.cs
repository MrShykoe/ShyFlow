using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Island;

public partial class App : System.Windows.Application
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? title);

    private static System.Threading.Mutex? _single;

    private static readonly string LogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island", "crash.log");

    // Ошибки пишутся в %AppData%\Island\crash.log. При запуске вместе с Windows окон нет, и иначе причину не узнать.
    internal static void Log(string what, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var fi = new FileInfo(LogPath);
            if (fi.Exists && fi.Length > 200_000) fi.Delete();
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {what}: {ex}\r\n\r\n");
        }
        catch { }
    }

    // Для мест, которые срабатывают часто (таймеры, каждый кадр, события медиа): одна и та же ошибка пишется один раз,
    // иначе лог за минуту дорастёт до предела и сотрёт всё полезное.
    private static readonly HashSet<string> Seen = new();
    internal static void LogOnce(string what, Exception ex)
    {
        string key = what + "|" + ex.GetType().FullName + "|" + ex.Message;
        lock (Seen)
        {
            if (Seen.Count > 500 || !Seen.Add(key)) return;
        }
        Log(what, ex);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // шаг 75: у версии из Store автозапуск без ключа --autostart (StartupTask не передаёт аргументы):
        // если Windows запущена меньше трёх минут назад, считаем это запуском вместе с Windows
        bool autostart = e.Args.Contains("--autostart") || (AppInfo.IsPackaged && Environment.TickCount64 < 180_000);

        // одна копия: второй запуск (например, автозапуск и ярлык одновременно) тихо закрывается.
        // После обновления старая копия может ещё закрываться, поэтому ждём её до трёх секунд.
        try
        {
            _single = new System.Threading.Mutex(true, @"Local\ShyFlow-single-instance", out bool first);
            if (!first)
            {
                bool got = false;
                try { got = _single.WaitOne(3000); } catch (System.Threading.AbandonedMutexException) { got = true; }
                if (!got) Environment.Exit(0);   // Shutdown() тут не годится: StartupUri всё равно создал бы второе окно
            }
        }
        catch { }

        // сбой не должен убивать остров молча
        DispatcherUnhandledException += (_, ev) => { Log("UI", ev.Exception); ev.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => Log("Fatal", ev.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ev) => { Log("Task", ev.Exception); ev.SetObserved(); };

        // вход в Windows: панель задач, звук и медиа-сервисы поднимаются не сразу. Ждём панель (до 40 секунд) и ещё чуть-чуть.
        if (autostart)
        {
            try
            {
                for (int i = 0; i < 80 && FindWindow("Shell_TrayWnd", null) == IntPtr.Zero; i++) System.Threading.Thread.Sleep(500);
                System.Threading.Thread.Sleep(2500);
            }
            catch { }
        }

        // одна иконка ShyFlow для всех окон (настройки, плеер и т.д.)
        // один шрифт для всего: Segoe UI Variable (Windows 11), на Windows 10 запасной Segoe UI
        try
        {
            var ff = new FontFamily("Segoe UI Variable Text, Segoe UI");
            TextElement.FontFamilyProperty.OverrideMetadata(typeof(TextElement), new FrameworkPropertyMetadata(ff));
            TextBlock.FontFamilyProperty.OverrideMetadata(typeof(TextBlock), new FrameworkPropertyMetadata(ff));
            System.Windows.Controls.Control.FontFamilyProperty.OverrideMetadata(typeof(System.Windows.Controls.Control), new FrameworkPropertyMetadata(ff));
        }
        catch { }
        try
        {
            var icon = BitmapFrame.Create(new Uri("pack://application:,,,/ShyFlow.ico", UriKind.Absolute));
            icon.Freeze();
            Window.IconProperty.OverrideMetadata(typeof(Window), new FrameworkPropertyMetadata(icon));
        }
        catch { }
        base.OnStartup(e);
    }
}
