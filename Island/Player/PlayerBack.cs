using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Island;

// Оборот обложки и подсказки в плеере на весь экран.
//
// Клик по обложке переворачивает её (так же, как при смене трека: сжимается в ребро и разворачивается другой стороной).
// На обороте стикер из острова: гифка, картинка или видео (Настройки, «Внешний вид», «Стикер в острове»), с тем же вырезанным фоном.
// Под стикером размытая и затемнённая обложка трека, поэтому оборот выглядит частью той же карточки. Стикера нет: подсказка, где его выбрать.
//
// Подсказки: пока плеер открыт, остров спрятан, и его плашки (подгонка текста, сдвиг, таймер) показываются здесь,
// маленькой чёрной капсулой сверху, там же, где обычно остров.
public sealed partial class PlayerWindow
{
    private readonly SBorder _back = new();
    private readonly Image _stickerImg = new();
    private readonly StackPanel _noSticker = new();
    private readonly Border _backBlur = new();
    private bool _wantBack, _backShown;
    private IReadOnlyList<BitmapSource>? _stkFrames;
    private IReadOnlyList<int>? _stkDelays;
    private int _stkIdx;
    private readonly DispatcherTimer _stkTimer = new(DispatcherPriority.Render);

    private UIElement BuildBack()
    {
        _back.Visibility = Visibility.Collapsed;
        _back.Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x20));
        var inner = new Grid();
        inner.Children.Add(_backBlur);
        inner.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0)) });   // затемнение под стикером
        _stickerImg.Stretch = Stretch.Uniform;
        RenderOptions.SetBitmapScalingMode(_stickerImg, BitmapScalingMode.HighQuality);
        inner.Children.Add(_stickerImg);

        _noSticker.HorizontalAlignment = HorizontalAlignment.Center;
        _noSticker.VerticalAlignment = VerticalAlignment.Center;
        _noSticker.Children.Add(new VIcon
        {
            Text = "image", FontSize = 34, Foreground = MakeBrush(0xB0, 255, 255, 255), HorizontalAlignment = HorizontalAlignment.Center
        });
        _noSticker.Children.Add(new TextBlock
        {
            Text = Loc.T("Стикер не выбран"), Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0)
        });
        _noSticker.Children.Add(new TextBlock
        {
            Text = Loc.T("Настройки → Внешний вид → Стикер в острове"), Foreground = MakeBrush(0xA0, 255, 255, 255), FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0)
        });
        inner.Children.Add(_noSticker);
        // содержимое оборота обрезается по тем же мягким углам, что и сама карточка
        inner.SizeChanged += (_, e) => inner.Clip = Smooth.Shape(e.NewSize.Width, e.NewSize.Height, _back.CornerRadius);
        _back.Child = inner;
        _back.SizeChanged += (_, e) => _stickerImg.Margin = new Thickness(e.NewSize.Width * 0.12);

        _stkTimer.Tick += (_, _) => StickerStep();
        return _back;
    }

    // размытая обложка трека (та же, что на фоне окна) под стикером
    private void SetBackBackdrop(BitmapSource? bg)
    {
        if (bg == null) { _backBlur.Background = null; return; }
        var ib = new ImageBrush(bg) { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(ib, BitmapScalingMode.Linear);
        _backBlur.Background = ib;
    }

    // вызывается посередине переворота, когда карточка стоит ребром
    private void ApplySide()
    {
        if (_backShown == _wantBack) return;
        _backShown = _wantBack;
        _back.Visibility = _backShown ? Visibility.Visible : Visibility.Collapsed;
        _cover.Visibility = _backShown ? Visibility.Hidden : Visibility.Visible;   // Hidden: размер карточки не меняется
        StickerSync(true);
    }

    // кадры стикера берутся из острова; выбрали другой файл или поменяли вырезание фона: подхватываем на лету
    private void StickerSync(bool force = false)
    {
        var frames = _host.StickerFrames;
        if (force || !ReferenceEquals(frames, _stkFrames))
        {
            _stkFrames = frames;
            _stkDelays = _host.StickerDelays;
            _stkIdx = 0;
            bool has = frames.Count > 0;
            _stickerImg.Source = has ? frames[0] : null;
            _noSticker.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        }
        bool run = _backShown && _stkFrames != null && _stkFrames.Count > 1 && IsVisible;
        if (run && !_stkTimer.IsEnabled) { _stkTimer.Interval = FrameDelay(_stkIdx); _stkTimer.Start(); }
        else if (!run && _stkTimer.IsEnabled) _stkTimer.Stop();
    }

    private TimeSpan FrameDelay(int i) =>
        TimeSpan.FromMilliseconds(Math.Max(20, _stkDelays != null && i < _stkDelays.Count ? _stkDelays[i] : 80));

    private void StickerStep()
    {
        var f = _stkFrames;
        if (f == null || f.Count < 2 || !_backShown) { _stkTimer.Stop(); return; }
        _stkIdx = (_stkIdx + 1) % f.Count;
        _stickerImg.Source = f[_stkIdx];
        _stkTimer.Interval = FrameDelay(_stkIdx);
    }

    // ---------- подсказка сверху (вместо плашки спрятанного острова) ----------
    private SBorder? _hint;
    private VIcon? _hintIcon;
    private TextBlock? _hintText;
    private readonly TranslateTransform _hintMove = new();
    private DispatcherTimer? _hintTimer;

    internal void ShowHint(string icon, string text)
    {
        if (_hint == null)
        {
            _hintIcon = new VIcon { FontSize = 15, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            _hintText = new TextBlock { Foreground = Brushes.White, FontSize = 13.5, FontWeight = FontWeights.Medium, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(_hintIcon);
            row.Children.Add(_hintText);
            _hint = new SBorder
            {
                Background = Brushes.Black, CornerRadius = new CornerRadius(19), Padding = new Thickness(16, 9, 18, 9), MinHeight = 38,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0),
                Child = row, Opacity = 0, IsHitTestVisible = false, RenderTransform = _hintMove,
                BorderBrush = MakeBrush(0x22, 255, 255, 255), BorderThickness = new Thickness(1)
            };
            Panel.SetZIndex(_hint, 100);
            _root.Children.Add(_hint);
            _hintTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2200) };
            _hintTimer.Tick += (_, _) =>
            {
                _hintTimer.Stop();
                _hint.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)));
                _hintMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-14, TimeSpan.FromMilliseconds(260)) { EasingFunction = Out() });
            };
        }
        _hintIcon!.Text = icon;
        _hintText!.Text = text;
        _hint.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        _hintMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-24, 0, TimeSpan.FromMilliseconds(520)) { EasingFunction = new SpringEase(7, 11) });
        _hintTimer!.Stop();
        _hintTimer.Start();
    }
}
