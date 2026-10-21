using System.Windows.Threading;

namespace Island;

// Discord Rich Presence: пока играет музыка, «Слушает: название, исполнитель» с полоской времени.
// Когда ничего не играет, в профиле просто «Использует ShyFlow» с временем работы. Включается в настройках.
public partial class MainWindow
{
    private readonly DiscordRpc _discord = new();
    private DispatcherTimer? _discordTimer;
    private readonly long _startedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private void StartDiscord()
    {
        _discordTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _discordTimer.Tick += (_, _) => UpdateDiscord();
        _discordTimer.Start();
    }

    private void UpdateDiscord()
    {
        try
        {
            string id = AppInfo.DiscordAppId;
            if (!_cfg.DiscordRpc || string.IsNullOrWhiteSpace(id))
            {
                Task.Run(() => _discord.Shutdown());
                return;
            }
            Presence? p = null;
            if (_session != null && _curTitle != "")
            {
                string artist = string.IsNullOrWhiteSpace(_curArtist) ? "ShyFlow" : _curArtist;
                if (_playing)
                {
                    var tl = _session.GetTimelineProperties();
                    double total = (tl.EndTime - tl.StartTime).TotalSeconds;
                    var pos = tl.Position + (DateTimeOffset.Now - tl.LastUpdatedTime);
                    double cur = Math.Clamp((pos - tl.StartTime).TotalSeconds, 0, Math.Max(0, total));
                    long start = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)cur;
                    long end = total > 1 ? start + (long)total : 0;
                    p = new Presence(_curTitle, artist, start, end, AppInfo.DiscordLargeImage, "ShyFlow");
                }
                else
                {
                    p = new Presence(_curTitle, artist + " · " + Loc.T("пауза", "paused"), 0, 0, AppInfo.DiscordLargeImage, "ShyFlow");
                }
            }
            else
            {
                // музыки нет: показываем, что ShyFlow запущен
                p = new Presence(Loc.T("Использует ShyFlow", "Using ShyFlow"), Loc.T("Остров для Windows", "Island for Windows"),
                    _startedUnix, 0, AppInfo.DiscordLargeImage, "ShyFlow", 0);
            }
            Task.Run(() => _discord.Update(id, p));
        }
        catch (Exception ex) { App.LogOnce("Discord", ex); }
    }
}
