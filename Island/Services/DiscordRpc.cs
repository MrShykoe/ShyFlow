using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Island;

// Type: 2 «Слушает» (музыка), 0 «Играет» (просто пользуется ShyFlow)
internal sealed record Presence(string Details, string State, long Start, long End, string? LargeImage, string? LargeText, int Type = 2);

// Discord Rich Presence по локальному каналу discord-ipc-N, без сторонних библиотек.
// Нужен Application ID приложения с discord.com/developers (AppInfo.DiscordAppId).
internal sealed class DiscordRpc
{
    private readonly object _lock = new();
    private NamedPipeClientStream? _pipe;
    private string _appId = "";
    private volatile bool _ready;
    private DateTime _nextTry = DateTime.MinValue;
    private Presence? _last;
    private bool _lastSet;

    private bool Connected => _pipe is { IsConnected: true };

    // вызывается из фонового потока; сам решает, что и когда отправлять
    public void Update(string appId, Presence? p)
    {
        lock (_lock)
        {
            try
            {
                if (!EnsureConnected(appId) || !_ready) return;
                if (_lastSet && Same(_last, p)) return;
                Write(1, BuildJson(p));
                _last = p;
                _lastSet = true;
            }
            catch { CloseLocked(); }
        }
    }

    public void Shutdown()
    {
        lock (_lock)
        {
            try { if (Connected && _ready) Write(1, BuildJson(null)); } catch { }
            CloseLocked();
        }
    }

    private static bool Same(Presence? a, Presence? b)
    {
        if (a == null || b == null) return a == null && b == null;
        return a.Type == b.Type && a.Details == b.Details && a.State == b.State && a.LargeImage == b.LargeImage
            && (a.Start == 0) == (b.Start == 0) && Math.Abs(a.Start - b.Start) <= 3 && Math.Abs(a.End - b.End) <= 3;
    }

    private bool EnsureConnected(string appId)
    {
        if (Connected && _appId == appId) return true;
        CloseLocked();
        if (DateTime.UtcNow < _nextTry) return false;
        _nextTry = DateTime.UtcNow.AddSeconds(10);
        for (int i = 0; i < 10; i++)
        {
            NamedPipeClientStream? p = null;
            try
            {
                p = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                p.Connect(150);
                _pipe = p;
                _appId = appId;
                _ready = false;
                Write(0, JsonSerializer.Serialize(new { v = 1, client_id = appId }));
                var keep = p;
                _ = Task.Run(() => ReadLoop(keep));
                return true;
            }
            catch
            {
                try { p?.Dispose(); } catch { }
                _pipe = null;
            }
        }
        return false;
    }

    private void CloseLocked()
    {
        _ready = false;
        _lastSet = false;
        _last = null;
        var p = _pipe;
        _pipe = null;
        try { p?.Dispose(); } catch { }
    }

    private void Write(int op, string json)
    {
        var data = Encoding.UTF8.GetBytes(json);
        var buf = new byte[8 + data.Length];
        BitConverter.GetBytes(op).CopyTo(buf, 0);
        BitConverter.GetBytes(data.Length).CopyTo(buf, 4);
        data.CopyTo(buf, 8);
        var p = _pipe ?? throw new IOException();
        p.Write(buf, 0, buf.Length);
        p.Flush();
    }

    private static async Task<bool> ReadExact(Stream s, byte[] buf, int count)
    {
        int got = 0;
        while (got < count)
        {
            int n = await s.ReadAsync(buf.AsMemory(got, count - got));
            if (n <= 0) return false;
            got += n;
        }
        return true;
    }

    private async Task ReadLoop(NamedPipeClientStream p)
    {
        var hdr = new byte[8];
        try
        {
            while (p.IsConnected)
            {
                if (!await ReadExact(p, hdr, 8)) break;
                int op = BitConverter.ToInt32(hdr, 0), len = BitConverter.ToInt32(hdr, 4);
                if (len < 0 || len > (1 << 20)) break;
                var body = new byte[len];
                if (len > 0 && !await ReadExact(p, body, len)) break;
                if (op == 2) break;                                   // Discord закрыл канал
                string text = Encoding.UTF8.GetString(body);
                if (op == 3) { lock (_lock) { if (ReferenceEquals(_pipe, p)) Write(4, text); } }   // ping: отвечаем pong
                else if (op == 1 && text.Contains("\"READY\"")) _ready = true;
            }
        }
        catch { }
        lock (_lock) { if (ReferenceEquals(_pipe, p)) CloseLocked(); }
    }

    private static string Clip(string s)
    {
        s = (s ?? "").Trim();
        if (s.Length < 2) s = s.PadRight(2);
        return s.Length > 120 ? s[..120] : s;
    }

    private static string BuildJson(Presence? p)
    {
        object? act = null;
        if (p != null)
        {
            var a = new Dictionary<string, object?>
            {
                ["type"] = p.Type,   // 2 «Слушает», 0 «Играет»
                ["details"] = Clip(p.Details),
                ["state"] = Clip(p.State),
            };
            if (p.Start > 0)
            {
                var ts = new Dictionary<string, object?> { ["start"] = p.Start };
                if (p.End > p.Start) ts["end"] = p.End;
                a["timestamps"] = ts;
            }
            if (!string.IsNullOrEmpty(p.LargeImage))
                a["assets"] = new Dictionary<string, object?> { ["large_image"] = p.LargeImage, ["large_text"] = Clip(p.LargeText ?? "ShyFlow") };
            act = a;
        }
        var payload = new Dictionary<string, object?>
        {
            ["cmd"] = "SET_ACTIVITY",
            ["args"] = new Dictionary<string, object?> { ["pid"] = Environment.ProcessId, ["activity"] = act },
            ["nonce"] = Guid.NewGuid().ToString(),
        };
        return JsonSerializer.Serialize(payload);
    }
}
