using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace Island;

// Быстрые действия и выбор устройств звука и микрофона.
public partial class MainWindow
{
    // ============================================================
    //  БЫСТРЫЕ ДЕЙСТВИЯ
    // ============================================================
    private bool _actionsOpen;

    private void Actions_Click(object s, RoutedEventArgs e) { _actionsOpen = true; _pocketOpen = false; _devicesOpen = false; _timerOpen = false; _mixerOpen = false; _centerOpen = false; _trOpen = false; ApplyMode(Rest()); }
    private void ActionsClose_Click(object s, RoutedEventArgs e) { _actionsOpen = false; _devicesOpen = false; _timerOpen = false; _mixerOpen = false; _centerOpen = false; _trOpen = false; ApplyMode(Rest()); }

    private void CloseActions() { _actionsOpen = false; _devicesOpen = false; _timerOpen = false; _mixerOpen = false; _centerOpen = false; _trOpen = false; ApplyMode(Rest()); }

    // Вид (шаг 36): плитки как в Пункте управления. Сверху кружок со значком, снизу подпись.
    // Переключатели (тёмная тема, без звука) не закрывают панель: кружок становится белым, значок тёмным, и видно, что включено.
    //
    // Шаг 71: плиток стало 20, и в пять колонок подписи обрезались («Блокиро…», «Калькул…»). Теперь четыре колонки,
    // а на панели только закреплённые плитки (по умолчанию семь) и «Ещё». «Ещё» показывает все действия, их можно листать
    // колесом. Правый клик по плитке закрепляет её или убирает. Список закреплённых хранится в настройках (ActionTiles).
    private const int ActCols = 4;
    private const double ActRowH = 78.5;   // плитка 70.5 + поля по 4
    private int ActionsRows => Math.Clamp((int)Math.Ceiling(ActionsGrid.Children.Count / (double)ActCols), 1, 4);
    private double ActionsH => 58 + ActionsRows * ActRowH;   // высота острова: поля, заголовок, ряды плиток
    private DateTime _tileMuteAt = DateTime.MinValue;

    private sealed class Tile
    {
        public SBorder Circle = null!;
        public VIcon Icon = null!;
        public SolidColorBrush CircleBg = null!, IconFg = null!;
        public Func<bool>? IsOn;
        public string IconOff = "", IconOn = "";
        public bool Shown;
    }
    private readonly List<Tile> _tiles = new();

    // действие для плитки: Id не переводится и хранится в настройках
    private sealed record ActSpec(string Id, string Label, string Icon, Action Act, Func<bool>? IsOn = null, string? IconOn = null);
    private readonly List<ActSpec> _acts = new();
    private bool _actsAll;   // показаны все действия, а не только закреплённые
    private static readonly string[] DefaultActTiles = { "shot", "theme", "mute", "timer", "mixer", "collection", "focus" };

    private List<string> PinnedActs() =>
        (_cfg.ActionTiles ?? DefaultActTiles.ToList()).Where(id => _acts.Any(a => a.Id == id)).Distinct().ToList();

    private static readonly Color TileC = Color.FromArgb(0x1A, 255, 255, 255);
    private static readonly Color TileHoverC = Color.FromArgb(0x2C, 255, 255, 255);
    private static readonly Color CircleOffC = Color.FromArgb(0x26, 255, 255, 255);
    private static readonly Color IconOnC = Color.FromRgb(0x1C, 0x1C, 0x1E);

