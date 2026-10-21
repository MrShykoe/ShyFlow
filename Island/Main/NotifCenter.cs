using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Island;

// Уведомления, шаг 49: как на Mac.
//   - Висят дольше (Настройки, «Плашки», 4–15 секунд), а пока мышь над островом, не пропадают.
//   - Клик раскрывает уведомление: весь текст и кнопки «Открыть» (перейти туда, откуда оно), «Скрыть», «Все».
//     Клик по уже раскрытому тоже открывает источник.
//   - Смахнуть в сторону или вверх: уведомление улетает (жест в Input.cs).
//   - Шторка уведомлений: все пришедшие уведомления списком, новые сверху. Открывается колокольчиком в шапке быстрых действий (шаг 51; раньше был в развёрнутом острове),
//     плиткой в быстрых действиях, Ctrl+Alt+B, из меню острова или жестом «потянуть вниз» по развёрнутому острову.
//     В шторке: клик открывает источник, смахнуть карточку в сторону или крестик убирают её, «Очистить» убирает всё.
//
// Откуда уведомление. У Windows нет способа «нажать» чужое уведомление, поэтому по порядку:
//   1) у уведомления есть ссылка (activationType="protocol", например tg:// или discord://): открываем её, это точный переход;
//   2) программа уже открыта: её окно выходит на передний план (ищем по имени exe и коду приложения из базы уведомлений);
//   3) иначе запускаем программу по её коду (shell:AppsFolder\код), как из меню «Пуск».
internal sealed class NotifItem
{
    public string App = "", Title = "", Body = "", AppId = "", Launch = "";
    public DateTime At = DateTime.Now;
    public bool Keep;   // настоящее уведомление Windows: попадает в шторку (таймер, напоминания и т.п. нет)
}

public partial class MainWindow
{
    private const int CenterMax = 40;
    private readonly List<NotifItem> _notifs = new();
    private NotifItem? _notifCur;
    private bool _notifOpen, _centerOpen;
    private int _unread;

    private void InitNotifCenter()
    {
        // мышь над уведомлением: оно не пропадает, пока не уберёшь
        Pill.MouseEnter += (_, _) => { if (_mode == Mode.Notif) _toastTimer.Stop(); };
        Pill.MouseLeave += (_, _) =>
        {
            if (_mode != Mode.Notif || _toastTimer.IsEnabled) return;
            _toastTimer.Interval = TimeSpan.FromMilliseconds(_notifOpen ? 6000 : 2500);
            _toastTimer.Start();
        };
        UpdateBell();
    }

