using System.IO;
using System.Windows;
using System.Windows.Threading;
using Windows.Media.Control;

namespace Island;

// Реклама в Spotify (шаг 58). Как узнаём: играет Spotify, а у «трека» нет исполнителя (или исполнитель «Spotify»/«Advertisement»),
// и он не длиннее ~минуты.
//   «Пропускать рекламу» (AdSkip): кнопкой «вперёд» её не пропустить — во время рекламы Spotify её блокирует. Поэтому ShyFlow
//     перезапускает Spotify: закрывает, открывает свёрнутым и включает музыку дальше, реклама пропадает. Клиент не меняется,
//     ничего не взламывается. Не чаще раза в 20 секунд; без автопропуска «вперёд» в острове во время рекламы делает то же самое.
//   «Узнавать рекламу» (AdDetect, по умолчанию выкл): вместо названия «Реклама · 0:23», без текста песни и статистики.
//   «Глушить рекламу» (AdMute): звук только у Spotify выключается на время рекламы.
public partial class MainWindow
{
    private bool _adOn, _adMutedByUs, _adNow;
    private readonly DispatcherTimer _adTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _adHooked;

    private bool IsSpotifyAd(string title, string artist)
    {
        if (_session == null) return false;
        string src = _session.SourceAppUserModelId ?? "";
        if (src.IndexOf("spotify", StringComparison.OrdinalIgnoreCase) < 0) return false;
        if (title.Length == 0) return false;
        if (title.Equals("Advertisement", StringComparison.OrdinalIgnoreCase) || title.Equals("Spotify", StringComparison.OrdinalIgnoreCase)) return true;
        if (artist.Equals("Spotify", StringComparison.OrdinalIgnoreCase) || artist.Equals("Advertisement", StringComparison.OrdinalIgnoreCase)) return true;
        // второй признак: Spotify запретил «вперёд», а «трек» короткий — так ведёт себя реклама (у неё может быть и «исполнитель»)
        try
        {
            bool nextBlocked = !_session.GetPlaybackInfo().Controls.IsNextEnabled;
            double len = AdTotal();
            if (nextBlocked && len > 0 && len <= 65) return true;
        }
        catch { /* сессия пропала */ }
        if (!string.IsNullOrWhiteSpace(artist)) return false;
        // нет исполнителя: реклама, если короткая (у треков без исполнителя, например своих файлов, длина обычно больше)
        double total = AdTotal();
        return total <= 0 || total <= 65;
    }

    private double AdTotal()
    {
        try
        {
            var tl = _session?.GetTimelineProperties();
            return tl == null ? 0 : (tl.EndTime - tl.StartTime).TotalSeconds;
        }
        catch { return 0; }
    }

    private double AdLeft()
    {
        try
        {
            var tl = _session?.GetTimelineProperties();
            if (tl == null) return 0;
            var pos = tl.Position;
            if (_playing) pos += DateTimeOffset.Now - tl.LastUpdatedTime;
            return Math.Max(0, (tl.EndTime - pos).TotalSeconds);
        }
        catch { return 0; }
    }

    // вызывается из RefreshAsync вместо обычной обработки трека
    private void EnterAd(string brand)
    {
        if (!_adHooked)
        {
            _adHooked = true;
            _adTimer.Tick += (_, _) => AdTick();
            Closed += (_, _) => { if (_adMutedByUs) SetSpotifyMute(false); };   // закрыли во время рекламы: Spotify не должен остаться без звука
        }
        var status = _session?.GetPlaybackInfo().PlaybackStatus;
        _playing = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        PlayIcon.Text = _playing ? "pause" : "play";
        if (_playing) StartCapture(); else StopCapture();
        if (_adOn) { AdTick(); return; }

        _adOn = true;
        // в журнал уведомлений (меню острова, «Журнал уведомлений»): по этим строкам видно, если реклама узнаётся неправильно
        Log($"{DateTime.Now:HH:mm:ss} реклама: «{brand}», длина {AdTotal():0} с");
        // не трек: ни текста песни, ни статистики, ни «Сейчас играет»
        _curTitle = ""; _curArtist = "";
        SetLyricMode(false);
        TitleText.Text = Loc.T("Реклама");
        ArtistText.Text = brand.Equals("Spotify", StringComparison.OrdinalIgnoreCase) || brand.Equals("Advertisement", StringComparison.OrdinalIgnoreCase) ? "Spotify" : brand;
        AdTick();
        _adTimer.Start();
        if (_cfg.AdMute) SetSpotifyMute(true);
        if (_ready && _cfg.PlaybackToasts && _mode is Mode.Mini or Mode.Osd)
            ShowToast("megaphone", Loc.T("Реклама в Spotify") + (_cfg.AdMute ? Loc.T(": звук выключен") : ""), null, "key");
    }