    private void BuildActions()
    {
        _acts.Clear();

        // launch: открывает программу и закрывает панель. Ошибка запуска не роняет остров, а пишется в crash.log
        Action Launch(string file, string? args = null) => () =>
        {
            CloseActions();
            var psi = args == null ? new ProcessStartInfo(file) : new ProcessStartInfo(file, args);
            psi.UseShellExecute = true;
            Process.Start(psi);
        };
        void Add(string id, string label, string icon, Action act, Func<bool>? isOn = null, string? iconOn = null) =>
            _acts.Add(new ActSpec(id, label, icon, act, isOn, iconOn));

        Add("shot", "Скриншот", "camera", Launch("ms-screenclip:"));
        Add("theme", "Тёмная тема", "sun", ToggleWindowsTheme, () => !WindowsLight(), "moon");
        Add("mute", "Без звука", "volume-2", () =>
        {
            if (_dev == null) return;
            _tileMuteAt = DateTime.UtcNow;
            _dev.AudioEndpointVolume.Mute = !_dev.AudioEndpointVolume.Mute;
        }, () => _dev?.AudioEndpointVolume.Mute == true, "volume-x");
        Add("lock", "Блокировка", "lock", Launch("rundll32.exe", "user32.dll,LockWorkStation"));
        Add("timer", "Таймер", "hourglass", () => OpenTimer(0));
        Add("watch", "Секундомер", "timer", () => OpenTimer(1));
        Add("mixer", "Микшер", "sliders-vertical", OpenMixer);
        Add("devices", "Устройства звука", "headphones", () => OpenDevices());
        Add("calc", "Калькулятор", "calculator", Launch("calc.exe"));
        Add("taskmgr", "Диспетчер задач", "activity", Launch("taskmgr.exe"));
        Add("explorer", "Проводник", "folder-open", Launch("explorer.exe"));
        Add("settings", "Параметры", "settings", Launch("ms-settings:"));
        // шаг 49
        Add("notifs", "Уведомления", "bell", OpenCenter);
        Add("stats", "Статистика", "chart-column", () => { CloseActions(); OpenStats(); });
        Add("story", "Карточка для сторис", "share", () => { CloseActions(); MakeStory(); });
        Add("recap", "Итоги месяца", "sparkles", () => { CloseActions(); MakeRecap(DateTime.Today.Year, DateTime.Today.Month); });   // шаг 73 (Recap.cs)
        Add("collection", "Коллекция", "star", () => { CloseActions(); CollectionWindow.ShowCollection(); });
        Add("notepad", "Блокнот", "sticky-note", () => { CloseActions(); NotepadWindow.ShowNotepad(); });   // шаг 53
        // шаг 54
        Add("translate", "Переводчик", "languages", () => OpenTranslator(null, IntPtr.Zero));
        Add("focus", "Фокус", "zap", ToggleFocus, () => _focusOn);
        Add("calendar", "Календарь", "calendar", () => { CloseActions(); CalendarWindow.ShowCalendar(); });
        RenderActionTiles();
    }

    // плитки на панели: закреплённые и «Ещё», или все действия и «Меньше»
    private void RenderActionTiles()
    {
        ActionsGrid.Children.Clear();
        _tiles.Clear();
        var pinned = PinnedActs();
        var list = _actsAll ? _acts : pinned.Select(id => _acts.First(a => a.Id == id)).ToList();
        foreach (var a in list) AddTile(a, pinned.Contains(a.Id));
        // «Ещё» всегда последняя: так панель не бывает пустой, даже если открепить всё
        AddTile(_actsAll
            ? new ActSpec("", Loc.T("Меньше", "Less"), "chevron-up", () => SetActionsAll(false))
            : new ActSpec("", Loc.T("Ещё", "More"), "layout-grid", () => SetActionsAll(true)), false);
        ActionsTitle.Text = _actsAll ? Loc.T("Все действия", "All actions") : Loc.T("Быстрые действия");
        RefreshActions(false);
    }

    private void SetActionsAll(bool all)
    {
        _actsAll = all;
        RenderActionTiles();
        ActionsScroll.ScrollToTop();
        ApplyMode(Rest());          // остров пружиной меняет высоту под новое число рядов
        Droplets(ActionItems());    // плитки «вливаются», как при открытии панели
    }

