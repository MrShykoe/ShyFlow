using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Island;

// Стикер справа в развёрнутом острове: гифка, картинка (PNG, JPG, WEBP, BMP...) или видео (MP4, MOV, WEBM, MKV...),
// по желанию без фона. WPF сам гифки не анимирует и видео в маленький кусочек не вырезает, поэтому всё приводится к одному виду:
// набору кадров до 160 px по длинной стороне. Гифка склеивается с учётом disposal, у видео берутся кадры через Windows.Media.Editing
// (первые 10 секунд, 12 кадров в секунду, звук не нужен), у картинки один кадр.
// Вырезание фона считается по готовым кадрам, поэтому смена цвета или силы в настройках файл не перечитывает.
public partial class MainWindow
{
    private sealed class GifRaw
    {
        public int W, H;
        public readonly List<byte[]> Frames = new();   // Pbgra32, W x H
        public readonly List<int> Delays = new();      // мс
    }

    private const int GifMaxSide = 200;   // 200, а не 160: стикер виден ещё и на обороте обложки в плеере на весь экран
    private const int GifMaxFrames = 240;
    private const double VideoFps = 12, VideoMaxSeconds = 10;
    private static readonly HashSet<string> VideoExt = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm" };

    private GifRaw? _gifRaw;
    private string _gifFileSig = "", _gifKeySig = "", _gifLoading = "", _gifBad = "";   // _gifBad: файл, который не открылся (не читаем его снова на каждое изменение настроек)
    private int _gifGen;
    private List<BitmapSource> _gifFrames = new();
    private List<int> _gifDelays = new();
    private int _gifIdx;
    private double _gifW;                 // сколько места гифка занимает в верхней строке развёрнутого острова
    private DispatcherTimer? _gifTimer;

    // вызывается из ApplyFeatureSettings при любом изменении настроек
    private void ApplyGif()
    {
        string path = _cfg.GifPath ?? "";
        bool want = _cfg.GifEnabled && path.Length > 0 && File.Exists(path);
        if (!want) { GifOff(); return; }

        string fileSig = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
        string keySig = KeySig();

        if (_gifRaw != null && fileSig == _gifFileSig)
        {
            if (keySig != _gifKeySig) { _gifKeySig = keySig; BuildGifFrames(); }
            return;
        }
        if (fileSig == _gifLoading || fileSig == _gifBad) return;   // этот файл уже читается или не открылся

        _gifLoading = fileSig;
        int gen = ++_gifGen;
        Task.Run(() => DecodeMedia(path)).ContinueWith(t =>
        {
            var raw = t.IsCompletedSuccessfully ? t.Result : null;
            Dispatcher.BeginInvoke(() =>
            {
                if (gen != _gifGen) return;   // за это время выбрали другую гифку или выключили
                _gifLoading = "";
                if (raw == null || raw.Frames.Count == 0)
                {
                    GifOff();
                    _gifBad = fileSig;
                    // сообщение только если человек прямо сейчас выбирает файл в настройках (иначе при каждом запуске)
                    if (_settingsWin != null)
                        MacDialog.Info(_settingsWin, Loc.T("Не удалось открыть файл. Возможно, этот формат не поддерживается вашей версией Windows"));
                    return;
                }
                _gifRaw = raw;
                _gifFileSig = fileSig;
                _gifKeySig = KeySig();
                BuildGifFrames();
            });
        });
    }

    private string KeySig() => _cfg.GifRemoveBg ? $"{_cfg.GifKeyColor}|{_cfg.GifTolerance}|{_cfg.GifHoles}" : "";

    private void GifOff()
    {
        _gifGen++;
        _gifLoading = "";
        _gifTimer?.Stop();
        _gifRaw = null;
        _gifFileSig = _gifKeySig = "";
        _gifFrames = new();
        _gifDelays = new();
        FullGif.Source = null;
        FullGif.Visibility = Visibility.Collapsed;
        if (_gifW != 0)
        {
            _gifW = 0;
            UpdateWidths(MiniTitle.Text, ArtistText.Text);
        }
    }

