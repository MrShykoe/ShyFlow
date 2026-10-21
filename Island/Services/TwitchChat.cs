using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Island;

// Чат Twitch только на чтение (шаг 67): анонимный вход в IRC Twitch (justinfan), без аккаунта и ключей.
// Сообщения с именем и цветом ника; при обрыве переподключается сам (через 2, 4, 8... до 60 с).
internal sealed class TwitchChat : IDisposable
{
    public sealed record Msg(string Name, string Color, string Text);

    public event Action<Msg>? Message;          // не из потока окна
    public event Action<bool>? Connected;       // true — в канале, false — связь потеряна

    private readonly string _channel;
    private readonly CancellationTokenSource _cts = new();

    public TwitchChat(string channel)
    {
        _channel = channel.Trim().TrimStart('#').ToLowerInvariant();
        _ = Task.Run(Run);
    }

    // ник канала из «twitch.tv/имя», «@имя» или просто «имя»
    public static string? Normalize(string s)
    {
        s = s.Trim();
        var m = Regex.Match(s, @"twitch\.tv/([A-Za-z0-9_]{3,25})", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.ToLowerInvariant();
        s = s.TrimStart('@', '#');
        return Regex.IsMatch(s, @"^[A-Za-z0-9_]{3,25}$") ? s.ToLowerInvariant() : null;
    }

    private async Task Run()
    {
        int delay = 2;
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync("irc.chat.twitch.tv", 6697, _cts.Token);
                using var ssl = new SslStream(tcp.GetStream());
                await ssl.AuthenticateAsClientAsync("irc.chat.twitch.tv");
                using var w = new StreamWriter(ssl, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
                using var r = new StreamReader(ssl, Encoding.UTF8);
                await w.WriteLineAsync("CAP REQ :twitch.tv/tags");
                await w.WriteLineAsync("PASS SCHMOOPIIE");
                await w.WriteLineAsync("NICK justinfan" + Random.Shared.Next(10000, 99999));
                await w.WriteLineAsync("JOIN #" + _channel);
                while (!_cts.IsCancellationRequested)
                {
                    string? line = await r.ReadLineAsync(_cts.Token);
                    if (line == null) break;
                    if (line.StartsWith("PING")) { await w.WriteLineAsync("PONG" + line[4..]); continue; }
                    if (line.Contains(" JOIN #" + _channel)) { delay = 2; Connected?.Invoke(true); continue; }
                    if (Parse(line) is { } msg) Message?.Invoke(msg);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { App.LogOnce("Twitch", ex); }
            if (_cts.IsCancellationRequested) break;
            Connected?.Invoke(false);
            try { await Task.Delay(TimeSpan.FromSeconds(delay), _cts.Token); } catch { break; }
            delay = Math.Min(60, delay * 2);
        }
    }

    // «@color=#FF0000;display-name=Ник;... :ник!ник@ник.tmi.twitch.tv PRIVMSG #канал :текст»
    private static Msg? Parse(string line)
    {
        int pm = line.IndexOf(" PRIVMSG #", StringComparison.Ordinal);
        if (pm < 0) return null;
        int colon = line.IndexOf(" :", pm + 10, StringComparison.Ordinal);
        if (colon < 0) return null;
        string text = line[(colon + 2)..];
        if (text.StartsWith("\u0001ACTION ")) text = text[8..].TrimEnd('\u0001');   // /me
        string tags = line.StartsWith('@') ? line[1..line.IndexOf(' ')] : "";
        string Tag(string k) => Regex.Match(tags, "(?:^|;)" + k + "=([^;]*)").Groups[1].Value;
        string name = Tag("display-name");
        if (name.Length == 0)
        {
            int ex = line.IndexOf('!');
            int st = line.IndexOf(" :", StringComparison.Ordinal);
            name = ex > 0 && st >= 0 && st < ex ? line[(st + 2)..ex] : "?";
        }
        return new Msg(name.Replace("\\s", " "), Tag("color"), text);
    }

    public void Dispose() => _cts.Cancel();
}
