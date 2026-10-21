using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Island;

// Коллекционные карточки в плеере (шаг 45). ★ забирает обложку играющего трека в коллекцию:
// посреди экрана открывается карточка, как из пака: вспышка цвета редкости, карточка разворачивается из ребра,
// сверху «Новая карточка!» и редкость. Клик или Esc убирает её. ★ золотая, если трек уже в коллекции.
public sealed partial class PlayerWindow
{
    private Border? _starBtn;
    private Grid? _reveal;
    private DispatcherTimer? _revealTimer;
    private static readonly Color Gold = Color.FromRgb(0xFF, 0xC8, 0x3A);

    private void UpdateStar()
    {
        if (_starBtn?.Child is not VIcon v || _title == null) return;
        bool have = CardCollection.Find(_title, _artist ?? "") != null;
        v.Foreground = have ? new SolidColorBrush(Gold) : Brushes.White;
        _starBtn.ToolTip = Loc.T(have ? "Уже в коллекции: открыть карточку" : "В коллекцию");
    }

    private void CollectCurrent()
    {
        if (_title == null || _coverSrc == null) { ShowHint("star", Loc.T("У этого трека нет обложки")); return; }
        try
        {
            var (card, isNew) = CardCollection.Add(_coverSrc, _title, _artist ?? "");
            UpdateStar();
            Reveal(card, isNew);
            // достижения (Services\Achievements.cs): подсказка, когда карточка уже показалась
            if (isNew)
            {
                var d = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
                d.Tick += (_, _) => { d.Stop(); _host.CheckAchievements(); };
                d.Start();
            }
        }
        catch (Exception ex)
        {
            App.Log("Collection.Add", ex);
            ShowHint("triangle-alert", Loc.T("Не получилось сохранить карточку"));
        }
    }

    private void Reveal(CardEntry c, bool isNew)
    {
        CloseReveal();
        var acc = CollectibleCard.Accent(c.Rarity);
        var layer = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)), Opacity = 0 };
        Panel.SetZIndex(layer, 90);

        // вспышка цвета редкости за карточкой
        var burst = new Ellipse
        {
            Width = 760, Height = 760, IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5),
            Fill = new RadialGradientBrush(Color.FromArgb(0x90, acc.R, acc.G, acc.B), Color.FromArgb(0, acc.R, acc.G, acc.B))
        };
        var bs = new ScaleTransform(0.2, 0.2);
        burst.RenderTransform = bs;
        layer.Children.Add(burst);

        var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        col.Children.Add(new TextBlock
        {
            Text = isNew && c.Daily ? Loc.T("Карточка дня!", "Card of the day!") : Loc.T(isNew ? "Новая карточка!" : "Уже в коллекции"), Foreground = Brushes.White, FontSize = 26, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        col.Children.Add(new TextBlock
        {
            Text = Loc.T(CardCollection.RarityNames[c.Rarity]) + (c.Foil ? " · " + Loc.T("голографическая") : "") + "  ·  " + c.Score + "/10",
            Foreground = new SolidColorBrush(acc), FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 18)
        });
        var card = CollectibleCard.Showcase(c, CardCollection.Image(c), 360, null, null);
        var flip = new ScaleTransform(0.02, 0.85);
        var holder = new Border { Child = card, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = flip, HorizontalAlignment = HorizontalAlignment.Center };
        col.Children.Add(holder);
        var hint = new TextBlock
        {
            Text = Loc.T("Клик, чтобы убрать. Вся коллекция: кнопка справа от «вперёд»"), Foreground = new SolidColorBrush(Color.FromArgb(0x90, 255, 255, 255)),
            FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0)
        };
        col.Children.Add(hint);
        layer.Children.Add(col);
        layer.MouseLeftButtonUp += (_, e) => { if (!card.IsMouseOver) { e.Handled = true; CloseReveal(); } };
        _root.Children.Add(layer);
        _reveal = layer;

        layer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        // карточка разворачивается из ребра, как будто её достали из пака
        flip.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.02, 1, TimeSpan.FromMilliseconds(isNew ? 820 : 520))
        { BeginTime = TimeSpan.FromMilliseconds(120), EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 5 } });
        flip.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(620))
        { BeginTime = TimeSpan.FromMilliseconds(120), EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } });
        var grow = new DoubleAnimation(0.2, isNew ? 1.15 : 0.9, TimeSpan.FromMilliseconds(900)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        bs.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        bs.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        burst.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.45, TimeSpan.FromMilliseconds(1600)) { BeginTime = TimeSpan.FromMilliseconds(400) });
        if (isNew && c.Rarity >= 3) { try { Sfx.Done(); } catch { /* нет звука */ } }
        else { try { Sfx.Pop(); } catch { /* нет звука */ } }

        _revealTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(9) };
        _revealTimer.Tick += (_, _) => CloseReveal();
        _revealTimer.Start();
    }

    // true, если что-то было открыто
    private bool CloseReveal()
    {
        _revealTimer?.Stop();
        var layer = _reveal;
        if (layer == null) return false;
        _reveal = null;
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        a.Completed += (_, _) => _root.Children.Remove(layer);
        layer.BeginAnimation(OpacityProperty, a);
        return true;
    }
}
