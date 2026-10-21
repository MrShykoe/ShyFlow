using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace Island;

public sealed partial class SettingsWindow
{
    // ---------- помощники построения: карточки, строки, переключатели, сегменты, меню ----------
    private void Section(StackPanel page, string text) => page.Children.Add(new TextBlock
    {
        Text = Sent(Loc.T(text)), Foreground = Gray, FontSize = 12, FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(14, 18, 0, 7)
    });

    private StackPanel Card(StackPanel page, string? title = null)
    {
        if (title != null) Section(page, title);
        var inner = new StackPanel();
        var card = new SBorder { CornerRadius = new CornerRadius(18), Background = CardBg, Child = inner, BorderBrush = Br(0x10, 255, 255, 255), BorderThickness = new Thickness(1) };
        card.SizeChanged += (_, e) => card.Clip = Smooth.Shape(e.NewSize.Width, e.NewSize.Height, new CornerRadius(18));
        page.Children.Add(card);
        return inner;
    }

    private void Divider(StackPanel card)
    {
        if (card.Children.Count > 0)
            card.Children.Add(new Border { Height = 1, Background = Line, Margin = new Thickness(16, 0, 0, 0), Tag = "div" });
    }

    private TextBlock RowTitle(string text) => new TextBlock
    {
        Text = text, Foreground = Brushes.White, FontSize = 13.5, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap
    };
    private TextBlock RowSub(string text) => new TextBlock
    {
        Text = text, Foreground = Gray, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
    };

