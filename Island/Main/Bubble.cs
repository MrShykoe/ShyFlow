using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Island;

// Второй «пузырь» рядом с островом (шаг 49), как в iOS, когда идут две вещи сразу.
// Остров занят музыкой, а таймер, секундомер или копирование файлов живут в своём кружке справа:
// кольцо прогресса (сколько осталось у таймера, проценты копирования, секундная стрелка у секундомера) и время.
// Пузырь «отпочковывается» от края острова с пружиной и «втягивается» обратно, когда всё закончилось.
// Виден рядом со свёрнутым островом и плашками; в раскрытых панелях прячется. Клик открывает таймер.
// Настройки, «Плашки», «Таймер и копирование отдельным пузырём». Выключено: время, как раньше, внутри острова.
public partial class MainWindow
{
    private bool _bubbleOn;
    private string _bubbleKind = "";
    private static readonly Color CopyC = Color.FromRgb(0xBF, 0x5A, 0xF2);

    private void InitBubble()
    {
        // RenderTransformOrigin ставит Notch.cs: у края экрана пузырь растёт от него
        Bubble.MouseLeftButtonDown += (_, e) => { e.Handled = true; StackPress(e); };
        Bubble.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (StackRelease()) return;   // это был свайп между активностями, а не клик (Activities.cs)
            Sfx.Ui("tap");
            var a = new DoubleAnimation(0.8, 1, TimeSpan.FromMilliseconds(460)) { EasingFunction = Spr() };
            BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            if (_bubbleKind == "timer") OpenTimer(0);
            else if (_bubbleKind == "watch") OpenTimer(1);
            else if (_bubbleKind == "copy") ShowCopyWindow();
            else if (_bubbleKind == "clock") CalendarWindow.ShowCalendar();      // шаг 54: календарь с праздниками
            else if (_bubbleKind == "focus") FocusMenu();
            else if (_bubbleKind == "mic") ToggleMic();   // звонок: выключить и включить микрофон (MicToggle.cs)
            else if (_bubbleKind == "weather") ShowWeather();
            else if (_bubbleKind == "battery") Launch("ms-settings:batterysaver");
            else if (_bubbleKind is "ctx" or "build") ContextClick();
            else if (_bubbleKind == "live") ToggleChatOverlay();
            else if (_bubbleKind == "steam") Launch("steam://open/downloads");   // шаг 68: загрузки в Steam (SteamDl.cs)   // шаг 67: спрятать и показать чат (Streamer.cs)   // шаг 65: меню проекта, Discord, браузера (Context.cs)
            else if (_bubbleKind.StartsWith("web:")) Launch(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        };
        Bubble.SizeChanged += (_, _) => PlaceAppsBar();
        _idleTimer.Tick += (_, _) => { if (_cfg.BubbleAlways) UpdateBubble(); else _idleTimer.Stop(); };
        // фон острова под пузырём обрезается по его контуру (в режиме notch с ушами, Notch.cs)
        BubbleBgLayers.SizeChanged += (_, _) => ClipCapsuleBg(Bubble, BubbleBgLayers);
        InitActivities();
    }

    // что сейчас идёт (шаг 61: все сразу, а не только первое; порядок и выбор в Activities.cs)
    private IEnumerable<(string kind, string text, double frac, Color col, string icon, bool paused)> BubbleStates()
    {
        if (_cfg.BubbleCopy && _cfg.CopyProgress && _cpSeen && _cpLast >= 0 && _cpLast <= 100)
            yield return ("copy", _cpLast + "%", _cpLast / 100.0, CopyC, "copy", false);
        if (_tmSet && _cfg.BubbleTimer)
            yield return ("timer", FmtTimer(TmLeftNow), _tmTotal > 0 ? TmLeftNow / _tmTotal : 0, TimerC, "hourglass", !_tmRun);
        if ((_swRun || _swAcc > TimeSpan.Zero) && _cfg.BubbleWatch && !_tmSet)
            yield return ("watch", FmtWatch(SwNow, false), SwNow.TotalSeconds % 60 / 60, WatchC, "timer", !_swRun);
    }

