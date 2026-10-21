using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Island;

// Слова песни: загрузка, подгонка времени по строкам, строка в свёрнутом острове.
public partial class MainWindow
{
    // ============================================================
    //  СЛОВА ПЕСНИ (поиск и проверка в Services\LyricsEngine.cs)
    // ============================================================
    private bool _lyricMode;
    private double _lyrOffset;                 // сдвиг текста для текущего трека, секунды (+ = текст позже)
    private Color _accent = Colors.White;      // цвет свечения: берётся с обложки
    private readonly Dictionary<string, double> _offsets = new();
    private static string OffsetsFile => Path.Combine(DataDir, "offsets.json");
    private string _curTitle = "", _curArtist = "";
    private LyricsResult? _lyrRes;                            // как текст пришёл из источника
    private List<(double t, string s)>? _lyr;                 // с подгонкой по строкам: им пользуются остров и плеер
    private List<WordTimes?>? _lyrWords;                      // время слов (от начала строки), того же размера, что _lyr
    private int _lyrIdx = -1;
    private readonly Dictionary<string, LyricsResult?> _lyrCache = new();
    private readonly DispatcherTimer _lyrTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private bool _upgradeHooked;

    // снимок позиции трека: читаем раз в 0,4 с, а между чтениями считаем сами
    private TimeSpan _snapPos, _snapStart;
    private DateTimeOffset _snapAt;
    private DateTime _snapRead = DateTime.MinValue;

    // слова текущей строки: кисть и окно времени, в которое слово «загорается»
    private readonly List<(double t0, double t1, double x0, double x1)> _words = new();
    private LinearGradientBrush? _wave;
    private double _waveW;
    private const double WaveFeather = 28; // мягкость края волны, px
    private static readonly Color DimC = Color.FromArgb(0x40, 255, 255, 255);

    private int _lyrGen; // номер текущей загрузки текста: старые загрузки не должны перебить новый трек

    private string TrackKey => _curArtist + "|" + _curTitle;

    // failed = true: сеть или сервер не ответили (или трек уже сменился); это не «текста нет», запоминать нельзя
    private async Task<(LyricsResult? res, bool failed)> FetchLyrics(string title, string artist, int gen)
    {
        var quick = LyricsEngine.PeekSynced(title, artist); // уже находили раньше: без ожидания и без интернета
        if (quick != null) return (quick, false);
        double dur = 0;
        string album = "";
        try
        {
            // сразу после смены трека Windows ещё может отдавать длительность прошлого трека:
            // ждём, пока значение не перестанет меняться (две одинаковые проверки подряд)
            await Task.Delay(400);
            double prev = -1;
            for (int i = 0; i < 6; i++)
            {
                var s = _session;
                if (s == null || gen != _lyrGen) return (null, true);
                var mp = await s.TryGetMediaPropertiesAsync();
                if ((mp?.Title ?? "") != title) return (null, true); // трек уже другой
                album = mp?.AlbumTitle ?? "";
                var tl = s.GetTimelineProperties();
                dur = (tl.EndTime - tl.StartTime).TotalSeconds;
                if (dur > 1 && Math.Abs(dur - prev) < 0.3) break;
                prev = dur;
                await Task.Delay(350);
            }
        }
        catch (Exception ex) { App.LogOnce("Lyrics.Fetch", ex); }
        if (gen != _lyrGen) return (null, true);
        try
        {
            return await LyricsEngine.GetDetailedAsync(title, artist, album, dur, _cfg.LyricsPlain, _cfg.LyricsExtra);
        }
        catch (Exception ex) { App.LogOnce("Lyrics.Search", ex); return (null, true); }
    }

