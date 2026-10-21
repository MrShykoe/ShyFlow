using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Island;

// ============================================================
//  Ползунок (перемотка и громкость)
// ============================================================
internal sealed class SliderBar : Grid
{
    private readonly Border _track, _fill, _thumb;
    private readonly WaveBar _wave = new() { Visibility = Visibility.Collapsed };
    private bool _waveOn;
    private bool _drag;
    private double _value;

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 1); Relayout(); }
    }
    public bool Dragging => _drag;

    // прогресс волной (как голосовые в Telegram) вместо линии
    public bool Wave
    {
        get => _waveOn;
        set
        {
            if (_waveOn == value) return;
            _waveOn = value;
            _wave.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            var o = value ? Visibility.Collapsed : Visibility.Visible;
            _track.Visibility = _fill.Visibility = _thumb.Visibility = o;
            Height = value ? 36 : 26;
            Relayout();
        }
    }
    public void SetSeed(string seed) => _wave.SetSeed(seed);
    public event Action<double>? Moving;      // пока тянем
    public event Action<double>? Committed;   // отпустили

    public SliderBar()
    {
        Height = 26;
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        _track = new SBorder
        {
            Height = 5, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255))
        };
        _fill = new SBorder
        {
            Height = 5, Width = 0, CornerRadius = new CornerRadius(4), Background = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
        };
        _thumb = new SBorder
        {
            Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = Brushes.White, Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
        };
        Children.Add(_track);
        Children.Add(_fill);
        Children.Add(_thumb);
        Children.Add(_wave);
        SizeChanged += (_, _) => Relayout();
    }

    private SolidColorBrush? _accBrush;

    public void SetAccent(Color c)
    {
        if (_accBrush == null)
        {
            _accBrush = new SolidColorBrush(Colors.White);
            _fill.Background = _accBrush;
            _thumb.Background = _accBrush;
            _wave.Fill = _accBrush;
        }
        ColorTween.To(_accBrush, SolidColorBrush.ColorProperty, new ColorAnimation(c, TimeSpan.FromMilliseconds(450)));
    }

    private void Relayout()
    {
        double w = ActualWidth;
        _fill.Width = Math.Max(0, w * _value);
        _thumb.Margin = new Thickness(Math.Max(0, w * _value - 7), 0, 0, 0);
        _wave.Value = _value;
    }

    private void Hover(bool on)
    {
        var d = TimeSpan.FromMilliseconds(160);
        _track.BeginAnimation(HeightProperty, new DoubleAnimation(on ? 9 : 5, d));
        _fill.BeginAnimation(HeightProperty, new DoubleAnimation(on ? 9 : 5, d));
        _thumb.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, d));
    }

    private void SetFrom(MouseEventArgs e)
    {
        double w = ActualWidth;
        if (w <= 0) return;
        _value = Math.Clamp(e.GetPosition(this).X / w, 0, 1);
        Relayout();
        Moving?.Invoke(_value);
    }

    protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); Hover(true); }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); if (!_drag) Hover(false); }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _drag = true;
        CaptureMouse();
        SetFrom(e);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag) SetFrom(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_drag) return;
        SetFrom(e);
        _drag = false;
        ReleaseMouseCapture();
        Hover(IsMouseOver);
        Committed?.Invoke(_value);
        e.Handled = true;
    }
}

// ============================================================
//  Полноэкранный плеер
// ============================================================
public sealed partial class PlayerWindow : Window
{
    private static readonly SolidColorBrush Hov = MakeBrush(0x2A, 255, 255, 255);
    private static readonly SolidColorBrush Soft = MakeBrush(0xB8, 255, 255, 255);
    private static readonly SolidColorBrush Dim = MakeBrush(0x99, 255, 255, 255);

    private readonly MainWindow _host;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private bool _closing;

    // слои
    private readonly Grid _root = new();
    private readonly ScaleTransform _rootScale = new(1, 1);
    private readonly Border _bgA = new(), _bgB = new(), _shade = new();
    private Border _bgFront;

    // левая колонка
    private readonly StackPanel _left = new();
    private readonly TranslateTransform _leftShift = new();
    private readonly Grid _coverHost = new();
    private readonly SBorder _cover = new();
    private readonly Grid _faces = new();   // обложка и её оборот (см. PlayerBack.cs)
    private readonly ScaleTransform _coverScale = new(0.86, 0.86);
    private readonly ScaleTransform _coverFlipX = new(1, 1);
    private readonly ScaleTransform _tiltScale = new(1, 1);
    private readonly SkewTransform _tiltSkew = new(0, 0);
    private readonly SBorder _glare = new();
    private readonly RadialGradientBrush _glareBrush = new() { RadiusX = 0.9, RadiusY = 0.9 };
    private bool _tilting;
    private readonly List<TextBlock> _trTbs = new();
    private List<string>? _trShown;
    private Action? _pendingCover;
    private readonly VIcon _coverGlyph = new();
    private readonly TextBlock _titleTb = new(), _artistTb = new(), _tL = new(), _tR = new();
    private readonly VIcon _volIcon = new();
    private readonly TranslateTransform _titleT = new(), _artistT = new();
    private readonly Grid _titleHost = new(), _artistHost = new();
    private readonly Canvas _titleFx = new(), _artistFx = new();
    private LetterSwap? _titleSwap, _artistSwap;
    private bool _lettersBuilt;
    private readonly SliderBar _seek = new(), _vol = new();
    private readonly Border _playBtn;

    // слова
    private readonly Grid _lyrPanel = new();
    private readonly Canvas _lyrCanvas = new();
    private readonly StackPanel _lyrStack = new();
    private readonly List<Border> _wraps = new();
    private readonly List<FrameworkElement> _lines = new();
    private readonly List<TranslateTransform> _lt = new();
    private readonly List<List<Word>> _words = new();

    // одно слово строки: свой отрезок времени и своя кисть-«волна»
    private sealed class Word
    {
        public TextBlock Tb = null!;
        public readonly TranslateTransform T = new();
        public double T0, T1, P = -1;
        public LinearGradientBrush? Br;
        public bool Letter;          // true: это одна буква (режим «по буквам»), а не целое слово
        public SolidColorBrush? Sb;  // цвет буквы (от тусклого к белому)
        public double Boost;         // буквы выделенного слова поднимаются чуть выше
    }

    // Выделенное слово: его тянут («бляяяядь»), кричат капсом, держат в конце фразы или вся строка поётся медленно.
    // Пока слово поётся и держится, оно разгорается цветом обложки и чуть увеличивается, потом мягко гаснет.
    private sealed class Emph
    {
        public FrameworkElement El = null!;
        public readonly ScaleTransform Sc = new(1, 1);
        public DropShadowEffect? Fx;
        public double T0, T1;        // когда слово поётся
        public double Es;            // сила выделения самого слова (0..1)
        public double Eh, H;         // последнее слово перед паузой: сила и сколько секунд его держат
        public double G = -1;        // текущее свечение, чтобы не дёргать эффект без нужды
    }

    private const double Feather = 0.45;
    private static readonly Color DimC = Color.FromArgb(0x55, 255, 255, 255);

    // Плавность строк: начинают двигаться мягко (без рывка с места), долго и ровно доезжают
    private static readonly IEasingFunction LineEase = new BezierEase(0.3, 0.05, 0.15, 1);
    private const int ScrollMs = 820, LineMs = 650;

    private readonly List<List<Emph>> _emph = new();
    // следующая строка заранее чуть приподнимается и подрастает, а когда становится текущей, плавно доезжает вместе со всеми
    private readonly List<TranslateTransform> _preT = new();
    private readonly List<ScaleTransform> _preS = new();
    private int _preIdx = -1;
    private DateTime _activatedAt = DateTime.MinValue;
    private bool _playing;
    private double[] _lineOp = Array.Empty<double>(), _lineSc = Array.Empty<double>(), _lineBlur = Array.Empty<double>();
    private List<(double t, string s)>? _lyrShown;
    private bool _hasLyrics, _lyrFresh;
    private int _idx = -2;
    private double _lyrFont = 36, _lyrMargin = 9;