    // ---------- уведомление в острове ----------
    private void ShowNotifItem(NotifItem n)
    {
        n.App = Loc.T(n.App); n.Title = Loc.T(n.Title); n.Body = Loc.T(n.Body);
        _notifClick = null;
        string key = n.Title + "|" + n.Body;
        if (key == _lastNotifKey && (DateTime.UtcNow - _lastNotifAt).TotalSeconds < 3) return; // дубль
        _lastNotifKey = key;
        _lastNotifAt = DateTime.UtcNow;

        if (n.Keep && _cfg.NotifCenter)
        {
            _notifs.Insert(0, n);
            if (_notifs.Count > CenterMax) _notifs.RemoveRange(CenterMax, _notifs.Count - CenterMax);
            if (_mode != Mode.Center) _unread++;
            UpdateBell();
            if (FocusQuiet) { _focusMuted++; return; }   // фокус (Focus.cs): не всплывает, тихо лежит в шторке
            if (_mode == Mode.Center) { RenderCenter(); ApplyMode(Mode.Center); return; }   // шторка открыта: просто появляется сверху
        }
        n = StreamSafe(n);   // режим стримера: всплывает без текста, в шторке он остаётся (Streamer.cs)
        _notifCur = n;
        _notifOpen = false;
        if (EventsToBubble) { ShowEventNotif(n); return; }   // шаг 52: в пузыре рядом с островом (EventBubble.cs)

        string who = !string.IsNullOrWhiteSpace(n.App) ? n.App : n.Title;
        FillNotifIcon(NotifIcon, NotifInit, NotifImg, n, who);
        NotifApp.Text = n.App;
        NotifApp.Visibility = string.IsNullOrWhiteSpace(n.App) ? Visibility.Collapsed : Visibility.Visible;
        NotifTime.Visibility = NotifApp.Visibility;
        NotifTime.Text = Loc.T("сейчас");
        NotifTitle.Text = n.Title;
        NotifBody.Text = n.Body;
        NotifBody.Visibility = string.IsNullOrWhiteSpace(n.Body) ? Visibility.Collapsed : Visibility.Visible;

        // прошлое уведомление могли смахнуть: возвращаем на место
        var t = Get(NotifInner).T;
        t.BeginAnimation(TranslateTransform.XProperty, null); t.X = 0;
        t.BeginAnimation(TranslateTransform.YProperty, null); t.Y = 0;
        NotifInner.BeginAnimation(OpacityProperty, null);
        NotifInner.Opacity = 1;

        bool twoLines = LayoutNotif(out double w);
        ApplyMode(Mode.Notif, w);
        Pop(NotifIcon, 0.4, 600);
        Spin(NotifIcon, -30);
        Slide(NotifTexts, 16, 520);
        Wobble();
        Sfx.Ui("notif");
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(_cfg.NotifSeconds, 3, 30) + (twoLines ? 1.5 : 0));
        if (!Pill.IsMouseOver) _toastTimer.Start();
    }

    // значок уведомления: настоящая иконка программы, если она открыта, иначе буквы на её цвете
    private void FillNotifIcon(SBorder box, TextBlock init, Image img, NotifItem n, string who)
    {
        var icon = AppIconFor(n);
        if (icon != null)
        {
            img.Source = icon;
            img.Visibility = Visibility.Visible;
            init.Visibility = Visibility.Collapsed;
            box.Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255));
            return;
        }
        img.Visibility = Visibility.Collapsed;
        init.Visibility = Visibility.Visible;
        var col = AppColor(who);
        box.Background = new LinearGradientBrush(
            Color.FromRgb((byte)Math.Min(255, col.R + 30), (byte)Math.Min(255, col.G + 30), (byte)Math.Min(255, col.B + 30)), col, 90);
        bool light = 0.299 * col.R + 0.587 * col.G + 0.114 * col.B > 186;
        init.Foreground = light ? Brushes.Black : Brushes.White;
        init.Text = Initials(who);
    }

    // ширина и высота уведомления; true, если текст в две строки
    private bool LayoutNotif(out double w)
    {
        var n = _notifCur;
        string title = n?.Title ?? "", app = n?.App ?? "", body = n?.Body ?? "";
        double col1 = Math.Max(TextW(title, 14, FontWeights.SemiBold), TextW(app, 11, FontWeights.Medium) + 50);
        double bodyW = TextW(body, 12.5, FontWeights.Normal);
        w = Math.Clamp(12 + 44 + 12 + Math.Max(col1, Math.Min(bodyW, 300)) + 18 + 6, 260, 480);
        double textW = w - 12 - 44 - 12 - 18;
        bool twoLines = bodyW > textW - 3;   // запас на округление: впритык строка уже обрезается «…»

        NotifBody.MaxHeight = _notifOpen ? 16 * 8 : 32;
        NotifTitle.TextWrapping = _notifOpen ? TextWrapping.Wrap : TextWrapping.NoWrap;
        NotifTitle.MaxHeight = _notifOpen ? 40 : double.PositiveInfinity;
        NotifIcon.VerticalAlignment = _notifOpen ? VerticalAlignment.Top : VerticalAlignment.Center;
        NotifBtns.Visibility = _notifOpen ? Visibility.Visible : Visibility.Collapsed;
        if (!_notifOpen) NotifReplies.Visibility = Visibility.Collapsed;
        if (!_notifOpen)
        {
            _notifH = 76 + (twoLines ? 16 : 0) + (NotifApp.Visibility == Visibility.Visible ? 0 : -12);
            return twoLines;
        }
        // раскрыто: остров шире и выше, ровно под весь текст и кнопки
        w = Math.Max(w, 400);
        NotifInner.Measure(new Size(w - 30, double.PositiveInfinity));
        _notifH = Math.Clamp(NotifInner.DesiredSize.Height + 28, 110, 330);
        return twoLines;
    }

    // клик по уведомлению в острове; true: обработали, остров остаётся как есть
    private bool NotifClick()
    {
        var n = _notifCur;
        if (n == null) return false;
        if (!n.Keep && n.AppId.Length == 0 && n.Launch.Length == 0) return false;   // своё (таймер, напоминание): клик просто закрывает
        if (!_notifOpen) { ExpandNotif(); return true; }
        OpenNotifSource(n);
        ApplyMode(Rest());
        return true;
    }

    private void ExpandNotif()
    {
        _notifOpen = true;
        NotifBtns.Children.Clear();
        NotifBtns.Children.Add(NotifBtn("x", "Скрыть", false, () => DismissNotif(0)));
        if (_cfg.NotifCenter) NotifBtns.Children.Add(NotifBtn("bell", "Все", false, OpenCenter));
        if (_notifCur is { Keep: true } && QuickReplies().Count > 0) NotifBtns.Children.Add(NotifBtn("message-circle", "Ответить", false, ToggleReplies));
        NotifBtns.Children.Add(NotifBtn("external-link", "Открыть", true, () => { if (_notifCur != null) OpenNotifSource(_notifCur); ApplyMode(Rest()); }));
        LayoutNotif(out double w);
        ApplyMode(Mode.Notif, w);
        Droplets(NotifBtns.Children.OfType<FrameworkElement>());
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(15);
        if (!Pill.IsMouseOver) _toastTimer.Start();
    }

    // ---------- быстрые ответы (шаг 56) ----------
    // Заготовки из настроек («Плашки», «Быстрые ответы», через «;»). Клик: фраза в буфер, открывается программа,
    // откуда уведомление; если включено «Вставлять сразу», ShyFlow сам нажимает Ctrl+V.
    private List<string> QuickReplies() =>
        (_cfg.QuickReplies ?? "").Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).Take(8).ToList();

    private void ToggleReplies()
    {
        bool show = NotifReplies.Visibility != Visibility.Visible;
        NotifReplies.Children.Clear();
        if (show)
            foreach (var r in QuickReplies())
            {
                var text = r;
                var chip = new SBorder
                {
                    CornerRadius = new CornerRadius(13), Padding = new Thickness(11, 5, 11, 6), Margin = new Thickness(6, 0, 0, 6), Cursor = Cursors.Hand,
                    Background = new SolidColorBrush(Color.FromArgb(0x30, 0x0A, 0x84, 0xFF)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x0A, 0x84, 0xFF)), BorderThickness = new Thickness(1),
                    Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.Medium }
                };
                chip.MouseLeftButtonDown += (_, e) => e.Handled = true;
                chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; _ = SendReply(text); };
                NotifReplies.Children.Add(chip);
            }
        NotifReplies.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        LayoutNotif(out double w);
        ApplyMode(Mode.Notif, w);
        if (show) Droplets(NotifReplies.Children.OfType<FrameworkElement>());
        _toastTimer.Stop();   // выбирает ответ: не прятать
    }

    private async Task SendReply(string text)
    {
        var n = _notifCur;
        if (n == null) return;
        Sfx.Ui("tap");
        CopyBack(text);
        bool found = FindAppWindow(n) != IntPtr.Zero || n.Launch.Length > 0;
        OpenNotifSource(n);
        ApplyMode(Rest());
        if (_cfg.QuickReplyPaste && found)
        {
            await Task.Delay(700);   // программа успевает выйти на передний план и поставить курсор в поле сообщения
            SendCombo(0x56);
            return;
        }
        ShowToast("message-circle", Loc.T("Ответ в буфере: вставь Ctrl+V"), null, "copy");
    }

    private FrameworkElement NotifBtn(string icon, string text, bool primary, Action act)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new VIcon { Text = icon, FontSize = 12, Foreground = primary ? Brushes.Black : Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = Loc.T(text), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = primary ? Brushes.Black : Brushes.White, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var b = new SBorder
        {
            Child = row, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 6, 13, 6), Margin = new Thickness(6, 0, 0, 0), Cursor = Cursors.Hand,
            Background = primary ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255))
        };
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Pop(b, 0.85, 380);
            Sfx.Ui("tap");
            try { act(); } catch (Exception ex) { App.LogOnce("Notif.Btn", ex); }
        };
        return b;
    }

    // смахнули: уведомление улетает в сторону (dir = -1/1) или вверх (0), остров сворачивается
    private void DismissNotif(int dir)
    {
        _toastTimer.Stop();
        Sfx.Ui("dismiss");
        var t = Get(NotifInner).T;
        var d = TimeSpan.FromMilliseconds(200);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        if (dir != 0) t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dir * 160, d) { EasingFunction = ease });
        else t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-30, d) { EasingFunction = ease });
        var fade = new DoubleAnimation(0, d);
        fade.Completed += (_, _) => { if (_mode == Mode.Notif) ApplyMode(Rest()); };
        NotifInner.BeginAnimation(OpacityProperty, fade);
    }

    // ---------- откуда уведомление ----------
    private void OpenNotifSource(NotifItem n)
    {
        try
        {
            // 1) точная ссылка из самого уведомления
            if (n.Launch.Length > 0 && Uri.TryCreate(n.Launch, UriKind.Absolute, out var u) && !u.IsFile)
            {
                Process.Start(new ProcessStartInfo(n.Launch) { UseShellExecute = true });
                return;
            }
            // 2) программа уже открыта
            var h = FindAppWindow(n);
            if (h != IntPtr.Zero)
            {
                if (IsIconic(h)) ShowWindow(h, 9 /*SW_RESTORE*/);
                SetForegroundWindow(h);
                return;
            }
            // 3) запустить по коду приложения, как из «Пуска»
            if (n.AppId.Length > 0 && n.AppId.IndexOfAny(Path.GetInvalidFileNameChars().Where(c => c != '\\').ToArray()) < 0)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + n.AppId) { UseShellExecute = true });
                return;
            }
            ShowToast("triangle-alert", Loc.T("Не нашёл программу: ") + n.App, null, "key");
        }
        catch (Exception ex) { App.LogOnce("Notif.Open", ex); }
    }

    // слова из имени и кода приложения: «TelegramMessengerLLP.TelegramDesktop_…!Telegram.Desktop» -> telegram, telegramdesktop…
    private static readonly HashSet<string> NotifNoise = new() { "desktop", "microsoft", "windows", "com", "app", "apps", "client", "squirrel", "exe", "launcher", "llp", "inc", "ltd", "corp", "the", "system" };

    private static List<string> NotifTokens(NotifItem n)
    {
        var words = new List<string>();
        foreach (var src in new[] { n.AppId, n.App })
        {
            var cur = new System.Text.StringBuilder();
            foreach (char ch in src.ToLowerInvariant() + " ")
            {
                if (char.IsLetterOrDigit(ch)) { cur.Append(ch); continue; }
                if (cur.Length >= 3 && !NotifNoise.Contains(cur.ToString())) words.Add(cur.ToString());
                cur.Clear();
            }
        }
        return words.Distinct().ToList();
    }

    private static bool NotifMatches(List<string> tokens, string appName, string exe, string title)
    {
        if (exe is "explorer" or "applicationframehost")
            return appName.Length >= 3 && title.Contains(appName, StringComparison.OrdinalIgnoreCase);   // приложения из Магазина: по заголовку окна
        if (exe.Length < 3) return false;
        foreach (var t in tokens)
            if (t == exe || (exe.Length >= 4 && t.Contains(exe)) || (t.Length >= 4 && exe.Contains(t))) return true;
        return false;
    }

    private IntPtr FindAppWindow(NotifItem n)
    {
        var tokens = NotifTokens(n);
        if (tokens.Count == 0) return IntPtr.Zero;
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (!IsAppWindow(h)) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == OwnPid) return true;
            string exe = Path.GetFileNameWithoutExtension(ProcessPath(pid)).ToLowerInvariant();
            if (!NotifMatches(tokens, n.App, exe, WinTitle(h))) return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    // иконка программы, от которой уведомление (если она открыта)
    private BitmapSource? AppIconFor(NotifItem n)
    {
        if (n.AppId.Length == 0 && !n.Keep) return null;   // таймер, напоминание: свой цветной значок
        try
        {
            var tokens = NotifTokens(n);
            if (tokens.Count == 0) return null;
            foreach (var app in _apps.Values)
            {
                string exe = Path.GetFileNameWithoutExtension(app.Path).ToLowerInvariant();
                if (app.Icon != null && NotifMatches(tokens, n.App, exe, app.Name)) return app.Icon;
            }
            var h = FindAppWindow(n);
            if (h == IntPtr.Zero) return null;
            GetWindowThreadProcessId(h, out uint pid);
            string path = ProcessPath(pid);
            return (path.Length > 0 ? AppIcon(path) : null) ?? WindowIcon(h);
        }
        catch { return null; }
    }

    // ---------- шторка ----------
    private double CenterH()
    {
        CenterList.Measure(new Size(408, double.PositiveInfinity));
        double list = _notifs.Count == 0 ? 90 : CenterList.DesiredSize.Height;
        return Math.Clamp(12 + 32 + 8 + list + 14, 150, 390);
    }

    private void OpenCenter()
    {
        if (!_cfg.NotifCenter) return;
        _pocketOpen = false; _actionsOpen = false; _devicesOpen = false; _timerOpen = false; _mixerOpen = false; _centerOpen = false; _trOpen = false; _autoOpened = false;
        _centerOpen = true;
        _toastTimer.Stop();
        _unread = 0;
        UpdateBell();
        RenderCenter();
        ApplyMode(Rest());
        Activate();
    }

    private void ToggleCenter()
    {
        if (_centerOpen) { _centerOpen = false; _trOpen = false; ApplyMode(Rest()); }
        else OpenCenter();
    }

    private void CenterClose_Click(object s, RoutedEventArgs e) { _centerOpen = false; _trOpen = false; ApplyMode(Rest()); }
    private void Bell_Click(object s, RoutedEventArgs e) { if (s is FrameworkElement b) Pop(b, 0.7, 420); OpenCenter(); }
    private void Center_Click(object s, RoutedEventArgs e) => OpenCenter();

    private void CenterClear_Click(object s, RoutedEventArgs e)
    {
        if (_notifs.Count == 0) return;
        Sfx.Ui("dismiss");
        // карточки разлетаются по очереди, потом шторка становится пустой
        int i = 0;
        foreach (var el in CenterList.Children.OfType<FrameworkElement>().ToList())
        {
            var t = Get(el).T;
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(260, TimeSpan.FromMilliseconds(220))
            { BeginTime = TimeSpan.FromMilliseconds(i * 35), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { BeginTime = TimeSpan.FromMilliseconds(i * 35) });
            i++;
        }
        var done = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240 + i * 35) };
        done.Tick += (_, _) =>
        {
            done.Stop();
            _notifs.Clear();
            RenderCenter();
            if (_mode == Mode.Center) ApplyMode(Mode.Center);
        };
        done.Start();
    }

    private void UpdateBell()
    {
        bool on = _cfg.NotifCenter;
        BellBtn.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        BellBadge.Visibility = on && _unread > 0 ? Visibility.Visible : Visibility.Collapsed;
        BellCount.Text = _unread > 9 ? "9+" : _unread.ToString();
    }

    private void RenderCenter()
    {
        CenterList.Children.Clear();
        CenterCount.Text = _notifs.Count > 0 ? _notifs.Count.ToString() : "";
        CenterEmpty.Visibility = _notifs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CenterClear.Visibility = _notifs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var n in _notifs) CenterList.Children.Add(CenterCard(n));
    }

    private static string Ago(DateTime at)
    {
        var d = DateTime.Now - at;
        if (d.TotalMinutes < 1) return Loc.T("сейчас");
        if (d.TotalHours < 1) return (int)d.TotalMinutes + " " + Loc.T("мин");
        if (d.TotalDays < 1 && at.Date == DateTime.Today) return at.ToString("HH:mm");
        return at.ToString("d MMM, HH:mm", Loc.Culture);
    }

    private FrameworkElement CenterCard(NotifItem n)
    {
        string who = !string.IsNullOrWhiteSpace(n.App) ? n.App : n.Title;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var icoBox = new SBorder { Width = 36, Height = 36, CornerRadius = new CornerRadius(11), VerticalAlignment = VerticalAlignment.Top };
        var init = new TextBlock { FontWeight = FontWeights.Bold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var img = new Image { Width = 24, Height = 24 };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        icoBox.Child = new Grid { Children = { init, img } };
        FillNotifIcon(icoBox, init, img, n, who);
        grid.Children.Add(icoBox);

        var texts = new StackPanel { Margin = new Thickness(11, 0, 22, 0) };
        Grid.SetColumn(texts, 1);
        var top = new Grid();
        top.Children.Add(new TextBlock { Text = n.App, Foreground = new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255)), FontSize = 11, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 60, 0) });
        top.Children.Add(new TextBlock { Text = Ago(n.At), Foreground = new SolidColorBrush(Color.FromArgb(0x60, 255, 255, 255)), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right });
        texts.Children.Add(top);
        texts.Children.Add(new TextBlock { Text = n.Title, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
        if (!string.IsNullOrWhiteSpace(n.Body))
            texts.Children.Add(new TextBlock { Text = n.Body, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255)), FontSize = 12, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 15, MaxHeight = 30 });
        grid.Children.Add(texts);

        // крестик: виден при наведении
        var x = new SBorder
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -2, -4, 0), Opacity = 0, Cursor = Cursors.Hand,
            Child = new VIcon { Text = "x", FontSize = 10, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        Grid.SetColumnSpan(x, 2);
        grid.Children.Add(x);

        var bg = new SolidColorBrush(Color.FromArgb(0x16, 255, 255, 255));
        var card = new SBorder { Child = grid, CornerRadius = new CornerRadius(16), Padding = new Thickness(10, 9, 12, 10), Margin = new Thickness(0, 0, 0, 6), Background = bg, Cursor = Cursors.Hand, ToolTip = Loc.T("Клик: открыть. Смахни в сторону, чтобы убрать") };
        card.MouseEnter += (_, _) => { ColorTween.To(bg, SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0x26, 255, 255, 255), TimeSpan.FromMilliseconds(140))); x.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140))); };
        card.MouseLeave += (_, _) => { ColorTween.To(bg, SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0x16, 255, 255, 255), TimeSpan.FromMilliseconds(220))); x.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))); };
        x.MouseLeftButtonDown += (_, e) => e.Handled = true;
        x.MouseLeftButtonUp += (_, e) => { e.Handled = true; RemoveCard(card, n, 1); };

        // смахнуть карточку: тянется за мышью, дальше 90 px улетает
        Point start = default;
        bool down = false, drag = false;
        var tt = Get(card).T;
        card.MouseLeftButtonDown += (_, e) => { e.Handled = true; start = e.GetPosition(CenterPanel); down = true; drag = false; card.CaptureMouse(); };
        card.MouseMove += (_, e) =>
        {
            if (!down) return;
            double dx = e.GetPosition(CenterPanel).X - start.X;
            if (Math.Abs(dx) > 5) drag = true;
            if (!drag) return;
            tt.BeginAnimation(TranslateTransform.XProperty, null);
            tt.X = dx;
            card.Opacity = Math.Clamp(1 - Math.Abs(dx) / 260, 0.25, 1);
        };
        card.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (!down) return;
            down = false;
            card.ReleaseMouseCapture();
            double dx = e.GetPosition(CenterPanel).X - start.X;
            if (!drag)
            {
                Sfx.Ui("tap");
                _notifs.Remove(n);
                _centerOpen = false; _trOpen = false;
                OpenNotifSource(n);
                ApplyMode(Rest());
                return;
            }
            if (Math.Abs(dx) > 90) { RemoveCard(card, n, Math.Sign(dx)); return; }
            double cur = tt.X;
            tt.X = 0;
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(cur, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = Spr(), FillBehavior = FillBehavior.Stop });
            card.Opacity = 1;
        };
        card.LostMouseCapture += (_, _) =>
        {
            if (!down) return;
            down = false;
            tt.BeginAnimation(TranslateTransform.XProperty, null);
            tt.X = 0;
            card.Opacity = 1;
        };
        return card;
    }

    // карточка улетает, место под ней схлопывается, шторка становится ниже
    private void RemoveCard(SBorder card, NotifItem n, int dir)
    {
        Sfx.Ui("dismiss");
        _notifs.Remove(n);
        var tt = Get(card).T;
        tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dir * 420, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
        double h = card.ActualHeight;
        var shrink = new DoubleAnimation(h, 0, TimeSpan.FromMilliseconds(240)) { BeginTime = TimeSpan.FromMilliseconds(150), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        shrink.Completed += (_, _) =>
        {
            RenderCenter();
            if (_mode == Mode.Center) ApplyMode(Mode.Center);
        };
        card.BeginAnimation(HeightProperty, shrink);
        card.BeginAnimation(MarginProperty, new ThicknessAnimation(new Thickness(0), TimeSpan.FromMilliseconds(240)) { BeginTime = TimeSpan.FromMilliseconds(150) });
    }

    private IEnumerable<FrameworkElement> CenterItems() => CenterList.Children.OfType<FrameworkElement>().Take(6);
}
