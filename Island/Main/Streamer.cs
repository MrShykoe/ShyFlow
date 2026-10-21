namespace Island;

// Режим стримера (шаг 67). Настройки, «Режим стримера»:
//   чат Twitch под островом (Services\TwitchChat.cs, Ui\ChatOverlay.cs): анонимно, только чтение, мышь проходит насквозь;
//   в пузыре активность «LIVE» с последним сообщением; клик по ней прячет и показывает чат;
//   уведомления без текста: всплывает только имя программы и «скрыто», чтобы личное не попало в эфир
//   (в шторке уведомлений текст остаётся, её открываешь сам).
public partial class MainWindow
{
    private TwitchChat? _chat;
    private ChatOverlay? _chatWin;
    private string _chatFor = "";
    private bool _chatLive, _chatHidden;
    private TwitchChat.Msg? _chatLast;
    private static readonly System.Windows.Media.Color LiveC = System.Windows.Media.Color.FromRgb(0x91, 0x46, 0xFF);

    // из ApplySettings: включить, выключить или сменить канал
    private void ApplyStreamer()
    {
        string? ch = _cfg.StreamerMode ? TwitchChat.Normalize(_cfg.TwitchChannel ?? "") : null;
        if (ch == _chatFor && (ch == null) == (_chat == null)) return;
        _chat?.Dispose(); _chat = null;
        _chatWin?.Close(); _chatWin = null;
        _chatFor = ch ?? "";
        _chatLive = false; _chatLast = null;
        if (ch != null)
        {
            _chatWin = new ChatOverlay();
            _chatWin.Place(MonitorDip, 56);
            if (!_chatHidden) _chatWin.Show();
            _chatWin.System(Loc.T("Подключаюсь к чату ") + ch + "…");
            _chat = new TwitchChat(ch);
            _chat.Connected += on => Dispatcher.BeginInvoke(() =>
            {
                _chatLive = on;
                _chatWin?.System(on ? Loc.T("Чат ") + ch + Loc.T(" на связи") : Loc.T("Связь с чатом потеряна, переподключаюсь"));
                UpdateBubble();
            });
            _chat.Message += m => Dispatcher.BeginInvoke(() =>
            {
                _chatLast = m;
                _chatWin?.Add(m);
                UpdateBubble();
            });
        }
        UpdateBubble();
    }

    // активность «LIVE» в стеке пузыря (Activities.cs)
    private (string kind, string text, double frac, System.Windows.Media.Color col, string icon, bool paused)? StreamState()
    {
        if (_chat == null) return null;
        string t = _chatLast != null ? _chatLast.Name + ": " + _chatLast.Text : (_chatLive ? "LIVE · " + _chatFor : "…");
        if (t.Length > 26) t = t[..25] + "…";
        return ("live", t, 1, LiveC, "message-circle", _chatHidden);
    }

    private void ToggleChatOverlay()
    {
        if (_chatWin == null) return;
        _chatHidden = !_chatHidden;
        if (_chatHidden) _chatWin.Hide(); else { _chatWin.Place(MonitorDip, 56); _chatWin.Show(); }
        UpdateBubble();
    }

    // уведомление для показа в эфире: без заголовка и текста
    private NotifItem StreamSafe(NotifItem n) =>
        !_cfg.StreamerMode || !_cfg.StreamerHideNotifs ? n
        : new NotifItem { App = n.App, Title = n.App.Length > 0 ? n.App : Loc.T("Уведомление"), Body = Loc.T("Скрыто: режим стримера"), AppId = n.AppId, Launch = n.Launch, Keep = n.Keep, At = n.At };
}