    // правый клик по плитке: закрепить на панели или убрать с неё
    private void TogglePinAct(string id)
    {
        var list = PinnedActs();
        if (!list.Remove(id)) list.Add(id);
        _cfg.ActionTiles = list;
        _cfg.Save();
        RenderActionTiles();
        if (_mode == Mode.Actions) ApplyMode(Rest());
    }

    private void AddTile(ActSpec spec, bool pinned)
    {
        var tile = new Tile { IsOn = spec.IsOn, IconOff = spec.Icon, IconOn = spec.IconOn ?? spec.Icon };
        tile.CircleBg = new SolidColorBrush(CircleOffC);
        tile.IconFg = new SolidColorBrush(Colors.White);
        tile.Icon = new VIcon
        {
            Text = spec.Icon, FontSize = 14, Foreground = tile.IconFg,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        tile.Circle = new SBorder
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = tile.CircleBg, Child = tile.Icon,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(9, 8, 0, 0)
        };
        string label = Loc.T(spec.Label);
        var text = new TextBlock
        {
            Text = label, Foreground = Brushes.White, FontSize = 11.5, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 13.5, MaxHeight = 27,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 8, 7)
        };
        var face = new Grid();
        face.Children.Add(tile.Circle);
        face.Children.Add(text);
        // в списке всех действий закреплённые отмечены точкой в углу
        if (_actsAll && pinned)
            face.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 5, Height = 5, Fill = new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 9, 9, 0)
            });
        var bg = new SolidColorBrush(TileC);
        var chip = new SBorder
        {
            Child = face, CornerRadius = new CornerRadius(16), Margin = new Thickness(4), Height = ActRowH - 8,
            Background = bg, Cursor = Cursors.Hand
        };
        if (spec.Id.Length > 0)
        {
            chip.ToolTip = label + "\n" + Loc.T(pinned ? "Правый клик: убрать с панели" : "Правый клик: закрепить на панели",
                                                 pinned ? "Right-click: remove from panel" : "Right-click: pin to panel");
            var item = new MenuItem { Header = Loc.T(pinned ? "Убрать с панели" : "Закрепить на панели", pinned ? "Remove from panel" : "Pin to panel") };
            item.Click += (_, _) => TogglePinAct(spec.Id);
            chip.ContextMenu = new ContextMenu { Items = { item } };
        }
        void Bg(Color c, int ms) => ColorTween.To(bg, SolidColorBrush.ColorProperty, new ColorAnimation(c, TimeSpan.FromMilliseconds(ms)));
        void Press(double to, int ms, IEasingFunction ease)
        {
            var t = Get(chip);
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
            t.S.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            t.S.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        HoldOnPress(chip);   // шаг 70: плитка сжимается при нажатии, клик у края больше не теряется (Tap.cs)
        chip.MouseEnter += (_, _) => Bg(TileHoverC, 140);
        chip.MouseLeave += (_, _) => { Bg(TileC, 220); Press(1, 300, Spr()); };
        chip.MouseLeftButtonDown += (_, e) => { Press(0.93, 110, new CubicEase { EasingMode = EasingMode.EaseOut }); e.Handled = true; };
        chip.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Press(1, 460, Spr());
            Pop(tile.Circle, 0.7, 480);
            try { spec.Act(); }
            catch (Exception ex) { App.Log("Action " + spec.Label, ex); }
            if (tile.IsOn != null) RefreshActions();
        };
        _tiles.Add(tile);
        ActionsGrid.Children.Add(chip);
    }

    // переключатели показывают текущее состояние: белый кружок и тёмный значок = включено
    private void RefreshActions(bool animate = true)
    {
        var d = TimeSpan.FromMilliseconds(animate ? 220 : 1);
        foreach (var t in _tiles)
        {
            if (t.IsOn == null) continue;
            bool on;
            try { on = t.IsOn(); } catch { on = false; }
            if (t.Shown == on && animate) continue;
            t.Shown = on;
            t.Icon.Text = on ? t.IconOn : t.IconOff;   // значок перетекает в новый (VIcon)
            ColorTween.To(t.CircleBg, SolidColorBrush.ColorProperty, new ColorAnimation(on ? Colors.White : CircleOffC, d));
            ColorTween.To(t.IconFg, SolidColorBrush.ColorProperty, new ColorAnimation(on ? IconOnC : Colors.White, d));
        }
    }

    private static bool WindowsLight()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return Convert.ToInt32(k?.GetValue("AppsUseLightTheme", 1) ?? 1) != 0;
    }

    private void ToggleWindowsTheme()
    {
        using var k = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", true);
        if (k == null) return;
        int cur = Convert.ToInt32(k.GetValue("AppsUseLightTheme", 1));
        int nv = cur == 0 ? 1 : 0;
        k.SetValue("AppsUseLightTheme", nv, RegistryValueKind.DWord);
        k.SetValue("SystemUsesLightTheme", nv, RegistryValueKind.DWord);
        SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, UIntPtr.Zero, "ImmersiveColorSet", 2, 2000, out _);
        // из панели действий плашку не показываем: тема видна на самой плитке, а плашка закрыла бы панель
        if (_mode != Mode.Actions)
            ShowToast(nv == 0 ? "moon" : "sun", nv == 0 ? "Тема Windows: тёмная" : "Тема Windows: светлая", null, "key");
    }

    // ============================================================
    //  ЗВУК И МИКРОФОН: смена устройств по умолчанию прямо из острова
    // ============================================================
    private bool _devicesOpen;

    private void OpenDevices()
    {
        _pocketOpen = false; _actionsOpen = false; _autoOpened = false;
        _devicesOpen = true;
        RenderDevices();
        ApplyMode(Rest());
    }

    private void DevicesClose_Click(object s, RoutedEventArgs e)
    {
        _devicesOpen = false; _timerOpen = false; _mixerOpen = false; _centerOpen = false; _trOpen = false;
        ApplyMode(Rest());
    }

    private TextBlock DevHeader(string text) => new()
    {
        Text = text, Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93)), FontSize = 11.5,
        FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 0, 6)
    };

    private void RenderDevices()
    {
        DevOut.Children.Clear();
        DevIn.Children.Clear();
        DevOut.Children.Add(DevHeader(Loc.T("ЗВУК", "SOUND")));
        DevIn.Children.Add(DevHeader(Loc.T("МИКРОФОН", "MICROPHONE")));
        foreach (var d in AudioSwitch.List(DataFlow.Render).Take(4)) DevOut.Children.Add(DevChip(d));
        foreach (var d in AudioSwitch.List(DataFlow.Capture).Take(4)) DevIn.Children.Add(DevChip(d));
    }

    private Border DevChip(AudioSwitch.Dev d)
    {
        var tb = new TextBlock
        {
            Text = d.Name, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Brushes.White, FontWeight = d.IsDefault ? FontWeights.SemiBold : FontWeights.Normal
        };
        var chip = new SBorder
        {
            Child = tb, CornerRadius = new CornerRadius(11), Padding = new Thickness(11, 7, 11, 7), Margin = new Thickness(0, 0, 0, 5),
            Background = d.IsDefault ? new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255)),
            Cursor = System.Windows.Input.Cursors.Hand, ToolTip = d.Name
        };
        if (!d.IsDefault)
        {
            chip.MouseEnter += (_, _) => chip.Background = new SolidColorBrush(Color.FromArgb(0x38, 255, 255, 255));
            chip.MouseLeave += (_, _) => chip.Background = new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255));
        }
        chip.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (d.IsDefault) return;
            Pop(chip, 0.88, 420);
            if (AudioSwitch.SetDefault(d.Id))
            {
                // громкость и значок микрофона теперь следят за новым устройством
                try { if (_dev != null) _dev.AudioEndpointVolume.OnVolumeNotification -= OnVol; } catch { }
                InitVolume();
                InitMicMute();
            }
            RenderDevices();
        };
        return chip;
    }
}