    // шаг 53: «Пузырь всегда» — когда ничего не идёт, в пузыре время, дата и время или заряд батареи
    private readonly System.Windows.Threading.DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private (string kind, string text, double frac, Color col, string icon, bool paused)? IdleState()
    {
        var now = DateTime.Now;
        double sec = (now.Second + now.Millisecond / 1000.0) / 60;
        if (_cfg.BubbleIdle == 2 && GetSystemPowerStatus(out var ps) && (ps.BatteryFlag & 128) == 0 && ps.BatteryLifePercent <= 100)
        {
            int pct = ps.BatteryLifePercent;
            bool charging = ps.ACLineStatus == 1;
            var col = charging ? Color.FromRgb(0x30, 0xD1, 0x58) : pct <= 20 ? Color.FromRgb(0xFF, 0x45, 0x3A) : pct <= 40 ? Color.FromRgb(0xFF, 0xD6, 0x0A) : Colors.White;
            return ("battery", pct + "%", pct / 100.0, col, charging ? "battery-charging" : "battery", false);
        }
        if (_cfg.BubbleIdle == 3)
        {
            var w = Weather.Last;
            if (w == null || (DateTime.Now - w.At).TotalMinutes > 20) RefreshWeather();
            return w == null ? ("weather", "…", 0, Colors.White, "cloud", false) : ("weather", Weather.Deg(w.Temp), 0, Colors.White, Weather.Icon(w.Code, w.Day), false);
        }
        string text = _cfg.BubbleIdle == 1 ? now.ToString("ddd d", Loc.Culture) + " · " + now.ToString("HH:mm") : now.ToString("HH:mm");
        // праздник сегодня: рядом с датой (Services\Holidays.cs)
        if (_cfg.BubbleIdle == 1)
        {
            var h = Holidays.For(now).FirstOrDefault();
            if (h != null) text = now.ToString("d MMM", Loc.Culture) + " · " + (h.Text.Length > 22 ? h.Text[..21] + "…" : h.Text);
        }
        return ("clock", text, sec, Colors.White, "clock", false);
    }

    private bool _weatherBusy;

    private async void RefreshWeather()
    {
        if (_weatherBusy) return;
        _weatherBusy = true;
        try { await Weather.Get(_cfg.WeatherCity ?? ""); }
        finally { _weatherBusy = false; }
        UpdateBubble();
    }

    // клик по погоде: сейчас и на три дня вперёд
    private async void ShowWeather()
    {
        var w = await Weather.Get(_cfg.WeatherCity ?? "");
        if (w == null) { ShowToast("cloud", Loc.T("Погода: нет связи"), null, "key"); return; }
        var days = w.Days.Skip(1).Take(3).Select((d, i) =>
            (i == 0 ? Loc.T("Завтра") : d.day.ToString("ddd", Loc.Culture)) + " " + Weather.Deg(d.max) + "/" + Weather.Deg(d.min) + " " + Weather.Text(d.code));
        ShowNotif(Loc.T("Погода") + (w.City.Length > 0 ? " · " + w.City : ""), Weather.Deg(w.Temp) + ", " + Weather.Text(w.Code), string.Join("  ·  ", days));
    }

