using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Island;

// Файл на телефон по QR (шаг 66). Маленький HTTP-сервер в локальной сети: только на время раздачи, только расшаренные файлы.
//   Ссылка вида http://192.168.1.5:PORT/s/ТОКЕН: случайный токен 128 бит, живёт 10 минут и до 5 скачиваний.
//   По ссылке страница с именем, размером и кнопкой «Скачать»; файл отдаётся по /s/ТОКЕН/dl.
//   Нет раздач — сервер остановлен и порт закрыт. Windows при первом запуске спросит про брандмауэр: нужна «Частная сеть».
internal static class LanShare
{
    internal sealed class Item
    {
        public required string Token, Path, Name;
        public long Size;
        public DateTime Until;
        public int Downloads;
    }

    public const int MaxDownloads = 5;
    public static readonly TimeSpan Life = TimeSpan.FromMinutes(10);
    private static readonly Dictionary<string, Item> _items = new();
    private static TcpListener? _listener;
    private static CancellationTokenSource? _cts;
    private static int _port;

    // файл скачан целиком (на телефон), вызывается не из потока окна
    public static event Action<Item>? Downloaded;

    public static string? Share(string path, out Item? item)
    {
        item = null;
        if (!File.Exists(path)) return null;
        string? ip = LanIp();
        if (ip == null) return null;
        lock (_items)
        {
            Cleanup();
            if (_listener == null && !StartServer()) return null;
            var t = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            item = new Item { Token = t, Path = path, Name = System.IO.Path.GetFileName(path), Size = new FileInfo(path).Length, Until = DateTime.UtcNow + Life };
            _items[t] = item;
        }
        return $"http://{ip}:{_port}/s/{item.Token}";
    }

    public static void Stop(string token)
    {
        lock (_items) { _items.Remove(token); Cleanup(); }
    }

    // просроченные убираем; раздач не осталось — сервер выключается
    private static void Cleanup()
    {
        foreach (var k in _items.Where(kv => kv.Value.Until < DateTime.UtcNow || kv.Value.Downloads >= MaxDownloads).Select(kv => kv.Key).ToList())
            _items.Remove(k);
        if (_items.Count == 0 && _listener != null)
        {
            _cts?.Cancel();
            try { _listener.Stop(); } catch { }
            _listener = null;
        }
    }

