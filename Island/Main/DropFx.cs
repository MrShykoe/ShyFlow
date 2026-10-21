using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Island;

// Перетаскивание на остров (файлы, папки, ссылки, текст).
//
// Пока тащат над островом: он раскрывается и сразу показывает, что именно тащат (значок по типу, имя файла,
// «Файлов: 3», сайт ссылки или начало текста). Вокруг бежит пунктир по тем же мягким углам, значок дышит,
// а сам остров слегка тянется за курсором, как магнит. Отпустили: значок «проглатывается» внутрь, остров вздрагивает,
// открывается карман, и новая карточка всплывает со вспышкой цвета обложки.
public partial class MainWindow
{
    private sealed record DropInfo(int Tab, string Icon, string Title, string Sub);

    // текст ссылки: одна строка без пробелов, http или https
    private static string? UrlOf(string? t)
    {
        t = t?.Trim();
        if (string.IsNullOrEmpty(t) || t.Length > 2000 || t.Any(char.IsWhiteSpace)) return null;
        return Uri.TryCreate(t, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) ? u.ToString() : null;
    }

    // текст или ссылка из перетаскиваемых данных (браузеры кладут ссылку и как текст, и как UniformResourceLocatorW)
    private static string? DragText(IDataObject d)
    {
        try
        {
            if (d.GetDataPresent(DataFormats.UnicodeText) && d.GetData(DataFormats.UnicodeText) is string s && s.Trim().Length > 0) return s;
            if (d.GetDataPresent("UniformResourceLocatorW") && d.GetData("UniformResourceLocatorW") is MemoryStream ms)
                return Encoding.Unicode.GetString(ms.ToArray()).TrimEnd('\0');
            if (d.GetDataPresent(DataFormats.Text) && d.GetData(DataFormats.Text) is string a) return a;
        }
        catch (Exception ex) { App.LogOnce("Drop.Text", ex); }
        return null;
    }

    private static string[]? DragFiles(IDataObject d)
    {
        try { return d.GetDataPresent(DataFormats.FileDrop) && d.GetData(DataFormats.FileDrop) is string[] p && p.Length > 0 ? p : null; }
        catch { return null; }
    }

    private DropInfo Describe(IDataObject d)
    {
        var files = DragFiles(d);
        if (files != null)
        {
            string Name(string p) => System.IO.Path.GetFileName(p.TrimEnd('\\', '/'));
            if (files.Length == 1)
                return new DropInfo(0, FileLook(files[0]).icon, Loc.T(Directory.Exists(files[0]) ? "Папка в карман" : "Файл в карман"), Name(files[0]));
            return new DropInfo(0, "file", Loc.T("Файлов: ") + files.Length, string.Join(", ", files.Take(3).Select(Name)));
        }
        var text = DragText(d);
        var url = UrlOf(text);
        if (url != null) return new DropInfo(1, "link", Loc.T("Ссылка в карман"), new Uri(url).Host);
        if (!string.IsNullOrWhiteSpace(text))
        {
            string one = text.Trim().Replace("\r", " ").Replace("\n", " ");
            return new DropInfo(1, "file-text", Loc.T("Текст в карман"), one.Length > 70 ? one[..70] + "…" : one);
        }
        return new DropInfo(0, "download", Loc.T("Кинь сюда"), Loc.T("файл, ссылку или текст"));
    }