    private async Task LoadLyricsAsync(string title, string artist)
    {
        if (!_cfg.Lyrics || string.IsNullOrWhiteSpace(title)) return;
        HookUpgrades();
        int gen = ++_lyrGen;
        string key = artist + "|" + title;
        _lyrOffset = _offsets.TryGetValue(key, out var savedOff) ? savedOff : 0;
        LyricsResult? res;
        if (!_lyrCache.TryGetValue(key, out res))
        {
            // если сеть не ответила, пробуем ещё два раза и «нет текста» не запоминаем
            int attempt = 0;
            while (true)
            {
                var r = await FetchLyrics(title, artist, gen);
                if (gen != _lyrGen) return;
                res = r.res;
                if (!r.failed) { _lyrCache[key] = res; break; }
                if (++attempt > 2) break;
                await Task.Delay(attempt == 1 ? 6000 : 15000);
                if (gen != _lyrGen) return;
            }
        }
        if (gen != _lyrGen || !_cfg.Lyrics || title != _curTitle || artist != _curArtist || _session == null) return; // трек уже сменился
        _lyrRes = res;
        ApplyLineSync();
        EnsureTranslation();
        if (_lyr != null) { _lyrIdx = -2; SetLyricMode(true); }
    }

    // текст по строкам уже на экране, а NetEase или Kugou нашли ту же песню со временем каждого слова: подменяем на лету
    private void HookUpgrades()
    {
        if (_upgradeHooked) return;
        _upgradeHooked = true;
        LyricsEngine.Upgraded += (title, artist, res) => Dispatcher.BeginInvoke(() =>
        {
            string key = artist + "|" + title;
            _lyrCache[key] = res;
            if (key != TrackKey || !_lyricMode || _lyrRes == null) return;
            _lyrRes = res;
            ApplyLineSync();
            EnsureTranslation();
            _lyrIdx = -2;
        });
    }

    // ============================================================
    //  ПОДГОНКА ВРЕМЕНИ ПО СТРОКАМ
    // ============================================================
    // Ctrl+Alt+, и Ctrl+Alt+. двигают весь текст. Этого мало, когда текст от другой версии трека: в начале совпадает,
    // а после длинного проигрыша уезжает. Правый клик по строке в плеере говорит «эта строка поётся сейчас»:
    // строка и все следующие за ней (до следующей подогнанной) сдвигаются так, чтобы начаться в эту секунду.
    // Подгонки хранятся по времени строки (не по номеру), поэтому переживают перезагрузку текста. Ctrl+Alt+0 сбрасывает всё.
    private static string LineSyncFile => Path.Combine(DataDir, "linesync.json");
    private Dictionary<string, Dictionary<string, double>>? _lineSync;

    private Dictionary<string, Dictionary<string, double>> LineSync
    {
        get
        {
            if (_lineSync != null) return _lineSync;
            try
            {
                if (File.Exists(LineSyncFile))
                    _lineSync = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double>>>(File.ReadAllText(LineSyncFile));
            }
            catch (Exception ex) { App.Log("LineSync.Load", ex); }
            return _lineSync ??= new();
        }
    }

    private void SaveLineSync()
    {
        try { SafeFile.WriteAllText(LineSyncFile, JsonSerializer.Serialize(LineSync)); }
        catch (Exception ex) { App.Log("LineSync.Save", ex); }
    }

    private static string LineKey(double t) => ((long)Math.Round(t * 1000)).ToString();

    // _lyr = строки из источника + подгонки этого трека
    private void ApplyLineSync()
    {
        var res = _lyrRes;
        if (res == null) { _lyr = null; _lyrWords = null; return; }
        var lines = res.Lines;
        var words = res.Words.Count == lines.Count ? res.Words : null;
        if (!LineSync.TryGetValue(TrackKey, out var anchors) || anchors.Count == 0)
        {
            _lyr = lines;
            _lyrWords = words;
            return;
        }
        var adj = new List<(double t, string s)>(lines.Count);
        double delta = 0, prev = double.MinValue;
        foreach (var (t, s) in lines)
        {
            if (anchors.TryGetValue(LineKey(t), out var d)) delta = d;
            double nt = Math.Max(0, t + delta);
            if (nt < prev + 0.05) nt = prev + 0.05;   // подгонки не должны перепутать порядок строк
            adj.Add((nt, s));
            prev = nt;
        }
        _lyr = adj;
        _lyrWords = words;
    }