    private void AdTick()
    {
        if (!_adOn) { _adTimer.Stop(); return; }
        double left = AdLeft();
        string text = Loc.T("Реклама") + (left >= 1 ? " · " + Fmt(left) : "");
        if (MiniTitle.Text != text) { MiniTitle.Text = text; UpdateWidths(text, ArtistText.Text); }
    }

    private void ExitAd()
    {
        if (!_adOn) return;
        _adOn = false;
        _adTimer.Stop();
        if (_adMutedByUs) SetSpotifyMute(false);
    }

    // ---------- пропуск: перезапуск Spotify ----------
    private bool _adSkipping;
    private DateTime _adSkipAt = DateTime.MinValue;

    // реклама сейчас: «вперёд» (кнопка, свайп, Ctrl+Alt+→, плеер) пропускает её перезапуском; true — перехватили
    internal bool SkipAdInstead()
    {
        if (!_adNow) return false;
        _ = SkipAd(true);
        return true;
    }

    private async Task SkipAd(bool manual)
    {
        if (_adSkipping || _session == null) return;
        if (!manual && (DateTime.UtcNow - _adSkipAt).TotalSeconds < 20) return;   // не перезапускать по кругу
        _adSkipping = true;
        _adSkipAt = DateTime.UtcNow;
        try
        {
            string aumid = _session.SourceAppUserModelId ?? "";
            var procs = System.Diagnostics.Process.GetProcessesByName("Spotify");
            string exe = procs.Select(p => ProcessPath((uint)p.Id)).FirstOrDefault(p => p.Length > 0) ?? "";
            var prevFg = GetForegroundWindow();
            bool spotifyWasFront = procs.Any(p => { try { return p.MainWindowHandle == prevFg && prevFg != IntPtr.Zero; } catch { return false; } });
            Log($"{DateTime.Now:HH:mm:ss} реклама: пропуск, перезапуск Spotify ({(exe.Length > 0 ? exe : aumid)})");
            if (_ready) ShowToast("rotate-ccw", Loc.T("Пропускаю рекламу…"), null, "key");

            foreach (var p in procs) { try { p.Kill(entireProcessTree: true); } catch { } p.Dispose(); }
            await Task.Delay(700);

            // Spotify из Магазина запускается по коду приложения, обычный — своим exe, сразу свёрнутым
            bool store = exe.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) || exe.Length == 0;
            if (store && aumid.Length > 0) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + aumid) { UseShellExecute = true });
            else if (exe.Length > 0) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--minimized") { UseShellExecute = true });
            else return;

            // ждём, пока Spotify снова появится в Windows как плеер, и включаем музыку
            GlobalSystemMediaTransportControlsSession? s = null;
            for (int i = 0; i < 50 && s == null; i++)
            {
                await Task.Delay(300);
                s = _mgr?.GetSessions().FirstOrDefault(x => (x.SourceAppUserModelId ?? "").Contains("spotify", StringComparison.OrdinalIgnoreCase));
            }
            if (s == null) { ShowToast("triangle-alert", Loc.T("Spotify не запустился заново"), null, "key"); return; }
            for (int i = 0; i < 8; i++)
            {
                await Task.Delay(i == 0 ? 600 : 500);
                try
                {
                    if (s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) break;
                    await s.TryPlayAsync();
                }
                catch { /* сессия ещё не готова */ }
            }
            // окно Spotify не должно выскакивать поверх того, что ты делал
            if (!spotifyWasFront)
            {
                await Task.Delay(400);
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("Spotify"))
                {
                    try { if (p.MainWindowHandle != IntPtr.Zero) ShowWindow(p.MainWindowHandle, 6 /*SW_MINIMIZE*/); } catch { }
                    p.Dispose();
                }
                if (prevFg != IntPtr.Zero) SetForegroundWindow(prevFg);
            }
            if (_ready) ShowToast("", Loc.T("Реклама пропущена"), null, "dl");
        }
        catch (Exception ex) { App.Log("Ads.Skip", ex); }
        finally { _adSkipping = false; }
    }

    // глушим только Spotify: его звуковые сессии в микшере Windows
    private void SetSpotifyMute(bool mute)
    {
        try
        {
            var dev = _dev;
            if (dev == null) return;
            var mgr = dev.AudioSessionManager;
            mgr.RefreshSessions();
            var all = mgr.Sessions;
            bool any = false;
            for (int i = 0; i < all.Count; i++)
            {
                var ses = all[i];
                if (ses.IsSystemSoundsSession) continue;
                string exe = Path.GetFileNameWithoutExtension(ProcessPath(ses.GetProcessID));
                if (!exe.Equals("Spotify", StringComparison.OrdinalIgnoreCase)) continue;
                ses.SimpleAudioVolume.Mute = mute;
                any = true;
            }
            _adMutedByUs = mute && any;
        }
        catch (Exception ex) { App.LogOnce("Ads.Mute", ex); }
    }
}
