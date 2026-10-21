using System.Windows;
using System.Windows.Threading;

namespace Island;

// Учёт прослушиваний для статистики (шаг 49): раз в 5 секунд, пока музыка играет, трек получает 5 секунд.
// После 30 секунд трека засчитывается «прослушивание» и сохраняется маленькая обложка. Файл пишется раз в минуту и при выходе.
public partial class MainWindow
{
    private readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private string _statKey = "";
    private int _statRun, _statTicks;
    private bool _statCounted;

    private void StartStats()
    {
        _statsTimer.Tick += (_, _) => StatsTick();
        _statsTimer.Start();
        Closed += (_, _) => ListenStats.Save();
    }

    private void StatsTick()
    {
        if (++_statTicks % 12 == 0) ListenStats.Save();
        if (!_cfg.ListenStats || !_playing || _session == null || _curTitle.Length == 0) return;
        if (_adNow) return;   // реклама в Spotify — не трек (Ads.cs)
        try
        {
            string key = _curArtist + "|" + _curTitle;
            if (key != _statKey) { _statKey = key; _statRun = 0; _statCounted = false; }
            ListenStats.Add(_curTitle, _curArtist, 5);
            _statRun += 5;
            if (!_statCounted && _statRun >= 30)
            {
                _statCounted = true;
                if (_lastCover != null) ListenStats.SaveCover(_curTitle, _curArtist, _lastCover);
                ListenStats.CountPlay(_curTitle, _curArtist);
                CheckAchievements();
            }
        }
        catch (Exception ex) { App.LogOnce("Stats.Tick", ex); }
    }

    private void Stats_Click(object s, RoutedEventArgs e) => OpenStats();
    internal void OpenStats() => StatsWindow.ShowStats();
}
