using System.Windows;
using System.Windows.Media;
using NAudio.CoreAudioApi;

namespace Island;

// Быстро выключить микрофон (шаг 59).
//   Ctrl+Alt+V из любой программы, кнопка с микрофоном в шапке «Быстрых действий»,
//   а во время звонка (микрофон кто-то использует) — пузырь рядом с островом: «Discord» с микрофоном, клик выключает и включает.
// Выключается микрофон по умолчанию во всех ролях Windows (для звонков и для остального), как кнопка в «Параметрах».
public partial class MainWindow
{
    private static readonly Color MicLiveC = Color.FromRgb(0x30, 0xD1, 0x58), MicOffC = Color.FromRgb(0xFF, 0x45, 0x3A);
    private string _micApp = "";

    private bool MicMuted
    {
        get
        {
            try
            {
                using var en = new MMDeviceEnumerator();
                return en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications).AudioEndpointVolume.Mute;
            }
            catch { return false; }
        }
    }

    private void ToggleMic() => SetMicMute(!MicMuted);

    private void SetMicMute(bool mute)
    {
        bool any = false;
        try
        {
            using var en = new MMDeviceEnumerator();
            var done = new HashSet<string>();
            foreach (var role in new[] { Role.Communications, Role.Console, Role.Multimedia })
            {
                try
                {
                    var d = en.GetDefaultAudioEndpoint(DataFlow.Capture, role);
                    if (!done.Add(d.ID)) continue;
                    d.AudioEndpointVolume.Mute = mute;
                    any = true;
                }
                catch { /* для этой роли микрофона нет */ }
            }
        }
        catch (Exception ex) { App.LogOnce("Mic.Toggle", ex); }
        if (!any) { ShowToast("mic-off", Loc.T("Микрофон не найден"), null, "mic"); return; }
        _lastMicMute = mute;   // своё уведомление о смене не показываем дважды (SystemToasts.cs)
        Sfx.Ui("tap");
        ShowToast(mute ? "mic-off" : "mic", mute ? "Микрофон выключен" : "Микрофон включён", null, "mic");
        UpdateMicUi();
    }

    private void MicBtn_Click(object s, RoutedEventArgs e)
    {
        if (s is FrameworkElement b) Pop(b, 0.7, 420);
        ToggleMic();
    }

    // кнопка в действиях и пузырь звонка
    private void UpdateMicUi()
    {
        bool muted = MicMuted;
        MicIcon.Text = muted ? "mic-off" : "mic";
        MicIcon.Foreground = new SolidColorBrush(muted ? MicOffC : Colors.White);
        MicBtn.ToolTip = Loc.T(muted ? "Включить микрофон (Ctrl+Alt+V)" : "Выключить микрофон (Ctrl+Alt+V)");
        UpdateBubble();
    }

    // пузырь звонка: микрофон занят какой-то программой
    private (string kind, string text, double frac, Color col, string icon, bool paused)? MicState()
    {
        if (!_cfg.BubbleMic || !_micOn) return null;
        bool muted = _lastMicMute;
        string app = _micApp.Length > 14 ? _micApp[..13] + "…" : _micApp;
        return ("mic", muted ? Loc.T("выкл") : app, 1, muted ? MicOffC : MicLiveC, muted ? "mic-off" : "mic", false);
    }
}
