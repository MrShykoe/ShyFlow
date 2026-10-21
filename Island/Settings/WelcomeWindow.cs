using System.Windows.Controls.Primitives;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Island;

// Приветствие при каждом запуске: на весь экран плавно сменяются «hello» на разных языках,
// потом короткая первая настройка (язык, главные функции). Esc пропускает всё.
// Насовсем окно отключается кнопкой «Больше не показывать» на последнем шаге (AppSettings.HideWelcome).
public sealed class WelcomeWindow : Window
{
    private static readonly string[] Greetings = { "hello", "привіт", "hola", "bonjour", "ciao", "こんにちは", "你好", "hello" };
    private const int Steps = 3;

    private static readonly Color Green = Color.FromRgb(0x34, 0xC7, 0x59);
    private static readonly Color Blue = Color.FromRgb(0x0A, 0x84, 0xFF);
    private static readonly Color Off = Color.FromRgb(0x48, 0x48, 0x4C);
    private static readonly Brush Gray = new SolidColorBrush(Color.FromRgb(0x98, 0x98, 0x9D));

    private readonly AppSettings _s;
    private readonly Action _changed;
    private readonly Grid _root = new() { Opacity = 0 };
    private readonly TextBlock _hello = new();
    private readonly BlurEffect _helloBlur = new() { Radius = 30 };
    private readonly ScaleTransform _helloScale = new(0.9, 0.9);
    private readonly Grid _setup = new() { Visibility = Visibility.Collapsed, Opacity = 0 };
    private readonly Border _pageHost = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 640 };
    private int _step;
    private bool _skipped, _setupShown, _closing;

    public WelcomeWindow(AppSettings s, Action changed)
    {
        _s = s;
        _changed = changed;
        Sfx.Warmup();   // звуки считаются заранее, чтобы первый не опоздал
        Title = "ShyFlow";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        Topmost = true;
        Background = Brushes.Black;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Content = _root;

        // мягкое цветное свечение за текстом
        var glow = new Ellipse
        {
            Width = 900, Height = 520, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.55, IsHitTestVisible = false,
            Fill = new RadialGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0x70, 0x5E, 0x5C, 0xE6), 0),
                new GradientStop(Color.FromArgb(0x30, 0xFF, 0x37, 0x5F), 0.55),
                new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1),
            }),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1)
        };
        var pulse = new DoubleAnimation(0.92, 1.1, TimeSpan.FromSeconds(3.2))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
        ((ScaleTransform)glow.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        ((ScaleTransform)glow.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        _root.Children.Add(glow);

        _hello.FontFamily = new FontFamily("Segoe Script, Segoe UI Variable Display, Segoe UI");
        _hello.FontSize = 130;
        _hello.FontWeight = FontWeights.Bold;
        _hello.HorizontalAlignment = HorizontalAlignment.Center;
        _hello.VerticalAlignment = VerticalAlignment.Center;
        _hello.Opacity = 0;
        _hello.Padding = new Thickness(30, 10, 30, 30);
        _hello.Foreground = new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(Color.FromRgb(0x5E, 0xA8, 0xFF), 0),
            new GradientStop(Color.FromRgb(0xB0, 0x6B, 0xFF), 0.5),
            new GradientStop(Color.FromRgb(0xFF, 0x6B, 0x9D), 1),
        }, 0);
        _hello.Effect = _helloBlur;
        _hello.RenderTransformOrigin = new Point(0.5, 0.5);
        _hello.RenderTransform = _helloScale;
        _root.Children.Add(_hello);

        _setup.Children.Add(_pageHost);
        _root.Children.Add(_setup);

        Loaded += (_, _) =>
        {
            _root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900)));
            Focus();
            Sfx.Intro();
            _ = RunIntro();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Finish(false);
            else if ((e.Key == Key.Enter || e.Key == Key.Space) && !_setupShown) Skip();
        };
        MouseLeftButtonDown += (_, _) => { if (!_setupShown) Skip(); };
    }

    // ---------- «hello» ----------
    private void Appear(string text)
    {
        _hello.Text = text;
        var d = TimeSpan.FromMilliseconds(700);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _hello.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, d) { EasingFunction = ease });
        _helloBlur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(30, 0, d) { EasingFunction = ease });
        _helloScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, 1, d) { EasingFunction = ease });
        _helloScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, 1, d) { EasingFunction = ease });
    }

    private void Vanish(int ms = 450)
    {
        var d = TimeSpan.FromMilliseconds(ms);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        _hello.BeginAnimation(OpacityProperty, new DoubleAnimation(0, d) { EasingFunction = ease });
        _helloBlur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(30, d) { EasingFunction = ease });
        _helloScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.06, d) { EasingFunction = ease });
        _helloScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.06, d) { EasingFunction = ease });
    }

    private async Task RunIntro()
    {
        await Task.Delay(500);
        for (int i = 0; i < Greetings.Length && !_skipped; i++)
        {
            Appear(Greetings[i]);
            if (i > 0) Sfx.Greeting(i);   // первое «hello» звучит вместе со вступлением
            await Task.Delay(i == Greetings.Length - 1 ? 1300 : 950);
            if (_skipped) return;
            if (i < Greetings.Length - 1) { Vanish(); await Task.Delay(460); }
        }
        if (_skipped) return;
        Vanish(600);
        await Task.Delay(650);
        GoSetup();
    }

    private void Skip()
    {
        if (_skipped || _setupShown) return;
        _skipped = true;
        Vanish(300);
        Dispatcher.BeginInvoke(new Action(async () => { await Task.Delay(320); GoSetup(); }));
    }

    private void GoSetup()
    {
        if (_setupShown) return;
        _setupShown = true;
        Sfx.Next();
        _setup.Visibility = Visibility.Visible;
        BuildPage(true);
        _setup.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)));
    }

    // ---------- первая настройка ----------
    private void BuildPage(bool animate)
    {
        var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        var titles = new[]
        {
            (Loc.T("Язык", "Language"), Loc.T("На каком языке показывать остров и настройки.", "Which language ShyFlow should speak.")),
            (Loc.T("Что включить", "What to turn on"), Loc.T("Всё это настраивается позже в окне настроек.", "All of this lives in Settings later.")),
            (Loc.T("Всё готово", "You're all set"), Loc.T("Пара подсказок, и можно слушать.", "A few tips and you're good to go.")),
        };
        col.Children.Add(new TextBlock
        {
            Text = Loc.T("ШАГ ", "STEP ") + (_step + 1) + Loc.T(" ИЗ ", " OF ") + Steps,
            Foreground = Gray, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8)
        });
        col.Children.Add(new TextBlock { Text = titles[_step].Item1, Foreground = Brushes.White, FontSize = 38, FontWeight = FontWeights.Bold });
        col.Children.Add(new TextBlock
        {
            Text = titles[_step].Item2, Foreground = Gray, FontSize = 16, Margin = new Thickness(0, 6, 0, 28), TextWrapping = TextWrapping.Wrap
        });
        col.Children.Add(_step switch { 0 => LanguagePage(), 1 => FeaturesPage(), _ => TipsPage() });
        col.Children.Add(NavRow());

        _pageHost.Child = col;
        if (!animate) return;
        var tr = new TranslateTransform(0, 24);
        col.RenderTransform = tr;
        col.Opacity = 0;
        var d = TimeSpan.FromMilliseconds(600);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        col.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, d));
        tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(24, 0, d) { EasingFunction = ease });
    }

    private FrameworkElement LanguagePage()
    {
        var wrap = new WrapPanel();
        var names = Loc.Names.Select((n, i) => i == 0 ? Loc.T("Авто", "Auto") : n).ToArray();   // шаг 74: 7 языков
        for (int i = 0; i < names.Length; i++)
        {
            int idx = i;
            wrap.Children.Add(Chip(names[i], _s.Language == i, () =>
            {
                _s.Language = idx;
                Loc.Init(idx);
                BuildPage(false);
            }, 150, 64));
        }
        return wrap;
    }

    private FrameworkElement FeaturesPage()
    {
        var card = new StackPanel();
        void Row(string title, string? sub, bool on, Action<bool> set)
        {
            if (card.Children.Count > 0)
                card.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(0x16, 255, 255, 255)), Margin = new Thickness(18, 0, 0, 0) });
            var g = new Grid { Margin = new Thickness(18, 13, 16, 13) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            tp.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.Medium });
            if (sub != null) tp.Children.Add(new TextBlock { Text = sub, Foreground = Gray, FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0) });
            g.Children.Add(tp);
            var sw = Switch(on, set);
            Grid.SetColumn(sw, 1);
            g.Children.Add(sw);
            card.Children.Add(g);
        }
        Row(Loc.T("Запускать вместе с Windows", "Start with Windows"), null, _s.Autostart, v => _s.Autostart = v);
        Row(Loc.T("Слова песен", "Song lyrics"), Loc.T("Строки вместо названия, под голос", "Shown in the island, in sync with the song"), _s.Lyrics, v => _s.Lyrics = v);
        Row(Loc.T("Прогресс волной", "Waveform progress"), Loc.T("Как у голосовых в Telegram", "Like voice messages in Telegram"), _s.WaveSeek, v => _s.WaveSeek = v);
        Row(Loc.T("Уведомления Windows", "Windows notifications"), Loc.T("Telegram, браузер и другие всплывашки в острове", "Telegram, browser and others pop up in the island"), _s.Notifications, v => _s.Notifications = v);
        if (AppInfo.DiscordAppId.Length > 0)
            Row(Loc.T("Показывать в Discord", "Show in Discord"), Loc.T("Что ты слушаешь, видно в профиле", "What you listen to appears in your profile"), _s.DiscordRpc, v => _s.DiscordRpc = v);
        return new SBorder { CornerRadius = new CornerRadius(18), Background = new SolidColorBrush(Color.FromArgb(0x16, 255, 255, 255)), Child = card };
    }

    private FrameworkElement TipsPage()
    {
        var col = new StackPanel();
        void Tip(string icon, string text)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            row.Children.Add(new SBorder
            {
                Width = 44, Height = 44, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 16, 0),
                Background = new SolidColorBrush(Color.FromArgb(0x1E, 255, 255, 255)),
                Child = new VIcon { Text = icon, FontSize = 18, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
            row.Children.Add(new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 540 });
            col.Children.Add(row);
        }
        Tip("maximize-2", Loc.T("Нажми на остров, чтобы раскрыть его и увидеть плеер", "Click the island to open the player"));
        Tip("image", Loc.T("Нажми на обложку в раскрытом острове: плеер откроется на весь экран", "Click the cover in the open island for the full-screen player"));
        Tip("download", Loc.T("Перетащи файл на остров: он попадёт в карман", "Drop a file on the island to keep it in the pocket"));
        Tip("settings", Loc.T("Настройки: правый клик по острову или Ctrl+Alt+S. Не помнишь, где что: просто начни печатать в окне настроек", "Settings: right-click the island or Ctrl+Alt+S. Can't remember where something is: just start typing in the Settings window"));
        Tip("sliders-horizontal", Loc.T("Ctrl + Alt + I скрывает и показывает остров", "Ctrl + Alt + I hides and shows the island"));
        return col;
    }

    // ---------- элементы ----------
    private FrameworkElement NavRow()
    {
        var g = new Grid { Margin = new Thickness(0, 34, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (_step > 0)
        {
            var back = Pill(Loc.T("Назад", "Back"), false, () => { Sfx.Back(); _step--; BuildPage(true); });
            back.HorizontalAlignment = HorizontalAlignment.Left;
            g.Children.Add(back);
        }

        var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < Steps; i++)
            dots.Children.Add(new SBorder
            {
                Width = i == _step ? 22 : 7, Height = 7, CornerRadius = new CornerRadius(4), Margin = new Thickness(3, 0, 3, 0),
                Background = new SolidColorBrush(i == _step ? Colors.White : Color.FromArgb(0x55, 255, 255, 255))
            });
        Grid.SetColumn(dots, 1);
        g.Children.Add(dots);

        bool last = _step == Steps - 1;
        var next = Pill(last ? Loc.T("Готово", "Done") : Loc.T("Далее", "Next"), true, () =>
        {
            if (last) Finish(false); else { Sfx.Next(); _step++; BuildPage(true); }
        });
        next.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(next, 2);
        g.Children.Add(next);
        if (!last) return g;

        // в самом конце: отдельная кнопка, после неё приветствие больше не открывается при запуске
        var never = Pill(Loc.T("Больше не показывать", "Don't show again"), false, () => Finish(true));
        never.HorizontalAlignment = HorizontalAlignment.Center;
        never.Margin = new Thickness(0, 16, 0, 0);
        var wrap = new StackPanel();
        wrap.Children.Add(g);
        wrap.Children.Add(never);
        return wrap;
    }

    private static Border Pill(string text, bool primary, Action click)
    {
        var bg = new SolidColorBrush(primary ? Colors.White : Color.FromArgb(0x22, 255, 255, 255));
        var b = new SBorder
        {
            CornerRadius = new CornerRadius(20), Padding = new Thickness(28, 11, 28, 11), Cursor = Cursors.Hand, Background = bg,
            Child = new TextBlock { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = primary ? Brushes.Black : Brushes.White }
        };
        var scale = new ScaleTransform(1, 1);
        b.RenderTransform = scale;
        b.RenderTransformOrigin = new Point(0.5, 0.5);
        b.MouseEnter += (_, _) => ColorTween.To(bg, SolidColorBrush.ColorProperty, new ColorAnimation(primary ? Color.FromRgb(0xE6, 0xE6, 0xE6) : Color.FromArgb(0x38, 255, 255, 255), TimeSpan.FromMilliseconds(120)));
        b.MouseLeave += (_, _) => ColorTween.To(bg, SolidColorBrush.ColorProperty, new ColorAnimation(primary ? Colors.White : Color.FromArgb(0x22, 255, 255, 255), TimeSpan.FromMilliseconds(180)));
        b.MouseLeftButtonUp += (_, e) =>
        {
            var a = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new SpringEase(6.5, 10) };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            e.Handled = true;
            click();
        };
        return b;
    }

    private static Border Chip(string text, bool selected, Action click, double w, double h)
    {
        var b = new SBorder
        {
            Width = w, Height = h, Margin = new Thickness(0, 0, 14, 14), CornerRadius = new CornerRadius(20), Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromArgb(selected ? (byte)0x30 : (byte)0x14, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(selected ? Blue : Colors.Transparent), BorderThickness = new Thickness(2),
            Child = new TextBlock
            {
                Text = text, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Medium,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        b.MouseLeftButtonUp += (_, _) => { Sfx.Pop(); click(); };
        return b;
    }

    private static FrameworkElement Switch(bool on, Action<bool> set)
    {
        var brush = new SolidColorBrush(on ? Green : Off);
        var move = new TranslateTransform(on ? 20 : 0, 0);
        var knob = new Ellipse
        {
            Width = 24, Height = 24, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(3, 0, 0, 0), RenderTransform = move
        };
        var track = new SBorder { Width = 50, Height = 30, CornerRadius = new CornerRadius(15), Background = brush, Cursor = Cursors.Hand, Child = knob };
        track.MouseLeftButtonUp += (_, e) =>
        {
            on = !on;
            Sfx.Pop();
            var d = TimeSpan.FromMilliseconds(320);
            ColorTween.To(brush, SolidColorBrush.ColorProperty, new ColorAnimation(on ? Green : Off, d));
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(on ? 20 : 0, d) { EasingFunction = new SpringEase(6.5, 10) });
            set(on);
            e.Handled = true;
        };
        return track;
    }

    // dontShowAgain: true только по кнопке «Больше не показывать». «Готово» и Esc лишь закрывают окно, при следующем запуске оно откроется снова.
    private void Finish(bool dontShowAgain)
    {
        if (_closing) return;
        _closing = true;
        Sfx.Done();
        if (dontShowAgain) _s.HideWelcome = true;
        _s.Save();
        try { _changed(); } catch { }
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(500));
        a.Completed += (_, _) => Close();
        _root.BeginAnimation(OpacityProperty, a);
    }
}