    private static void Launch(string uri)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { App.LogOnce("Bubble.Launch", ex); }
    }

    private void UpdateBubble()
    {
        var st = PickActivity();
        if (_cfg.BubbleAlways && !_idleTimer.IsEnabled) _idleTimer.Start();
        BubbleBgLayers.Visibility = _cfg.BubbleIslandBg ? Visibility.Visible : Visibility.Collapsed;   // выключено: просто фон темы
        bool want = st != null && !_hidden && !_fsMini && _mode is Mode.Mini or Mode.Osd or Mode.Notif;
        if (st is { } s)
        {
            _bubbleKind = s.kind;
            var brush = new SolidColorBrush(s.col);
            BubbleRing.Stroke = brush;
            BubbleRing.Data = Arc(s.frac);
            BubbleIcon.Text = s.icon;
            BubbleIcon.Foreground = brush;
            BubbleText.Text = s.text;
            BubbleText.Foreground = brush;
            // шаг 62: прогресс (копирование, скачивание, «готово») жидкостью вместо кольца
            bool liquid = IsLiquid(s.kind);
            BubbleWave.Visibility = liquid ? Visibility.Visible : Visibility.Collapsed;
            BubbleTrack.Visibility = BubbleRing.Visibility = liquid ? Visibility.Collapsed : Visibility.Visible;
            if (liquid)
            {
                BubbleWave.Color = s.col;
                if (s.kind != "done") BubbleWave.Value = s.frac;
                BubbleIcon.Foreground = Brushes.White;
            }
            MultiWaves(s.kind);
            Bubble.Opacity = s.paused ? 0.65 : 1;
            Bubble.ToolTip = Loc.T(s.kind switch { "copy" => "Копирование файлов", "timer" => "Таймер", "clock" => "Клик: календарь и праздники", "battery" => "Заряд батареи", "focus" => "Фокус: клик — пауза, пропустить, остановить", "weather" => "Клик: прогноз", "mic" => "Микрофон: клик выключает и включает (Ctrl+Alt+V)", "dl" => "Скачивается обновление", "build" => "Идёт сборка", "ctx" => "Клик: быстрые действия", "live" => "Чат стрима. Клик: спрятать или показать", "steam" => "Загрузка в Steam (примерно). Клик: загрузки Steam",
                "done" => s.text, _ when s.kind.StartsWith("web:") => s.kind[4..] + "\n" + Loc.T("Клик: папка «Загрузки»"), _ => "Секундомер" })
                + (_stackKinds.Length > 1 ? "\n" + Loc.T("Свайп или колесо: другие активности") : "");
        }
        if (want == _bubbleOn) return;
        _bubbleOn = want;
        var d = TimeSpan.FromMilliseconds(want ? 620 : 260);
        double side = BubbleLeft ? -1 : 1;
        double from = -side * (8 + Math.Max(38, Bubble.ActualWidth));   // от края острова
        if (want)
        {
            Bubble.Visibility = Visibility.Visible;
            Bubble.UpdateLayout();
            from = -side * (8 + Math.Max(38, Bubble.ActualWidth));
            PlaceAppsBar();
            BubbleIn.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, 0, d) { EasingFunction = new SpringEase(6, 11) });
            var sc = new DoubleAnimation(0.35, 1, d) { EasingFunction = new SpringEase(6, 11) };
            BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, sc);
            BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, sc);
            Bubble.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Bubble.Opacity, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop });
            if (_ready) Sfx.Ui("bubble");
        }
        else
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            BubbleIn.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, d) { EasingFunction = ease });
            var sc = new DoubleAnimation(0.35, d) { EasingFunction = ease };
            sc.Completed += (_, _) =>
            {
                if (_bubbleOn) return;   // за это время пузырь снова понадобился
                Bubble.Visibility = Visibility.Collapsed;
                PlaceAppsBar();
            };
            BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, sc);
            BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, sc);
        }
    }

    // окно копирования Проводника на передний план
    private static void ShowCopyWindow()
    {
        EnumWindows((h, _) =>
        {
            var cls = new System.Text.StringBuilder(64);
            if (GetClassName(h, cls, 64) == 0 || cls.ToString() != "OperationStatusWindow" || !IsWindowVisible(h)) return true;
            if (IsIconic(h)) ShowWindow(h, 9 /*SW_RESTORE*/);
            SetForegroundWindow(h);
            return false;
        }, IntPtr.Zero);
    }

    // справа (или слева) от острова, на одной высоте с ним
    private void PlaceBubble()
    {
        if (Bubble.Visibility != Visibility.Visible) return;
        BubbleMove.X = (BubbleLeft ? -1 : 1) * (Pill.Width / 2 + 8 + Bubble.ActualWidth / 2);
        double top = Pill.Margin.Top;
        if (Math.Abs(Bubble.Margin.Top - top) > 0.1) Bubble.Margin = new Thickness(0, top, 0, 0);
    }

    // сколько места пузырь занимает справа (капсула значков приложений встаёт за ним)
    private double BubbleSpace => Bubble.Visibility == Visibility.Visible ? Bubble.ActualWidth + 8 : 0;

    // кольцо: дуга от 12 часов по часовой стрелке
    private static Geometry Arc(double f)
    {
        const double r = 10.75, c = 12;
        f = Math.Clamp(f, 0, 1);
        if (f <= 0.001) return Geometry.Empty;
        if (f >= 0.999) return new EllipseGeometry(new Point(c, c), r, r);
        double a = f * 2 * Math.PI;
        var end = new Point(c + r * Math.Sin(a), c - r * Math.Cos(a));
        var fig = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, f > 0.5, SweepDirection.Clockwise, true));
        var g = new PathGeometry { Figures = { fig } };
        g.Freeze();
        return g;
    }
}
