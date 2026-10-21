using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Island;

// Таймер и секундомер (Ctrl+Alt+T или плитки в быстрых действиях).
// Пока что-то идёт, в свёрнутом острове справа видно оставшееся время, как Live Activity на iPhone.
// Клик по этой плашке открывает панель. Таймер закончился: уведомление в острове и звук три раза.
public partial class MainWindow
{
    private const double TimerH = 180;
    private static readonly Color TimerC = Color.FromRgb(0xFF, 0x9F, 0x0A);   // оранжевый, как в «Часах» iOS
    private static readonly Color WatchC = Color.FromRgb(0x34, 0xC7, 0x59);   // зелёный
    private static readonly int[] PresetMin = { 1, 5, 10, 25 };

    private bool _timerOpen;
    private int _tmTab;                 // 0 таймер, 1 секундомер
    private double _tmTotal;            // длительность таймера, с
    private double _tmLeft;             // сколько осталось, пока таймер на паузе
    private DateTime _tmEnd;            // когда закончится, пока идёт
    private bool _tmRun, _tmSet;        // идёт; заведён (идёт или на паузе)
    private TimeSpan _swAcc;            // секундомер: набежало до последней паузы
    private DateTime _swFrom;
    private bool _swRun;
    private readonly List<TimeSpan> _laps = new();
    private readonly DispatcherTimer _tmTick = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _tmBuilt;
    private int _badgeLen = -1;

    private double TmLeftNow => _tmRun ? Math.Max(0, (_tmEnd - DateTime.UtcNow).TotalSeconds) : _tmLeft;
    private TimeSpan SwNow => _swAcc + (_swRun ? DateTime.UtcNow - _swFrom : TimeSpan.Zero);

    private void StartTimers()
    {
        _tmTotal = Math.Clamp(_cfg.TimerLast, 10, 36000);
        _tmLeft = _tmTotal;
        _tmTick.Tick += (_, _) => TimersTick();
        MiniTimer.Cursor = Cursors.Hand;
        MiniTimer.MouseLeftButtonUp += (_, e) => { e.Handled = true; OpenTimer(_tmSet ? 0 : _swRun || _swAcc > TimeSpan.Zero ? 1 : 0); };
    }

    // ---------- открыть и закрыть ----------
    private void ToggleTimerPanel()
    {
        if (_timerOpen) { _timerOpen = false; ApplyMode(Rest()); }
        else OpenTimer(_tmTab);
    }

    private void OpenTimer(int tab)
    {
        _tmTab = tab;
        _timerOpen = true; _mixerOpen = false; _centerOpen = false; _trOpen = false; _actionsOpen = false; _pocketOpen = false; _devicesOpen = false;
        ApplyMode(Rest());
        RenderTimer();
    }

    private void TimerClose_Click(object s, RoutedEventArgs e) { _timerOpen = false; ApplyMode(Rest()); }

    private IEnumerable<FrameworkElement> TimerItems() =>
        new FrameworkElement[] { TmTabTimer, TmTabWatch, TmDigits, TmMinus, TmPlus, TmReset, TmGo }
            .Concat(TmPresets.Children.OfType<FrameworkElement>());

    private void OnModeForTimers(Mode m)
    {
        if (m == Mode.Timer) EnsureTmTick();
    }

    // ---------- вид панели ----------
    private void BuildTimerUi()
    {
        if (_tmBuilt) return;
        _tmBuilt = true;
        foreach (var (b, icon, text) in new[] { (TmTabTimer, "hourglass", "Таймер"), (TmTabWatch, "timer", "Секундомер") })
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new VIcon { Text = icon, FontSize = 12, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = Loc.T(text), Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.Medium, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            b.Content = sp;
        }
        var chip = (Style)FindResource("Chip");
        foreach (int m in PresetMin)
        {
            int min = m;
            var b = new Button { Style = chip, Content = $"{min} {Loc.T("мин")}", Height = 30, Margin = new Thickness(0, 0, 6, 0), FontSize = 12 };
            b.Click += (_, _) =>
            {
                _tmTotal = min * 60;
                _cfg.TimerLast = min * 60;
                _cfg.Save();
                StartTimer(_tmTotal);
                Pop(b, 0.8, 420);
            };
            TmPresets.Children.Add(b);
        }
    }

