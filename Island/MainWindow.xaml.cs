using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using Windows.Media.Control;

namespace Island;

public partial class MainWindow : Window
{
    // ---------- пружина ----------
    private sealed class Spring
    {
        public double P, V, T;
        public double K = 170, D = 13; // меньше D = больше «желе»
        public void Step(double dt)
        {
            double a = -K * (P - T) - D * V;
            V += a * dt;
            P += V * dt;
        }
        public bool Settled => Math.Abs(P - T) < 0.05 && Math.Abs(V) < 0.05;
    }

    private enum Mode { Mini, Full, Osd, Notif, Pocket, Actions, Drop, Devices, Timer, Mixer, Center, Translate }

    private readonly Spring _w = new() { P = 190, T = 190 };
    private readonly Spring _h = new() { P = 38, T = 38 };
    private readonly Spring _r = new() { P = 19, T = 19 };
    private readonly Stopwatch _sw = new();
    private bool _running;
    private Mode _mode = Mode.Mini;
    private bool _expanded;       // пользователь раскрыл плеер
    private bool _pocketOpen;     // открыт карман
    private bool _autoOpened;     // карман открылся сам при перетаскивании
    private bool _ready;          // первые 2 секунды игнорируем события
    private double _miniW = 190, _fullW = 400;
    private const double OsdBarW = 170;
    private readonly DispatcherTimer _toastTimer = new();
    private readonly DispatcherTimer _autoClose = new() { Interval = TimeSpan.FromMilliseconds(1800) };
    private readonly DispatcherTimer _leaveTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };

    // ---------- настройки и вид ----------
    private readonly AppSettings _cfg = AppSettings.Load();
    private SettingsWindow? _settingsWin;
    private BitmapSource? _lastCover;
    private bool _playing;

    // ---------- направление переключения ----------
    private int _skipDir = 1;
    private DateTime _skipAt = DateTime.MinValue;
    private DateTime _lastIconAt = DateTime.MinValue;

    // ---------- перемотка ----------
    private readonly DispatcherTimer _progTimer = new() { Interval = TimeSpan.FromMilliseconds(66) };
    private bool _seeking;
    private double _seekRatio;

    // ---------- пружинные трансформации ----------
    private sealed class Tx
    {
        public readonly ScaleTransform S = new(1, 1);
        public readonly RotateTransform R = new(0);
        public readonly TranslateTransform T = new(0, 0);
    }
    private readonly Dictionary<FrameworkElement, Tx> _tx = new();

    private Mode Rest() => _devicesOpen ? Mode.Devices : _timerOpen ? Mode.Timer : _mixerOpen ? Mode.Mixer : _actionsOpen ? Mode.Actions : _pocketOpen ? Mode.Pocket : _trOpen ? Mode.Translate : _centerOpen ? Mode.Center : (_expanded ? Mode.Full : Mode.Mini);

    // ---------- эквалайзер ----------
    private const int Bands = 7;
    private readonly List<Border> _bars = new();
    private readonly double[] _bp = new double[Bands], _bv = new double[Bands];
    private readonly float[] _target = new float[Bands];
    private readonly float[] _buf = new float[2048];
    private int _bufN;
    private readonly float[] _bandPeak = new float[Bands];
    private bool _hidden;
    private WasapiLoopbackCapture? _cap;

    // ---------- музыка (SMTC) ----------
    private GlobalSystemMediaTransportControlsSessionManager? _mgr;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string _lastTitle = "", _lastStatus = "";

    // ---------- громкость и яркость ----------
    private MMDevice? _dev;
    private float _lastVol;
    private bool _lastMute;
    private ManagementEventWatcher? _bw;

    // ---------- уведомления ----------
    private AutomationEventHandler? _uiaHandler;
    private string _lastNotifKey = "";
    private DateTime _lastNotifAt = DateTime.MinValue;

    // ---------- карман ----------
    private readonly List<string> _files = new();
    private readonly List<string> _clips = new();
    private int _tab; // 0 файлы, 1 буфер
    private DateTime _ignoreClipUntil = DateTime.MinValue;
    private static readonly string PocketFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island", "pocket.json");

    // ---------- окно ----------
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    private const int WM_HOTKEY = 0x0312;
    private const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WM_CLIPBOARDUPDATE = 0x031D;
    private IntPtr _hwnd;

    public MainWindow()
    {
        InitializeComponent();
        BindAccentUi();
        PlaceOnMonitor();

        Mini.HorizontalAlignment = Full.HorizontalAlignment = Pocket.HorizontalAlignment =
            Notif.HorizontalAlignment = Osd.HorizontalAlignment = Actions.HorizontalAlignment = HorizontalAlignment.Center;
        FixWidths(Mode.Mini, 0);
        RebuildEq();
        Pill.PreviewMouseWheel += Pill_Wheel;
        InitGestures();
        InitFluid();
        InitNotch();
        InitDropFx();
        InitNotifCenter();   // шаг 49: шторка, раскрытие и смахивание уведомлений
        InitBubble();        // пузырь таймера и копирования рядом с островом
        InitEventBubble();   // пузырь событий: плашки и уведомления рядом с островом (шаг 52)
        InitCollect();       // шаг 71: ★ в развёрнутом острове, карточка выпадает прямо в нём (Collect.cs)
        InitScreens();       // шаг 74: прятаться в полноэкранных приложениях, монитор с активным окном (Screens.cs)
        // шаг 73: своя картинка (карточка, итоги, сторис) в буфере — без плашки «скопировано» и без кармана
        StoryCards.BeforeClipboard = () => _ignoreClipUntil = DateTime.UtcNow.AddSeconds(2);
        StartStats();        // статистика прослушиваний (Stats.cs)
        StartDayCheck();     // праздник сегодня (Today.cs)
        StartUpdates();      // автообновление с GitHub (Update.cs)
        LoadFiles();
        RenderPocket();
        ApplySettings();
        _progTimer.Tick += (_, _) => { if (_mode == Mode.Full) UpdateProgress(); };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(_hwnd, GWL_EXSTYLE, GetWindowLong(_hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
            AddClipboardFormatListener(_hwnd);
            RegisterHotkeys();
            InitTopmost();
        };
        // клик вне острова сворачивает его
        Deactivated += (_, _) =>
        {
            if (!_cfg.CollapseOnOutside) return;
            if (_mode == Mode.Full || _mode == Mode.Pocket || _mode == Mode.Actions || _mode == Mode.Devices || _mode == Mode.Timer || _mode == Mode.Mixer || _mode == Mode.Center || _mode == Mode.Translate)
            {
                _expanded = false; _pocketOpen = false; _autoOpened = false; _actionsOpen = false; _devicesOpen = false; _timerOpen = false; _mixerOpen = false; _centerOpen = false; _trOpen = false;
                ApplyMode(Rest());
            }
        };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ApplyMode(Rest()); };
        _autoClose.Tick += (_, _) =>
        {
            _autoClose.Stop();
            if (!_autoOpened) return;
            if (Pill.IsMouseOver) { _autoOpened = false; return; } // человек рядом: оставляем открытым
            _pocketOpen = false; _autoOpened = false;
            ApplyMode(Rest());
        };
        _leaveTimer.Tick += (_, _) =>
        {
            _leaveTimer.Stop();
            if (_mode == Mode.Drop) ApplyMode(Rest());
            else if (_autoOpened) { _pocketOpen = false; _autoOpened = false; ApplyMode(Rest()); }
        };
        Loaded += async (_, _) =>
        {
            // приветствие: при каждом запуске, пока в его конце не нажали «Больше не показывать» (ключ --welcome показывает его в любом случае)
            try
            {
                if (!_cfg.HideWelcome || Environment.GetCommandLineArgs().Contains("--welcome")) { _welcome = new WelcomeWindow(_cfg, ApplySettings); _welcome.Show(); }
            }
            catch (Exception ex) { App.Log("Welcome", ex); }
            try { MaybeWhatsNew(); } catch (Exception ex) { App.Log("WhatsNew", ex); }   // после обновления: презентация новинок (Update.cs)
            // каждый шаг отдельно: если при входе в Windows какой-то сервис ещё не поднялся, остальное всё равно запустится
            try { await InitMediaAsync(); } catch (Exception ex) { App.Log("Media", ex); }
            try { InitVolume(); } catch (Exception ex) { App.Log("Volume", ex); }
            try { InitBrightness(); } catch (Exception ex) { App.Log("Brightness", ex); }
            try { InitNotifications(); } catch (Exception ex) { App.Log("Notifications", ex); }
            try { ApplyStreamer(); } catch (Exception ex) { App.Log("Streamer", ex); }   // режим стримера (Streamer.cs)
            try { InitSteam(); } catch (Exception ex) { App.Log("Steam", ex); }         // загрузки Steam (SteamDl.cs)
            try { StartFeatures(); } catch (Exception ex) { App.Log("Features", ex); }
            try { StartDiscord(); } catch (Exception ex) { App.Log("Discord", ex); }
            await Task.Delay(2000);
            _ready = true;
        };
        Closed += (_, _) =>
        {
            StopCapture();
            try { _discord.Shutdown(); } catch { }
            try { _bw?.Stop(); } catch { }
            try { Automation.RemoveAllEventHandlers(); } catch { }
            if (_hwnd != IntPtr.Zero) { RemoveClipboardFormatListener(_hwnd); for (int i = 1; i <= MaxHotkeyId; i++) UnregisterHotKey(_hwnd, i); }
        };
    }

    // ============================================================
    //  АНИМАЦИОННЫЕ ПОМОЩНИКИ
    // ============================================================
    private Tx Get(FrameworkElement e)
    {
        if (!_tx.TryGetValue(e, out var t))
        {
            t = new Tx();
            var g = new TransformGroup();
            g.Children.Add(t.S); g.Children.Add(t.R); g.Children.Add(t.T);
            e.RenderTransform = g;
            e.RenderTransformOrigin = new Point(0.5, 0.5);
            _tx[e] = t;
        }
        return t;
    }

    private static IEasingFunction Spr(double osc = 2, double spring = 4) =>
        new SpringEase(6.5, 5 + osc * 2.5);

    private void Pop(FrameworkElement e, double from = 0.5, int ms = 520)
    {
        var t = Get(e);
        var a = new DoubleAnimation(from, 1, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Spr() };
        t.S.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        t.S.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    private void Slide(FrameworkElement e, double fromX, int ms = 480)
    {
        var t = Get(e);
        t.T.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, TimeSpan.FromMilliseconds(ms))
        { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 } });
    }

    private void Spin(FrameworkElement e, double from, int ms = 650)
    {
        var t = Get(e);
        t.R.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(ms))
        { EasingFunction = Spr(1, 5) });
    }

    private static void FadeIn(UIElement e, int ms = 300) =>
        e.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms)));

    // кнопка «вдавливается» и пружинит, а стрелки ещё и толкаются в свою сторону
    private void Press(FrameworkElement b, double dx)
    {
        Pop(b, 0.7, 520);
        if (dx != 0) Slide(b, dx, 520);
    }

    // обложка «переворачивается»: сжимается в ребро, меняется картинка, разворачивается новой стороной
    private readonly Dictionary<Border, Brush> _pendingCover = new();

    private void SetCoverBrush(Border el, Brush brush, bool flip)
    {
        // переворот уже идёт: просто подменяем картинку, которая появится на обратной стороне
        if (_pendingCover.ContainsKey(el)) { _pendingCover[el] = brush; return; }
        if (!flip || !_cfg.CoverFlip) { el.Background = brush; return; }

        _pendingCover[el] = brush;
        var t = Get(el);
        var half = new DoubleAnimation(t.S.ScaleX, 0, TimeSpan.FromMilliseconds(150))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        half.Completed += (_, _) =>
        {
            if (_pendingCover.Remove(el, out var next)) el.Background = next;
            t.S.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 } });
            t.S.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(420))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        };
        t.S.BeginAnimation(ScaleTransform.ScaleXProperty, half);
        t.S.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(t.S.ScaleY, 0.92, TimeSpan.FromMilliseconds(150))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
    }

    // смена трека: обложка и текст «влетают» с той стороны, куда переключили
    private void AnimateTrack(int dir)
    {
        if (!_cfg.CoverFlip) // иначе обложка переворачивается (см. SetCoverBrush)
        {
            Slide(CoverBig, dir * 26); Pop(CoverBig, 0.8);
            Slide(CoverMini, dir * 14); Pop(CoverMini, 0.6);
        }
        // если текст сменился по буквам (см. IslandLetters.cs), обычный «слайд» не нужен
        if (!_swT) { Slide(TitleText, dir * 34); FadeIn(TitleText); }
        if (!_swA) { Slide(ArtistText, dir * 46); FadeIn(ArtistText); }
        if (!_swM) { Slide(MiniTitle, dir * 28); FadeIn(MiniTitle); }
        _swM = _swT = _swA = false;
    }

    // ============================================================
    //  НАСТРОЙКИ
    // ============================================================
    private void ApplySettings()
    {
        PlaceOnMonitor();
        ApplyFluidSettings();
        NotchRetarget();
        if (_langBuilt != _cfg.Language) ApplyLanguage();
        ApplyCoverShape();
        if (_eqStyleBuilt != _cfg.EqStyle) RebuildEq();
        UpdateFullLyrVisibility();
        MiniEq.Visibility = _cfg.Equalizer && !_clockMode ? Visibility.Visible : Visibility.Collapsed;
        FullEq.Visibility = _cfg.Equalizer ? Visibility.Visible : Visibility.Collapsed;
        if (!NeedCapture()) StopCapture();
        else if (_playing) StartCapture();
        ApplyTint(_cfg.CoverTint ? _lastCover : null);
        _cfg.ApplyAutostart();
        ApplyFeatureSettings();
        UpdateWidths(MiniTitle.Text, ArtistText.Text);
        ApplyAccentUi();
        EnsureTranslation();
        SliderHost.Cursor = _cfg.SeekClick ? Cursors.Hand : Cursors.Arrow;
        ApplyWaveSeek();
        ApplyPerfSettings();
        ApplyVibeSettings();   // звуки интерфейса и упругость (Vibe.cs)
        if (IsLoaded) ApplyStreamer();   // режим стримера: чат Twitch (Streamer.cs)
        if (IsLoaded) { UpdateBell(); UpdateBubble(); UpdateMiniTimer(); PlaceAppsBar(); PlaceDock(); if (!_cfg.NotifCenter && _centerOpen) { _centerOpen = false; _trOpen = false; ApplyMode(Rest()); } }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWin != null) { _settingsWin.Activate(); return; }
        _settingsWin = new SettingsWindow(_cfg, ApplySettings, UiColor);
        var w = _settingsWin;
        _settingsWin.Closed += (_, _) =>
        {
            _settingsWin = null;
            if (w.WantsReopen) Settings_Click(this, new RoutedEventArgs()); // после импорта окно открывается заново
        };
        _settingsWin.Show();
    }

    // ============================================================
    //  РАЗМЫТАЯ ОБЛОЖКА НА ФОНЕ
    // ============================================================
    private void ApplyTint(BitmapSource? bmp)
    {
        if (bmp == null || !_cfg.CoverTint)
        {
            BgImg.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(500)));
            return;
        }
        try
        {
            // обложка размывается так же гладко, как в плеере: без кубиков и ромбов
            var dark = SmoothBg(bmp, 8, 112, 56, 0.5);
            if (dark == null) return;
            var brush = new ImageBrush(dark) { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
            BgImg.Background = brush;
            BgImg.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700)) { EasingFunction = new CubicEase() });
        }
        catch (Exception ex) { App.LogOnce("CoverTint", ex); }
    }

    // ============================================================
    //  ПЕРЕМОТКА
    // ============================================================
    private static string Fmt(double sec) => $"{(int)sec / 60}:{(int)sec % 60:00}";

    private void SetSeekVisual(double r)
    {
        double w = SliderHost.ActualWidth;
        SeekFill.Width = Math.Max(0, w * r);
        SeekThumb.Margin = new Thickness(Math.Max(0, w * r - 6), 0, 0, 0);
        SeekWave.Value = r;
    }

    private void UpdateProgress()
    {
        if (_session == null) return;
        SeekWave.SetSeed(_curArtist + "|" + _curTitle);
        try
        {
            var tl = _session.GetTimelineProperties();
            double total = (tl.EndTime - tl.StartTime).TotalSeconds;
            if (total <= 1)
            {
                SliderHost.Opacity = 0.35; SeekFill.Width = 0; SeekWave.Value = 0;
                TimeL.Text = "0:00"; TimeR.Text = "";
                return;
            }
            SliderHost.Opacity = 1;
            var pos = tl.Position;
            if (_playing) pos += DateTimeOffset.Now - tl.LastUpdatedTime;
            double cur = Math.Clamp((pos - tl.StartTime).TotalSeconds, 0, total);
            if (_seeking) cur = _seekRatio * total;
            SetSeekVisual(cur / total);
            TimeL.Text = Fmt(cur);
            TimeR.Text = Fmt(total);
        }
        catch (Exception ex) { App.LogOnce("Progress", ex); }
    }

    private void SeekFromMouse(MouseEventArgs e)
    {
        double w = SliderHost.ActualWidth;
        if (w <= 0) return;
        _seekRatio = Math.Clamp(e.GetPosition(SliderHost).X / w, 0, 1);
        SetSeekVisual(_seekRatio);
        try
        {
            var tl = _session?.GetTimelineProperties();
            if (tl != null) TimeL.Text = Fmt(_seekRatio * (tl.EndTime - tl.StartTime).TotalSeconds);
        }
        catch (Exception ex) { App.LogOnce("Seek", ex); }
    }

    private void Seek_Down(object s, MouseButtonEventArgs e)
    {
        if (!_cfg.SeekClick) { e.Handled = true; return; }
        _seeking = true;
        SliderHost.CaptureMouse();
        if (!_cfg.WaveSeek) Pop(SeekThumb, 1.6, 450);
        SeekFromMouse(e);
        e.Handled = true;
    }

    private void Seek_Move(object s, MouseEventArgs e) { if (_seeking) SeekFromMouse(e); }

    private async void Seek_Up(object s, MouseButtonEventArgs e)
    {
        e.Handled = true; // чтобы клик по ползунку не сворачивал остров
        if (!_seeking) return;
        SeekFromMouse(e);
        _seeking = false;
        SliderHost.ReleaseMouseCapture();
        if (_session == null) return;
        try
        {
            var tl = _session.GetTimelineProperties();
            double total = (tl.EndTime - tl.StartTime).TotalSeconds;
            if (total > 1)
            {
                long ticks = tl.StartTime.Ticks + (long)(_seekRatio * total * TimeSpan.TicksPerSecond);
                await _session.TryChangePlaybackPositionAsync(ticks);
            }
        }
        catch (Exception ex) { App.LogOnce("Seek", ex); }
    }

    private void Seek_Enter(object s, MouseEventArgs e) { if (_cfg.SeekClick && !_cfg.WaveSeek) Pop(SeekThumb, 1.5, 450); }
    private void Seek_Leave(object s, MouseEventArgs e) { }

    // ============================================================
    //  ЭКВАЛАЙЗЕР
    // ============================================================
    private void BuildBars(Panel p)
    {
        for (int i = 0; i < Bands; i++)
        {
            var b = new SBorder
            {
                Width = 3, Height = 3, CornerRadius = new CornerRadius(1.5),
                Background = _uiBrush, Margin = new Thickness(0, 0, 3, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            p.Children.Add(b);
            _bars.Add(b);
        }
    }

    private void StartCapture()
    {
        if (_cap != null || !NeedCapture()) return;
        try
        {
            var c = new WasapiLoopbackCapture();
            c.DataAvailable += OnData;
            c.StartRecording();
            _cap = c;
            EnsureLoop();
        }
        catch { _cap = null; }
    }

    private void StopCapture()
    {
        var c = _cap;
        _cap = null;
        if (c == null) return;
        try { c.DataAvailable -= OnData; c.StopRecording(); c.Dispose(); } catch { }
        Array.Clear(_target);
        EnsureLoop();
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var cap = sender as WasapiLoopbackCapture;
        if (cap == null) return;
        var wf = cap.WaveFormat;
        if (wf.Encoding != WaveFormatEncoding.IeeeFloat || wf.BitsPerSample != 32) return;
        int ch = wf.Channels;
        int frames = e.BytesRecorded / (4 * ch);
        for (int i = 0; i < frames; i++)
        {
            float m = 0;
            for (int c = 0; c < ch; c++) m += BitConverter.ToSingle(e.Buffer, (i * ch + c) * 4);
            _buf[_bufN++] = m / ch;
            if (_bufN == _buf.Length) { Analyze(wf.SampleRate); _bufN = 0; }
        }
    }

    private static readonly double[] Edges = { 40, 100, 220, 480, 1000, 2200, 5000, 12000 };

    private void Analyze(int sampleRate)
    {
        int n = _buf.Length; // 2048
        var cx = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            cx[i].X = (float)(_buf[i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1))));
            cx[i].Y = 0;
        }
        FastFourierTransform.FFT(true, 11, cx);
        var band = new float[Bands];
        float mx = 0;
        for (int b = 0; b < Bands; b++)
        {
            int lo = Math.Max(1, (int)(Edges[b] * n / sampleRate));
            int hi = Math.Max(lo + 1, (int)(Edges[b + 1] * n / sampleRate));
            float sum = 0;
            for (int k = lo; k < hi && k < n / 2; k++) sum += (float)Math.Sqrt(cx[k].X * cx[k].X + cx[k].Y * cx[k].Y);
            band[b] = sum / (hi - lo);
            if (band[b] > mx) mx = band[b];
        }
        // у каждой полоски своя «шкала»: басы громче высоких, но живыми должны быть все
        for (int b = 0; b < Bands; b++)
        {
            _bandPeak[b] = Math.Max(_bandPeak[b] * 0.997f, band[b]);
            _target[b] = (mx < 1e-5f || _bandPeak[b] < 1e-6f)
                ? 0
                : (float)Math.Sqrt(Math.Clamp(band[b] / _bandPeak[b], 0, 1));
        }
    }

    // ============================================================
    //  АНИМАЦИЯ
    // ============================================================
    private void EnsureLoop()
    {
        if (_running) return;
        _running = true;
        _sw.Restart();
        CompositionTarget.Rendering += Tick;
    }

    private void Go(double w, double h, double r)
    {
        FluidKick(w - _w.T, h - _h.T);
        _w.T = w; _h.T = h; _r.T = r;
        EnsureLoop();
    }

    private void Tick(object? s, EventArgs e)
    {
        if (!FrameDue(e)) return;   // Performance.cs: раз за кадр; в экономном режиме не чаще 30 раз в секунду
        double dt = Math.Min(0.032, _sw.Elapsed.TotalSeconds);
        _sw.Restart();

        _w.Step(dt); _h.Step(dt); _r.Step(dt);
        Pill.Width = Math.Max(24, _w.P) + 2 * _earNow;   // + уши выреза в режиме notch (Notch.cs)
        PlaceAppsBar();   // капсула значков приложений едет вместе с шириной острова (AppsBar.cs)
        Pill.Height = Math.Max(_fsMini ? 4 : 24, _h.P);
        bool notchMoving = NotchStep(dt);
        bool fluidActive = FluidStep(dt);
        if (fluidActive) FluidApply(_r.P);
        else FluidRest(_r.P, Pill.Height);

        bool barsActive = false;
        for (int i = 0; i < Bands; i++)
        {
            double t = _cap != null ? _target[i] : 0;
            double a = 300 * (t - _bp[i]) - 17 * _bv[i];
            _bv[i] += a * dt;
            _bp[i] += _bv[i] * dt;
            if (_bp[i] < 0) { _bp[i] = 0; _bv[i] = 0; }
            if (Math.Abs(_bp[i] - t) > 0.01 || Math.Abs(_bv[i]) > 0.01) barsActive = true;
        }
        if (_mode == Mode.Mini || _mode == Mode.Full) UpdateEqVisuals();

        // эквалайзера не видно (карман, плашка, остров спрятан): кадры ему не нужны, даже если звук ещё слушаем.
        // Пока открыт счётчик кадров, цикл держим, чтобы было что считать.
        bool eqShown = !_hidden && ((_mode == Mode.Mini && MiniEq.Visibility == Visibility.Visible) ||
                                    (_mode == Mode.Full && FullEq.Visibility == Visibility.Visible));
        if (_w.Settled && _h.Settled && _r.Settled && !fluidActive && !barsActive && !notchMoving && (_cap == null || !eqShown) && !_fpsOn)
        {
            Pill.Width = _w.T + 2 * _earNow; Pill.Height = _h.T; NotchSnap(); PlaceAppsBar(); FluidRest(_r.T, _h.T);
            CompositionTarget.Rendering -= Tick;
            _running = false;
        }
    }

    // ============================================================
    //  РЕЖИМЫ ОСТРОВА
    // ============================================================
    private static void FadeTo(UIElement e, bool on) =>
        e.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(on ? 240 : 110)));

    private void ApplyMode(Mode m, double w = 280)
    {
        var prev = _mode;
        // шаг 71: панель действий каждый раз открывается с закреплёнными плитками, а не со списком всех,
        // и с актуальным набором (его могли сбросить или импортировать в настройках) (Actions.cs)
        if (m == Mode.Actions && prev != Mode.Actions) { _actsAll = false; RenderActionTiles(); ActionsScroll.ScrollToTop(); }
        _mode = m;
        if (m != Mode.Pocket && PocketSearchWrap.Visibility == Visibility.Visible) CloseSearch();   // ушли из кармана: поиск сбрасывается
        switch (m)
        {
            case Mode.Mini: if (_fsMini) Go(56, 5, 2.5); else Go(_miniW, 38, 19); break;   // шаг 76: в игре тонкая черта (Screens.cs)
            case Mode.Full: Go(_fullW, FullH(), 34); break;
            case Mode.Pocket: Go(440, PocketH, 30); break;
            case Mode.Actions: Go(440, ActionsH, 30); break;
            case Mode.Devices: Go(480, 232, 30); break;
            case Mode.Timer: Go(440, TimerH, 30); break;
            case Mode.Mixer: Go(440, MixerH(), 30); break;
            case Mode.Notif: Go(w, _notifH, 30); break;
            case Mode.Center: Go(440, CenterH(), 30); break;
            case Mode.Translate: Go(440, TrH, 30); break;
            case Mode.Drop: Go(330, 100, 30); break;
            default: Go(w, 54, 27); break;
        }
        FixWidths(m, w);
        Full.IsHitTestVisible = m == Mode.Full;
        Pocket.IsHitTestVisible = m == Mode.Pocket;
        Actions.IsHitTestVisible = m == Mode.Actions;
        DevPanel.IsHitTestVisible = m == Mode.Devices;
        TimerPanel.IsHitTestVisible = m == Mode.Timer;
        MixerPanel.IsHitTestVisible = m == Mode.Mixer;
        CenterPanel.IsHitTestVisible = m == Mode.Center;
        TrPanel.IsHitTestVisible = m == Mode.Translate;
        Notif.IsHitTestVisible = m == Mode.Notif;   // кнопки раскрытого уведомления
        if (m == Mode.Full) { _progTimer.Start(); UpdateProgress(); } else _progTimer.Stop();
        Reveal(Mini, m == Mode.Mini && !_fsMini, -8);
        // в игре черта полупрозрачная; плашки и уведомления поверх игры — как обычно, в полную силу
        if (_fsMini) Pill.BeginAnimation(OpacityProperty, new DoubleAnimation(m == Mode.Mini ? 0.45 : 1, TimeSpan.FromMilliseconds(m == Mode.Mini ? 400 : 120)));
        bool openFull = Reveal(Full, m == Mode.Full) && m == Mode.Full;
        bool openPocket = Reveal(Pocket, m == Mode.Pocket) && m == Mode.Pocket;
        bool openActions = Reveal(Actions, m == Mode.Actions) && m == Mode.Actions;
        bool openDevices = Reveal(DevPanel, m == Mode.Devices) && m == Mode.Devices;
        bool openTimer = Reveal(TimerPanel, m == Mode.Timer) && m == Mode.Timer;
        bool openMixer = Reveal(MixerPanel, m == Mode.Mixer) && m == Mode.Mixer;
        bool openCenter = Reveal(CenterPanel, m == Mode.Center) && m == Mode.Center;
        Reveal(TrPanel, m == Mode.Translate);
        Reveal(Osd, m == Mode.Osd, -8);
        Reveal(Notif, m == Mode.Notif, -10);
        Reveal(DropHint, m == Mode.Drop, -8);
        if (openFull) Droplets(FullItems());
        if (openPocket) Droplets(PocketItems());
        if (openActions) { RefreshActions(false); UpdateMicUi(); Droplets(ActionItems()); }
        if (openDevices) Droplets(DeviceItems());
        if (openTimer) { RenderTimer(); Droplets(TimerItems()); }
        if (openMixer) Droplets(MixerList.Children.OfType<FrameworkElement>());
        if (openCenter) { CenterScroll.ScrollToTop(); Droplets(CenterItems()); }
        if (m != Mode.Notif) _notifOpen = false;
        ModeSound(prev, m);   // звук раскрытия и закрытия (Vibe.cs)
        // шаг 53: строка слов могла смениться, пока остров был раскрыт, и в свёрнутом не появлялась до следующей.
        // Вернулись в свёрнутый: текущая строка рисуется заново на следующем кадре
        if (m == Mode.Mini && prev != Mode.Mini && _lyricMode) _lyrIdx = -2;
        UpdateBubble();       // пузырь виден только рядом со свёрнутым островом (Bubble.cs)
        OnModeForTimers(m);
        DropBounce(m == Mode.Drop);
    }

    private void Retarget()
    {
        if (_mode == Mode.Mini) { if (_fsMini) Go(56, 5, 2.5); else Go(_miniW, 38, 19); }
        else if (_mode == Mode.Full) Go(_fullW, FullH(), 34);
        FixWidths(_mode, 0);
    }

    // Содержимое всегда выкладывается сразу на итоговую ширину, а остров лишь «раскрывается» вокруг него.
    // Так текст не пересчитывается на каждом кадре пружины и не мигает многоточием.
    private void FixWidths(Mode m, double w)
    {
        switch (m)
        {
            case Mode.Mini: Mini.Width = Math.Max(10, _miniW - 22); break;
            case Mode.Full: Full.Width = Math.Max(10, _fullW - 36); break;
            case Mode.Pocket: Pocket.Width = 408; break;
            case Mode.Drop: DropHint.Width = 312; break;
            case Mode.Actions: Actions.Width = 408; break;
            case Mode.Devices: DevPanel.Width = 448; break;
            case Mode.Timer: TimerPanel.Width = 408; break;
            case Mode.Mixer: MixerPanel.Width = 408; break;
            case Mode.Center: CenterPanel.Width = 408; break;
            case Mode.Translate: TrPanel.Width = 408; break;
            case Mode.Notif: if (w > 0) Notif.Width = Math.Max(10, w - 30); break;
            case Mode.Osd: if (w > 0) Osd.Width = Math.Max(10, w - 27); break;
        }
    }

    private void Pill_Click(object sender, MouseButtonEventArgs e)
    {
        if (_mode == Mode.Osd || _mode == Mode.Notif)
        {
            _toastTimer.Stop();
            if (_mode == Mode.Osd && _osdClick is { } oc) { oc(); return; }   // плашка «скопировано»: закрепить (SmartClip.cs)
            var act = _notifClick; _notifClick = null;
            if (_mode == Mode.Notif && act != null) { act(); return; } // клик по напоминанию открывает заметки
            if (_mode == Mode.Notif && NotifClick()) return;           // уведомление раскрывается, второй клик ведёт к источнику (NotifCenter.cs)
            ApplyMode(Rest()); // клик закрывает плашку
            return;
        }
        if (_pocketOpen || _actionsOpen || _devicesOpen || _timerOpen || _mixerOpen || _centerOpen || _trOpen) return;
        _expanded = !_expanded;
        ApplyMode(Rest());
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void Test_Click(object sender, RoutedEventArgs e) =>
        ShowNotifItem(new NotifItem { App = "Telegram", Title = "Алекс", Body = "Го вечером в дс? Все уже собираются", AppId = "TelegramDesktop", Keep = true });

    private double TextW(string? s, double size, FontWeight weight)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(_islandFont, FontStyles.Normal, weight, FontStretches.Normal),   // тем же шрифтом, что на экране (свой шрифт, Customize.cs)
            size, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return ft.WidthIncludingTrailingWhitespace;
    }

    private void UpdateWidths(string title, string artist)
    {
        ApplyMiniLayout();
        if (_clockMode)
        {
            // шаг 72: часы рисуются шрифтом 12 (как название трека), а мерились шрифтом 13 — справа оставалось пустое место
            _miniW = Math.Clamp(MiniFixed() + TextW(title, 12, FontWeights.Normal), 110, 300);
            Retarget();
            return;
        }
        if (_lyricMode) title = MiniTitle.Text; // в режиме слов остров подстраивается под текущую строку
        double eqFull = (_cfg.Equalizer ? 54 : 0) + 32 + 32; // + кнопки «на весь экран» и ★ (шаг 71)
        double newMini = Math.Clamp(MiniFixed() + TextW(title, 12, FontWeights.Normal), 120, _lyricMode ? 430 : 380);
        // в режиме слов остров не дёргается из-за небольшого сужения: лишние пара пикселей не видны, а пружина и желе на каждой строке лагают
        if (!(_lyricMode && newMini < _miniW && _miniW - newMini < 14)) _miniW = newMini;
        // + 4: запас на округление, без него название впритык обрезалось «…» при свободном месте
        double col = Math.Max(Math.Max(TextW(title, 16, FontWeights.SemiBold), TextW(artist, 13, FontWeights.Normal)) + 4, 130);
        _fullW = Math.Clamp(36 + 64 + 14 + Math.Min(col, 300) + eqFull + _gifW, 360, 560 + _gifW);
        Retarget();
    }

    // ============================================================
    //  ПЛАШКИ: громкость, яркость, пауза, копирование
    // ============================================================
    private int _volTries;

    private void InitVolume()
    {
        try
        {
            var en = new MMDeviceEnumerator();
            _dev = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _lastVol = _dev.AudioEndpointVolume.MasterVolumeLevelScalar;
            _lastMute = _dev.AudioEndpointVolume.Mute;
            _dev.AudioEndpointVolume.OnVolumeNotification += OnVol;
        }
        catch
        {
            // звуковая служба при входе в Windows поднимается не сразу: повторяем раз в 5 секунд, до двух минут
            if (++_volTries <= 24)
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                t.Tick += (_, _) => { t.Stop(); InitVolume(); };
                t.Start();
            }
        }
    }

    private void OnVol(AudioVolumeNotificationData d)
    {
        Dispatcher.BeginInvoke(() =>
        {
            bool changed = Math.Abs(d.MasterVolume - _lastVol) > 0.004f || d.Muted != _lastMute;
            _lastVol = d.MasterVolume;
            _lastMute = d.Muted;
            // звук выключили плиткой в быстрых действиях: состояние видно на самой плитке, плашка закрыла бы панель
            bool fromTile = _mode == Mode.Actions && (DateTime.UtcNow - _tileMuteAt).TotalMilliseconds < 800;
            if (fromTile) RefreshActions();
            else if (_mode == Mode.Mixer) { }   // открыт микшер: общая громкость и так видна на его ползунке
            else if (_ready && changed && _cfg.VolumeToasts)
            {
                ShowToast(d.Muted ? "\uE74F" : "\uE767", null, d.Muted ? 0 : d.MasterVolume, "vol");
                if (_cfg.WheelControl) ScheduleTip("wheel", "volume-2", "колесо мыши над островом: громкость, с Shift перемотка", "mouse wheel over the island: volume, with Shift to seek", 2600);   // шаг 73 (Tips.cs)
            }
        });
    }

    private void InitBrightness()
    {
        try
        {
            var q = new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent");
            _bw = new ManagementEventWatcher(new ManagementScope(@"\\.\root\wmi"), q);
            _bw.EventArrived += (s, e) =>
            {
                int b = Convert.ToInt32(e.NewEvent["Brightness"]);
                Dispatcher.BeginInvoke(() => { if (_ready && _cfg.BrightnessToasts) ShowToast("\uE706", null, b / 100.0, "sun"); });
            };
            _bw.Start();
        }
        catch (Exception ex) { App.Log("Brightness.Init", ex); }
    }

    // ============================================================
    //  УВЕДОМЛЕНИЯ WINDOWS (Telegram, браузер и всё остальное, что показывает всплывашки)
    // ============================================================
    private void InitNotifications()
    {
        StartWpnReader();
        try
        {
            _uiaHandler = OnUiaWindow;
            Automation.AddAutomationEventHandler(WindowPattern.WindowOpenedEvent,
                AutomationElement.RootElement, TreeScope.Children, _uiaHandler);
        }
        catch (Exception ex) { App.Log("Notifications.Init", ex); }
    }

    private static readonly string LogFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island", "uia.log");

    private static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
            var fi = new FileInfo(LogFile);
            if (fi.Exists && fi.Length > 200_000) File.WriteAllText(LogFile, "");
            File.AppendAllText(LogFile, line + Environment.NewLine);
        }
        catch { }
    }

    // Windows рисует всплывашку окном оболочки. Мы ловим появление окна и читаем из него текст.
    // В журнал пишутся только служебные данные окна (класс, название, процесс), без текста сообщений.
    private static readonly string[] NotifWords =
    {
        "notification", "уведомл", "benachricht", "notificaci", "notifica", "notificação", "notifikation",
        "powiadom", "bildirim", "oznám", "értesít", "повідомл", "通知", "알림"
    };
    private static readonly string[] CenterWords =
    {
        "center", "centre", "центр", "zentrum", "centro", "centrum", "merkezi", "cent", "中心", "센터"
    };

    private void OnUiaWindow(object sender, AutomationEventArgs e)
    {
        try
        {
            var el = sender as AutomationElement;
            if (el == null || !_cfg.Notifications) return;
            var cur = el.Current;
            string cls = cur.ClassName ?? "", nm = cur.Name ?? "", pn = "";
            try { pn = Process.GetProcessById(cur.ProcessId).ProcessName; } catch { }
            Log($"{DateTime.Now:HH:mm:ss} class={cls} name={nm} proc={pn}");

            string name = nm.ToLowerInvariant();
            bool shell = pn.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                         pn.Equals("ShellHost", StringComparison.OrdinalIgnoreCase) ||
                         pn.Equals("explorer", StringComparison.OrdinalIgnoreCase);
            bool toast = shell && NotifWords.Any(w => name.Contains(w)) && !CenterWords.Any(w => name.Contains(w));
            if (!toast) return;

            // содержимое всплывашки подгружается не мгновенно: пробуем несколько раз
            _ = Task.Run(async () =>
            {
                int[] waits = { 0, 120, 200, 300, 500 };
                for (int k = 0; k < waits.Length; k++)
                {
                    if (waits[k] > 0) await Task.Delay(waits[k]);
                    var list = ReadToastTexts(el);
                    if (list.Count == 0) continue;
                    Log($"   toast matched, texts={list.Count}, try={k + 1}");

                    string app = "", title, body = "";
                    if (list.Count >= 3) { app = list[0]; title = list[1]; body = string.Join(" ", list.Skip(2)); }
                    else if (list.Count == 2) { title = list[0]; body = list[1]; }
                    else title = list[0];

                    await Dispatcher.BeginInvoke(() => { if (_ready) ShowNotif(app, title, body); });
                    return;
                }
                Log("   toast matched, but no text found");
            });
        }
        catch (Exception ex) { App.LogOnce("Notifications.Uia", ex); }
    }

    private static List<string> ReadToastTexts(AutomationElement el)
    {
        var list = new List<string>();
        try
        {
            foreach (AutomationElement t in el.FindAll(TreeScope.Descendants,
                         new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)))
            {
                string n = (t.Current.Name ?? "").Trim();
                if (n.Length > 0 && !list.Contains(n)) list.Add(n);
            }
            if (list.Count == 0) // запасной путь: берём названия любых элементов, кроме кнопок и картинок
            {
                foreach (AutomationElement t in el.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition))
                {
                    var ct = t.Current.ControlType;
                    if (ct == ControlType.Button || ct == ControlType.Image) continue;
                    string n = (t.Current.Name ?? "").Trim();
                    if (n.Length > 0 && !list.Contains(n)) list.Add(n);
                }
            }
        }
        catch (Exception ex) { App.LogOnce("Notifications.Text", ex); }
        return list;
    }

    private void Log_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(LogFile)) { Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!); File.WriteAllText(LogFile, ""); }
            Process.Start(new ProcessStartInfo("notepad.exe", "\"" + LogFile + "\"") { UseShellExecute = true });
        }
        catch { }
    }

    // ============================================================
    //  СКРЫТЬ ОСТРОВ (Ctrl+Alt+I), если он закрывает кнопку под собой
    // ============================================================
    private void Hide_Click(object sender, RoutedEventArgs e) => ToggleHidden();

    private void ToggleHidden()
    {
        OnManualToggle();   // шаг 74: показали вручную в полноэкранной игре — больше не прятать до её конца (Screens.cs)
        var t = Get(Pill).T;
        double off = -(Pill.ActualHeight + 30);
        if (!_hidden)
        {
            _hidden = true;
            var a = new DoubleAnimation(off, TimeSpan.FromMilliseconds(450))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseIn, Amplitude = 0.5 } };
            a.Completed += (_, _) => { if (_hidden) { Pill.Visibility = Visibility.Collapsed; NotchLayout(); } };
            t.BeginAnimation(TranslateTransform.YProperty, a);
        }
        else
        {
            _hidden = false;
            Pill.Visibility = Visibility.Visible;
            NotchLayout();
            t.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(off, 0, TimeSpan.FromMilliseconds(650)) { EasingFunction = Spr() });
        }
        AppsHiddenChanged();   // капсула значков приложений прячется вместе с островом
        UpdateBubble();        // и пузырь рядом с ним
        // спрятанному острову не нужен ни звук для эквалайзера, ни покадровая анимация
        if (!NeedCapture()) StopCapture();
        else if (_playing) StartCapture();
        EnsureLoop();
    }

    // ============================================================
    //  МУЗЫКА
    // ============================================================
    private async Task InitMediaAsync()
    {
        if (await TryInitMedia(true)) return;
        _ = RetryMediaAsync();   // сразу после входа в Windows медиа-служба бывает ещё не готова: пробуем снова
    }

    private async Task<bool> TryInitMedia(bool first)
    {
        try
        {
            var mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            mgr.CurrentSessionChanged += (_, _) => Dispatcher.Invoke(AttachSession);
            _mgr = mgr;
            AttachSession();
            return true;
        }
        catch (Exception ex)
        {
            _mgr = null;
            if (first) { App.Log("Media", ex); MiniTitle.Text = Loc.T("Нет доступа к музыке"); }
            return false;
        }
    }

    private async Task RetryMediaAsync()
    {
        for (int i = 0; i < 24 && _mgr == null; i++)
        {
            await Task.Delay(5000);
            if (await TryInitMedia(false)) return;
        }
    }

    private void AttachSession()
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaChanged;
            _session.PlaybackInfoChanged -= OnMediaChanged;
        }
        _session = _mgr?.GetCurrentSession();
        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnMediaChanged;
            _session.PlaybackInfoChanged += OnMediaChanged;
        }
        _ = RefreshAsync();
    }

    private void OnMediaChanged(object? s, object? e) => Dispatcher.Invoke(() => _ = RefreshAsync());

    private async Task RefreshAsync()
    {
        if (_session == null)
        {
            TitleText.Text = Loc.T("Ничего не играет");
            ArtistText.Text = "";
            _playing = false;
            StopCapture();
            EnterIdle();
            return;
        }
        try
        {
            var props = await _session.TryGetMediaPropertiesAsync();
            string title = props.Title ?? "", artist = props.Artist ?? "";
            ExitIdle();
            // реклама в Spotify (Ads.cs): пропустить перезапуском Spotify; по желанию «Реклама · 0:23» вместо трека
            _adNow = IsSpotifyAd(title, artist);
            if (_adNow && _cfg.AdSkip) _ = SkipAd(false);
            if (_adNow && _cfg.AdDetect) { EnterAd(title); return; }
            if (!_adNow) ExitAd();
            bool newTrack = title != _curTitle;
            // тот же заголовок, но исполнитель сменился или подгрузился позже (так делают браузеры): текст надо искать заново
            bool artistChanged = !newTrack && artist != _curArtist;
            string oldTitle = _curTitle, oldArtist = _curArtist;
            if (newTrack) { _curTitle = title; _curArtist = artist; SetLyricMode(false); }
            else if (artistChanged) { _curArtist = artist; SetLyricMode(false); }
            if (!_lyricMode) MiniTitle.Text = title;
            TitleText.Text = title;
            ArtistText.Text = artist;
            UpdateWidths(title, artist);
            UpdateIslandStar();   // шаг 71 (Collect.cs)
            if (newTrack) LettersOnTrack(oldTitle, oldArtist, title, artist); // по буквам: старый текст улетает, новый опускается

            var status = _session.GetPlaybackInfo().PlaybackStatus;
            bool playing = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            _playing = playing;
            string newStatus = status.ToString();
            bool statusChanged = newStatus != _lastStatus;
            PlayIcon.Text = playing ? "pause" : "play";
            if (statusChanged && _lastStatus != "") Pop(PlayBtn, 0.5, 560);

            if (playing) StartCapture(); else StopCapture();

            if (props.Thumbnail != null)
            {
                using var rs = await props.Thumbnail.OpenReadAsync();
                using var ms = new MemoryStream();
                await rs.AsStreamForRead().CopyToAsync(ms);
                ms.Position = 0;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                var brush = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                bool flipCover = title != _lastTitle && _lastTitle != "";
                SetCoverBrush(CoverMini, brush, flipCover);
                SetCoverBrush(CoverBig, brush, flipCover);
                _lastCover = bmp;
                _coverFor = title;    // обложка этого трека, а не оставшаяся от прошлого
                UpdateIslandStar();   // шаг 71: ★ золотая, если трек уже в коллекции (Collect.cs)
                _accent = AccentOf(bmp);
                ApplyAccentUi();
                ApplyTint(_cfg.CoverTint ? bmp : null);
            }

            // направление: если жали наши кнопки, берём его, иначе считаем «вперёд»
            int dir = (DateTime.UtcNow - _skipAt).TotalSeconds < 4 ? _skipDir : 1;
            bool trackChanged = title != _lastTitle && _lastTitle != "";
            _lastTitle = title;
            _lastStatus = newStatus;
            if (newTrack || artistChanged) _ = LoadLyricsAsync(title, artist);

            bool quiet = _mode == Mode.Full || _mode == Mode.Pocket; // в развёрнутом острове плашки не нужны
            if (trackChanged)
            {
                TipsOnTrack();   // шаг 73: советы для новичка (Tips.cs)
                AnimateTrack(dir);
                if (_ready && _cfg.PlaybackToasts && !quiet)
                    ShowToast(dir > 0 ? "\uE101" : "\uE100", title, null, dir > 0 ? "next" : "prev");
            }
            else if (statusChanged && _ready && _cfg.PlaybackToasts && !quiet)
            {
                ShowToast(playing ? "\uE102" : "\uE103", title, null, "play");
            }
        }
        catch (Exception ex) { App.LogOnce("Media.Refresh", ex); }
    }

    private async void Prev_Click(object s, RoutedEventArgs e)
    {
        _skipDir = -1; _skipAt = DateTime.UtcNow;
        if (s is FrameworkElement b) Press(b, -9);
        if (_session != null) await _session.TrySkipPreviousAsync();
    }

    private async void Next_Click(object s, RoutedEventArgs e)
    {
        _skipDir = 1; _skipAt = DateTime.UtcNow;
        if (s is FrameworkElement b) Press(b, 9);
        if (_adNow) { _ = SkipAd(true); return; }   // реклама: «вперёд» в Spotify заблокирован, пропускаем перезапуском (Ads.cs)
        if (_session != null) await _session.TrySkipNextAsync();
    }

    private async void Play_Click(object s, RoutedEventArgs e)
    {
        Press(PlayBtn, 0);
        if (_session != null) await _session.TryTogglePlayPauseAsync();
    }
}
