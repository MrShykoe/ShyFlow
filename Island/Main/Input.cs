using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NAudio.CoreAudioApi;

namespace Island;

// Управление: горячие клавиши, колесо мыши, жесты, подсказка при перетаскивании.
public partial class MainWindow
{
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);

    // ============================================================
    //  ГОРЯЧИЕ КЛАВИШИ (Ctrl+Alt+...)
    // ============================================================
    private bool _hk1, _hkReg;
    private static readonly (int id, uint vk)[] HotKeys =
        { (2, 0x20), (3, 0x27), (4, 0x25), (5, 0x50), (6, 0x4E), (7, 0x41), (8, 0x45), (9, 0xBC), (10, 0xBE), (11, 0x44),
          (12, 0x30), (13, 0x46), (14, 0x54), (15, 0x4D), (16, 0x53), (17, 0x42), (18, 0x51), (19, 0x56) };   // 0: сброс подгонки текста, F: счётчик кадров, T: таймер, M: микшер, S: настройки, B: шторка уведомлений, Q: перевести выделенное, V: микрофон
    internal const int MaxHotkeyId = 19;
    private const uint HkMods = 0x0002 | 0x0001 | 0x4000; // Ctrl + Alt, без автоповтора

    private void RegisterHotkeys()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (!_hk1) { RegisterHotKey(_hwnd, 1, HkMods, 0x49); _hk1 = true; } // I: скрыть остров, всегда
        if (_cfg.Hotkeys && !_hkReg)
        {
            foreach (var (id, vk) in HotKeys) RegisterHotKey(_hwnd, id, HkMods, vk);
            _hkReg = true;
        }
        else if (!_cfg.Hotkeys && _hkReg)
        {
            foreach (var (id, _) in HotKeys) UnregisterHotKey(_hwnd, id);
            _hkReg = false;
        }
    }

    private async void OnHotkey(int id)
    {
        try
        {
            switch (id)
            {
                case 1: ToggleHidden(); break;
                case 2: if (_session != null) await _session.TryTogglePlayPauseAsync(); break;
                case 3: _skipDir = 1; _skipAt = DateTime.UtcNow; if (SkipAdInstead()) break; if (_session != null) await _session.TrySkipNextAsync(); break;
                case 4: _skipDir = -1; _skipAt = DateTime.UtcNow; if (_session != null) await _session.TrySkipPreviousAsync(); break;
                case 5:
                    _pocketOpen = !_pocketOpen; _actionsOpen = false; _devicesOpen = false; _timerOpen = false; _mixerOpen = false; _centerOpen = false; _trOpen = false; _autoOpened = false; _tab = 0;
                    RenderPocket(); ApplyMode(Rest());
                    break;
                case 6:
                    _pocketOpen = true; _actionsOpen = false; _devicesOpen = false; _timerOpen = false; _mixerOpen = false; _centerOpen = false; _trOpen = false; _autoOpened = false; _tab = 2;
                    RenderPocket(); ApplyMode(Rest());
                    Activate();
                    await Task.Delay(300);
                    NoteBox.Focus();
                    break;
                case 7:
                    _actionsOpen = !_actionsOpen; _pocketOpen = false; _devicesOpen = false; _timerOpen = false; _mixerOpen = false; _centerOpen = false; _trOpen = false;
                    ApplyMode(Rest());
                    break;
                case 8:
                    _pocketOpen = false; _actionsOpen = false; _devicesOpen = false; _timerOpen = false; _mixerOpen = false; _centerOpen = false; _trOpen = false;
                    _expanded = !_expanded;
                    ApplyMode(Rest());
                    break;
                case 9: AdjustLyricOffset(-0.25); break;   // Ctrl+Alt+, : текст раньше
                case 10: AdjustLyricOffset(0.25); break;   // Ctrl+Alt+. : текст позже
                case 11:                                   // Ctrl+Alt+D : звук и микрофон
                    if (_devicesOpen) { _devicesOpen = false; _timerOpen = false; _mixerOpen = false; _centerOpen = false; _trOpen = false; ApplyMode(Rest()); } else OpenDevices();
                    break;
                case 12: ResetLineSync(); break;           // Ctrl+Alt+0 : сбросить подгонку текста
                case 13: ToggleFps(); break;               // Ctrl+Alt+F : счётчик кадров
                case 14: ToggleTimerPanel(); break;        // Ctrl+Alt+T : таймер и секундомер
                case 15: ToggleMixer(); break;             // Ctrl+Alt+M : громкость программ
                case 16: Settings_Click(this, new RoutedEventArgs()); break;   // Ctrl+Alt+S : настройки (если уже открыты, выходят на передний план)
                case 17: ToggleCenter(); break;            // Ctrl+Alt+B : шторка уведомлений
                case 18: QuickTranslate(); break;
                case 19: ToggleMic(); break;               // Ctrl+Alt+V : выключить и включить микрофон (MicToggle.cs)          // Ctrl+Alt+Q : перевести выделенный текст (Translator.cs)
            }
        }
        catch (Exception ex) { App.LogOnce("Hotkey", ex); }
    }

    // ============================================================
    //  КОЛЕСО МЫШИ НА СВЁРНУТОМ ОСТРОВЕ: громкость, с Shift перемотка
    // ============================================================
    private double _seekTarget;
    private DateTime _seekAt = DateTime.MinValue;

    private void Pill_Wheel(object sender, MouseWheelEventArgs e)
    {
        if (!_cfg.WheelControl || (_mode != Mode.Mini && _mode != Mode.Osd)) return;
        e.Handled = true;
        int notches = Math.Max(1, Math.Abs(e.Delta) / 120);
        int sign = e.Delta > 0 ? 1 : -1;
        bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
        if (shift) { _ = SeekByAsync(sign * 5.0 * notches); return; }
        try
        {
            var v = _dev?.AudioEndpointVolume;
            if (v == null) return;
            float nv = Math.Clamp(v.MasterVolumeLevelScalar + sign * 0.02f * notches, 0f, 1f);
            if (sign > 0 && v.Mute) v.Mute = false;
            v.MasterVolumeLevelScalar = nv;
            ShowToast(nv <= 0 ? "\uE74F" : "\uE767", null, nv, "vol");
        }
        catch (Exception ex) { App.LogOnce("Wheel", ex); }
    }

    private async Task SeekByAsync(double delta)
    {
        var s = _session;
        if (s == null) return;
        try
        {
            var tl = s.GetTimelineProperties();
            double total = (tl.EndTime - tl.StartTime).TotalSeconds;
            if (total <= 1) return;
            var pos = tl.Position;
            if (_playing) pos += DateTimeOffset.Now - tl.LastUpdatedTime;
            double cur = (pos - tl.StartTime).TotalSeconds;
            // при быстром кручении опираемся на прошлую цель: плеер обновляет позицию с задержкой
            if ((DateTime.UtcNow - _seekAt).TotalMilliseconds < 700) cur = _seekTarget;
            double nt = Math.Clamp(cur + delta, 0, Math.Max(0, total - 0.5));
            _seekTarget = nt;
            _seekAt = DateTime.UtcNow;
            await s.TryChangePlaybackPositionAsync(tl.StartTime.Ticks + (long)(nt * TimeSpan.TicksPerSecond));
            _snapRead = DateTime.MinValue;
            ShowToast(delta > 0 ? "\uE101" : "\uE100",
                $"{(delta > 0 ? "+" : "")}{(int)delta} с   {Fmt(nt)} / {Fmt(total)}", null, delta > 0 ? "next" : "prev");
        }
        catch (Exception ex) { App.LogOnce("Wheel.Seek", ex); }
    }

    // ============================================================
    //  ЖЕСТЫ МЫШЬЮ
    //  Свёрнутый остров: влево следующий трек, вправо предыдущий, вниз раскрыть.
    //  Уведомление: смахнуть в сторону или вверх, чтобы убрать.
    //  Развёрнутый остров: потянуть вниз за пустое место, чтобы открыть шторку уведомлений.
    //
    //  Шаг 49: раньше после свайпа остров отпускал мышь, отпускание кнопки до него не доходило,
    //  и «резинка» так и оставалась сдвинутой (остров залипал вбок). Теперь мышь держится до отпускания,
    //  а если её всё-таки отняли (Alt+Tab, всплывающее окно), остров всё равно возвращается на место.
    // ============================================================
    private Point _swipeStart;
    private bool _swipeDown, _swiped;
    private Mode _swipeMode;

    private void InitGestures()
    {
        Pill.PreviewMouseLeftButtonDown += (_, e) =>
        {
            // шаг 70: нажали кнопку (ответ в уведомлении, таймер в свёрнутом острове) — это не жест.
            // Раньше дрожание пера или мыши больше чем на 6 px забирало мышь у кнопки, и её клик пропадал
            bool onCtl = IsInteractive(e.OriginalSource as DependencyObject);
            bool mini = _mode == Mode.Mini && _cfg.Gestures && !onCtl;
            bool notif = _mode == Mode.Notif && !onCtl;
            bool full = _mode == Mode.Full && _cfg.NotifCenter && !onCtl;
            if (!mini && !notif && !full) return;
            _swipeStart = e.GetPosition(this);
            _swipeDown = true;
            _swiped = false;
            _swipeMode = _mode;
            // прошлая «резинка» ещё пружинит: забираем её текущее положение, чтобы остров не прыгал и не «тормозил»
            var t = Get(Pill).T;
            double cur = t.X;
            t.BeginAnimation(TranslateTransform.XProperty, null);
            t.X = cur;
        };
        Pill.PreviewMouseMove += (_, e) =>
        {
            if (!_swipeDown || _swiped) return;
            if (e.LeftButton != MouseButtonState.Pressed) { EndGesture(); return; }
            var p = e.GetPosition(this);
            double dx = p.X - _swipeStart.X, dy = p.Y - _swipeStart.Y;
            if ((Math.Abs(dx) > 6 || Math.Abs(dy) > 6) && !Pill.IsMouseCaptured) Pill.CaptureMouse();
            switch (_swipeMode)
            {
                case Mode.Mini:
                    Get(Pill).T.X = Rubber(dx, 26);   // остров тянется за мышью, как резинка
                    // шаг 63: у каждого направления своё действие (Gestures.cs)
                    if (Math.Abs(dx) >= 44 && Math.Abs(dx) > Math.Abs(dy)) DoGesture(dx < 0 ? _cfg.GestureLeft : _cfg.GestureRight);
                    else if (dy >= 34 && dy > Math.Abs(dx)) DoGesture(_cfg.GestureDown);
                    else if (dy <= -18 && -dy > Math.Abs(dx)) DoGesture(_cfg.GestureUp);   // вверх места мало: остров у края экрана
                    break;
                case Mode.Notif:
                    // уведомление едет за мышью и тускнеет
                    var nt = Get(NotifInner).T;
                    nt.BeginAnimation(TranslateTransform.XProperty, null);
                    nt.X = dx * 0.8;
                    NotifInner.Opacity = Math.Clamp(1 - Math.Max(Math.Abs(dx), -dy * 2) / 160, 0.2, 1);
                    if (Math.Abs(dx) >= 70 || dy <= -26) { _swiped = true; DismissNotif(Math.Abs(dx) >= 70 ? Math.Sign(dx) : 0); }
                    break;
                case Mode.Full:
                    if (dy >= 42 && dy > Math.Abs(dx) * 1.5) { _swiped = true; OpenCenter(); }
                    break;
            }
        };
        Pill.PreviewMouseLeftButtonUp += (_, e) =>
        {
            bool was = _swiped;
            bool moved = _swipeDown && (e.GetPosition(this) - _swipeStart).Length > 6;
            EndGesture();
            // это был свайп или протяжка, а не клик: остров не раскрываем и уведомление не открываем
            if (was || (moved && _swipeMode == Mode.Notif)) e.Handled = true;
        };
        Pill.LostMouseCapture += (_, _) => { if (_swipeDown) EndGesture(); };
        Pill.MouseEnter += (_, _) => { if (_mode == Mode.Mini && _cfg.Gestures) HoverScale(1.04); };
        Pill.MouseLeave += (_, _) => HoverScale(1.0);
    }

    // мягкое сопротивление: чем дальше тянешь, тем меньше остров поддаётся (как резинка на iPhone)
    private static double Rubber(double d, double max) => max * Math.Tanh(d * 0.35 / max);

    private void EndGesture()
    {
        bool notifDrag = _swipeDown && !_swiped && _swipeMode == Mode.Notif;
        _swipeDown = false;
        _swiped = false;
        if (Pill.IsMouseCaptured) Pill.ReleaseMouseCapture();
        ReleaseRubber();
        if (notifDrag)
        {
            // не дотянули: уведомление пружинит обратно
            var nt = Get(NotifInner).T;
            double cur = nt.X;
            nt.X = 0;
            nt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(cur, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = Spr(), FillBehavior = FillBehavior.Stop });
            NotifInner.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)) { FillBehavior = FillBehavior.Stop });
            NotifInner.Opacity = 1;
        }
    }

    // нажали на кнопку, ползунок или поле ввода: это не жест
    private static bool IsInteractive(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase or TextBox or System.Windows.Controls.Primitives.ScrollBar) return true;
            if (d is FrameworkElement fe && (fe.Cursor == Cursors.Hand || fe.Name == "SliderHost")) return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    private void HoverScale(double to)
    {
        var t = Get(Pill);
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(420))
        { EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 5 } };
        t.S.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        t.S.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    private void ReleaseRubber()
    {
        var t = Get(Pill).T;
        double cur = t.X;
        if (Math.Abs(cur) < 0.5) { t.X = 0; return; }
        t.X = 0;
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(cur, 0, TimeSpan.FromMilliseconds(520))
        {
            EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 4 },
            FillBehavior = FillBehavior.Stop
        });
    }

    private async void Swipe(int dir)
    {
        _swiped = true;   // дальше мышь не слушаем, но держим до отпускания (иначе отпускание теряется)
        ReleaseRubber();  // остров сразу пружинит обратно
        Sfx.Ui("swipe");
        NoteSkip(dir);
        try
        {
            if (_session == null) return;
            if (dir > 0 && SkipAdInstead()) return;   // реклама в Spotify (Ads.cs)
            if (dir > 0) await _session.TrySkipNextAsync(); else await _session.TrySkipPreviousAsync();
        }
        catch (Exception ex) { App.LogOnce("Swipe", ex); }
    }

    private void SwipeDown()
    {
        _swiped = true;
        ReleaseRubber();
        if (_pocketOpen || _actionsOpen) return;
        _expanded = true;
        ApplyMode(Rest());
    }

    // ============================================================
    //  ПОДСКАЗКА «КИНЬ ФАЙЛ СЮДА»: значок покачивается вниз
    // ============================================================
    private void DropBounce(bool on)
    {
        DropFx(on);   // пунктир, дыхание значка, магнит (DropFx.cs)
        var t = Get(DropIcon).T;
        if (on)
        {
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-3, 5, TimeSpan.FromMilliseconds(520))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
        }
        else
        {
            t.BeginAnimation(TranslateTransform.YProperty, null);
            t.Y = 0;
        }
    }
}
