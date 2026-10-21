using System.Windows.Threading;

namespace Island;

// Шаг 73: советы для новичка прямо в острове.
// Приветствие рассказывает о главном один раз, но функций много, и треть так и остаётся незамеченной.
// Здесь каждый совет показывается один раз и тогда, когда он пригодится: сменился трек — про свайп,
// поменялась громкость — про колесо, третий трек без единой карточки — про ★, через 10 минут — про меню.
// Только в свёрнутом острове, не чаще раза в три минуты; если остров был занят, совет подождёт следующего случая.
// Выключаются в настройках: «Основное», «Советы в острове». Показанные хранятся в TipsShown.
public partial class MainWindow
{
    private DateTime _lastTipAt = DateTime.MinValue;
    private int _tipTracks;
    private DispatcherTimer? _tipMenuTimer;

    private void TipsOnTrack()
    {
        _tipTracks++;
        if (_tipTracks == 1 && _cfg.Gestures)
            ScheduleTip("swipe", "arrow-left-right", "проведи по острову вбок: соседний трек", "swipe the island sideways: next or previous track", 2600);
        else if (_tipTracks >= 3 && CardCollection.All.Count == 0)
            ScheduleTip("star", "star", "раскрой остров и нажми ★: обложка станет карточкой", "open the island and press ★ to collect the cover", 2600);
        // про меню — через 10 минут после первого трека
        if (_tipMenuTimer == null)
        {
            _tipMenuTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
            _tipMenuTimer.Tick += (_, _) =>
            {
                _tipMenuTimer.Stop();
                ScheduleTip("menu", "settings", "правый клик по острову: коллекция, статистика, настройки", "right-click the island: collection, stats, settings", 0);
            };
            _tipMenuTimer.Start();
        }
    }

    private void ScheduleTip(string id, string glyph, string ru, string en, int delayMs)
    {
        if (!_cfg.Tips || _cfg.TipsShown.Contains(id)) return;
        void Fire()
        {
            if (!_ready || _hidden || _mode != Mode.Mini || _cfg.TipsShown.Contains(id)) return;   // остров занят: в другой раз
            if ((DateTime.UtcNow - _lastTipAt).TotalMinutes < 3) return;
            _lastTipAt = DateTime.UtcNow;
            _cfg.TipsShown.Add(id);
            _cfg.Save();
            ShowToast(glyph, Loc.T("Совет", "Tip") + ": " + Loc.T(ru, en), null, "key");
            _toastTimer.Stop();
            _toastTimer.Interval = TimeSpan.FromMilliseconds(5000);   // подольше, чтобы успеть прочитать
            _toastTimer.Start();
        }
        if (delayMs <= 0) { Fire(); return; }
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
        t.Tick += (_, _) => { t.Stop(); Fire(); };
        t.Start();
    }
}