    // состояние
    private string? _title, _artist;
    private BitmapSource? _coverSrc;
    private bool _coverInit;
    private bool? _wasPlaying;
    private int _volTick;

    // снимок позиции трека: читаем раз в 0,4 с, между чтениями считаем сами
    private TimeSpan _snapPos, _snapStart, _snapEnd;
    private DateTimeOffset _snapAt;
    private DateTime _snapRead = DateTime.MinValue;
    private double _total;

    private static SolidColorBrush MakeBrush(byte a, byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        br.Freeze();
        return br;
    }

    // ошибки в покадровом коде пишем в crash.log по одному разу, иначе лог забьётся за секунду
    private string? _lastErr;
    private void LogOnce(Exception ex)
    {
        string key = ex.GetType().Name + ": " + ex.Message;
        if (key == _lastErr) return;
        _lastErr = key;
        App.Log("Player", ex);
    }

    private static string Fmt(double sec) => $"{(int)sec / 60}:{(int)sec % 60:00}";

    private static IEasingFunction Out() => new CubicEase { EasingMode = EasingMode.EaseOut };

    public PlayerWindow(MainWindow host)
    {
        _host = host;
        _bgFront = _bgA;

        Title = Loc.T("ShyFlow: плеер");
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var mr = _host.MonitorDip;
        Left = mr.Left; Top = mr.Top; Width = mr.Width; Height = mr.Height;
        Background = Brushes.Black;
        UseLayoutRounding = true;
        FontFamily = _host.PlayerFont ?? new FontFamily("Segoe UI Variable Display, Segoe UI");   // свой шрифт, если выбран (Customize.cs)

        _root.Opacity = 0;
        _root.RenderTransformOrigin = new Point(0.5, 0.5);
        _root.RenderTransform = _rootScale;
        Content = _root;

        BuildBackground();
        BuildLyricsPanel();
        _playBtn = BuildLeft();
        BuildCloseButton();
        BuildExtras();

        _timer.Tick += (_, _) => Tick();
        SizeChanged += (_, _) => ApplyLayout();
        Loaded += (_, _) =>
        {
            ApplyLayout();
            _timer.Start();
            CompositionTarget.Rendering += OnRender;
            Tick();
            _root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));
            var z = new DoubleAnimation(1.04, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = Out() };
            _rootScale.BeginAnimation(ScaleTransform.ScaleXProperty, z);
            _rootScale.BeginAnimation(ScaleTransform.ScaleYProperty, z);
            Activate();
            Focus();
        };
    }

    // ---------- построение ----------
    private static Brush FallbackBg() =>
        new LinearGradientBrush(Color.FromRgb(0x2A, 0x2A, 0x32), Color.FromRgb(0x0A, 0x0A, 0x0C), 90);

    private void BuildBackground()
    {
        foreach (var b in new[] { _bgA, _bgB })
        {
            b.IsHitTestVisible = false;
            b.Opacity = 0;
            _root.Children.Add(b);
        }
        _bgA.Background = FallbackBg();
        _bgA.Opacity = 1;
        Panel.SetZIndex(_bgA, 1);
        _shade.IsHitTestVisible = false;
        _shade.Background = new LinearGradientBrush(Color.FromArgb(0x30, 0, 0, 0), Color.FromArgb(0x90, 0, 0, 0), 90);
        Panel.SetZIndex(_shade, 2);
        _root.Children.Add(_shade);
    }

    private void BuildLyricsPanel()
    {
        _lyrPanel.HorizontalAlignment = HorizontalAlignment.Right;
        _lyrPanel.Opacity = 0;
        _lyrPanel.IsHitTestVisible = false;
        _lyrPanel.OpacityMask = new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(Colors.Transparent, 0),
            new GradientStop(Colors.Black, 0.12),
            new GradientStop(Colors.Black, 0.88),
            new GradientStop(Colors.Transparent, 1)
        }, 90);
        _lyrCanvas.ClipToBounds = true;
        Canvas.SetLeft(_lyrStack, 0);
        Canvas.SetTop(_lyrStack, 0);
        _lyrCanvas.Children.Add(_lyrStack);
        _lyrPanel.Children.Add(_lyrCanvas);
        Panel.SetZIndex(_lyrPanel, 3);
        _root.Children.Add(_lyrPanel);
        HookLyricScroll();   // листать текст колесом и мышью (PlayerScroll.cs)
    }

    private Border IconBtn(string glyph, double fontSize, double box, Action click)
    {
        var tb = new VIcon
        {
            Text = glyph, FontSize = fontSize, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        var scale = new ScaleTransform(1, 1);
        var b = new SBorder
        {
            Width = box, Height = box, CornerRadius = new CornerRadius(box / 2), Background = Brushes.Transparent,
            Child = tb, Cursor = Cursors.Hand, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale,
            Margin = new Thickness(6, 0, 6, 0)
        };
        b.MouseEnter += (_, _) => b.Background = Hov;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) =>
        {
            var a = new DoubleAnimation(0.78, 1, TimeSpan.FromMilliseconds(480))
            { EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 4 } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            e.Handled = true;
            click();
        };
        return b;
    }

    private Border BuildLeft()
    {
        _left.HorizontalAlignment = HorizontalAlignment.Center;
        _left.VerticalAlignment = VerticalAlignment.Center;
        _left.RenderTransform = _leftShift;
        Panel.SetZIndex(_left, 4);

        // обложка
        _cover.Background = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x30));
        // обе стороны обложки (сама обложка и стикер на обороте) лежат в _faces: поворот, наклон и тень общие
        _faces.RenderTransformOrigin = new Point(0.5, 0.5);
        _faces.RenderTransform = new TransformGroup { Children = { _coverFlipX, _tiltScale, _tiltSkew, _coverScale } };
        _faces.Effect = new DropShadowEffect
        { Color = Colors.Black, BlurRadius = 60, ShadowDepth = 18, Direction = 270, Opacity = 0.55 };
        _coverGlyph.Text = "\uE8D6";
        _coverGlyph.Weight = 0.4;
        _coverGlyph.Foreground = MakeBrush(0x55, 255, 255, 255);
        _coverGlyph.HorizontalAlignment = HorizontalAlignment.Center;
        _coverGlyph.VerticalAlignment = VerticalAlignment.Center;
        _glareBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, 255, 255, 255), 0));
        _glareBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        _glare.Background = _glareBrush;
        _glare.Opacity = 0;
        _glare.IsHitTestVisible = false;
        var coverInner = new Grid();
        coverInner.Children.Add(_coverGlyph);
        coverInner.Children.Add(_glare);
        _cover.Child = coverInner;
        _faces.Children.Add(_cover);
        _faces.Children.Add(BuildBack());
        _faces.MouseMove += Cover_Move;
        _faces.MouseLeave += (_, _) => Cover_Leave();
        // клик по обложке переворачивает её: на обороте стикер из острова (гифка, картинка или видео)
        _faces.Cursor = Cursors.Hand;
        _faces.MouseLeftButtonUp += (_, e) => { e.Handled = true; _wantBack = !_wantBack; FlipCover(null); };
        _coverHost.Children.Add(_faces);
        _left.Children.Add(_coverHost);

        // название и исполнитель
        _titleTb.Foreground = Brushes.White;
        _titleTb.FontWeight = FontWeights.SemiBold;
        _titleTb.TextTrimming = TextTrimming.CharacterEllipsis;
        _titleHost.Margin = new Thickness(0, 30, 0, 0);
        _titleTb.RenderTransform = _titleT;
        _artistTb.Foreground = Soft;
        _artistTb.TextTrimming = TextTrimming.CharacterEllipsis;
        _artistHost.Margin = new Thickness(0, 2, 0, 0);
        _artistTb.RenderTransform = _artistT;
        // поверх текста лежит прозрачный слой, на нём рисуются отдельные буквы при смене трека (если включено в настройках)
        _titleFx.IsHitTestVisible = false;
        _artistFx.IsHitTestVisible = false;
        _titleHost.Children.Add(_titleTb);
        _titleHost.Children.Add(_titleFx);
        _artistHost.Children.Add(_artistTb);
        _artistHost.Children.Add(_artistFx);
        _titleSwap = new LetterSwap(_titleTb, _titleFx);
        _artistSwap = new LetterSwap(_artistTb, _artistFx);
        _left.Children.Add(_titleHost);
        _left.Children.Add(_artistHost);

        // перемотка
        _seek.Margin = new Thickness(0, 20, 0, 0);
        _seek.Moving += v => { if (_total > 1) _tL.Text = Fmt(v * _total); };
        _seek.Committed += v => SeekToSeconds(v * _total);
        _left.Children.Add(_seek);
        _tL.Foreground = Dim; _tL.FontSize = 13;
        _tR.Foreground = Dim; _tR.FontSize = 13; _tR.HorizontalAlignment = HorizontalAlignment.Right;
        var times = new Grid { Margin = new Thickness(0, -2, 0, 0) };
        times.Children.Add(_tL);
        times.Children.Add(_tR);
        _left.Children.Add(times);

        // кнопки
        var play = IconBtn("play", 30, 84, Toggle);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        // ★ забирает обложку в коллекцию карточек, справа открывается сама коллекция (PlayerCards.cs)
        _starBtn = IconBtn("star", 19, 52, CollectCurrent);
        _starBtn.ToolTip = Loc.T("В коллекцию");
        row.Children.Add(_starBtn);
        row.Children.Add(IconBtn("skip-back", 22, 64, () => Skip(-1)));
        row.Children.Add(play);
        row.Children.Add(IconBtn("skip-forward", 22, 64, () => Skip(1)));
        var coll = IconBtn("layout-grid", 18, 52, CollectionWindow.ShowCollection);
        coll.ToolTip = Loc.T("Коллекция карточек");
        row.Children.Add(coll);
        // шаг 49: карточка «Сейчас играет» для сторис (Main\Story.cs)
        var story = IconBtn("share", 18, 52, () => _host.MakeStory());
        story.ToolTip = Loc.T("Карточка для сторис");
        row.Children.Add(story);
        _left.Children.Add(row);

        // громкость
        _volIcon.Text = "\uE767";
        _volIcon.FontSize = 18;
        _volIcon.Foreground = Soft;
        _volIcon.VerticalAlignment = VerticalAlignment.Center;
        _volIcon.Margin = new Thickness(0, 0, 12, 0);
        _vol.VerticalAlignment = VerticalAlignment.Center;
        _vol.Moving += v =>
        {
            try { var d = _host.VolumeDevice; if (d != null) d.AudioEndpointVolume.MasterVolumeLevelScalar = (float)v; } catch { }
        };
        var vrow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
        vrow.Children.Add(_volIcon);
        vrow.Children.Add(_vol);
        _left.Children.Add(vrow);

        _root.Children.Add(_left);
        return play;
    }

    private void BuildCloseButton()
    {
        // Шаг 71: вместо светофора macOS (красный и жёлтый делали одно и то же, зелёный не работал) одна своя кнопка:
        // стеклянный кружок со стрелкой вниз, «свернуть в остров». При наведении светлеет, стрелка чуть опускается.
        var icon = new VIcon
        {
            Text = "chevron-down", FontSize = 18, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        var iconMove = new TranslateTransform();
        icon.RenderTransform = iconMove;
        var bg = new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255));
        var scale = new ScaleTransform(1, 1);
        var btn = new SBorder
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = bg, Child = icon,
            BorderBrush = MakeBrush(0x22, 255, 255, 255), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(26, 26, 0, 0), Cursor = Cursors.Hand, ToolTip = Loc.T("Свернуть в остров (Esc)", "Back to the island (Esc)"),
            Opacity = 0.8, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale
        };
        void Scale(double to, int ms)
        {
            var an = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, an);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, an);
        }
        btn.MouseEnter += (_, _) =>
        {
            btn.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
            bg.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0x38, 255, 255, 255), TimeSpan.FromMilliseconds(150)));
            iconMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(2, TimeSpan.FromMilliseconds(220)));
            Scale(1.08, 260);
        };
        btn.MouseLeave += (_, _) =>
        {
            btn.BeginAnimation(OpacityProperty, new DoubleAnimation(0.8, TimeSpan.FromMilliseconds(220)));
            bg.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0x1F, 255, 255, 255), TimeSpan.FromMilliseconds(220)));
            iconMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)));
            Scale(1, 260);
        };
        btn.MouseLeftButtonDown += (_, e) => { e.Handled = true; Scale(0.9, 110); };
        btn.MouseLeftButtonUp += (_, e) => { e.Handled = true; Scale(1, 200); CloseAnimated(); };
        Panel.SetZIndex(btn, 5);
        _root.Children.Add(btn);
    }

    // ---------- размеры ----------
    private void ApplyLayout()
    {
        double W = ActualWidth, H = ActualHeight;
        if (W <= 0 || H <= 0) return;
        double S = Math.Clamp(Math.Min(H * 0.44, W * 0.32), 240, 520);
        _left.Width = S;
        _coverHost.Width = _coverHost.Height = S;
        _cover.CornerRadius = new CornerRadius(S * 0.035 + 6);
        _glare.CornerRadius = _cover.CornerRadius;
        _back.CornerRadius = _cover.CornerRadius;
        _coverGlyph.FontSize = S * 0.3;
        _titleTb.FontSize = Math.Clamp(S * 0.062, 20, 30);
        _artistTb.FontSize = Math.Clamp(S * 0.046, 15, 22);
        _vol.Width = S * 0.55;
        ExtrasLayout(S, W);

        _lyrPanel.Width = W * 0.46;
        _lyrPanel.Margin = new Thickness(0, 0, W * 0.04, 0);
        _lyrStack.Width = W * 0.46;
        _lyrFont = Math.Clamp(H * 0.044, 26, 52);
        _lyrMargin = _lyrFont * 0.26;
        foreach (var line in _words)
            foreach (var w in line) { w.Tb.FontSize = _lyrFont; w.Tb.Margin = new Thickness(0, 0, _lyrFont * 0.28, 0); }
        foreach (var wr in _wraps) wr.Margin = new Thickness(0, _lyrMargin, 0, _lyrMargin);
        foreach (var tt in _trTbs) { tt.FontSize = _lyrFont * 0.5; tt.Margin = new Thickness(0, _lyrFont * 0.12, 0, 0); }

        _leftShift.BeginAnimation(TranslateTransform.XProperty, null);
        _leftShift.X = _hasLyrics ? -W * 0.25 : 0;
        if (_lines.Count > 0) ScrollTo(Math.Max(_idx, 0), false);
    }

    // ---------- фон ----------
    // Обложка сжимается до 8x8, слегка насыщается и плавно растягивается: получается мягкий цветной градиент.
    private static BitmapSource? MakeBg(BitmapSource bmp)
    {
        try
        {
            const int N = 8;
            var small = new TransformedBitmap(bmp, new ScaleTransform((double)N / bmp.PixelWidth, (double)N / bmp.PixelHeight));
            var conv = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            conv.CopyPixels(px, stride, 0);
            for (int i = 0; i < px.Length; i += 4)
            {
                double b = px[i], g = px[i + 1], r = px[i + 2];
                double l = 0.299 * r + 0.587 * g + 0.114 * b;
                r = l + (r - l) * 1.35; g = l + (g - l) * 1.35; b = l + (b - l) * 1.35;
                px[i] = (byte)Math.Clamp(b * 0.62, 0, 255);
                px[i + 1] = (byte)Math.Clamp(g * 0.62, 0, 255);
                px[i + 2] = (byte)Math.Clamp(r * 0.62, 0, 255);
                px[i + 3] = 255;
            }
            const int OW = 128, OH = 72;
            var o = new byte[OW * OH * 4];
            for (int y = 0; y < OH; y++)
            {
                double fy = (y + 0.5) / OH * h - 0.5;
                int y0 = (int)Math.Floor(fy);
                double ty = fy - y0; ty = ty * ty * (3 - 2 * ty);
                int ya = Math.Clamp(y0, 0, h - 1), yb = Math.Clamp(y0 + 1, 0, h - 1);
                for (int x = 0; x < OW; x++)
                {
                    double fx = (x + 0.5) / OW * w - 0.5;
                    int x0 = (int)Math.Floor(fx);
                    double tx = fx - x0; tx = tx * tx * (3 - 2 * tx);
                    int xa = Math.Clamp(x0, 0, w - 1), xb = Math.Clamp(x0 + 1, 0, w - 1);
                    for (int c = 0; c < 3; c++)
                    {
                        double top = px[ya * stride + xa * 4 + c] * (1 - tx) + px[ya * stride + xb * 4 + c] * tx;
                        double bot = px[yb * stride + xa * 4 + c] * (1 - tx) + px[yb * stride + xb * 4 + c] * tx;
                        o[(y * OW + x) * 4 + c] = (byte)(top * (1 - ty) + bot * ty);
                    }
                    o[(y * OW + x) * 4 + 3] = 255;
                }
            }
            var res = BitmapSource.Create(OW, OH, 96, 96, PixelFormats.Bgra32, null, o, OW * 4);
            res.Freeze();
            return res;
        }
        catch { return null; }
    }

    private void SetCover(BitmapSource? bmp)
    {
        bool had = _coverInit;
        _coverInit = true;
        _coverSrc = bmp;
        void ApplyFace()
        {
            if (bmp != null)
            {
                var br = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                RenderOptions.SetBitmapScalingMode(br, BitmapScalingMode.HighQuality);
                _cover.Background = br;
                _coverGlyph.Visibility = Visibility.Collapsed;
            }
            else
            {
                _cover.Background = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x30));
                _coverGlyph.Visibility = Visibility.Visible;
            }
        }
        // смена трека: обложка сжимается в ребро, меняется картинка и разворачивается новой стороной
        if (had && _host.CoverFlip) FlipCover(ApplyFace); else ApplyFace();

        // новый фон ложится сверху и проявляется, старый потом гасим
        var old = _bgFront;
        var next = old == _bgA ? _bgB : _bgA;
        var bg = bmp != null ? MakeBg(bmp) : null;
        SetBackBackdrop(bg);
        if (bg != null)
        {
            var ib = new ImageBrush(bg) { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(ib, BitmapScalingMode.Linear);
            next.Background = ib;
        }
        else next.Background = FallbackBg();
        Panel.SetZIndex(next, 1);
        Panel.SetZIndex(old, 0);
        _bgFront = next;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700)) { EasingFunction = Out() };
        fade.Completed += (_, _) =>
        {
            if (old == _bgFront) return;
            old.BeginAnimation(OpacityProperty, null);
            old.Opacity = 0;
        };
        next.BeginAnimation(OpacityProperty, fade);

        if (had) AnimateCoverScale(_wasPlaying == true ? 1.0 : 0.86, 0.9, 520);
    }

    // объёмная обложка: лёгкий наклон за курсором, блик и сдвиг тени
    private void Cover_Move(object sender, MouseEventArgs e)
    {
        if (!_host.Cover3D) return;
        double w = _coverHost.ActualWidth, h = _coverHost.ActualHeight;
        if (w < 10 || h < 10) return;
        var p = e.GetPosition(_coverHost);
        double nx = Math.Clamp(p.X / w * 2 - 1, -1, 1), ny = Math.Clamp(p.Y / h * 2 - 1, -1, 1);
        _tiltScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _tiltScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _tiltSkew.BeginAnimation(SkewTransform.AngleXProperty, null);
        _tiltSkew.BeginAnimation(SkewTransform.AngleYProperty, null);
        _tiltScale.ScaleX = 1 - 0.045 * Math.Abs(nx);
        _tiltScale.ScaleY = 1 - 0.03 * Math.Abs(ny);
        _tiltSkew.AngleY = -nx * 3.5;
        _tiltSkew.AngleX = ny * 2.5;
        _glareBrush.Center = _glareBrush.GradientOrigin = new Point(0.5 + nx * 0.4, 0.5 + ny * 0.4);
        if (!_tilting)
        {
            _tilting = true;
            _glare.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        }
        if (_faces.Effect is DropShadowEffect fx)
        {
            double dx = -nx * 14, dy = 18 - ny * 8;
            double ang = Math.Atan2(-dy, dx) * 180 / Math.PI;
            if (ang < 0) ang += 360;
            fx.BeginAnimation(DropShadowEffect.ShadowDepthProperty, null);
            fx.BeginAnimation(DropShadowEffect.DirectionProperty, null);
            fx.ShadowDepth = Math.Sqrt(dx * dx + dy * dy);
            fx.Direction = ang;
        }
    }

    private void Cover_Leave()
    {
        if (!_tilting) return;
        _tilting = false;
        var d = TimeSpan.FromMilliseconds(420);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _tiltScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, d) { EasingFunction = ease });
        _tiltScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, d) { EasingFunction = ease });
        _tiltSkew.BeginAnimation(SkewTransform.AngleXProperty, new DoubleAnimation(0, d) { EasingFunction = ease });
        _tiltSkew.BeginAnimation(SkewTransform.AngleYProperty, new DoubleAnimation(0, d) { EasingFunction = ease });
        _glare.BeginAnimation(OpacityProperty, new DoubleAnimation(0, d));
        if (_faces.Effect is DropShadowEffect fx)
        {
            fx.BeginAnimation(DropShadowEffect.ShadowDepthProperty, new DoubleAnimation(18, d) { EasingFunction = ease });
            fx.BeginAnimation(DropShadowEffect.DirectionProperty, new DoubleAnimation(270, d) { EasingFunction = ease });
        }
    }

    // apply: что поменять на обложке, пока она стоит ребром (новый трек), или null: просто перевернуть на другую сторону
    // (_wantBack). Если переворот уже идёт, новая картинка подменяет ту, что ждёт своей очереди.
    private bool _flipping;

    private void FlipCover(Action? apply)
    {
        if (apply != null) _pendingCover = apply;
        if (_flipping) return;
        _flipping = true;
        var half = new DoubleAnimation(_coverFlipX.ScaleX, 0, TimeSpan.FromMilliseconds(170))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        half.Completed += (_, _) =>
        {
            var a = _pendingCover; _pendingCover = null;
            a?.Invoke();
            ApplySide();
            _flipping = false;
            _coverFlipX.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(480))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } });
            if (_host.Cover3D)
            {
                var d2 = TimeSpan.FromMilliseconds(480);
                _tiltScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, 1, d2)
                { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } });
                _tiltSkew.BeginAnimation(SkewTransform.AngleYProperty, new DoubleAnimation(-7, 0, d2)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        };
        _coverFlipX.BeginAnimation(ScaleTransform.ScaleXProperty, half);
        if (_host.Cover3D) // перспектива: сторона, уходящая от нас, чуть уменьшается и кренится
        {
            _tilting = false;
            var d = TimeSpan.FromMilliseconds(170);
            _tiltScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(_tiltScale.ScaleY, 0.9, d)
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            _tiltSkew.BeginAnimation(SkewTransform.AngleYProperty, new DoubleAnimation(_tiltSkew.AngleY, 7, d)
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        }
    }

    private void AnimateCoverScale(double to, double? from = null, int ms = 550)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Out() };
        if (from != null) a.From = from;
        _coverScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        _coverScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    // ---------- трек ----------
    private static void Enter(UIElement e, TranslateTransform t, double fromX)
    {
        t.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(fromX, 0, TimeSpan.FromMilliseconds(500)) { EasingFunction = Out() });
        e.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));
    }

    private void SetTrack(string title, string artist)
    {
        bool first = _title == null;
        string oldTitle = _title ?? "", oldArtist = _artist ?? "";
        _title = title; _artist = artist;
        UpdateStar();
        _titleTb.Text = title;
        _artistTb.Text = artist;
        if (!first)
        {
            // по буквам: старый текст взлетает и растворяется, новый опускается сверху вниз
            bool letters = _host.LetterAnim;
            bool tDone = letters && _titleSwap != null && _titleSwap.Run(oldTitle, title);
            bool aDone = letters && _artistSwap != null && _artistSwap.Run(oldArtist, artist, 90);
            int dir = _host.SkipDirNow;
            if (!tDone) Enter(_titleTb, _titleT, dir * 36);
            if (!aDone) Enter(_artistTb, _artistT, dir * 48);
        }
    }

    // ---------- управление ----------
    private async void Toggle()
    {
        var s = _host.CurSession;
        if (s == null) return;
        try { await s.TryTogglePlayPauseAsync(); } catch (Exception ex) { App.LogOnce("Player.Toggle", ex); }
    }

    private async void Skip(int dir)
    {
        var s = _host.CurSession;
        if (s == null) return;
        _host.NoteSkip(dir);
        if (dir > 0 && _host.SkipAdInstead()) { ShowHint("rotate-ccw", Loc.T("Пропускаю рекламу…")); return; }   // реклама в Spotify (Ads.cs)
        try
        {
            if (dir > 0) await s.TrySkipNextAsync();
            else await s.TrySkipPreviousAsync();
        }
        catch (Exception ex) { App.LogOnce("Player.Skip", ex); }
    }

    private async void SeekToSeconds(double sec)
    {
        var s = _host.CurSession;
        if (s == null || _total <= 1) return;
        sec = Math.Clamp(sec, 0, _total);
        try
        {
            // сразу подставляем позицию у себя, чтобы полоска не прыгала назад, пока плеер отвечает
            _snapPos = _snapStart + TimeSpan.FromSeconds(sec);
            _snapAt = DateTimeOffset.Now;
            _snapRead = DateTime.UtcNow.AddMilliseconds(700);
            await s.TryChangePlaybackPositionAsync(_snapStart.Ticks + (long)(sec * TimeSpan.TicksPerSecond));
        }
        catch (Exception ex) { App.LogOnce("Player.Seek", ex); }
    }

    // ---------- слова ----------
    private void BuildLyrics(List<(double t, string s)>? list)
    {
        _lyrShown = list;
        _lyrStack.Children.Clear();
        _wraps.Clear();
        _lines.Clear();
        _trTbs.Clear();
        var tr = _host.TranslationNow;
        _trShown = tr;
        _lt.Clear();
        _words.Clear();
        _emph.Clear();
        _preT.Clear();
        _preS.Clear();
        _preIdx = -1;
        _idx = -2;
        _lyrFresh = true;
        ResetManualScroll();   // новый текст: ручная прокрутка прошлого не действует (PlayerScroll.cs)
        bool letters = _host.LetterAnim;
        _lettersBuilt = letters;
        bool has = list != null && list.Count > 0;
        var lineWords = _host.CurWords;
        if (has)
        {
            int n = list!.Count;
            // шаг 76: дуэт — строки второго голоса справа, метки голосов не показываем (Services\Duet.cs)
            var duet = Duet.Analyze(list.Select(x => x.s).ToList(), _artist);
            for (int i = 0; i < n; i++)
            {
                string text = duet.text[i];
                bool right = duet.side != null && duet.side[i] == 1;
                double t0 = list[i].t;
                double t1 = i + 1 < n ? list[i + 1].t : t0 + 4;
                var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) parts = new[] { "♪" };

                // точное время слов (NetEase, Kugou), если источник его дал; иначе окно строки делится между словами по слогам
                var wt = lineWords != null && i < lineWords.Count ? lineWords[i] : null;
                if (wt != null && wt.Count != parts.Length) wt = null;
                double win = wt != null ? Math.Max(0.3, wt.End[^1]) : LyricsEngine.LineWindow(text, t0, t1);
                var wts = LyricsEngine.WordWeights(parts);
                double total = wts.Sum(), acc = 0;
                int wi = 0;
                double letterSmooth = 0.5;
                var letterTimes = !letters ? null
                    : wt != null ? LetterTiming.Within(parts, t0, wt, out letterSmooth)
                    : LetterTiming.Words(parts, t0, win, out letterSmooth);

                // для выделения слов: темп строки (секунд на слог) и пауза после неё (там часто тянут последнее слово)
                bool interlude = LyricsEngine.IsInterlude(text) || string.IsNullOrWhiteSpace(text);   // ● ● ● и пустые строки (♪) не выделяем
                double pace = win / Math.Max(0.001, total);
                double pause = t1 - (t0 + win);

                var preS = new ScaleTransform(1, 1);
                var preT = new TranslateTransform();
                var preG = new TransformGroup();
                preG.Children.Add(preS);
                preG.Children.Add(preT);
                var panel = new WrapPanel
                {
                    RenderTransform = preG, RenderTransformOrigin = new Point(right ? 1 : 0, 0.5),
                    HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left
                };
                var ws = new List<Word>();
                var em = new List<Emph>();
                for (int pi = 0; pi < parts.Length; pi++)
                {
                    string part = parts[pi];
                    double dur = wts[wi++] / total * win;
                    double wT0 = t0 + acc;
                    acc += dur;
                    if (wt != null) { wT0 = t0 + wt.Start[pi]; dur = Math.Max(0.05, wt.End[pi] - wt.Start[pi]); }
                    // со временем слов темп свой у каждого слова: секунд на слог. Протяжное слово видно прямо по нему
                    double wordPace = wt != null ? dur / Math.Max(1, LyricsEngine.Syl(part)) : pace;
                    double es = interlude ? 0 : Emphasis(part, parts, wordPace);
                    double eh = 0, hold = 0;
                    if (!interlude && wt == null && pi == parts.Length - 1 && pause >= 1.2)
                    {
                        hold = Math.Min(pause * 0.8, 3);
                        eh = Math.Clamp((pause - 1.2) / 2.5, 0.2, 0.6);
                    }
                    bool emphasized = es > 0.12 || eh > 0;

                    FrameworkElement el;
                    if (letters)
                    {
                        // режим «по буквам»: слово = ряд отдельных букв; время каждой буквы идёт за слогами (гласные дольше)
                        var els = LetterTiming.Elements(part);
                        var sp = new StackPanel
                        {
                            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, _lyrFont * 0.28, 0)
                        };
                        var starts = letterTimes![wi - 1];
                        for (int k = 0; k < els.Count; k++)
                        {
                            var lw = new Word { Letter = true, T0 = starts[k], T1 = starts[k] + letterSmooth, Boost = Math.Max(es, eh) };
                            lw.Tb = new TextBlock
                            {
                                Text = els[k], Foreground = Brushes.White, FontSize = _lyrFont,
                                FontWeight = FontWeights.Bold, RenderTransform = lw.T
                            };
                            sp.Children.Add(lw.Tb);
                            ws.Add(lw);
                        }
                        panel.Children.Add(sp);
                        el = sp;
                    }
                    else
                    {
                        var w = new Word { T0 = wT0, T1 = wT0 + Math.Max(dur, 0.18) };
                        w.Tb = new TextBlock
                        {
                            Text = part, Foreground = Brushes.White, FontSize = _lyrFont, FontWeight = FontWeights.Bold,
                            Margin = new Thickness(0, 0, _lyrFont * 0.28, 0), RenderTransform = w.T
                        };
                        panel.Children.Add(w.Tb);
                        ws.Add(w);
                        el = w.Tb;
                    }

                    if (emphasized)
                    {
                        var emp = new Emph { El = el, T0 = wT0, T1 = wT0 + Math.Max(dur, 0.25), Es = es, Eh = eh, H = hold };
                        // увеличение от низа середины слова: строка не прыгает, слово растёт вверх и в стороны
                        if (el.RenderTransform is Transform old && old != Transform.Identity)
                        {
                            var g = new TransformGroup();
                            g.Children.Add(emp.Sc);
                            g.Children.Add(old);
                            el.RenderTransform = g;
                        }
                        else el.RenderTransform = emp.Sc;
                        el.RenderTransformOrigin = new Point(0.5, 0.8);
                        em.Add(emp);
                    }
                }

                // строка + (если включён перевод) её перевод мелким шрифтом; прозрачность, масштаб и размытие идут на весь блок
                var unit = new StackPanel
                {
                    Opacity = 0.4, RenderTransformOrigin = new Point(right ? 1 : 0, 0.5), RenderTransform = new ScaleTransform(0.9, 0.9),
                    HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Stretch
                };
                unit.Children.Add(panel);
                if (tr != null && i < tr.Count && !string.IsNullOrWhiteSpace(tr[i])
                    && !string.Equals(tr[i].Trim(), text.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    var ttb = new TextBlock
                    {
                        Text = tr[i], Foreground = new SolidColorBrush(Color.FromArgb(0xB4, 255, 255, 255)),
                        FontSize = _lyrFont * 0.5, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, _lyrFont * 0.12, 0, 0), TextAlignment = right ? TextAlignment.Right : TextAlignment.Left
                    };
                    unit.Children.Add(ttb);
                    _trTbs.Add(ttb);
                }

                var lt = new TranslateTransform();
                var wrap = new SBorder
                {
                    Child = unit, Margin = new Thickness(0, _lyrMargin, 0, _lyrMargin),
                    Padding = new Thickness(14, 4, 14, 4), CornerRadius = new CornerRadius(14),
                    Background = Brushes.Transparent, Cursor = Cursors.Hand, RenderTransform = lt
                };
                wrap.MouseEnter += (_, _) => wrap.Background = Hov;
                wrap.MouseLeave += (_, _) => wrap.Background = Brushes.Transparent;
                wrap.MouseLeftButtonUp += (_, e) => { if (_host.SeekClick) SeekToSeconds(t0); e.Handled = true; };
                // правый клик: «эта строка поётся сейчас», текст с этой строки подгоняется под текущую секунду
                int li = i;
                wrap.MouseRightButtonUp += (_, e) => { _host.SyncLineNow(li, CurSeconds()); e.Handled = true; };
                wrap.ToolTip = Loc.T("Клик: перемотать сюда. Правый клик: подогнать время, если строка поётся сейчас");
                _lyrStack.Children.Add(wrap);
                _wraps.Add(wrap);
                _lines.Add(unit);
                _lt.Add(lt);
                _words.Add(ws);
                _emph.Add(em);
                _preT.Add(preT);
                _preS.Add(preS);
            }
            _lineOp = Enumerable.Repeat(-1.0, n).ToArray();
            _lineSc = Enumerable.Repeat(-1.0, n).ToArray();
            _lineBlur = Enumerable.Repeat(-1.0, n).ToArray();
        }
        _hasLyrics = has;
        _lyrPanel.IsHitTestVisible = has;
        _lyrPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(has ? 1 : 0, TimeSpan.FromMilliseconds(450)));
        _leftShift.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(has ? -ActualWidth * 0.25 : 0, TimeSpan.FromMilliseconds(650)) { EasingFunction = Out() });
    }

    private void StyleLine(int i, double op, double sc, bool animate)
    {
        var ms = TimeSpan.FromMilliseconds(LineMs);
        var tb = _lines[i];
        if (Math.Abs(_lineOp[i] - op) > 0.001)
        {
            _lineOp[i] = op;
            if (animate) tb.BeginAnimation(OpacityProperty, new DoubleAnimation(op, ms) { EasingFunction = LineEase });
            else { tb.BeginAnimation(OpacityProperty, null); tb.Opacity = op; }
        }
        if (Math.Abs(_lineSc[i] - sc) > 0.001)
        {
            _lineSc[i] = sc;
            var st = (ScaleTransform)tb.RenderTransform;
            if (animate)
            {
                var a = new DoubleAnimation(sc, ms) { EasingFunction = LineEase };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
            else
            {
                st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                st.ScaleX = st.ScaleY = sc;
            }
        }
    }

    // активная строка светится цветом обложки, остальные тем сильнее размыты, чем дальше от неё (как в Apple Music)
    private void StyleEffect(int i, int d, bool active, bool animate)
    {
        if (i < 0 || i >= _lines.Count || i >= _lineBlur.Length) return;
        var panel = _lines[i];
        if (active)
        {
            _lineBlur[i] = -2;
            if (!_host.LyricGlow) { panel.Effect = null; return; }
            var gl = new DropShadowEffect { Color = _host.AccentNow, BlurRadius = 26, ShadowDepth = 0, Opacity = 0 };
            panel.Effect = gl;
            gl.BeginAnimation(DropShadowEffect.OpacityProperty,
                new DoubleAnimation(0, 0.5, TimeSpan.FromMilliseconds(animate ? 650 : 1)));
            return;
        }
        double target = !_host.LyricBlur || d >= 7 ? 0 : d == 1 ? 1.2 : d == 2 ? 2.6 : d == 3 ? 4.0 : 5.0;
        if (Math.Abs(_lineBlur[i] - target) < 0.01) return;
        if (target <= 0)
        {
            panel.Effect = null;
            _lineBlur[i] = 0;
            return;
        }
        double from = _lineBlur[i] >= 0 ? _lineBlur[i] : 0;
        var bl = new BlurEffect { Radius = from, RenderingBias = RenderingBias.Performance };
        panel.Effect = bl;
        if (animate) bl.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(450)));
        else bl.Radius = target;
        _lineBlur[i] = target;
    }

    private void SetIndex(int idx, bool animate)
    {
        if (_idx >= 0 && _idx < _words.Count) ResetWords(_idx, animate);
        // приподнятая заранее строка доезжает на место вместе с прокруткой (та же кривая и длительность),
        // поэтому движение получается одним ровным ходом вверх без остановки
        if (_preIdx >= 0) SettlePre(_preIdx, animate);
        _preIdx = -1;
        _idx = idx;
        _activatedAt = DateTime.UtcNow;
        if (idx >= 0 && idx < _words.Count) ActivateWords(idx);
        for (int i = 0; i < _lines.Count; i++)
        {
            if (_manualScroll) { ManualLook(i); continue; }   // листают: все строки ярко, без размытия (PlayerScroll.cs)
            int d = Math.Abs(i - idx);
            double op = idx < 0 ? 0.4 : d == 0 ? 1.0 : d == 1 ? 0.5 : d == 2 ? 0.34 : 0.22;
            double sc = idx >= 0 && d == 0 ? 1.0 : 0.9;
            StyleLine(i, op, sc, animate);
            StyleEffect(i, d, idx >= 0 && d == 0, animate);
        }
        ScrollTo(Math.Max(idx, 0), animate);
    }

    // активная строка держится чуть выше середины экрана.
    // Строки сдвигаются «волной»: чем ниже строка, тем позже она трогается с места.
    // где должна стоять вся колонка, чтобы строка i была чуть выше середины
    private double TargetFor(int i)
    {
        i = Math.Clamp(i, 0, _wraps.Count - 1);
        double y = 0;
        for (int j = 0; j < i; j++) y += _wraps[j].ActualHeight + _lyrMargin * 2;
        y += _lyrMargin + _wraps[i].ActualHeight / 2;
        return _lyrCanvas.ActualHeight * 0.40 - y;
    }

    private void ScrollTo(int i, bool animate)
    {
        if (_wraps.Count == 0 || !IsLoaded) return;
        if (_manualScroll) return;   // человек листает сам (PlayerScroll.cs): вернёмся к строке, когда отпустит
        i = Math.Clamp(i, 0, _wraps.Count - 1);
        _lyrStack.UpdateLayout();
        double target = TargetFor(i);
        for (int j = 0; j < _lt.Count; j++)
        {
            var t = _lt[j];
            if (animate && Math.Abs(j - i) <= 12)
            {
                double delay = j > i ? Math.Min((j - i) * 45, 380) : 0;
                t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(ScrollMs))
                {
                    EasingFunction = LineEase,
                    BeginTime = TimeSpan.FromMilliseconds(delay)
                });
            }
            else
            {
                t.BeginAnimation(TranslateTransform.YProperty, null);
                t.Y = target;
            }
        }
    }

    // ---------- слова: волна подсветки ----------
    // Раньше при смене строки все её слова мгновенно опускались на 5-10 px и тускнели, а потом каждое резко
    // подпрыгивало на место: короткое слово за 70 мс. Теперь слова стоят на месте и плавно приподнимаются
    // на пару пикселей по своим часам (не быстрее 0,45 с), а тусклость непропетых слов наплывает за полсекунды.
    private const double DimFadeSec = 0.45;

    private static double Smoother(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * x * (x * (x * 6 - 15) + 10);
    }

    private void ActivateWords(int i)
    {
        foreach (var w in _words[i])
        {
            w.T.BeginAnimation(TranslateTransform.YProperty, null);   // снять «возврат на место» от прошлого раза
            w.P = -1;
            if (w.Letter)
            {
                w.Sb = new SolidColorBrush(Colors.White);
                w.Tb.Foreground = w.Sb;
                continue;
            }
            var br = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            br.GradientStops.Add(new GradientStop(Colors.White, 0));
            br.GradientStops.Add(new GradientStop(Colors.White, 0));
            br.GradientStops.Add(new GradientStop(Colors.White, 0));
            br.GradientStops.Add(new GradientStop(Colors.White, 1));
            w.Br = br;
            w.Tb.Foreground = br;
        }
        foreach (var e in _emph[i]) { e.G = -1; e.Sc.BeginAnimation(ScaleTransform.ScaleXProperty, null); e.Sc.BeginAnimation(ScaleTransform.ScaleYProperty, null); }
        UpdateWords(i, CurSeconds() + 0.15 - _host.LyricOffsetNow);
    }

    // строка перестала быть текущей: слова мягко садятся обратно, свечение выделенных гаснет
    private void ResetWords(int i, bool animate = true)
    {
        var back = TimeSpan.FromMilliseconds(animate ? 600 : 1);
        foreach (var w in _words[i])
        {
            w.Br = null;
            w.Sb = null;
            w.P = -1;
            w.Tb.Foreground = Brushes.White;
            if (Math.Abs(w.T.Y) > 0.01)
                w.T.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, back) { EasingFunction = Out() });
        }
        foreach (var e in _emph[i])
        {
            e.G = -1;
            var down = new DoubleAnimation(1, back) { EasingFunction = Out() };
            e.Sc.BeginAnimation(ScaleTransform.ScaleXProperty, down);
            e.Sc.BeginAnimation(ScaleTransform.ScaleYProperty, down);
            if (e.Fx is { } fx)
            {
                var off = new DoubleAnimation(0, back);
                off.Completed += (_, _) => { if (ReferenceEquals(e.El.Effect, fx)) e.El.Effect = null; };
                fx.BeginAnimation(DropShadowEffect.OpacityProperty, off);
                e.Fx = null;
            }
        }
    }

    private void UpdateWords(int i, double t)
    {
        // непропетые слова тускнеют не скачком, а вместе с тем, как строка становится яркой
        double fade = Smoother((DateTime.UtcNow - _activatedAt).TotalSeconds / DimFadeSec);
        var dim = Mix(Colors.White, DimC, fade);
        double wordLift = _lyrFont * 0.06, letterLift = _lyrFont * 0.075;
        foreach (var w in _words[i])
        {
            if (w.Letter)
            {
                if (w.Sb == null) continue;
                // буква загорается и поднимается мягкой S-кривой; окно растянуто, чтобы соседние буквы шли волной
                double win = Math.Max(0.4, (w.T1 - w.T0) * 1.4);
                double lp = Math.Clamp((t - w.T0 + 0.08) / win, 0, 1);
                double le = Smoother(lp);
                w.Sb.Color = Mix(dim, Colors.White, le);
                w.T.Y = -letterLift * (1 + 0.6 * w.Boost) * (le + 0.12 * Math.Sin(Math.PI * lp));
                continue;
            }
            if (w.Br == null) continue;
            double p = Math.Clamp((t - w.T0) / Math.Max(0.05, w.T1 - w.T0), 0, 1);
            double cx = -Feather + p * (1 + 2 * Feather);
            w.Br.GradientStops[1].Offset = Math.Clamp(cx - Feather, 0, 1);
            w.Br.GradientStops[2].Offset = Math.Clamp(cx + Feather, 0, 1);
            w.Br.GradientStops[2].Color = dim;
            w.Br.GradientStops[3].Color = dim;
            // подъём по своим часам: начинается чуть раньше слова и длится не меньше 0,45 с, поэтому без рывка
            double rise = Math.Clamp(w.T1 - w.T0, 0.45, 0.9);
            w.T.Y = -wordLift * Smoother((t - w.T0 + 0.12) / rise);
        }
        UpdateEmph(i, t);
    }

    // сила выделения слова по тексту строки
    private static double Emphasis(string w, string[] line, double pace)
    {
        double e = 0;
        double st = LyricsEngine.Stretch(w);                 // «бляяяядь», «ooooh»
        if (st > 0) e = Math.Max(e, Math.Min(1, 0.5 + st * 0.12));
        var letters = w.Where(char.IsLetter).ToArray();
        bool lineCaps = line.All(p => p.Where(char.IsLetter).All(char.IsUpper));
        if (letters.Length >= 3 && letters.All(char.IsUpper) && !lineCaps) e = Math.Max(e, 0.5);   // одно слово капсом
        if (w.EndsWith('!')) e = Math.Max(e, 0.35);
        if (pace > 0.55) e = Math.Max(e, Math.Clamp((pace - 0.55) / 0.9, 0, 1));                  // строку из 1-2 слов поют медленно
        return e;
    }

    // свечение выделенных слов: растёт, пока слово поётся и держится, потом мягко спадает до лёгкого следа
    private void UpdateEmph(int i, double t)
    {
        if (i < 0 || i >= _emph.Count) return;
        bool glow = _host.LyricGlow;
        foreach (var e in _emph[i])
        {
            double sing = e.Es * Smoother((t - e.T0) / Math.Max(0.3, e.T1 - e.T0));
            double held = e.H > 0 ? e.Eh * Smoother((t - e.T1) / e.H) : 0;
            double g = Math.Max(sing, held);
            double end = e.T1 + e.H;
            if (t > end)
            {
                double peak = Math.Max(e.Es, e.Eh);
                g = peak * (0.3 + 0.7 * Math.Exp(-(t - end) / 0.6));
            }
            if (Math.Abs(g - e.G) < 0.004) continue;
            e.G = g;
            double s = 1 + 0.07 * g;
            e.Sc.ScaleX = e.Sc.ScaleY = s;
            if (!glow || g < 0.02)
            {
                if (e.Fx != null) { e.El.Effect = null; e.Fx = null; }
                continue;
            }
            if (e.Fx == null)
            {
                var ac = _host.AccentNow;
                e.Fx = new DropShadowEffect { Color = Mix(ac, Colors.White, 0.35), ShadowDepth = 0, RenderingBias = RenderingBias.Performance };
                e.El.Effect = e.Fx;
            }
            e.Fx.BlurRadius = 8 + 22 * g;
            e.Fx.Opacity = Math.Min(1, 0.9 * g + 0.1);
        }
    }

    // следующая строка заранее приподнимается (на ~8 px) и чуть подрастает перед тем, как её запоют
    private void UpdatePre(double t)
    {
        if (_lyrShown == null) return;
        int n = _idx + 1;
        if (n != _preIdx)
        {
            if (_preIdx >= 0) SettlePre(_preIdx, true);
            _preIdx = n;
            if (n >= 0 && n < _preT.Count)
            {
                _preT[n].BeginAnimation(TranslateTransform.YProperty, null);
                _preS[n].BeginAnimation(ScaleTransform.ScaleXProperty, null);
                _preS[n].BeginAnimation(ScaleTransform.ScaleYProperty, null);
            }
        }
        if (n < 0 || n >= _preT.Count || n >= _lyrShown.Count) return;
        if (LyricsEngine.IsInterlude(_lyrShown[n].s) || string.IsNullOrWhiteSpace(_lyrShown[n].s)) return;
        double tn = _lyrShown[n].t;
        double tc = _idx >= 0 && _idx < _lyrShown.Count ? _lyrShown[_idx].t : tn - 3;
        double lead = Math.Clamp((tn - tc) * 0.45, 0.25, 0.7);
        double p = Smoother((t - (tn - lead)) / lead);
        _preT[n].Y = -_lyrFont * 0.22 * p;
        _preS[n].ScaleX = _preS[n].ScaleY = 1 + 0.025 * p;
    }

    private void SettlePre(int n, bool animate)
    {
        if (n < 0 || n >= _preT.Count) return;
        var d = TimeSpan.FromMilliseconds(animate ? ScrollMs : 1);
        _preT[n].BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, d) { EasingFunction = LineEase });
        var s = new DoubleAnimation(1, d) { EasingFunction = LineEase };
        _preS[n].BeginAnimation(ScaleTransform.ScaleXProperty, s);
        _preS[n].BeginAnimation(ScaleTransform.ScaleYProperty, s);
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    private double CurSeconds()
    {
        if (_total <= 1) return 0;
        var pos = _snapPos;
        if (_playing) pos += DateTimeOffset.Now - _snapAt;
        return Math.Clamp((pos - _snapStart).TotalSeconds, 0, _total);
    }

    // каждый кадр: нужная строка и положение волны (раз в 80 мс было бы рывками)
    private TimeSpan _rt;
    private DateTime _ecoAt;

    private void OnRender(object? sender, EventArgs e)
    {
        // одно событие кадра приходит несколько раз; в экономном режиме острова плеер тоже рисует не чаще 30 раз в секунду
        if (e is RenderingEventArgs re) { if (re.RenderingTime == _rt) return; _rt = re.RenderingTime; }
        if (_host.Eco)
        {
            var now = DateTime.UtcNow;
            if ((now - _ecoAt).TotalMilliseconds < 31) return;
            _ecoAt = now;
        }
        try
        {
            UpdateViz();
            if (!_hasLyrics || _lyrShown == null) return;
            double t = CurSeconds() + 0.15 - _host.LyricOffsetNow;
            int idx = -1;
            for (int i = 0; i < _lyrShown.Count; i++) { if (_lyrShown[i].t <= t) idx = i; else break; }
            if (idx != _idx)
            {
                SetIndex(idx, !_lyrFresh);
                _lyrFresh = false;
            }
            if (_idx >= 0 && _idx < _words.Count) UpdateWords(_idx, t);
            UpdatePre(t);
        }
        catch (Exception ex) { LogOnce(ex); }
    }

    // ---------- главный такт ----------
    private void Tick()
    {
        try
        {
            var s = _host.CurSession;
            bool has = s != null;

            string title = has ? _host.CurTitle : "Ничего не играет";
            string artist = has ? _host.CurArtist : "";
            if (title != _title || artist != _artist) SetTrack(title, artist);

            var cover = has ? _host.CurCover : null;
            if (!_coverInit || !ReferenceEquals(cover, _coverSrc)) SetCover(cover);

            var lyr = has ? _host.CurLyrics : null;
            if (!ReferenceEquals(lyr, _lyrShown) || (lyr != null && _host.LetterAnim != _lettersBuilt)
                || (lyr != null && !ReferenceEquals(_host.TranslationNow, _trShown))) BuildLyrics(lyr);

            bool playing = has && _host.IsPlaying;
            ExtrasTick(s, has);
            StickerSync();
            if (_wasPlaying != playing)
            {
                bool first = _wasPlaying == null;
                _wasPlaying = playing;
                ((VIcon)_playBtn.Child).Text = playing ? "pause" : "play";
                AnimateCoverScale(playing ? 1.0 : 0.86, null, first ? 700 : 550);
                _snapRead = DateTime.MinValue;
            }

            // позиция
            if (has && (DateTime.UtcNow - _snapRead).TotalMilliseconds > 400)
            {
                var tl = s!.GetTimelineProperties();
                _snapPos = tl.Position; _snapStart = tl.StartTime; _snapEnd = tl.EndTime; _snapAt = tl.LastUpdatedTime;
                _snapRead = DateTime.UtcNow;
            }
            if (!has) { _snapStart = _snapEnd = TimeSpan.Zero; }
            _total = (_snapEnd - _snapStart).TotalSeconds;
            bool seekable = has && _total > 1;
            _playing = playing;
            double cur = seekable ? CurSeconds() : 0;
            _seek.Wave = _host.WaveSeek;
            _seek.SetSeed(_host.CurArtist + "|" + _host.CurTitle);
            _seek.Opacity = seekable ? 1 : 0.35;
            _seek.IsHitTestVisible = seekable && _host.SeekClick;
            if (!_seek.Dragging)
            {
                _seek.Value = seekable ? cur / _total : 0;
                _tL.Text = seekable ? Fmt(cur) : "0:00";
                _tR.Text = seekable ? "-" + Fmt(_total - cur) : "";
            }

            // громкость (подтягиваем, если её изменили клавишами)
            if (++_volTick % 4 == 0 && !_vol.Dragging)
            {
                var dev = _host.VolumeDevice;
                if (dev != null)
                {
                    var ev = dev.AudioEndpointVolume;
                    float v = ev.MasterVolumeLevelScalar;
                    if (Math.Abs(v - _vol.Value) > 0.004) _vol.Value = v;
                    _volIcon.Text = ev.Mute ? "volume-x" : "volume-2";
                }
            }
        }
        catch (Exception ex) { LogOnce(ex); }
    }

    // ---------- закрытие и клавиши ----------
    private void CloseAnimated()
    {
        if (_closing) return;
        _closing = true;
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        a.Completed += (_, _) => Close();
        _root.BeginAnimation(OpacityProperty, a);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        switch (e.Key)
        {
            case Key.Escape: if (!CloseReveal()) CloseAnimated(); e.Handled = true; break;   // сначала закрывается открытая карточка
            case Key.Space: Toggle(); e.Handled = true; break;
            case Key.Left: Skip(-1); e.Handled = true; break;
            case Key.Right: Skip(1); e.Handled = true; break;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        CompositionTarget.Rendering -= OnRender;
        _host.OnPlayerClosed();
        base.OnClosed(e);
    }
}
