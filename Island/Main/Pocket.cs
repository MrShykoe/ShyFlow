using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Island;

// Карман: файлы, буфер обмена и заметки. Перетаскивание файлов и текста на остров.
// Вид (шаг 36): вкладки стали переключателем с переезжающей подсветкой, вместо плашек карточки.
// У файла превью картинки или цветной значок по типу, имя в две строки и размер; у текста несколько строк.
// Крестик удаления появляется при наведении (ПКМ тоже удаляет), «очистить» срабатывает со второго нажатия.
public partial class MainWindow
{
    private const double PocketH = 206;               // высота острова с открытым карманом
    private const double TabW = 94;                   // ширина одной вкладки
    private const double FileCardW = 92, TextCardW = 168, CardH = 136, NoteCardH = 92;

    private static readonly Color CardC = Color.FromArgb(0x1A, 255, 255, 255);
    private static readonly Color CardHoverC = Color.FromArgb(0x2C, 255, 255, 255);
    private static readonly Color DangerC = Color.FromRgb(0xFF, 0x45, 0x3A);

    private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".ico" };

    // превью картинок: читаем один раз в фоне и помним, чтобы перерисовка кармана их не перечитывала
    private readonly Dictionary<string, BitmapSource?> _thumbs = new(StringComparer.OrdinalIgnoreCase);

    private bool _clearArmed;
    private readonly DispatcherTimer _clearTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private bool _tabsBuilt;
    private readonly TextBlock[] _tabCount = new TextBlock[3];
    private readonly FrameworkElement[] _tabFace = new FrameworkElement[3];

    // ============================================================
    //  КАРМАН: хранение
    // ============================================================
    private void LoadFiles()
    {
        try
        {
            if (File.Exists(PocketFile))
            {
                var l = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(PocketFile));
                // папки тоже можно положить в карман, поэтому проверяем и их
                if (l != null) _files.AddRange(l.Where(p => File.Exists(p) || Directory.Exists(p)));
            }
        }
        catch (Exception ex) { App.Log("Pocket.Load", ex); }
        LoadPins();
        LoadClipImages();
    }

    // ---------- закреплённое (pins.json, отдельно от настроек: «Сбросить настройки» его не стирает) ----------
    private sealed class PinStore
    {
        public List<string> Files { get; set; } = new();
        public List<string> Clips { get; set; } = new();   // текст буфера: закреплённый хранится и между запусками
        public List<string> Notes { get; set; } = new();
    }
    private PinStore _pins = new();
    private static string PinsFile => Path.Combine(DataDir, "pins.json");

    private List<string> PinList(int tab) => tab == 0 ? _pins.Files : tab == 1 ? _pins.Clips : _pins.Notes;
    private bool IsPinned(int tab, string item) => PinList(tab).Contains(item);

    private void TogglePin(int tab, string item)
    {
        var l = PinList(tab);
        if (!l.Remove(item)) l.Insert(0, item);
        try { SafeFile.WriteAllText(PinsFile, JsonSerializer.Serialize(_pins)); }
        catch (Exception ex) { App.Log("Pins.Save", ex); }
    }

    private void LoadPins()
    {
        try
        {
            if (File.Exists(PinsFile)) _pins = JsonSerializer.Deserialize<PinStore>(File.ReadAllText(PinsFile)) ?? new PinStore();
        }
        catch (Exception ex) { App.Log("Pins.Load", ex); }
        foreach (var c in _pins.Clips.AsEnumerable().Reverse()) if (!_clips.Contains(c)) _clips.Insert(0, c);
    }

    // что показать на вкладке: закреплённое первым, и только то, что подходит под поиск
    private List<string> Shown(int tab, List<string> all, Func<string, string> text)
    {
        var q = _pocketQuery.Trim();
        var list = all.Where(x => q.Length == 0 || text(x).Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        return list.Where(x => IsPinned(tab, x)).Concat(list.Where(x => !IsPinned(tab, x))).ToList();
    }

    // ---------- поиск по карману (значок лупы): ищет на текущей вкладке, Tab переключает вкладку, Esc закрывает ----------
    private string _pocketQuery = "";

    private void PocketSearch_Click(object s, RoutedEventArgs e)
    {
        if (PocketSearchWrap.Visibility == Visibility.Visible) { CloseSearch(); return; }
        TabTrack.Visibility = Visibility.Collapsed;
        PocketSearchWrap.Visibility = Visibility.Visible;
        Pop(PocketSearchWrap, 0.85, 420);
        Activate();   // остров не забирает фокус сам по себе, а печатать нужно
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => PocketSearch.Focus()));
    }

    private void CloseSearch()
    {
        _pocketQuery = "";
        PocketSearch.Text = "";
        PocketSearchWrap.Visibility = Visibility.Collapsed;
        TabTrack.Visibility = Visibility.Visible;
        RenderPocket();
    }

    private void PocketSearch_TextChanged(object s, TextChangedEventArgs e)
    {
        PocketSearchHint.Visibility = PocketSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _pocketQuery = PocketSearch.Text;
        RenderPocket();
    }

    private void PocketSearch_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CloseSearch(); e.Handled = true; }
        // Tab: следующая вкладка, поиск остаётся
        else if (e.Key == Key.Tab) { _tab = (_tab + 1) % 3; RenderPocket(); e.Handled = true; }
    }

    private void SaveFiles()
    {
        try { SafeFile.WriteAllText(PocketFile, JsonSerializer.Serialize(_files)); }
        catch (Exception ex) { App.Log("Pocket.Save", ex); }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(OnClipboard));
        else if (msg == WM_HOTKEY)
        {
            int hk = wParam.ToInt32();
            Dispatcher.BeginInvoke(new Action(() => OnHotkey(hk)));
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OnClipboard()
    {
        try
        {
            if (DateTime.UtcNow < _ignoreClipUntil) return;
            // скриншот (Win+Shift+S, PrintScreen) или скопированная картинка
            if (Clipboard.ContainsImage())
            {
                // шаг 54: картинка тоже попадает в историю буфера (карман, вкладка «Буфер»)
                bool keep = _cfg.ClipboardImages && _cfg.ClipboardHistory && !Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing");
                var shot = keep ? SaveClipImage() : null;
                // шаг 64: в значке плашки миниатюра самой картинки
                if (_cfg.Captures && _ready) ShowToast("\uE722", keep ? "Картинка в буфере: она и в кармане" : "Изображение в буфере обмена", null, "cam", Thumb(shot ?? SafeClipImage()));
                return;
            }
            if (!_cfg.ClipboardHistory) return;
            // менеджеры паролей помечают такие копирования, их не сохраняем
            if (Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing")) return;
            if (!Clipboard.ContainsText()) return;
            var t = Clipboard.GetText().Trim();
            if (t.Length == 0) return;
            AddClip(t);
            if (_ready && _cfg.CopyToast && _mode != Mode.Pocket) ShowClipToast(t);   // цвет, ссылка, код (SmartClip.cs)
        }
        catch { /* буфер может быть занят другой программой: это нормально, следующее копирование прочитаем */ }
    }

    private void AddClip(string t)
    {
        _clips.Remove(t);
        _clips.Insert(0, t);
        // хранится 15 последних; закреплённые не считаются и не вытесняются
        while (_clips.Count(c => !IsPinned(1, c)) > 15)
        {
            int last = _clips.FindLastIndex(c => !IsPinned(1, c));
            if (last < 0) break;
            DropClipFile(_clips[last]);
            _clips.RemoveAt(last);
        }
        RenderPocket();
    }

    private void CopyBack(string text)
    {
        try
        {
            _ignoreClipUntil = DateTime.UtcNow.AddMilliseconds(500);
            Clipboard.SetText(text);
        }
        catch (Exception ex) { App.Log("Pocket.Copy", ex); }
    }

    // ============================================================
    //  КАРМАН: вид
    // ============================================================
    private static SolidColorBrush Solid(Color c) => new(c);

    // значок и цвет карточки по типу файла
    private static (string icon, Color col) FileLook(string path)
    {
        if (Directory.Exists(path)) return ("folder", Color.FromRgb(0xFF, 0xC8, 0x4A));
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".png": case ".jpg": case ".jpeg": case ".gif": case ".webp": case ".bmp": case ".tif": case ".tiff": case ".ico":
                return ("file-image", Color.FromRgb(0x34, 0xC7, 0xA0));
            case ".mp3": case ".wav": case ".flac": case ".ogg": case ".m4a": case ".aac": case ".opus":
                return ("file-music", Color.FromRgb(0xFF, 0x5F, 0x8A));
            case ".mp4": case ".mkv": case ".avi": case ".mov": case ".webm": case ".wmv":
                return ("file-video", Color.FromRgb(0x9B, 0x7B, 0xFF));
            case ".zip": case ".rar": case ".7z": case ".tar": case ".gz":
                return ("file-down", Color.FromRgb(0xFF, 0x9F, 0x0A));
            case ".pdf": case ".doc": case ".docx": case ".txt": case ".md": case ".rtf": case ".odt":
            case ".xls": case ".xlsx": case ".csv": case ".ppt": case ".pptx":
                return ("file-text", Color.FromRgb(0x4F, 0xA3, 0xFF));
            default:
                return ("file", Color.FromRgb(0xA0, 0xA0, 0xA8));
        }
    }

    // общий каркас карточки: подсветка при наведении, лёгкое нажатие, крестик и булавка в углу
    private SBorder PocketCard(UIElement content, double w, double h, string tip, Action remove, int tab, string item)
    {
        bool pinned = IsPinned(tab, item);
        var removeAll = remove;
        remove = () => { if (IsPinned(tab, item)) TogglePin(tab, item); removeAll(); };
        var bg = Solid(CardC);
        var card = new SBorder
        {
            Tag = item,   // по нему находим только что брошенную карточку, чтобы подсветить (DropFx.cs)
            Width = w, Height = h, CornerRadius = new CornerRadius(16), Background = bg, Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand, ToolTip = tip, VerticalAlignment = VerticalAlignment.Top
        };
        HoldOnPress(card);   // шаг 70: карточка сжимается при нажатии, клик у края больше не теряется (Tap.cs)
        var x = new SBorder
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Solid(Color.FromArgb(0xE6, 0x2C, 0x2C, 0x30)),
            BorderBrush = Solid(Color.FromArgb(0x30, 255, 255, 255)), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 5, 0),
            Opacity = 0, Cursor = Cursors.Hand, ToolTip = Loc.T("Убрать"),
            Child = new VIcon { Text = "x", FontSize = 9, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        // нажатие на крестик ловим на самой карточке и первым: тогда остальные обработчики
        // (открыть файл, начать перетаскивание, скопировать текст) уже не срабатывают
        // булавка: закреплённое стоит первым, не вытесняется новым и не стирается «очистить».
        // У закреплённой карточки булавка видна всегда (белый кружок), у остальных появляется при наведении
        var pin = new SBorder
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10),
            Background = pinned ? Brushes.White : Solid(Color.FromArgb(0xE6, 0x2C, 0x2C, 0x30)),
            BorderBrush = Solid(Color.FromArgb(0x30, 255, 255, 255)), BorderThickness = new Thickness(pinned ? 0 : 1),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 29, 0),
            Opacity = pinned ? 1 : 0, Cursor = Cursors.Hand, ToolTip = Loc.T(pinned ? "Открепить" : "Закрепить"),
            Child = new VIcon
            {
                Text = "pin", FontSize = 9.5, Foreground = pinned ? Solid(Color.FromRgb(0x1C, 0x1C, 0x1E)) : Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        // нажатие на крестик или булавку ловим на самой карточке и первым: тогда остальные обработчики
        // (открыть файл, начать перетаскивание, скопировать текст) уже не срабатывают
        static bool On(FrameworkElement b, object src) => src is DependencyObject d && (ReferenceEquals(d, b) || b.IsAncestorOf(d));
        card.PreviewMouseLeftButtonDown += (_, e) => { if (On(x, e.OriginalSource) || On(pin, e.OriginalSource)) e.Handled = true; };
        card.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (On(x, e.OriginalSource)) { e.Handled = true; RemoveCard(card, remove); }
            else if (On(pin, e.OriginalSource)) { e.Handled = true; TogglePin(tab, item); RenderPocket(); }
        };
        var grid = new Grid();
        grid.Children.Add(content);
        grid.Children.Add(pin);
        grid.Children.Add(x);
        card.Child = grid;

        void Fade(Color c, double xo, int ms)
        {
            ColorTween.To(bg, SolidColorBrush.ColorProperty, new ColorAnimation(c, TimeSpan.FromMilliseconds(ms)));
            x.BeginAnimation(OpacityProperty, new DoubleAnimation(xo, TimeSpan.FromMilliseconds(ms)));
            if (!pinned) pin.BeginAnimation(OpacityProperty, new DoubleAnimation(xo, TimeSpan.FromMilliseconds(ms)));
        }
        void Press(double to, int ms, IEasingFunction ease)
        {
            var t = Get(card);
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
            t.S.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            t.S.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        card.MouseEnter += (_, _) => Fade(CardHoverC, 1, 140);
        card.MouseLeave += (_, _) => { Fade(CardC, 0, 220); Press(1, 300, Spr()); };
        card.PreviewMouseLeftButtonDown += (_, _) => Press(0.95, 110, new CubicEase { EasingMode = EasingMode.EaseOut });
        card.PreviewMouseLeftButtonUp += (_, _) => Press(1, 420, Spr());
        card.MouseRightButtonUp += (_, e) => { RemoveCard(card, remove); e.Handled = true; };
        return card;
    }

    // карточка уходит: сжимается и тает, затем список перерисовывается без неё
    private void RemoveCard(FrameworkElement card, Action remove)
    {
        if (!card.IsHitTestVisible) return;
        card.IsHitTestVisible = false;
        var t = Get(card);
        var d = TimeSpan.FromMilliseconds(170);
        var shrink = new DoubleAnimation(0.82, d) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        t.S.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        t.S.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        var fade = new DoubleAnimation(0, d);
        fade.Completed += (_, _) => { remove(); RenderPocket(); };
        card.BeginAnimation(OpacityProperty, fade);
    }

    private SBorder FileCard(string path)
    {
        var (icon, col) = FileLook(path);
        bool isDir = Directory.Exists(path);
        var preview = new SBorder
        {
            Height = 74, Margin = new Thickness(6, 6, 6, 0), CornerRadius = new CornerRadius(11),
            Background = Solid(Color.FromArgb(0x30, col.R, col.G, col.B)), ClipToBounds = true
        };
        var pv = new Grid();
        pv.Children.Add(new VIcon
        {
            Text = icon, FontSize = 26, Foreground = Solid(col),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        });
        if (ImageExt.Contains(Path.GetExtension(path)))
        {
            var img = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            pv.Children.Add(img);
            SetThumb(path, img);
            preview.SizeChanged += (_, e) => preview.Clip = Smooth.Shape(e.NewSize.Width, e.NewSize.Height, new CornerRadius(11));
        }
        string ext = isDir ? "" : Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        if (ext.Length is > 0 and <= 5)
            pv.Children.Add(new SBorder
            {
                CornerRadius = new CornerRadius(5), Background = Solid(Color.FromArgb(0x8C, 0, 0, 0)), Padding = new Thickness(5, 1, 5, 1),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(5),
                Child = new TextBlock { Text = ext, Foreground = Brushes.White, FontSize = 9, FontWeight = FontWeights.SemiBold }
            });
        preview.Child = pv;

        string name = Path.GetFileName(path.TrimEnd('\\', '/'));
        if (name.Length == 0) name = path;
        string sub = isDir ? Loc.T("Папка") : "";
        if (!isDir) try { sub = FmtSize(new FileInfo(path).Length); } catch { sub = Loc.T("Нет файла"); }

        var body = new StackPanel();
        body.Children.Add(preview);
        body.Children.Add(new TextBlock
        {
            Text = name, Foreground = Brushes.White, FontSize = 11.5, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 31, LineHeight = 15, Margin = new Thickness(8, 5, 8, 0)
        });
        body.Children.Add(new TextBlock
        {
            Text = sub, Foreground = Solid(Color.FromArgb(0x80, 255, 255, 255)), FontSize = 10.5, Margin = new Thickness(8, 1, 8, 0)
        });

        // шаг 66: кнопка «на телефон» (QR) в углу превью, видна при наведении; у папок её нет
        SBorder? phone = null;
        if (!isDir)
        {
            phone = new SBorder
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Solid(Color.FromArgb(0xE6, 0x2C, 0x2C, 0x30)),
                BorderBrush = Solid(Color.FromArgb(0x30, 255, 255, 255)), BorderThickness = new Thickness(1), Opacity = 0, Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(5),
                ToolTip = Loc.T("На телефон: QR"),
                Child = new VIcon { Text = "smartphone", FontSize = 10, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            pv.Children.Add(phone);
        }

        var card = PocketCard(body, FileCardW, CardH, path + "\n" + Loc.T("Клик: открыть, перетащи: забрать"),
            () => { _files.Remove(path); SaveFiles(); }, 0, path);
        if (phone != null)
        {
            var ph = phone;
            card.MouseEnter += (_, _) => ph.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
            card.MouseLeave += (_, _) => ph.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)));
            // ловим раньше открытия файла и перетаскивания (они подписаны ниже)
            bool OnPhone(object src) => src is DependencyObject d && (ReferenceEquals(d, ph) || ph.IsAncestorOf(d));
            card.PreviewMouseLeftButtonDown += (_, e) => { if (OnPhone(e.OriginalSource)) e.Handled = true; };
            card.PreviewMouseLeftButtonUp += (_, e) => { if (OnPhone(e.OriginalSource)) { e.Handled = true; ShowQr(path); } };
        }

        // перетаскивание наружу и открытие по клику (как раньше)
        Point? start = null;
        card.PreviewMouseLeftButtonDown += (_, e) => start = e.GetPosition(null);
        card.PreviewMouseMove += (_, e) =>
        {
            if (start == null || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(null);
            if (Math.Abs(p.X - start.Value.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(p.Y - start.Value.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                start = null;
                try { DragDrop.DoDragDrop(card, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy); }
                catch (Exception ex) { App.Log("Pocket.Drag", ex); }
            }
        };
        card.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (start == null) return;
            start = null;
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { App.Log("Pocket.Open", ex); }
        };
        return card;
    }

    // превью читается в фоне (большие фото не тормозят остров) и плавно проявляется
    private void SetThumb(string path, Image img)
    {
        void Show(BitmapSource? b)
        {
            if (b == null) return;
            img.Source = b;
            img.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
        }
        if (_thumbs.TryGetValue(path, out var cached)) { if (cached != null) { img.Source = cached; img.Opacity = 1; } return; }
        _thumbs[path] = null;
        Task.Run(() =>
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(path);
                bi.DecodePixelWidth = 180;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.EndInit();
                bi.Freeze();
                return (BitmapSource)bi;
            }
            catch { return null; }   // битая или неподдерживаемая картинка: остаётся значок
        }).ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            _thumbs[path] = t.Result;
            Show(t.Result);
        }));
    }

    // карточка текста (буфер и заметки): несколько строк и подпись «Копировать», после клика «Скопировано»
    private SBorder TextCard(string text, double h, int lines, string tip, Action remove, int tab)
    {
        var body = new Grid { Margin = new Thickness(12, 10, 12, 9) };
        body.RowDefinitions.Add(new RowDefinition());
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string shown = text.Length > 400 ? text[..400] : text;
        // ссылка: сверху сайт крупно со значком, ниже адрес; клик открывает в браузере (Shift+клик копирует)
        string? url = UrlOf(text);
        // шаг 64: цвет и код в буфере (SmartClip.cs)
        bool isColor = tab == 1 && TryColor(text, out var swatch);
        string lang = "";
        bool isCode = tab == 1 && !isColor && LooksLikeCode(text, out lang);
        var meta = url != null && tab == 1 ? CachedLink(url) : null;
        if (url != null)
        {
            var u = new Uri(url);
            var head = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            var site = new StackPanel { Orientation = Orientation.Horizontal };
            var favicon = LoadIcon(meta?.Icon);
            site.Children.Add(new SBorder
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(7), Background = Solid(Color.FromArgb(0x30, 0x4F, 0xA3, 0xFF)),
                Child = favicon != null
                    ? new Image { Source = favicon, Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                    : new VIcon { Text = "link", FontSize = 11, Foreground = Solid(Color.FromRgb(0x7F, 0xBC, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
            site.Children.Add(new TextBlock
            {
                Text = u.Host.StartsWith("www.") ? u.Host[4..] : u.Host, Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            });
            head.Children.Add(site);
            // заголовок страницы, если он уже известен (превью ссылок), иначе адрес
            if (meta?.Title.Length > 0)
                head.Children.Add(new TextBlock
                {
                    Text = meta.Title, Foreground = Brushes.White, FontSize = 11.5, FontWeight = FontWeights.Medium, LineHeight = 15,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 45, Margin = new Thickness(0, 6, 0, 0)
                });
            head.Children.Add(new TextBlock
            {
                Text = Uri.UnescapeDataString(u.PathAndQuery.TrimEnd('/')), Foreground = Solid(Color.FromArgb(0x99, 255, 255, 255)), FontSize = 11,
                LineHeight = 14, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = (meta?.Title.Length > 0 ? 1 : lines - 2) * 14, Margin = new Thickness(0, meta?.Title.Length > 0 ? 3 : 6, 0, 0)
            });
            body.Children.Add(head);
            if (meta == null && _cfg.LinkPreviews && tab == 1)
                _ = FetchLink(url).ContinueWith(t => Dispatcher.BeginInvoke(() => { if (t.Result != null && _mode == Mode.Pocket && _tab == 1) RenderPocket(); }));
        }
        else if (isColor)
        {
            // образец цвета во всю ширину, под ним HEX и RGB
            var sw = new StackPanel();
            sw.Children.Add(new SBorder
            {
                Height = 54, CornerRadius = new CornerRadius(10), Background = Solid(swatch),
                BorderBrush = Solid(Color.FromArgb(0x30, 255, 255, 255)), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 14, 0)
            });
            sw.Children.Add(new TextBlock { Text = Hex(swatch), Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
            sw.Children.Add(new TextBlock { Text = $"rgb({swatch.R}, {swatch.G}, {swatch.B})", Foreground = Solid(Color.FromArgb(0x99, 255, 255, 255)), FontSize = 11 });
            body.Children.Add(sw);
        }
        else if (isCode)
        {
            var cb = CodeBlock(shown, lines + 1);
            cb.Margin = new Thickness(0, 0, 14, 0);
            body.Children.Add(cb);
        }
        else
            body.Children.Add(new TextBlock
            {
                Text = shown.Replace("\r", ""), Foreground = Brushes.White, FontSize = 12, LineHeight = 16,
                TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = lines * 16,
                Margin = new Thickness(0, 0, 14, 0)
            });
        string idleIcon = url != null ? "link" : "copy",
               idleText = url != null ? Loc.T("Открыть") : isCode && lang.Length > 0 ? Loc.T("Копировать") + " · " + lang : Loc.T("Копировать");
        var footIcon = new VIcon { Text = idleIcon, FontSize = 10.5, Foreground = Solid(Color.FromArgb(0x80, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center };
        var footText = new TextBlock
        {
            Text = idleText, Foreground = Solid(Color.FromArgb(0x80, 255, 255, 255)), FontSize = 10.5,
            Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
        };
        var foot = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        foot.Children.Add(footIcon);
        foot.Children.Add(footText);
        Grid.SetRow(foot, 1);
        body.Children.Add(foot);

        var card = PocketCard(body, TextCardW, h, tip, remove, tab, text);
        if (url != null) card.ToolTip = url + "\n" + Loc.T("Клик: открыть, Shift+клик: скопировать");
        DispatcherTimer? back = null;
        card.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (url != null && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (Exception ex) { App.Log("Pocket.OpenLink", ex); }
                Pop(foot, 0.7, 420);
                return;
            }
            CopyBack(text);
            footIcon.Text = "check";
            footText.Text = Loc.T("Скопировано");
            Pop(foot, 0.7, 420);
            back?.Stop();
            back = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            back.Tick += (_, _) => { back.Stop(); footIcon.Text = idleIcon; footText.Text = idleText; };
            back.Start();
        };
        return card;
    }

    // вкладки: значок, название и счётчик серым. Строятся один раз, дальше меняются только числа
    private void BuildTabs()
    {
        if (_tabsBuilt) return;
        _tabsBuilt = true;
        var tabs = new[] { TabFiles, TabClips, TabNotes };
        var names = new[] { "Файлы", "Буфер", "Заметки" };
        var icons = new[] { "folder", "clipboard", "sticky-note" };
        for (int i = 0; i < 3; i++)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new VIcon { Text = icons[i], FontSize = 12, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock
            {
                Text = Loc.T(names[i]), Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.Medium,
                Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            });
            _tabCount[i] = new TextBlock
            {
                Foreground = Solid(Color.FromArgb(0x80, 255, 255, 255)), FontSize = 11.5,
                Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            };
            sp.Children.Add(_tabCount[i]);
            tabs[i].Content = sp;
            _tabFace[i] = sp;
        }
        _clearTimer.Tick += (_, _) => DisarmClear();
    }

    private void StyleTabs(bool animate)
    {
        BuildTabs();
        int[] counts = { _files.Count, _clips.Count, _notes.Count };
        for (int i = 0; i < 3; i++)
        {
            _tabCount[i].Text = counts[i] > 0 ? counts[i].ToString() : "";
            _tabFace[i].BeginAnimation(OpacityProperty, new DoubleAnimation(_tab == i ? 1 : 0.55, TimeSpan.FromMilliseconds(animate ? 200 : 1)));
        }
        double x = _tab * TabW;
        if (animate)
            TabPillMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(460)) { EasingFunction = new SpringEase(7, 13) });
        else { TabPillMove.BeginAnimation(TranslateTransform.XProperty, null); TabPillMove.X = x; }
        NoteWrap.Visibility = _tab == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private int _shownTab = -1;

    private void RenderPocket()
    {
        bool tabChanged = _shownTab != _tab;
        StyleTabs(tabChanged && _shownTab >= 0);
        if (tabChanged) { DisarmClear(); PocketScroll.ScrollToHorizontalOffset(0); }
        _shownTab = _tab;
        PocketList.Children.Clear();

        int count = _tab == 0 ? _files.Count : _tab == 1 ? _clips.Count : _notes.Count;
        PocketEmptyIcon.Text = _tab == 0 ? "folder-open" : _tab == 1 ? "clipboard" : "sticky-note";
        PocketEmpty.Text = Loc.T(_tab == 0 ? "Перетащи файлы на остров" : _tab == 1 ? "Скопируй текст или картинку, и они появятся здесь" : "Напиши заметку ниже и нажми Enter");
        PocketEmptyBox.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PocketClear.Visibility = count == 0 ? Visibility.Hidden : Visibility.Visible;
        PocketClear.ToolTip = Loc.T(_tab == 0 ? "Убрать все файлы из кармана" : _tab == 1 ? "Очистить историю буфера" : "Удалить все заметки");

        if (_tab == 0)
        {
            foreach (var path in Shown(0, _files, Path.GetFileName)) PocketList.Children.Add(FileCard(path));
        }
        else if (_tab == 1)
        {
            foreach (var text in Shown(1, _clips, s => s))
                PocketList.Children.Add(IsImgClip(text) ? ImageClipCard(text)
                    : TextCard(text, CardH, 5, Loc.T("Клик: скопировать обратно"), () => _clips.Remove(text), 1));
        }
        else
        {
            foreach (var text in Shown(2, _notes, s => s))
                PocketList.Children.Add(TextCard(text, NoteCardH, 3, text, () => { _notes.Remove(text); SaveNotes(); }, 2));
        }

        // поиск ничего не нашёл
        if (count > 0 && PocketList.Children.Count == 0)
        {
            PocketEmptyIcon.Text = "search";
            PocketEmpty.Text = Loc.T("Ничего не найдено");
            PocketEmptyBox.Visibility = Visibility.Visible;
        }

        // смена вкладки: карточки всплывают каплями, как при открытии кармана
        if (tabChanged && _mode == Mode.Pocket) Droplets(PocketList.Children.OfType<FrameworkElement>(), 0, 30);
    }

    // ---------- «очистить»: первое нажатие краснеет и ждёт, второе (в течение 2,5 с) очищает ----------
    private void PocketClear_Click(object s, RoutedEventArgs e)
    {
        if (!_clearArmed)
        {
            _clearArmed = true;
            PocketClearIcon.Foreground = Solid(DangerC);
            PocketClear.ToolTip = Loc.T("Нажми ещё раз, чтобы очистить");
            Pop(PocketClear, 0.75, 420);
            _clearTimer.Stop();
            _clearTimer.Start();
            return;
        }
        DisarmClear();
        // закреплённое «очистить» не трогает
        if (_tab == 0) { _files.RemoveAll(x => !IsPinned(0, x)); SaveFiles(); }
        else if (_tab == 1) { foreach (var c in _clips.Where(x => !IsPinned(1, x))) DropClipFile(c); _clips.RemoveAll(x => !IsPinned(1, x)); }
        else { _notes.RemoveAll(x => !IsPinned(2, x)); SaveNotes(); }
        RenderPocket();
    }

    private void DisarmClear()
    {
        _clearTimer.Stop();
        if (!_clearArmed) return;
        _clearArmed = false;
        PocketClearIcon.Foreground = Brushes.White;
    }

    // ---------- плавная прокрутка колесом и затухание по краям ----------
    private static readonly DependencyProperty PocketOffsetProp = DependencyProperty.RegisterAttached(
        "PocketOffset", typeof(double), typeof(MainWindow),
        new PropertyMetadata(0.0, (d, e) => (d as ScrollViewer)?.ScrollToHorizontalOffset((double)e.NewValue)));
    private double _pocketTarget;
    private bool _pocketScrolling;
    private int _pocketScrollId;
    private readonly GradientStop _fadeL = new(Colors.Black, 0), _fadeR = new(Colors.Black, 1);

    private void PocketScroll_Wheel(object s, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (PocketScroll.ScrollableWidth <= 0) return;
        double from = PocketScroll.HorizontalOffset;
        double start = _pocketScrolling ? _pocketTarget : from;
        _pocketTarget = Math.Clamp(start - e.Delta * 0.8, 0, PocketScroll.ScrollableWidth);
        int id = ++_pocketScrollId;
        _pocketScrolling = true;
        var a = new DoubleAnimation(from, _pocketTarget, TimeSpan.FromMilliseconds(340)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        a.Completed += (_, _) => { if (id == _pocketScrollId) _pocketScrolling = false; };
        PocketScroll.BeginAnimation(PocketOffsetProp, a);
    }

    private void PocketScroll_Changed(object s, ScrollChangedEventArgs e)
    {
        PocketScroll.OpacityMask ??= new LinearGradientBrush(new GradientStopCollection
        {
            _fadeL, new GradientStop(Colors.Black, 0.07), new GradientStop(Colors.Black, 0.93), _fadeR
        }, new Point(0, 0), new Point(1, 0));
        _fadeL.Color = PocketScroll.HorizontalOffset > 1 ? Colors.Transparent : Colors.Black;
        _fadeR.Color = PocketScroll.HorizontalOffset < PocketScroll.ScrollableWidth - 1 ? Colors.Transparent : Colors.Black;
    }

    private void Pocket_Click(object s, RoutedEventArgs e) { _pocketOpen = true; _actionsOpen = false; _devicesOpen = false; _timerOpen = false; _mixerOpen = false; _centerOpen = false; _trOpen = false; _autoOpened = false; ApplyMode(Rest()); }
    private void PocketClose_Click(object s, RoutedEventArgs e) { _pocketOpen = false; _autoOpened = false; DisarmClear(); ApplyMode(Rest()); }
    private void TabFiles_Click(object s, RoutedEventArgs e) { _tab = 0; RenderPocket(); }
    private void TabClips_Click(object s, RoutedEventArgs e) { _tab = 1; RenderPocket(); }
    private void TabNotes_Click(object s, RoutedEventArgs e) { _tab = 2; RenderPocket(); }

    // ============================================================
    //  ЗАМЕТКИ
    // ============================================================
    private readonly List<string> _notes = new();
    private readonly DispatcherTimer _noteTimer = new() { Interval = TimeSpan.FromMinutes(30) };
    private int _noteIdx;
    private Action? _notifClick;
    private static string NotesFile => Path.Combine(DataDir, "notes.json");

    private void LoadNotes()
    {
        try
        {
            if (File.Exists(NotesFile))
            {
                var l = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(NotesFile));
                if (l != null) _notes.AddRange(l);
            }
        }
        catch (Exception ex) { App.Log("Notes.Load", ex); }
        RenderPocket();
    }

    private void SaveNotes()
    {
        try { SafeFile.WriteAllText(NotesFile, JsonSerializer.Serialize(_notes)); }
        catch (Exception ex) { App.Log("Notes.Save", ex); }
    }

    private void AddNote()
    {
        var t = NoteBox.Text.Trim();
        if (t.Length == 0) return;
        _notes.Insert(0, t);
        SaveNotes();
        NoteBox.Text = "";
        RenderPocket();
        if (PocketList.Children.Count > 0 && PocketList.Children[0] is FrameworkElement first) Pop(first, 0.6, 520);
    }

    private void NoteBox_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AddNote();
        e.Handled = true;
    }

    private void NoteBox_TextChanged(object s, TextChangedEventArgs e)
    {
        bool has = NoteBox.Text.Trim().Length > 0;
        NoteHint.Visibility = NoteBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoteAdd.BeginAnimation(OpacityProperty, new DoubleAnimation(has ? 1 : 0.35, TimeSpan.FromMilliseconds(150)));
    }

    private void NoteAdd_Click(object s, MouseButtonEventArgs e)
    {
        e.Handled = true;
        AddNote();
        NoteBox.Focus();
    }

    private void NoteReminder()
    {
        if (!_cfg.NoteReminders || _notes.Count == 0) return;
        if (_mode == Mode.Pocket || _mode == Mode.Actions) return;
        string n = _notes[_noteIdx++ % _notes.Count].Replace("\r", " ").Replace("\n", " ");
        ShowNotif("Заметка", "Заметка в кармане", n);
        if (_cfg.ReminderSound) Sfx.Reminder();   // шаг 53: глазами напоминание легко пропустить
        _notifClick = () => { _pocketOpen = true; _actionsOpen = false; _devicesOpen = false; _timerOpen = false; _mixerOpen = false; _centerOpen = false; _trOpen = false; _tab = 2; RenderPocket(); ApplyMode(Rest()); };
    }
}