    // «эта строка поётся сейчас»: clock = время трека в секундах (как его видит плеер)
    internal void SyncLineNow(int idx, double clock)
    {
        if (_lyrRes == null || idx < 0 || idx >= _lyrRes.Lines.Count) return;
        var orig = _lyrRes.Lines[idx];
        if (string.IsNullOrWhiteSpace(orig.s) || LyricsEngine.IsInterlude(orig.s)) return;
        double want = clock + 0.15 - _lyrOffset;           // так же, как текст сверяется со временем
        double d = Math.Round(want - orig.t, 2);
        if (!LineSync.TryGetValue(TrackKey, out var anchors)) LineSync[TrackKey] = anchors = new();
        anchors[LineKey(orig.t)] = d;
        SaveLineSync();
        ApplyLineSync();
        EnsureTranslation();
        _lyrIdx = -2;
        Tell("clock", Loc.T("Строка подогнана: ") + (d >= 0 ? "+" : "") + d.ToString("0.0#", Loc.Culture) + Loc.T(" с"));
    }

    private void ResetLineSync()
    {
        bool had = LineSync.Remove(TrackKey);
        if (had) SaveLineSync();
        if (_offsets.Remove(TrackKey))
        {
            try { SafeFile.WriteAllText(OffsetsFile, JsonSerializer.Serialize(_offsets)); }
            catch (Exception ex) { App.Log("Offsets.Save", ex); }
        }
        _lyrOffset = 0;
        ApplyLineSync();
        EnsureTranslation();
        _lyrIdx = -2;
        Tell("clock", "Текст: подгонка сброшена");
    }

    private void SetLyricMode(bool on)
    {
        if (on == _lyricMode && !on) return;
        _lyricMode = on;
        UpdateFullLyrVisibility();
        if (on)
        {
            _snapRead = DateTime.MinValue;
            _clkAt = DateTime.MinValue;
            LyrHook(true);
            LyricTick();
        }
        else
        {
            LyrHook(false);
            LettersOff();
            GlowOff(MiniGlow);
            GlowOff(FullGlow);
            MiniTitleOld.Opacity = 0;
            _lyr = null; _lyrRes = null; _lyrWords = null; _lyrIdx = -1; _words.Clear(); _wave = null;
            MiniTitle.Foreground = Brushes.White;
            MiniTitle.Inlines.Clear();
            if (!_clockMode) MiniTitle.Text = _curTitle;
            UpdateWidths(_curTitle, _curArtist);
        }
    }

    // Часы текста: свои, идут по реальному времени и лишь мягко подстраиваются под позицию из Windows.
    // Раньше позиция бралась из Windows как есть, и у браузеров и плееров, которые обновляют её рывками,
    // волна и строки дёргались взад-вперёд, а строка иногда проигрывала смену заново.
    private double _clkT;
    private DateTime _clkAt = DateTime.MinValue;
    private bool _lyrHooked;
    private double _waveA = -9, _waveB = -9;

    private void LyrHook(bool on)
    {
        if (on == _lyrHooked) return;
        _lyrHooked = on;
        if (on) CompositionTarget.Rendering += LyrFrame;
        else CompositionTarget.Rendering -= LyrFrame;
    }

    // каждый кадр экрана, а не раз в 50 мс: волна движется плавно
    private DateTime _lyrFrameAt;
    private TimeSpan _lyrRt;

    private void LyrFrame(object? s, EventArgs e)
    {
        if (!IsVisible || _hidden) return;
        // одно событие кадра может прийти несколько раз; в экономном режиме не чаще 30 раз в секунду
        if (e is RenderingEventArgs re) { if (re.RenderingTime == _lyrRt) return; _lyrRt = re.RenderingTime; }
        if (_eco)
        {
            var now = DateTime.UtcNow;
            if ((now - _lyrFrameAt).TotalMilliseconds < 31) return;
            _lyrFrameAt = now;
        }
        LyricTick();
    }