    private void BuildGifFrames()
    {
        var raw = _gifRaw;
        if (raw == null || raw.Frames.Count == 0) return;

        int[][]? keys = null;
        double tol = 0, feather = 0;
        bool holes = false;
        if (_cfg.GifRemoveBg)
        {
            (tol, feather) = _cfg.GifTolerance switch { 0 => (24.0, 22.0), 2 => (80.0, 40.0), _ => (48.0, 30.0) };
            holes = _cfg.GifHoles;
            keys = GifKeyColors(raw);   // null: края уже прозрачные, вырезать нечего
        }

        int count = raw.Frames.Count;
        var pixels = new byte[count][];
        if (keys != null && keys.Length > 0)
        {
            var k2 = keys; double t2 = tol, f2 = feather; bool h2 = holes;
            int W = raw.W, H = raw.H;
            Parallel.For(0, count, i =>
            {
                var c = (byte[])raw.Frames[i].Clone();
                CutBackground(c, W, H, k2, t2, f2, h2);
                pixels[i] = c;
            });
        }
        else for (int i = 0; i < count; i++) pixels[i] = raw.Frames[i];

        var frames = new List<BitmapSource>(count);
        for (int i = 0; i < count; i++)
        {
            var bs = BitmapSource.Create(raw.W, raw.H, 96, 96, PixelFormats.Pbgra32, null, pixels[i], raw.W * 4);
            bs.Freeze();
            frames.Add(bs);
        }
        _gifFrames = frames;
        _gifDelays = raw.Delays;
        _gifIdx = 0;

        double dh = 64;
        double dw = Math.Clamp(dh * raw.W / raw.H, 24, 96);
        FullGif.Width = dw;
        FullGif.Height = dh;
        FullGif.Source = frames[0];
        FullGif.Visibility = Visibility.Visible;

        double w = dw + 10;
        if (Math.Abs(w - _gifW) > 0.5)
        {
            _gifW = w;
            UpdateWidths(MiniTitle.Text, ArtistText.Text);   // остров раздвигается под гифку
        }

        _gifTimer ??= MakeGifTimer();
        _gifTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(20, _gifDelays[0]));
        if (frames.Count > 1) _gifTimer.Start(); else _gifTimer.Stop();
    }

    private DispatcherTimer MakeGifTimer()
    {
        var t = new DispatcherTimer(DispatcherPriority.Render);
        t.Tick += (_, _) =>
        {
            if (_gifFrames.Count < 2) { t.Stop(); return; }
            // остров свёрнут или спрятан: кадры не меняем, только изредка проверяем
            if (_mode != Mode.Full || !IsVisible) { t.Interval = TimeSpan.FromMilliseconds(250); return; }
            _gifIdx = (_gifIdx + 1) % _gifFrames.Count;
            FullGif.Source = _gifFrames[_gifIdx];
            t.Interval = TimeSpan.FromMilliseconds(Math.Max(20, _gifDelays[_gifIdx]));
        };
        return t;
    }

    // ---------- цвет, который вырезаем ----------
    private int[][]? GifKeyColors(GifRaw raw)
    {
        string k = (_cfg.GifKeyColor ?? "auto").Trim();
        if (!k.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(k);
                return new[] { new[] { (int)c.R, (int)c.G, (int)c.B } };
            }
            catch { /* кривой HEX: ниже возьмём цвет из рамки картинки */ }
        }

        // авто: смотрим на рамку кадра (два пикселя толщиной) в первом и среднем кадре
        int W = raw.W, H = raw.H;
        var samples = new List<(int r, int g, int b)>();
        int total = 0, clear = 0;
        var seen = new HashSet<int>();
        foreach (int fi in new[] { 0, raw.Frames.Count / 2 })
        {
            if (!seen.Add(fi)) continue;
            var f = raw.Frames[fi];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (x >= 2 && x < W - 2 && y >= 2 && y < H - 2) { x = W - 3; continue; }   // внутри рамки: перескакиваем
                    total++;
                    int o = (y * W + x) * 4, a = f[o + 3];
                    if (a < 24) { clear++; continue; }
                    samples.Add((Math.Min(255, f[o + 2] * 255 / a), Math.Min(255, f[o + 1] * 255 / a), Math.Min(255, f[o] * 255 / a)));
                }
        }
        if (total == 0 || samples.Count == 0 || clear > total * 0.6) return null;

        var result = new List<int[]>();
        var rest = samples;
        for (int round = 0; round < 2 && rest.Count > 0; round++)
        {
            // самый частый цвет: корзины по 16 уровней, потом среднее по близким пикселям
            var hist = new Dictionary<int, int>();
            foreach (var (r, g, b) in rest)
            {
                int key = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
                hist[key] = hist.TryGetValue(key, out var n0) ? n0 + 1 : 1;
            }
            var top = hist.OrderByDescending(p => p.Value).First();
            int tr = ((top.Key >> 8) & 15) * 16 + 8, tg = ((top.Key >> 4) & 15) * 16 + 8, tb = (top.Key & 15) * 16 + 8;
            long sr = 0, sg = 0, sb = 0; int cnt = 0;
            foreach (var (r, g, b) in rest)
                if (ColorDist(r, g, b, tr, tg, tb) < 40) { sr += r; sg += g; sb += b; cnt++; }
            if (cnt == 0) break;
            // второй цвет берём, только если он заметно часто встречается (например, шахматный фон)
            if (round == 1 && cnt < samples.Count * 0.18) break;
            var mean = new[] { (int)(sr / cnt), (int)(sg / cnt), (int)(sb / cnt) };
            result.Add(mean);
            rest = rest.Where(p => ColorDist(p.r, p.g, p.b, mean[0], mean[1], mean[2]) >= 60).ToList();
        }
        return result.Count > 0 ? result.ToArray() : null;
    }

    // «красное среднее»: ближе к тому, как глаз видит разницу цветов, чем простое расстояние по RGB
    private static double ColorDist(int r1, int g1, int b1, int r2, int g2, int b2)
    {
        double rm = (r1 + r2) / 2.0;
        double dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
        return Math.Sqrt((2 + rm / 256) * dr * dr + 4 * dg * dg + (2 + (255 - rm) / 256) * db * db) / 2.0;
    }

    // Вырезание фона. Раньше каждый пиксель сравнивался с цветом отдельно: внутри фигуры дырявились такие же цвета
    // (белки глаз), а по краю оставалась светлая или тёмная «каёмка». Теперь:
    //  1) фон ищется заливкой от краёв кадра: вырезается только то, что соединено с краем (holes = true: и внутри тоже);
    //  2) на границе пиксели получают плавную прозрачность по похожести на фон;
    //  3) у граничных пикселей из цвета вычитается примесь фона, поэтому каёмки нет;
    //  4) прозрачность чуть сглаживается, края без «ступенек».
    private static void CutBackground(byte[] px, int W, int H, int[][] keys, double tol, double feather, bool holes)
    {
        int n = W * H;
        var dist = new float[n];
        var kidx = new byte[n];
        for (int i = 0; i < n; i++)
        {
            int o = i * 4, a = px[o + 3];
            if (a < 24) { dist[i] = 0; continue; }   // уже прозрачное считается фоном
            int r = Math.Min(255, px[o + 2] * 255 / a), g = Math.Min(255, px[o + 1] * 255 / a), b = Math.Min(255, px[o] * 255 / a);
            double best = double.MaxValue; int bi = 0;
            for (int k = 0; k < keys.Length; k++)
            {
                double d = ColorDist(r, g, b, keys[k][0], keys[k][1], keys[k][2]);
                if (d < best) { best = d; bi = k; }
            }
            dist[i] = (float)best;
            kidx[i] = (byte)bi;
        }

        // 1) ядро фона
        var core = new bool[n];
        if (holes)
        {
            for (int i = 0; i < n; i++) core[i] = dist[i] <= tol;
        }
        else
        {
            var stack = new int[n];
            int sp = 0;
            void Push(int i)
            {
                if (!core[i] && dist[i] <= tol) { core[i] = true; stack[sp++] = i; }
            }
            for (int x = 0; x < W; x++) { Push(x); Push((H - 1) * W + x); }
            for (int y = 0; y < H; y++) { Push(y * W); Push(y * W + W - 1); }
            while (sp > 0)
            {
                int i = stack[--sp];
                int x = i % W, y = i / W;
                if (x > 0) Push(i - 1);
                if (x < W - 1) Push(i + 1);
                if (y > 0) Push(i - W);
                if (y < H - 1) Push(i + W);
            }
        }

        // 2) кромка
        var alpha = new float[n];
        var ring = new bool[n];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                if (core[i]) continue;
                alpha[i] = 1f;
                bool near4 = (x > 0 && core[i - 1]) || (x < W - 1 && core[i + 1]) || (y > 0 && core[i - W]) || (y < H - 1 && core[i + W]);
                bool near8 = near4;
                if (!near8)
                    for (int dy = -1; dy <= 1 && !near8; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx >= W || yy >= H) continue;
                            if (core[yy * W + xx]) { near8 = true; break; }
                        }
                if (!near8) continue;
                if (dist[i] < tol + feather)
                {
                    alpha[i] = (float)Math.Clamp((dist[i] - tol) / feather, 0, 1);
                    ring[i] = true;
                }
                else if (near4) alpha[i] = 0.7f;   // вплотную к фону: слегка «подъедаем» край
            }

        // 4) сглаживание прозрачности ([1 2 1] по строкам и столбцам), ядро фона остаётся полностью прозрачным
        var tmp = new float[n];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                float l = x > 0 ? alpha[i - 1] : alpha[i], r = x < W - 1 ? alpha[i + 1] : alpha[i];
                tmp[i] = (l + 2 * alpha[i] + r) * 0.25f;
            }
        var sm = new float[n];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                float u = y > 0 ? tmp[i - W] : tmp[i], d = y < H - 1 ? tmp[i + W] : tmp[i];
                sm[i] = core[i] ? 0f : (u + 2 * tmp[i] + d) * 0.25f;
            }

        // 3) итоговые пиксели
        for (int i = 0; i < n; i++)
        {
            int o = i * 4;
            int a0 = px[o + 3];
            float a = sm[i];
            if (a <= 0.004f || a0 == 0) { px[o] = px[o + 1] = px[o + 2] = px[o + 3] = 0; continue; }
            double r = px[o + 2] * 255.0 / a0, g = px[o + 1] * 255.0 / a0, b = px[o] * 255.0 / a0;
            if (ring[i] && alpha[i] > 0.05f)
            {
                // убираем примесь цвета фона: c = (c - (1 - a) * key) / a
                var kc = keys[kidx[i]];
                double ia = 1.0 - alpha[i], aa = alpha[i];
                r = Math.Clamp((r - ia * kc[0]) / aa, 0, 255);
                g = Math.Clamp((g - ia * kc[1]) / aa, 0, 255);
                b = Math.Clamp((b - ia * kc[2]) / aa, 0, 255);
            }
            double fa = a * a0 / 255.0;
            px[o] = (byte)Math.Round(b * fa);
            px[o + 1] = (byte)Math.Round(g * fa);
            px[o + 2] = (byte)Math.Round(r * fa);
            px[o + 3] = (byte)Math.Round(255 * fa);
        }
    }

    // ---------- чтение файла (в фоне) ----------
    // maxSide и maxFrames: стикеру хватает 200 px, фону острова (IslandBg.cs) нужно крупнее
    private static async Task<GifRaw?> DecodeMedia(string path, int maxSide = GifMaxSide, int maxFrames = GifMaxFrames)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (ext == ".gif") return DecodeGif(path, maxSide, maxFrames);
            if (VideoExt.Contains(ext)) return await DecodeVideoAsync(path, maxSide);
            return DecodeImage(path, maxSide);
        }
        catch (Exception ex)
        {
            App.Log("Sticker", ex);
            return null;
        }
    }

    // обычная картинка: один кадр
    private static GifRaw? DecodeImage(string path, int maxSide)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (dec.Frames.Count == 0) return null;
            BitmapSource src = dec.Frames[0];
            double sc = Math.Min(1.0, (double)maxSide / Math.Max(src.PixelWidth, src.PixelHeight));
            if (sc < 1.0) src = new TransformedBitmap(src, new ScaleTransform(sc, sc));
            var conv = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight;
            if (w <= 0 || h <= 0) return null;
            var px = new byte[w * h * 4];
            conv.CopyPixels(px, w * 4, 0);
            var raw = new GifRaw { W = w, H = h };
            raw.Frames.Add(px);
            raw.Delays.Add(1000);
            return raw;
        }
        catch (Exception ex)
        {
            App.Log("Sticker image", ex);
            return null;
        }
    }

    // видео: кадры через Windows.Media.Editing (понимает всё, что умеет проигрыватель Windows)
    private static async Task<GifRaw?> DecodeVideoAsync(string path, int maxSide)
    {
        try
        {
            var file = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            var clip = await global::Windows.Media.Editing.MediaClip.CreateFromFileAsync(file);
            var vp = clip.GetVideoEncodingProperties();
            int vw = (int)vp.Width, vh = (int)vp.Height;
            if (vw <= 0 || vh <= 0) return null;
            double sc = Math.Min(1.0, (double)maxSide / Math.Max(vw, vh));
            int ow = Math.Max(2, (int)Math.Round(vw * sc)), oh = Math.Max(2, (int)Math.Round(vh * sc));

            double durMs = Math.Max(1, clip.OriginalDuration.TotalMilliseconds);
            double totalMs = Math.Min(durMs, VideoMaxSeconds * 1000);
            int n = Math.Max(1, (int)Math.Round(totalMs / 1000.0 * VideoFps));
            n = Math.Min(n, (int)(VideoMaxSeconds * VideoFps));
            double stepMs = totalMs / n;

            var comp = new global::Windows.Media.Editing.MediaComposition();
            comp.Clips.Add(clip);

            var raw = new GifRaw();
            for (int i = 0; i < n; i++)
            {
                var t = TimeSpan.FromMilliseconds(Math.Min(durMs - 1, i * stepMs));
                using var st = await comp.GetThumbnailAsync(t, ow, oh, global::Windows.Media.Editing.VideoFramePrecision.NearestFrame);
                uint size = (uint)st.Size;
                var buf = new global::Windows.Storage.Streams.Buffer(size);
                await st.ReadAsync(buf, size, global::Windows.Storage.Streams.InputStreamOptions.None);
                var bytes = new byte[buf.Length];
                using (var dr = global::Windows.Storage.Streams.DataReader.FromBuffer(buf)) dr.ReadBytes(bytes);

                using var ms = new MemoryStream(bytes);
                var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var conv = new FormatConvertedBitmap(dec.Frames[0], PixelFormats.Pbgra32, null, 0);
                if (raw.W == 0) { raw.W = conv.PixelWidth; raw.H = conv.PixelHeight; }
                if (conv.PixelWidth != raw.W || conv.PixelHeight != raw.H) continue;
                var px = new byte[raw.W * raw.H * 4];
                conv.CopyPixels(px, raw.W * 4, 0);
                raw.Frames.Add(px);
                raw.Delays.Add(Math.Max(40, (int)Math.Round(stepMs)));
            }
            return raw.Frames.Count > 0 ? raw : null;
        }
        catch (Exception ex)
        {
            App.Log("Sticker video", ex);
            return null;
        }
    }

    private static int Q(BitmapMetadata? m, string q, int def)
    {
        try
        {
            var o = m?.GetQuery(q);
            if (o != null) return Convert.ToInt32(o);
        }
        catch { }
        return def;
    }

    private static GifRaw? DecodeGif(string path, int maxSide, int maxFrames)
    {
        try
        {
            using var ms = new MemoryStream(File.ReadAllBytes(path));
            var dec = new GifBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            int n = dec.Frames.Count;
            if (n == 0) return null;

            int W = 0, H = 0;
            try
            {
                W = Convert.ToInt32(dec.Metadata.GetQuery("/logscrdesc/Width"));
                H = Convert.ToInt32(dec.Metadata.GetQuery("/logscrdesc/Height"));
            }
            catch { }
            if (W <= 0 || H <= 0) { W = dec.Frames[0].PixelWidth; H = dec.Frames[0].PixelHeight; }

            double sc = Math.Min(1.0, (double)maxSide / Math.Max(W, H));
            int ow = Math.Max(1, (int)Math.Round(W * sc)), oh = Math.Max(1, (int)Math.Round(H * sc));
            var raw = new GifRaw { W = ow, H = oh };

            var canvas = new byte[W * H * 4];
            int step = (n + maxFrames - 1) / maxFrames;   // слишком длинные гифки прореживаем

            for (int i = 0; i < n; i++)
            {
                var fr = dec.Frames[i];
                var md = fr.Metadata as BitmapMetadata;
                int left = Q(md, "/imgdesc/Left", 0), top = Q(md, "/imgdesc/Top", 0);
                int fw = fr.PixelWidth, fh = fr.PixelHeight;
                int delay = Q(md, "/grctlext/Delay", 10) * 10;
                if (delay < 20) delay = 100;   // как в браузерах: 0 и 10 мс означают «обычная»
                int disp = Q(md, "/grctlext/Disposal", 0);

                var conv = new FormatConvertedBitmap(fr, PixelFormats.Bgra32, null, 0);
                var fb = new byte[fw * fh * 4];
                conv.CopyPixels(fb, fw * 4, 0);

                byte[]? saved = disp == 3 ? (byte[])canvas.Clone() : null;

                for (int y = 0; y < fh; y++)
                {
                    int cy = top + y;
                    if (cy < 0 || cy >= H) continue;
                    for (int x = 0; x < fw; x++)
                    {
                        int cx = left + x;
                        if (cx < 0 || cx >= W) continue;
                        int s = (y * fw + x) * 4;
                        if (fb[s + 3] == 0) continue;   // прозрачный пиксель кадра оставляет то, что было
                        int d = (cy * W + cx) * 4;
                        canvas[d] = fb[s]; canvas[d + 1] = fb[s + 1]; canvas[d + 2] = fb[s + 2]; canvas[d + 3] = fb[s + 3];
                    }
                }

                if (i % step == 0) { raw.Frames.Add(ScaleToPm(canvas, W, H, ow, oh)); raw.Delays.Add(delay); }
                else raw.Delays[^1] += delay;

                if (disp == 2)
                {
                    for (int y = 0; y < fh; y++)
                    {
                        int cy = top + y;
                        if (cy < 0 || cy >= H) continue;
                        for (int x = 0; x < fw; x++)
                        {
                            int cx = left + x;
                            if (cx < 0 || cx >= W) continue;
                            int d = (cy * W + cx) * 4;
                            canvas[d] = canvas[d + 1] = canvas[d + 2] = canvas[d + 3] = 0;
                        }
                    }
                }
                else if (disp == 3 && saved != null) canvas = saved;
            }
            return raw;
        }
        catch { return null; }
    }

    // BGRA -> Pbgra32 с усреднением по площади (чтобы маленькая картинка не рябила)
    private static byte[] ScaleToPm(byte[] src, int W, int H, int ow, int oh)
    {
        var dst = new byte[ow * oh * 4];
        if (ow == W && oh == H)
        {
            for (int i = 0; i < src.Length; i += 4)
            {
                int a = src[i + 3];
                dst[i] = (byte)(src[i] * a / 255);
                dst[i + 1] = (byte)(src[i + 1] * a / 255);
                dst[i + 2] = (byte)(src[i + 2] * a / 255);
                dst[i + 3] = (byte)a;
            }
            return dst;
        }
        var acc = new int[ow * oh * 4];
        var cnt = new int[ow * oh];
        for (int y = 0; y < H; y++)
        {
            int dy = (int)((long)y * oh / H);
            for (int x = 0; x < W; x++)
            {
                int dx = (int)((long)x * ow / W);
                int s = (y * W + x) * 4;
                int a = src[s + 3];
                int o = dy * ow + dx;
                cnt[o]++;
                acc[o * 4] += src[s] * a / 255;
                acc[o * 4 + 1] += src[s + 1] * a / 255;
                acc[o * 4 + 2] += src[s + 2] * a / 255;
                acc[o * 4 + 3] += a;
            }
        }
        for (int o = 0; o < ow * oh; o++)
        {
            if (cnt[o] == 0) continue;
            for (int k = 0; k < 4; k++) dst[o * 4 + k] = (byte)(acc[o * 4 + k] / cnt[o]);
        }
        return dst;
    }
}