    // ---------- события перетаскивания ----------
    private void Pill_DragEnter(object s, DragEventArgs e)
    {
        _leaveTimer.Stop();
        _autoClose.Stop();
        _toastTimer.Stop();
        _actionsOpen = false; _devicesOpen = false; _timerOpen = false; _mixerOpen = false; _centerOpen = false; _trOpen = false;
        var info = Describe(e.Data);
        _tab = info.Tab;
        if (_pocketOpen)
        {
            RenderPocket();
            ApplyMode(Mode.Pocket);
        }
        else
        {
            // карман не раскрывается сразу: остров лишь наполовину открывается и показывает, что в него кладут
            DropIcon.Text = info.Icon;
            DropText.Text = info.Title;
            DropSub.Text = info.Sub;
            if (_mode != Mode.Drop) ApplyMode(Mode.Drop);
        }
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Pill_DragOver(object s, DragEventArgs e)
    {
        _leaveTimer.Stop();
        if (_mode == Mode.Drop) Magnet(e.GetPosition(Pill));
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    // DragLeave приходит и при переходе между кнопками внутри острова, поэтому закрываем с небольшой задержкой
    private void Pill_DragLeave(object s, DragEventArgs e)
    {
        _leaveTimer.Stop();
        _leaveTimer.Start();
    }

    private void Pill_Drop(object s, DragEventArgs e)
    {
        _leaveTimer.Stop();
        e.Handled = true;
        string? added = null;
        var files = DragFiles(e.Data);
        if (files != null)
        {
            foreach (var p in files.Reverse())
            {
                _files.Remove(p);
                _files.Insert(0, p);
            }
            SaveFiles();
            _tab = 0;
            added = files[0];
            // шаг 66: один файл — сразу QR для телефона (PhoneShare.cs)
            if (_cfg.DropQr && files.Length == 1 && File.Exists(files[0])) { var f = files[0]; Dispatcher.BeginInvoke(() => ShowQr(f)); }
        }
        else
        {
            var t = DragText(e.Data)?.Trim();
            if (!string.IsNullOrEmpty(t)) { AddClip(t); _tab = 1; added = t; }
        }
        if (added == null) { ApplyMode(Rest()); return; }   // ничего подходящего (например, картинка без файла)

        if (!_pocketOpen) { _pocketOpen = true; _autoOpened = true; } // теперь раскрываем карман: файл добавлен
        void Open()
        {
            RenderPocket();
            ApplyMode(Mode.Pocket);
            HighlightCard(added);
            if (_autoOpened) _autoClose.Start();
        }
        if (_mode == Mode.Drop)
        {
            Swallow();
            var d = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(190) };
            d.Tick += (_, _) => { d.Stop(); Open(); };
            d.Start();
        }
        else Open();
    }

    // ---------- живость, пока тащат ----------
    private bool _dropFx;

    // вызывается из ApplyMode через DropBounce: режим Drop включился или выключился
    private void DropFx(bool on)
    {
        if (on == _dropFx) return;
        _dropFx = on;
        var badge = Get(DropBadge);
        if (on)
        {
            // после «проглатывания» значок мог остаться сжатым: возвращаем
            badge.S.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            badge.S.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            badge.T.BeginAnimation(TranslateTransform.YProperty, null);
            badge.T.Y = 0;
            DropBadge.BeginAnimation(OpacityProperty, null);
            DropBadge.Opacity = 1;
            DropText.Opacity = DropSub.Opacity = 1;

            var c = UiColor;
            DropBadge.Background = new SolidColorBrush(Color.FromArgb(0x44, c.R, c.G, c.B));
            DropDash.Stroke = new SolidColorBrush(Color.FromArgb(0xA0, c.R, c.G, c.B));
            // пунктир бежит по кругу
            DropDash.BeginAnimation(Shape.StrokeDashOffsetProperty,
                new DoubleAnimation(0, -13, TimeSpan.FromMilliseconds(900)) { RepeatBehavior = RepeatBehavior.Forever });
            // значок дышит
            var pulse = new DoubleAnimation(1, 1.08, TimeSpan.FromMilliseconds(650))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            badge.S.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            badge.S.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        }
        else
        {
            DropDash.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            badge.S.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            badge.S.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            badge.S.ScaleX = badge.S.ScaleY = 1;
            // магнит отпускает остров на место
            Get(Pill).T.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(520)) { EasingFunction = Spr() });
        }
    }

    // рамка пунктиром по мягкому контуру, на 1 px внутрь от края подсказки
    private void InitDropFx()
    {
        DropHint.SizeChanged += (_, e) =>
            DropDash.Data = Smooth.Shape(Math.Max(1, e.NewSize.Width - 2), Math.Max(1, e.NewSize.Height - 2), new CornerRadius(22), 0, 0, 1, 1);
    }

    // остров тянется к курсору: до 9 px вбок, плавно (в режиме notch он привязан к краю экрана и не двигается)
    private void Magnet(Point p)
    {
        if (NotchK > 0.5 || !LiquidOn) return;
        double half = Math.Max(1, Pill.ActualWidth / 2);
        double want = Math.Clamp((p.X - half) / half, -1, 1) * 9;
        var t = Get(Pill).T;
        t.BeginAnimation(TranslateTransform.XProperty, null);
        t.X += (want - t.X) * 0.35;
    }

    // отпустили: значок уходит внутрь, текст гаснет, остров вздрагивает, как будто проглотил
    private void Swallow()
    {
        var b = Get(DropBadge);
        var d = TimeSpan.FromMilliseconds(180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        b.S.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.25, d) { EasingFunction = ease });
        b.S.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.25, d) { EasingFunction = ease });
        b.T.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, d) { EasingFunction = ease });
        DropBadge.BeginAnimation(OpacityProperty, new DoubleAnimation(0, d));
        DropText.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
        DropSub.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
        Kick(-1.6, 2.4);
        try { Sfx.Pop(); } catch { /* нет звука: ничего страшного */ }
    }

    // новая карточка в кармане: всплывает и вспыхивает цветом обложки
    private void HighlightCard(string item)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            var card = PocketList.Children.OfType<FrameworkElement>().FirstOrDefault(c => Equals(c.Tag, item));
            if (card == null) return;
            card.BringIntoView();
            Pop(card, 0.55, 640);
            var c = UiColor;
            var glow = new DropShadowEffect { Color = c, BlurRadius = 22, ShadowDepth = 0, Opacity = 0.95 };
            card.Effect = glow;
            var fade = new DoubleAnimation(0.95, 0, TimeSpan.FromMilliseconds(1100)) { BeginTime = TimeSpan.FromMilliseconds(250) };
            fade.Completed += (_, _) => { if (ReferenceEquals(card.Effect, glow)) card.Effect = null; };
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, fade);
        }));
    }
}