    private void LyricTick()
    {
        if (!_lyricMode || _lyr == null || _session == null) return;
        try
        {
            var now = DateTime.UtcNow;
            if (_clkAt != DateTime.MinValue && _playing) _clkT += Math.Min(0.25, (now - _clkAt).TotalSeconds);
            _clkAt = now;
            bool hard = _snapRead == DateTime.MinValue;
            if (hard || (now - _snapRead).TotalMilliseconds > 400)
            {
                var tl = _session.GetTimelineProperties();
                _snapPos = tl.Position; _snapStart = tl.StartTime; _snapAt = tl.LastUpdatedTime;
                _snapRead = now;
                var pos = _snapPos;
                if (_playing) pos += DateTimeOffset.Now - _snapAt;
                double meas = (pos - _snapStart).TotalSeconds;
                double d = meas - _clkT;
                if (hard || Math.Abs(d) > 0.7) _clkT = meas;   // перемотка или первое чтение: прыгаем
                else _clkT += d * 0.2;                         // обычный дрейф: подтягиваем понемногу
            }
            double t = _clkT + 0.15 - _lyrOffset;
            int idx = -1;
            for (int i = 0; i < _lyr.Count; i++) { if (_lyr[i].t <= t) idx = i; else break; }
            // крошечный откат назад не должен возвращать прошлую строку
            if (idx < _lyrIdx && _lyrIdx >= 0 && _lyrIdx < _lyr.Count && t > _lyr[_lyrIdx].t - 0.5) idx = _lyrIdx;
            if (idx != _lyrIdx) { _lyrIdx = idx; SetLine(idx); }
            if (_wave != null) UpdateWave(t);
            else if (_miniLetters) MiniSw.Update(t);
            FullLyrTick(idx, t);
        }
        catch (Exception ex) { App.LogOnce("Lyrics.Tick", ex); }
    }

    // Свечение: размытая копия строки цвета обложки лежит под текстом и кешируется как картинка.
    // Раньше тень висела на самом тексте, и при каждом шаге волны Windows заново считал размытие.
    private void GlowOn(TextBlock glow, TextBlock src, double op, double radius, bool animate)
    {
        glow.Text = src.Text;
        glow.FontSize = src.FontSize;
        glow.FontWeight = src.FontWeight;
        glow.MaxWidth = src.MaxWidth;
        glow.Foreground = new SolidColorBrush(_accent);
        if (glow.Effect is not BlurEffect be || Math.Abs(be.Radius - radius) > 0.1)
            glow.Effect = new BlurEffect { Radius = radius, RenderingBias = RenderingBias.Performance };
        if (animate) glow.BeginAnimation(OpacityProperty, new DoubleAnimation(0, op, TimeSpan.FromMilliseconds(500)));
        else { glow.BeginAnimation(OpacityProperty, null); glow.Opacity = op; }
    }

    private static void GlowOff(TextBlock glow)
    {
        glow.BeginAnimation(OpacityProperty, null);
        glow.Opacity = 0;
        glow.Effect = null;
        glow.Text = "";
    }

