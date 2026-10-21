using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Island;

// Пузырь событий (шаг 52). Настройки, «Плашки», «Всё в пузырях»: громкость, яркость, смена трека, Wi-Fi, зарядка,
// «Открыто: …», уведомления и прочее показываются маленькой капсулой рядом с островом, а сам остров не раскрывается
// и продолжает показывать музыку. Капсула стоит с той же стороны, что и пузырь таймера (за ним, если он виден).
//   Мышь над капсулой: не пропадает. Клик по уведомлению: перейти в программу (как «Открыть»), по остальному: убрать.
//   Уведомления всё равно попадают в шторку.
public partial class MainWindow
{
    private readonly DispatcherTimer _evTimer = new();
    private bool _evOn;
    private NotifItem? _evNotif;
    private string _evKind = "";

    private bool EventsToBubble => _cfg.EventsInBubble && !_hidden && !_fsMini;   // шаг 76: в игре плашки в самом острове

    private void InitEventBubble()
    {
        // RenderTransformOrigin ставит Notch.cs: у края экрана пузырь растёт от него
        EvBgLayers.SizeChanged += (_, _) => ClipCapsuleBg(EvBubble, EvBgLayers);
        EvBubble.SizeChanged += (_, _) => PlaceAppsBar();
        _evTimer.Tick += (_, _) => { _evTimer.Stop(); HideEvent(); };
        EvBubble.MouseEnter += (_, _) => _evTimer.Stop();
        EvBubble.MouseLeave += (_, _) => { if (_evOn) { _evTimer.Interval = TimeSpan.FromMilliseconds(1500); _evTimer.Start(); } };
        EvBubble.MouseLeftButtonDown += (_, e) => e.Handled = true;
        EvBubble.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Sfx.Ui("tap");
            var act = _notifClick;
            var n = _evNotif;
            if (_evKind == "notif" && act != null) { _notifClick = null; act(); }
            else if (n != null && (n.Keep || n.AppId.Length > 0)) OpenNotifSource(n);
            HideEvent();
        };
    }

    // плашка (громкость, трек, сеть...) в капсуле
    private void ShowEventToast(string glyph, string title, string sub, double? value, Color col, BitmapSource? art, bool round)
    {
        _evNotif = null;
        bool same = _evOn && _evKind == "toast:" + glyph + (value.HasValue ? "v" : "");
        _evKind = "toast:" + glyph + (value.HasValue ? "v" : "");
        EvInit.Visibility = Visibility.Collapsed;
        EvImg.Visibility = Visibility.Collapsed;
        if (art != null)
        {
            EvArt.Background = new ImageBrush(art) { Stretch = round ? Stretch.UniformToFill : Stretch.Uniform };
            EvArt.Margin = new Thickness(round ? 0 : 4);
            EvArt.Visibility = Visibility.Visible;
            EvIcon.Visibility = Visibility.Collapsed;
            EvBadge.Background = round ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255));
        }
        else
        {
            EvArt.Visibility = Visibility.Collapsed;
            EvIcon.Visibility = Visibility.Visible;
            EvIcon.Text = glyph;
            EvIcon.Foreground = new SolidColorBrush(IsWhiteish(col) ? Colors.White : col);
            EvBadge.Background = new SolidColorBrush(Color.FromArgb(IsWhiteish(col) ? (byte)0x26 : (byte)0x38, col.R, col.G, col.B));
        }
        if (value is double v)
        {
            v = Math.Clamp(v, 0, 1);
            EvTitle.Visibility = Visibility.Collapsed;
            EvSub.Visibility = Visibility.Collapsed;
            EvBar.Visibility = Visibility.Visible;
            EvFill.Background = new SolidColorBrush(IsWhiteish(col) ? Colors.White : col);
            EvFill.BeginAnimation(WidthProperty, new DoubleAnimation(70 * v, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            EvValue.Text = ((int)Math.Round(v * 100)).ToString();
            EvValue.Visibility = Visibility.Visible;
        }
        else
        {
            EvBar.Visibility = Visibility.Collapsed;
            EvValue.Visibility = Visibility.Collapsed;
            EvTitle.Visibility = Visibility.Visible;
            EvTitle.Text = title;
            EvSub.Text = sub;
            EvSub.Visibility = sub.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        ShowEvent(same, value.HasValue ? 1700 : 2400);
    }

    // уведомление в капсуле: иконка программы, заголовок и текст одной строкой
    private void ShowEventNotif(NotifItem n)
    {
        _evNotif = n;
        _evKind = "notif";
        string who = !string.IsNullOrWhiteSpace(n.App) ? n.App : n.Title;
        EvArt.Visibility = Visibility.Collapsed;
        EvIcon.Visibility = Visibility.Collapsed;
        FillNotifIcon(EvBadge, EvInit, EvImg, n, who);
        EvBar.Visibility = Visibility.Collapsed;
        EvValue.Visibility = Visibility.Collapsed;
        EvTitle.Visibility = Visibility.Visible;
        EvTitle.Text = n.Title;
        string sub = string.IsNullOrWhiteSpace(n.Body) ? n.App : n.Body;
        EvSub.Text = sub;
        EvSub.Visibility = string.IsNullOrWhiteSpace(sub) ? Visibility.Collapsed : Visibility.Visible;
        Sfx.Ui("notif");
        ShowEvent(false, Math.Clamp(_cfg.NotifSeconds, 3, 30) * 1000);
    }

    private void ShowEvent(bool same, int ms)
    {
        EvBgLayers.Visibility = _cfg.BubbleIslandBg ? Visibility.Visible : Visibility.Collapsed;
        _evTimer.Stop();
        _evTimer.Interval = TimeSpan.FromMilliseconds(ms);
        if (!EvBubble.IsMouseOver) _evTimer.Start();
        if (_evOn)
        {
            // уже видна: лёгкий толчок, если событие новое
            if (!same)
            {
                var p = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = Spr() };
                EvScale.BeginAnimation(ScaleTransform.ScaleXProperty, p);
                EvScale.BeginAnimation(ScaleTransform.ScaleYProperty, p);
            }
            return;
        }
        _evOn = true;
        EvBubble.Visibility = Visibility.Visible;
        EvBubble.UpdateLayout();
        PlaceAppsBar();
        double side = BubbleLeft ? -1 : 1;
        double from = -side * (8 + BubbleSpace + Math.Max(38, EvBubble.ActualWidth));
        var d = TimeSpan.FromMilliseconds(620);
        EvIn.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, 0, d) { EasingFunction = new SpringEase(6, 11) });
        var sc = new DoubleAnimation(0.35, 1, d) { EasingFunction = new SpringEase(6, 11) };
        EvScale.BeginAnimation(ScaleTransform.ScaleXProperty, sc);
        EvScale.BeginAnimation(ScaleTransform.ScaleYProperty, sc);
        EvBubble.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }

    private void HideEvent()
    {
        _evTimer.Stop();
        if (!_evOn) return;
        _evOn = false;
        double side = BubbleLeft ? -1 : 1;
        var d = TimeSpan.FromMilliseconds(260);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        EvIn.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-side * (8 + BubbleSpace + EvBubble.ActualWidth), d) { EasingFunction = ease });
        var sc = new DoubleAnimation(0.35, d) { EasingFunction = ease };
        sc.Completed += (_, _) =>
        {
            if (_evOn) return;
            EvBubble.Visibility = Visibility.Collapsed;
            PlaceAppsBar();
        };
        EvScale.BeginAnimation(ScaleTransform.ScaleXProperty, sc);
        EvScale.BeginAnimation(ScaleTransform.ScaleYProperty, sc);
        EvBubble.BeginAnimation(OpacityProperty, new DoubleAnimation(0, d));
    }

    // за пузырём таймера (или сразу у острова, если его нет)
    private void PlaceEvent()
    {
        if (EvBubble.Visibility != Visibility.Visible) return;
        double side = BubbleLeft ? -1 : 1;
        EvMove.X = side * (Pill.Width / 2 + 8 + BubbleSpace + EvBubble.ActualWidth / 2);
        double top = Pill.Margin.Top;
        if (Math.Abs(EvBubble.Margin.Top - top) > 0.1) EvBubble.Margin = new Thickness(0, top, 0, 0);
    }

    private double EvSpace => EvBubble.Visibility == Visibility.Visible ? EvBubble.ActualWidth + 8 : 0;
}