    private void RenderTimer()
    {
        BuildTimerUi();
        bool timer = _tmTab == 0;
        TmPillMove.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(timer ? 0 : 112, TimeSpan.FromMilliseconds(420)) { EasingFunction = new SpringEase(7, 13) });
        ((FrameworkElement)TmTabTimer.Content).Opacity = timer ? 1 : 0.55;
        ((FrameworkElement)TmTabWatch.Content).Opacity = timer ? 0.55 : 1;
        var vis = timer ? Visibility.Visible : Visibility.Collapsed;
        var hid = timer ? Visibility.Collapsed : Visibility.Visible;
        TmMinus.Visibility = TmPlus.Visibility = TmPresets.Visibility = TmBarTrack.Visibility = vis;
        TmLap.Visibility = TmLaps.Visibility = hid;
        var col = timer ? TimerC : WatchC;
        TmGo.Background = new SolidColorBrush(col);
        TmBar.Background = new SolidColorBrush(col);
        bool running = timer ? _tmRun : _swRun;
        TmGoIcon.Text = running ? "pause" : "play";
        TmGo.ToolTip = Loc.T(running ? "Пауза" : "Старт");
        UpdateTimerTexts();
    }

    private static string FmtTimer(double sec)
    {
        int s = (int)Math.Ceiling(Math.Max(0, sec) - 0.001);
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }

    private static string FmtWatch(TimeSpan t, bool tenths = true)
    {
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
        return tenths ? $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds / 100}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    private void UpdateTimerTexts()
    {
        if (_mode == Mode.Timer)
        {
            if (_tmTab == 0)
            {
                double left = _tmSet ? TmLeftNow : _tmTotal;
                TmDigits.Text = FmtTimer(left);
                TmDigits.Opacity = _tmSet && !_tmRun ? 0.55 : 1;   // на паузе цифры тусклее
                double frac = _tmSet && _tmTotal > 0 ? left / _tmTotal : 1;
                TmBar.Width = Math.Max(0, TmBarTrack.ActualWidth * frac);
            }
            else
            {
                TmDigits.Text = FmtWatch(SwNow);
                TmDigits.Opacity = !_swRun && _swAcc > TimeSpan.Zero ? 0.55 : 1;
                TmLaps.Text = string.Join("    ", _laps.Take(2).Select((l, i) => $"{Loc.T("Круг", "Lap")} {_laps.Count - i}  {FmtWatch(l)}"));
            }
        }
        UpdateMiniTimer();
    }

    // плашка в свёрнутом острове: таймер важнее секундомера
    private void UpdateMiniTimer()
    {
        string text = "";
        Color col = TimerC;
        string icon = "hourglass";
        if (_tmSet) { text = FmtTimer(TmLeftNow); }
        else if (_swRun || _swAcc > TimeSpan.Zero) { text = FmtWatch(SwNow, false); col = WatchC; icon = "timer"; }
        bool inBubble = _cfg.BubbleSplit && (_tmSet ? _cfg.BubbleTimer : _cfg.BubbleWatch);
        bool show = text.Length > 0 && !inBubble;   // шаг 49: обычно время в отдельном пузыре рядом с островом (Bubble.cs)
        UpdateBubble();
        bool paused = _tmSet ? !_tmRun : !_swRun;
        MiniTimer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            MiniTimerText.Text = text;
            MiniTimerIcon.Text = icon;
            var b = new SolidColorBrush(col);
            MiniTimerText.Foreground = b;
            MiniTimerIcon.Foreground = b;
            MiniTimer.Opacity = paused ? 0.6 : 1;
        }
        // ширину острова пересчитываем, только когда меняется число знаков, а не каждую секунду
        int len = show ? text.Length : 0;
        if (len != _badgeLen) { _badgeLen = len; UpdateWidths(_clockMode ? MiniTitle.Text : _curTitle, _curArtist); }
    }

    private double MiniTimerW => MiniTimer.Visibility == Visibility.Visible
        ? 8 + 12 + 4 + TextW(MiniTimerText.Text, 12, FontWeights.SemiBold) + 2 : 0;

    // ---------- ход времени ----------
    private void EnsureTmTick()
    {
        if (!_tmTick.IsEnabled) _tmTick.Start();
    }

    private void TimersTick()
    {
        if (_tmRun && TmLeftNow <= 0) TimerDone();
        UpdateTimerTexts();
        // ничего не идёт и панель закрыта: часы не нужны
        if (!_tmRun && !_swRun && _mode != Mode.Timer) _tmTick.Stop();
    }

    private void StartTimer(double sec)
    {
        _tmTotal = sec;
        _tmLeft = sec;
        _tmEnd = DateTime.UtcNow.AddSeconds(sec);
        _tmRun = _tmSet = true;
        EnsureTmTick();
        RenderTimer();
    }

    private void TimerDone()
    {
        _tmRun = _tmSet = false;
        _tmLeft = _tmTotal;
        if (_player != null && _hidden) _player.ShowHint("hourglass", Loc.T("Время вышло") + "  ·  " + FmtTimer(_tmTotal));
        else ShowNotif("Таймер", Loc.T("Время вышло"), FmtTimer(_tmTotal));
        // звонок три раза: синтезированный аккорд, без файлов
        for (int i = 0; i < 3; i++)
        {
            var d = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1 + i * 1300) };
            d.Tick += (_, _) => { d.Stop(); Sfx.Done(); };
            d.Start();
        }
        if (_mode == Mode.Timer) RenderTimer();
    }

    // ---------- кнопки ----------
    private void TmTabTimer_Click(object s, RoutedEventArgs e) { _tmTab = 0; RenderTimer(); }
    private void TmTabWatch_Click(object s, RoutedEventArgs e) { _tmTab = 1; RenderTimer(); }

    private void TmGo_Click(object s, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Pop(TmGo, 0.75, 460);
        if (_tmTab == 0)
        {
            if (_tmRun) { _tmLeft = TmLeftNow; _tmRun = false; }                                  // пауза
            else if (_tmSet) { _tmEnd = DateTime.UtcNow.AddSeconds(_tmLeft); _tmRun = true; }      // дальше
            else { StartTimer(_tmTotal); return; }                                                 // старт
        }
        else
        {
            if (_swRun) { _swAcc += DateTime.UtcNow - _swFrom; _swRun = false; }
            else { _swFrom = DateTime.UtcNow; _swRun = true; }
        }
        EnsureTmTick();
        RenderTimer();
    }

    private void TmReset_Click(object s, RoutedEventArgs e)
    {
        if (_tmTab == 0) { _tmRun = _tmSet = false; _tmLeft = _tmTotal; }
        else { _swRun = false; _swAcc = TimeSpan.Zero; _laps.Clear(); }
        RenderTimer();
    }

    // ± минута: до старта меняет длительность, на ходу продлевает или укорачивает
    private void TmAdd(double sec)
    {
        if (_tmRun) _tmEnd = _tmEnd.AddSeconds(Math.Max(sec, -TmLeftNow + 1));
        else if (_tmSet) _tmLeft = Math.Max(1, _tmLeft + sec);
        if (_tmSet) _tmTotal = Math.Max(_tmTotal + sec, TmLeftNow);
        else
        {
            _tmTotal = Math.Clamp(_tmTotal + sec, 60, 36000);
            _tmLeft = _tmTotal;
            _cfg.TimerLast = (int)_tmTotal;
            _cfg.Save();
        }
        UpdateTimerTexts();
    }

    private void TmMinus_Click(object s, RoutedEventArgs e) => TmAdd(-60);
    private void TmPlus_Click(object s, RoutedEventArgs e) => TmAdd(60);

    private void TmLap_Click(object s, RoutedEventArgs e)
    {
        if (!_swRun) return;
        var total = SwNow;
        var before = _laps.Aggregate(TimeSpan.Zero, (a, b) => a + b);
        _laps.Insert(0, total - before);
        UpdateTimerTexts();
    }
}