    // Волна: текст проявляется слева направо одной плавной полосой света, которая бежит вслед за голосом.
    // Точного времени слов в источниках нет, поэтому окно строки делится между словами по их длине.
    private void SetLine(int idx)
    {
        string prevText = MiniTitle.Text;
        _words.Clear();
        _wave = null;
        _waveA = _waveB = -9;
        MiniTitle.Inlines.Clear();
        MiniTitle.Foreground = Brushes.White;
        string line = idx >= 0 && _lyr != null ? Duet.Strip(_lyr[idx].s, _curArtist) : "";   // шаг 76: без метки голоса «v2:»
        if (string.IsNullOrWhiteSpace(line))
        {
            MiniTitle.Text = _curTitle;
        }
        else
        {
            MiniTitle.Text = line;
            if (_cfg.LyricsKaraoke && !IslandLetters)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                double t0 = _lyr![idx].t;
                double t1 = idx + 1 < _lyr.Count ? _lyr[idx + 1].t : t0 + 4;
                double win = LyricsEngine.LineWindow(line, t0, t1);
                var wts = LyricsEngine.WordWeights(parts);
                double total = wts.Sum(), acc = 0;
                string prefix = "";
                _waveW = Math.Max(10, TextW(line, 12, FontWeights.Normal));
                // точное время слов (NetEase, Kugou), если есть; иначе окно строки делится по слогам
                var wt = _lyrWords != null && idx < _lyrWords.Count ? _lyrWords[idx] : null;
                if (wt != null && wt.Count != parts.Length) wt = null;
                for (int k = 0; k < parts.Length; k++)
                {
                    double w = wts[k] / total * win;
                    double x0 = TextW(prefix, 12, FontWeights.Normal);
                    prefix += parts[k] + " ";
                    double x1 = TextW(prefix.TrimEnd(), 12, FontWeights.Normal);
                    if (wt != null) _words.Add((t0 + wt.Start[k], t0 + Math.Max(wt.End[k], wt.Start[k] + 0.05), x0, x1));
                    else _words.Add((t0 + acc, t0 + acc + Math.Max(w, 0.18), x0, x1));
                    acc += w;
                }
                double len = _waveW + 8;
                _wave = new LinearGradientBrush
                {
                    MappingMode = BrushMappingMode.Absolute,
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(len, 0)
                };
                _wave.GradientStops.Add(new GradientStop(Colors.White, 0));
                _wave.GradientStops.Add(new GradientStop(Colors.White, 0));
                _wave.GradientStops.Add(new GradientStop(DimC, 0));
                _wave.GradientStops.Add(new GradientStop(DimC, 1));
                MiniTitle.Foreground = _wave;
                UpdateWave(-1); // до начала строки текст почти невидим
            }
        }
        UpdateWidths(MiniTitle.Text, "");

        // по буквам: старая строка улетает, новая опускается и загорается волной вслед за голосом
        if (IslandLetters && MiniLetterLine(prevText, idx, line)) { GlowOff(MiniGlow); return; }

        // прошлая строка уходит вверх и гаснет, новая вплывает снизу
        if (!string.IsNullOrWhiteSpace(prevText) && prevText != MiniTitle.Text)
        {
            MiniTitleOld.Text = prevText;
            var go = Get(MiniTitleOld).T;
            MiniTitleOld.BeginAnimation(OpacityProperty, new DoubleAnimation(0.55, 0, TimeSpan.FromMilliseconds(300)));
            go.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -11, TimeSpan.FromMilliseconds(300))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        }
        MiniTitle.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)));
        Get(MiniTitle).T.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(420))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 } });
        if (GlowFx && !string.IsNullOrWhiteSpace(line))
        {
            GlowOn(MiniGlow, MiniTitle, 0.8, 7, true);
            Get(MiniGlow).T.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(420))
                { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 } });
        }
        else GlowOff(MiniGlow);
    }

    private void UpdateWave(double t)
    {
        if (_wave == null || _words.Count == 0) return;
        double x;
        if (t <= _words[0].t0) x = 0;
        else if (t >= _words[^1].t1) x = _waveW;
        else
        {
            x = _waveW;
            foreach (var w in _words)
            {
                if (t > w.t1) continue;
                double fr = Math.Clamp((t - w.t0) / (w.t1 - w.t0), 0, 1);
                x = w.x0 + fr * (w.x1 - w.x0);
                break;
            }
        }
        double len = _waveW + 8;
        double cx = -WaveFeather + x / _waveW * (_waveW + 2 * WaveFeather); // от «всё тусклое» до «всё яркое»
        double a = Math.Clamp((cx - WaveFeather) / len, 0, 1);
        double b = Math.Clamp((cx + WaveFeather) / len, 0, 1);
        if (Math.Abs(a - _waveA) < 0.0015 && Math.Abs(b - _waveB) < 0.0015) return; // меньше пикселя: не перерисовываем
        _waveA = a; _waveB = b;
        _wave.GradientStops[1].Offset = a;
        _wave.GradientStops[2].Offset = b;
    }
}
