using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Island;

// Меню значка программы в доке острова (правый клик), шаг 46:
//   Переключиться (или «Следующее окно»), Свернуть, Развернуть на весь экран, Показать в проводнике,
//   Открыть ещё одно окно, О программе, Закрыть (все окна), Завершить процесс (с вопросом: несохранённое пропадёт).
public partial class MainWindow
{
    private ContextMenu AppMenu(RunApp app)
    {
        var m = new ContextMenu();
        MenuItem Item(string text, Action act, Brush? color = null)
        {
            var i = new MenuItem { Header = Loc.T(text) };
            if (color != null) i.Foreground = color;
            i.Click += (_, _) =>
            {
                try { act(); }
                catch (Exception ex) { App.LogOnce("Apps.Menu", ex); MacDialog.Info(this, ex.Message); }
            };
            return i;
        }
        bool hasPath = app.Path.Length > 0 && File.Exists(app.Path) && !app.Key.StartsWith("uwp:");

        m.Items.Add(new MenuItem { Header = app.Name, IsEnabled = false, FontWeight = FontWeights.SemiBold });
        m.Items.Add(new Separator());
        // шаг 53: закреплённая программа видна в доке, даже когда закрыта; клик по такой запускает её
        if (app.Wins.Count == 0)
        {
            m.Items.Add(Item("Открыть", () => ActivateApp(app)));
            if (hasPath) m.Items.Add(Item("Показать в проводнике", () => Process.Start("explorer.exe", "/select,\"" + app.Path + "\"")));
            m.Items.Add(Item("Открепить", () => TogglePin(app)));
            return m;
        }
        if (hasPath) m.Items.Add(Item(IsPinned(app) ? "Открепить" : "Закрепить в доке", () => TogglePin(app)));
        m.Items.Add(Item(app.Wins.Count > 1 ? "Следующее окно" : "Переключиться", () => ActivateApp(app)));
        m.Items.Add(Item("Свернуть", () => { foreach (var h in app.Wins) ShowWindow(h, 6 /*SW_MINIMIZE*/); }));
        m.Items.Add(Item("Развернуть на весь экран", () =>
        {
            var h = app.Wins.Contains(_extFg) ? _extFg : app.Wins[0];
            ShowWindow(h, 3 /*SW_MAXIMIZE*/);
            SetForegroundWindow(h);
        }));
        if (hasPath)
        {
            m.Items.Add(Item("Показать в проводнике", () => Process.Start("explorer.exe", "/select,\"" + app.Path + "\"")));
            m.Items.Add(Item("Открыть ещё одно окно", () => Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true })));
        }
        m.Items.Add(Item("О программе", () => ShowAppInfo(app)));
        m.Items.Add(new Separator());
        m.Items.Add(Item(app.Wins.Count > 1 ? "Закрыть все окна" : "Закрыть", () =>
        {
            foreach (var h in app.Wins) PostMessage(h, 0x0010 /*WM_CLOSE*/, IntPtr.Zero, IntPtr.Zero);
        }));
        m.Items.Add(Item("Завершить процесс", () => KillApp(app), new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x60))));
        return m;
    }

    private void ShowAppInfo(RunApp app)
    {
        var lines = new List<string> { app.Name };
        if (app.Path.Length > 0)
        {
            try
            {
                var v = FileVersionInfo.GetVersionInfo(app.Path);
                if (!string.IsNullOrWhiteSpace(v.FileDescription) && v.FileDescription != app.Name) lines.Add(Loc.T("Описание: ") + v.FileDescription);
                if (!string.IsNullOrWhiteSpace(v.CompanyName)) lines.Add(Loc.T("Издатель: ") + v.CompanyName);
                if (!string.IsNullOrWhiteSpace(v.ProductVersion ?? v.FileVersion)) lines.Add(Loc.T("Версия: ") + (v.ProductVersion ?? v.FileVersion));
            }
            catch { /* нет доступа к файлу: просто без этих строк */ }
            lines.Add(Loc.T("Файл: ") + app.Path);
        }
        try
        {
            using var p = Process.GetProcessById((int)app.Pid);
            // память всех процессов этой программы (у браузеров их много)
            long mem = 0;
            foreach (var q in Process.GetProcessesByName(p.ProcessName)) { try { mem += q.WorkingSet64; } catch { } q.Dispose(); }
            lines.Add(Loc.T("Память: ") + FmtSize(mem));
            try { lines.Add(Loc.T("Запущена: ") + p.StartTime.ToString("HH:mm, d MMM", Loc.Culture)); } catch { /* чужой процесс от администратора */ }
            lines.Add("PID: " + app.Pid + "  ·  " + Loc.T("Окон: ") + app.Wins.Count);
        }
        catch { lines.Add(Loc.T("Окон: ") + app.Wins.Count); }
        MacDialog.Info(this, string.Join("\n", lines));
    }

    private void KillApp(RunApp app)
    {
        if (!MacDialog.Ask(this, Loc.T("Завершить «") + app.Name + Loc.T("»? Несохранённое в ней пропадёт"), Loc.T("Завершить"))) return;
        try
        {
            using var p = Process.GetProcessById((int)app.Pid);
            p.Kill(entireProcessTree: true);
        }
        catch (Exception ex) { MacDialog.Info(this, Loc.T("Не получилось завершить: ") + ex.Message); }
    }
}