    private void Row(StackPanel card, string title, string? sub, bool on, Action<bool> set)
    {
        Divider(card);
        var g = new Grid { Margin = new Thickness(16, 10, 14, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        tp.Children.Add(RowTitle(title));
        if (sub != null) tp.Children.Add(RowSub(sub));
        g.Children.Add(tp);
        var (tg, flip) = MakeToggle(on, v => { set(v); _s.Save(); _changed(); });
        Grid.SetColumn(tg, 1);
        g.Children.Add(tg);
        var tint = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
        void Tint(byte a, int ms) => ColorTween.To(tint, SolidColorBrush.ColorProperty,
            new ColorAnimation(Color.FromArgb(a, 255, 255, 255), TimeSpan.FromMilliseconds(ms)));
        var rowBox = new Border { Background = tint, Child = g, Cursor = Cursors.Hand };
        rowBox.MouseEnter += (_, _) => Tint(0x0A, 140);
        rowBox.MouseLeave += (_, _) => Tint(0, 220);
        rowBox.MouseLeftButtonDown += (_, e) => { Tint(0x1C, 50); flip(); e.Handled = true; };
        rowBox.MouseLeftButtonUp += (_, _) => Tint(0x0A, 280);
        card.Children.Add(rowBox);
    }

    // сегменты: для коротких вариантов (до трёх), выбранный плавно переезжает
    private FrameworkElement Segmented(string[] names, Func<int> get, Action<int> set)
    {
        var outer = new SBorder
        {
            CornerRadius = new CornerRadius(8), Background = ChipBg, Padding = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center
        };
        var host2 = new Grid();
        var grid = new UniformGrid { Rows = 1 };
        var thumbMove = new TranslateTransform();
        var thumbEl = new SBorder
        {
            CornerRadius = new CornerRadius(6), Background = SegSel, HorizontalAlignment = HorizontalAlignment.Left,
            RenderTransform = thumbMove, IsHitTestVisible = false
        };
        host2.Children.Add(thumbEl);
        host2.Children.Add(grid);
        var segTx = new List<TextBlock>();
        void Place(bool anim)
        {
            double w = names.Length == 0 ? 0 : grid.ActualWidth / names.Length;
            thumbEl.Width = w;
            double x = get() * w;
            if (anim)
                thumbMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(320))
                { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 } });
            else { thumbMove.BeginAnimation(TranslateTransform.XProperty, null); thumbMove.X = x; }
            for (int i = 0; i < segTx.Count; i++)
                segTx[i].BeginAnimation(OpacityProperty, new DoubleAnimation(get() == i ? 1 : 0.62, TimeSpan.FromMilliseconds(anim ? 200 : 1)));
        }
        grid.SizeChanged += (_, _) => Place(false);
        for (int i = 0; i < names.Length; i++)
        {
            int idx = i;
            var tb = new TextBlock
            {
                Text = names[i], Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.Medium,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(12, 4, 12, 4)
            };
            segTx.Add(tb);
            var seg = new SBorder { CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = tb };
            seg.MouseLeftButtonDown += (_, e) => { set(idx); _s.Save(); _changed(); Place(true); e.Handled = true; };
            grid.Children.Add(seg);
        }
        outer.Child = host2;
        return outer;
    }

    // выпадающее меню: для длинных списков
    private FrameworkElement PopupBtn(string[] names, Func<int> get, Action<int> set)
    {
        string Name(int i) => Loc.T(names[Math.Clamp(i, 0, names.Length - 1)]);
        var label = new TextBlock
        {
            Text = Name(get()), Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 9, 0)
        };
        var arrows = new Grid();
        arrows.Children.Add(new VIcon
        {
            Text = "chevrons-up-down", FontSize = 11, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        });
        var arrowBox = new SBorder
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(6), Background = _selBrush,
            Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center, Child = arrows
        };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(label);
        line.Children.Add(arrowBox);
        var btn = new SBorder
        {
            CornerRadius = new CornerRadius(8), Background = ChipBg, MinWidth = 96, Height = 26,
            Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Child = line
        };
        btn.MouseEnter += (_, _) => btn.Background = BtnBg;
        btn.MouseLeave += (_, _) => btn.Background = ChipBg;
        btn.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            var list = new StackPanel();
            var pop = new Popup
            {
                PlacementTarget = btn, Placement = PlacementMode.Bottom, StaysOpen = false,
                AllowsTransparency = true, PopupAnimation = PopupAnimation.None,
                HorizontalOffset = -12, VerticalOffset = -6
            };
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                bool sel = get() == i;
                var check = new VIcon
                {
                    Text = sel ? "check" : "", FontSize = 10, Foreground = Brushes.White,
                    Width = 18, VerticalAlignment = VerticalAlignment.Center
                };
                var itemText = new TextBlock { Text = Name(i), Foreground = Brushes.White, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
                var itemLine = new StackPanel { Orientation = Orientation.Horizontal };
                itemLine.Children.Add(check);
                itemLine.Children.Add(itemText);
                var item = new SBorder
                {
                    CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 5, 14, 5), Background = Brushes.Transparent,
                    Cursor = Cursors.Hand, Child = itemLine
                };
                item.MouseEnter += (_, _) => item.Background = _selBrush;
                item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
                item.MouseLeftButtonUp += (_, ev) =>
                {
                    ev.Handled = true;
                    set(idx); _s.Save(); _changed();
                    label.Text = Name(idx);
                    pop.IsOpen = false;
                };
                list.Children.Add(item);
            }
            var menu = new SBorder
            {
                Margin = new Thickness(12), Padding = new Thickness(5), CornerRadius = new CornerRadius(12),
                MinWidth = Math.Max(btn.ActualWidth, 120),
                Background = new SolidColorBrush(Color.FromRgb(46, 46, 49)),
                BorderBrush = Br(0x30, 255, 255, 255), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 18, Opacity = 0.55, ShadowDepth = 4, Direction = 270 },
                Child = list
            };
            var menuScale = new ScaleTransform(0.88, 0.88);
            menu.RenderTransform = menuScale;
            menu.RenderTransformOrigin = new Point(0.5, 0);
            menu.Opacity = 0;
            pop.Child = menu;
            pop.IsOpen = true;
            var grow = new DoubleAnimation(0.88, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 } };
            menuScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            menuScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            menu.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        };
        return btn;
    }

    private void Choice(StackPanel card, string title, string[] names, Func<int> get, Action<int> set)
    {
        Divider(card);
        var g = new Grid { Margin = new Thickness(16, 9, 12, 9) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = RowTitle(title);
        t.VerticalAlignment = VerticalAlignment.Center;
        t.Margin = new Thickness(0, 0, 14, 0);
        g.Children.Add(t);
        bool seg = names.Length <= 3 && names.Sum(n => n.Length) <= 24;
        var ctl = seg ? Segmented(names, get, set) : PopupBtn(names, get, set);
        Grid.SetColumn(ctl, 1);
        g.Children.Add(ctl);
        card.Children.Add(g);
    }

    // ползунок 0..1 (шаг 63, «Желеобразность»): сохраняется сразу, острову изменения отдаются чуть погодя, чтобы не дёргать его на каждом пикселе
    private void SliderRow(StackPanel card, string title, string? sub, Func<double> get, Action<double> set)
    {
        Divider(card);
        var g = new Grid { Margin = new Thickness(16, 10, 14, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        var tp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        tp.Children.Add(RowTitle(title));
        if (sub != null) tp.Children.Add(RowSub(sub));
        g.Children.Add(tp);
        var sl = new MiniSlider { Value = get(), VerticalAlignment = VerticalAlignment.Center, Accent = _fillBrush };
        var apply = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        apply.Tick += (_, _) => { apply.Stop(); _s.Save(); _changed(); };
        sl.Changed += v => { set(v); apply.Stop(); apply.Start(); };
        Grid.SetColumn(sl, 1);
        g.Children.Add(sl);
        card.Children.Add(g);
    }

    private Border PushButton(string text, Action act)
    {
        var btn = new SBorder
        {
            CornerRadius = new CornerRadius(8), Background = BtnBg, Padding = new Thickness(13, 5, 13, 5),
            Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.Medium }
        };
        var sc = new ScaleTransform(1, 1);
        btn.RenderTransform = sc;
        btn.RenderTransformOrigin = new Point(0.5, 0.5);
        void Press(double v, int ms)
        {
            var a = new DoubleAnimation(v, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        btn.MouseEnter += (_, _) => btn.Background = BtnHover;
        btn.MouseLeave += (_, _) => { btn.Background = BtnBg; Press(1, 220); };
        btn.MouseLeftButtonDown += (_, e) => { Press(0.93, 90); e.Handled = true; };
        btn.MouseLeftButtonUp += (_, e) => { Press(1, 320); act(); e.Handled = true; };
        return btn;
    }

    private void ActionRow(StackPanel card, string title, string? sub, string button, Action act)
    {
        Divider(card);
        var g = new Grid { Margin = new Thickness(16, 10, 14, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        tp.Children.Add(RowTitle(title));
        if (sub != null) tp.Children.Add(RowSub(sub));
        g.Children.Add(tp);
        var btn = PushButton(button, act);
        Grid.SetColumn(btn, 1);
        g.Children.Add(btn);
        card.Children.Add(g);
    }

    private void HotKeyRow(StackPanel card, string key, string text)
    {
        Divider(card);
        var g = new Grid { Margin = new Thickness(16, 9, 14, 9) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new SBorder
        {
            CornerRadius = new CornerRadius(6), Background = BtnBg, Padding = new Thickness(9, 2, 9, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = key, Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.Medium }
        });
        var d = new TextBlock { Text = text, Foreground = Gray, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(d, 1);
        g.Children.Add(d);
        card.Children.Add(g);
    }

    // переключатель; сам по клику не реагирует, щёлкает вся строка (flip).
    // Шаг 72: включено — белая дорожка (или цвет обложки) и тёмный кружок, выключено — полупрозрачная дорожка, как на острове
    private (FrameworkElement el, Action flip) MakeToggle(bool on, Action<bool> set)
    {
        var trackBrush = new SolidColorBrush(on ? ToggleCol(_accent) : OffColor);
        var thumbBrush = new SolidColorBrush(on ? ThumbCol(ToggleCol(_accent)) : Colors.White);
        _toggles.Add((trackBrush, () => on));
        _thumbs.Add((thumbBrush, () => on));
        var move = new TranslateTransform(on ? 16 : 0, 0);
        var squash = new ScaleTransform(1, 1);
        var thumbTf = new TransformGroup();
        thumbTf.Children.Add(squash);
        thumbTf.Children.Add(move);
        var thumb = new SBorder
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = thumbBrush,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0), RenderTransform = thumbTf, RenderTransformOrigin = new Point(0.5, 0.5),
            Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Direction = 270, Opacity = 0.35 }
        };
        var track = new SBorder
        {
            Width = 40, Height = 24, CornerRadius = new CornerRadius(12), Background = trackBrush,
            Child = thumb, VerticalAlignment = VerticalAlignment.Center
        };
        void Flip()
        {
            on = !on;
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(on ? 16 : 0, TimeSpan.FromMilliseconds(300))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 } });
            ColorTween.To(trackBrush, SolidColorBrush.ColorProperty, new ColorAnimation(on ? ToggleCol(_accent) : OffColor, TimeSpan.FromMilliseconds(200)));
            ColorTween.To(thumbBrush, SolidColorBrush.ColorProperty, new ColorAnimation(on ? ThumbCol(ToggleCol(_accent)) : Colors.White, TimeSpan.FromMilliseconds(200)));
            var stretch = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(340) };
            stretch.KeyFrames.Add(new LinearDoubleKeyFrame(1.22, KeyTime.FromPercent(0.35)));
            stretch.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0), new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 }));
            squash.BeginAnimation(ScaleTransform.ScaleXProperty, stretch);
            set(on);
        }
        return (track, Flip);
    }
}
