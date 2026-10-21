using System.Windows;
using System.Windows.Threading;

namespace Island;

// Автообновление в острове (шаг 55, сама проверка и скачивание — Services\Updater.cs).
// Через полминуты после запуска и потом раз в 6 часов ShyFlow смотрит, нет ли новой версии на GitHub.
// Есть: уведомление «Доступна версия X», клик — скачать (прогресс в плашке) и поставить; ShyFlow перезапустится сам.
// «Не сейчас» в настройках запоминает версию, и про неё больше не напоминаем. Проверить вручную: «Система», «О программе».
public partial class MainWindow
{
    internal static MainWindow? Instance { get; private set; }
    private readonly DispatcherTimer _updTimer = new() { Interval = TimeSpan.FromHours(6) };
    private UpdateInfo? _update;
    private bool _updating;

    // ---------- «Что нового» после обновления (шаг 57, Ui\WhatsNewWindow.cs) ----------
    private WelcomeWindow? _welcome;

    private void MaybeWhatsNew()
    {
        var cur = Updater.Current;
        string curS = cur.ToString(3);
        if (_cfg.IsFresh) { _cfg.LastSeenVersion = curS; _cfg.Save(); return; }   // первый запуск: и так всё новое
        // до 3.2 версию не запоминали: такие люди пришли максимум с 3.0
        var seen = Version.TryParse(_cfg.LastSeenVersion, out var v) ? v : new Version(3, 0, 0);
        if (seen >= cur) return;
        _cfg.LastSeenVersion = curS;
        _cfg.Save();
        if (!_cfg.ShowWhatsNew || WhatsNew.Since(seen).Count == 0) return;
        void Open() => WhatsNewWindow.ShowSince(seen, _cfg.UiSounds);
        // приветствие открыто: «Что нового» после него
        if (_welcome is { IsLoaded: true }) { _welcome.Closed += (_, _) => Open(); return; }
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        t.Tick += (_, _) => { t.Stop(); Open(); };
        t.Start();
    }

    private void WhatsNew_Click(object s, RoutedEventArgs e) => WhatsNewWindow.ShowSince(new Version(3, 0, 0), _cfg.UiSounds);

    private void StartUpdates()
    {
        Instance = this;
        if (AppInfo.IsPackaged) return;   // шаг 75: версию из Microsoft Store обновляет сам Store
        _updTimer.Tick += (_, _) => _ = AutoCheck();
        _updTimer.Start();
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        first.Tick += (_, _) => { first.Stop(); _ = AutoCheck(); };
        first.Start();
    }

    private async Task AutoCheck()
    {
        if (!_cfg.AutoUpdate || _updating) return;
        var u = await Updater.Check();
        if (u == null) return;
        _update = u;
        if (_cfg.SkipVersion == u.Tag) return;   // эту версию просили не предлагать
        ShowNotif("ShyFlow", Loc.T("Доступна версия ") + u.Ver, Loc.T("Клик: обновить. Настройки и всё твоё останутся"));
        _notifClick = () => { ApplyMode(Rest()); _ = RunUpdate(u); };
    }

    // кнопка «Проверить» в настройках
    internal async Task CheckUpdatesNow(Window owner)
    {
        if (_updating) { MacDialog.Info(owner, Loc.T("Обновление уже скачивается")); return; }
        var u = await Updater.Check();
        if (u == null) { MacDialog.Info(owner, Loc.T("У тебя последняя версия: ") + AppInfo.CurrentVersion); return; }
        _update = u;
        string notes = Updater.PlainNotes(u.Notes);
        bool go = MacDialog.Ask(owner, Loc.T("Доступна версия ") + u.Ver + Loc.T(" (у тебя ") + AppInfo.CurrentVersion + ")" +
                                        (notes.Length > 0 ? "\n\n" + notes : "") + "\n\n" + Loc.T("ShyFlow закроется на пару секунд и откроется уже новым."), Loc.T("Обновить"));
        if (go) { _cfg.SkipVersion = ""; _cfg.Save(); _ = RunUpdate(u); }
        else { _cfg.SkipVersion = u.Tag; _cfg.Save(); }
    }

    private async Task RunUpdate(UpdateInfo u)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            int lastPct = -1;
            var prog = new Progress<double>(p =>
            {
                int pct = (int)(p * 100);
                if (pct == lastPct) return;
                lastPct = pct;
                _dlFrac = p;   // активность «скачивание» в стеке пузыря (Activities.cs)
                UpdateBubble();
                ShowToast("download", null, p, "dl");
                OsdValue.Text = pct + "%";
            });
            ShowToast("download", Loc.T("Скачиваю обновление ") + u.Ver, null, "dl");
            string? path = await Updater.Download(u, prog);
            if (path == null)
            {
                ShowToast("triangle-alert", Loc.T("Обновление не скачалось, попробую позже"), null, "key");
                return;
            }
            ShowToast("", Loc.T("Устанавливаю ") + u.Ver + "…", null, "dl");
            await Task.Delay(700);
            if (!Updater.Install(path)) { ShowToast("triangle-alert", Loc.T("Не получилось запустить установку"), null, "key"); return; }
            // установщик сам закроет ShyFlow; выходим сами, чтобы файл точно освободился, и сохраняем всё нужное
            ListenStats.Save();
            await Task.Delay(300);
            Application.Current.Shutdown();
        }
        finally { _updating = false; _dlFrac = -1; UpdateBubble(); }
    }
}