    // адрес компьютера в домашней сети: включённый Wi-Fi или Ethernet со шлюзом, без виртуальных адаптеров
    private static string? LanIp()
    {
        try
        {
            var best = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet)
                .Where(n => !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) && !n.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) && !n.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase))
                .Select(n => (n, p: n.GetIPProperties()))
                .Where(x => x.p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
                .OrderBy(x => x.n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1)
                .SelectMany(x => x.p.UnicastAddresses)
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address));
            return best?.Address.ToString();
        }
        catch (Exception ex) { App.LogOnce("LanShare.Ip", ex); return null; }
    }

    private static bool StartServer()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, 0);
            _listener.Start();
            _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _cts = new CancellationTokenSource();
            var l = _listener;
            var ct = _cts.Token;
            _ = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    TcpClient c;
                    try { c = await l.AcceptTcpClientAsync(ct); } catch { break; }
                    _ = Task.Run(() => Serve(c));
                }
            });
            return true;
        }
        catch (Exception ex) { App.LogOnce("LanShare.Start", ex); _listener = null; return false; }
    }

    private static async Task Serve(TcpClient c)
    {
        using (c)
        {
            try
            {
                c.ReceiveTimeout = c.SendTimeout = 15000;
                var s = c.GetStream();
                // первая строка запроса: «GET /s/ТОКЕН[/dl] HTTP/1.1»; заголовки дочитываем до пустой строки
                var buf = new byte[4096];
                int n = 0;
                while (n < buf.Length)
                {
                    int r = await s.ReadAsync(buf.AsMemory(n));
                    if (r <= 0) break;
                    n += r;
                    if (Encoding.ASCII.GetString(buf, 0, n).Contains("\r\n\r\n")) break;
                }
                string req = Encoding.ASCII.GetString(buf, 0, n);
                var parts = req.Split(' ', 3);
                if (parts.Length < 2 || parts[0] != "GET") { await Text(s, 405, "Method Not Allowed"); return; }
                var seg = parts[1].Split('?')[0].Trim('/').Split('/');
                Item? it = null;
                lock (_items)
                {
                    Cleanup();
                    if (seg.Length >= 2 && seg[0] == "s") _items.TryGetValue(seg[1], out it);
                }
                if (it == null) { await Html(s, 404, Page("Ссылка не работает", "Срок вышел или раздачу остановили. Кинь файл на остров ещё раз.", null)); return; }
                if (seg.Length == 2) { await Html(s, 200, Page(it.Name, Size(it.Size), $"/s/{it.Token}/dl")); return; }
                if (seg.Length == 3 && seg[2] == "dl") { await SendFile(s, it); return; }
                await Text(s, 404, "Not Found");
            }
            catch { /* телефон закрыл соединение: ничего страшного */ }
        }
    }

    private static async Task SendFile(NetworkStream s, Item it)
    {
        string enc = Uri.EscapeDataString(it.Name);
        string ascii = new string(it.Name.Select(ch => ch < 128 && ch != '"' ? ch : '_').ToArray());
        string head = "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\n" +
                      $"Content-Length: {it.Size}\r\nContent-Disposition: attachment; filename=\"{ascii}\"; filename*=UTF-8''{enc}\r\n" +
                      "Cache-Control: no-store\r\nConnection: close\r\n\r\n";
        await s.WriteAsync(Encoding.UTF8.GetBytes(head));
        await using (var f = new FileStream(it.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, true))
            await f.CopyToAsync(s);
        await s.FlushAsync();
        lock (_items) it.Downloads++;
        Downloaded?.Invoke(it);
    }

    private static string Size(long b) => b >= 1_000_000_000 ? (b / 1e9).ToString("0.0") + " ГБ" : b >= 1_000_000 ? (b / 1e6).ToString("0.0") + " МБ" : Math.Max(1, b / 1000) + " КБ";

    private static string Page(string title, string sub, string? link)
    {
        static string E(string x) => WebUtility.HtmlEncode(x);
        return "<!doctype html><html lang=ru><head><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'>" +
               "<title>ShyFlow</title><style>body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:#000;color:#fff;" +
               "font-family:-apple-system,Segoe UI,Roboto,sans-serif}.c{background:#1c1c1e;border-radius:28px;padding:32px 26px;width:min(86vw,360px);text-align:center}" +
               "h1{font-size:19px;margin:0 0 6px;word-break:break-all}p{color:#98989d;margin:0 0 24px;font-size:15px}" +
               "a{display:block;background:#fff;color:#000;text-decoration:none;font-weight:600;font-size:17px;padding:15px;border-radius:16px}" +
               "small{display:block;color:#636366;margin-top:18px;font-size:12px}</style></head><body><div class=c>" +
               $"<h1>{E(title)}</h1><p>{E(sub)}</p>" + (link != null ? $"<a href='{link}'>Скачать</a>" : "") +
               "<small>ShyFlow · только в этой Wi-Fi сети</small></div></body></html>";
    }

    private static Task Html(NetworkStream s, int code, string html) => Reply(s, code, "text/html; charset=utf-8", html);
    private static Task Text(NetworkStream s, int code, string text) => Reply(s, code, "text/plain; charset=utf-8", text);

    private static async Task Reply(NetworkStream s, int code, string type, string body)
    {
        var b = Encoding.UTF8.GetBytes(body);
        string head = $"HTTP/1.1 {code} {(code == 200 ? "OK" : "Error")}\r\nContent-Type: {type}\r\nContent-Length: {b.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await s.WriteAsync(Encoding.ASCII.GetBytes(head));
        await s.WriteAsync(b);
    }
}
