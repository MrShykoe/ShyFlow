using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Island;

// Картинки в истории буфера (шаг 54). Скопировал картинку или сделал скриншот (Win+Shift+S, PrintScreen) —
// она сохраняется в %AppData%\Island\clips и появляется в кармане на вкладке «Буфер» с превью.
//   Клик: снова в буфер обмена (можно вставить куда угодно). Перетащить: в чат, папку, письмо (как файл PNG).
//   Хранятся 15 последних вместе с текстом и между запусками; закреплённые булавкой не вытесняются.
// В списке буфера картинка записана как «img:путь».
public partial class MainWindow
{
    private static string ClipsDir => Path.Combine(DataDir, "clips");
    private static bool IsImgClip(string c) => c.StartsWith("img:", StringComparison.Ordinal);
    private ulong _lastImgHash;

    private static BitmapSource? SafeClipImage()
    {
        try { return Clipboard.GetImage(); } catch { return null; }
    }

    // возвращает картинку из буфера (для миниатюры в плашке, шаг 64); сохраняется она в фоне
    private BitmapSource? SaveClipImage()
    {
        BitmapSource? img = null;
        try
        {
            // PNG из буфера точнее (с прозрачностью), обычная картинка — запасной вариант
            if (Clipboard.ContainsData("PNG") && Clipboard.GetData("PNG") is MemoryStream ms)
            {
                var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                img = dec.Frames[0];
            }
            img ??= Clipboard.GetImage();
        }
        catch (Exception ex) { App.LogOnce("Clip.Image", ex); }
        if (img == null || img.PixelWidth < 2 || img.PixelHeight < 2) return null;
        var frozen = new WriteableBitmap(img);
        frozen.Freeze();
        Task.Run(() =>
        {
            try
            {
                // та же картинка второй раз (некоторые программы кладут её дважды): не дублируем
                ulong h = ImgHash(frozen);
                if (h == _lastImgHash) return null;
                _lastImgHash = h;
                Directory.CreateDirectory(ClipsDir);
                string path = Path.Combine(ClipsDir, "clip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(frozen));
                using (var fs = File.Create(path)) enc.Save(fs);
                return path;
            }
            catch (Exception ex) { App.LogOnce("Clip.Save", ex); return null; }
        }).ContinueWith(t => Dispatcher.BeginInvoke(() => { if (t.Result is string p) AddClip("img:" + p); }));
        return frozen;
    }

    // грубый отпечаток: размер и каждый 97-й пиксель
    private static ulong ImgHash(BitmapSource b)
    {
        int stride = b.PixelWidth * ((b.Format.BitsPerPixel + 7) / 8);
        var px = new byte[stride * b.PixelHeight];
        b.CopyPixels(px, stride, 0);
        ulong h = 1469598103934665603UL ^ (ulong)(b.PixelWidth * 31 + b.PixelHeight);
        for (int i = 0; i < px.Length; i += 97) h = (h ^ px[i]) * 1099511628211UL;
        return h;
    }

    // при запуске: прошлые картинки снова в истории (новые первыми), лишние файлы удаляются
    private void LoadClipImages()
    {
        try
        {
            if (!Directory.Exists(ClipsDir)) return;
            var files = new DirectoryInfo(ClipsDir).GetFiles("clip-*.png").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            int n = 0;
            foreach (var f in files)
            {
                string key = "img:" + f.FullName;
                if (IsPinned(1, key)) continue;
                if (n < 15 && _cfg.ClipboardImages) { if (!_clips.Contains(key)) _clips.Add(key); n++; }
                else { try { f.Delete(); } catch { } }
            }
            _clips.RemoveAll(c => IsImgClip(c) && !File.Exists(c[4..]));
        }
        catch (Exception ex) { App.LogOnce("Clip.Load", ex); }
    }

    private void DropClipFile(string c)
    {
        if (!IsImgClip(c) || IsPinned(1, c)) return;
        try { File.Delete(c[4..]); } catch { /* занят: удалится при следующем запуске */ }
        _thumbs.Remove(c[4..]);
    }

    private SBorder ImageClipCard(string item)
    {
        string path = item[4..];
        var pv = new Grid();
        var img = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        pv.Children.Add(new VIcon { Text = "image", FontSize = 26, Foreground = Solid(Color.FromRgb(0x34, 0xC7, 0xA0)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        pv.Children.Add(img);
        SetThumb(path, img);
        var preview = new SBorder { Margin = new Thickness(6, 6, 6, 0), CornerRadius = new CornerRadius(11), Background = Solid(Color.FromArgb(0x30, 0x34, 0xC7, 0xA0)), Child = pv, Height = 96 };
        preview.SizeChanged += (_, e) => preview.Clip = Smooth.Shape(e.NewSize.Width, e.NewSize.Height, new CornerRadius(11));

        var footIcon = new VIcon { Text = "copy", FontSize = 10.5, Foreground = Solid(Color.FromArgb(0x80, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center };
        var footText = new TextBlock { Text = Loc.T("Копировать"), Foreground = Solid(Color.FromArgb(0x80, 255, 255, 255)), FontSize = 10.5, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var foot = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 9, 10, 0) };
        foot.Children.Add(footIcon);
        foot.Children.Add(footText);
        var body = new StackPanel();
        body.Children.Add(preview);
        body.Children.Add(foot);

        string when = "";
        try { when = File.GetLastWriteTime(path).ToString("HH:mm"); } catch { }
        var card = PocketCard(body, TextCardW, CardH, Loc.T("Картинка из буфера") + (when.Length > 0 ? ", " + when : "") + "\n" + Loc.T("Клик: скопировать, перетащи: забрать"),
            () => { DropClipFile(item); _clips.Remove(item); }, 1, item);

        Point? start = null;
        card.PreviewMouseLeftButtonDown += (_, e) => start = e.GetPosition(null);
        card.PreviewMouseMove += (_, e) =>
        {
            if (start == null || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(null);
            if (Math.Abs(p.X - start.Value.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - start.Value.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            start = null;
            try
            {
                var data = new DataObject(DataFormats.FileDrop, new[] { path });
                if (_thumbs.TryGetValue(path, out var th) && th != null) data.SetImage(LoadFull(path) ?? th);
                DragDrop.DoDragDrop(card, data, DragDropEffects.Copy);
            }
            catch (Exception ex) { App.Log("Clip.Drag", ex); }
        };
        DispatcherTimer? back = null;
        card.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (start == null) return;
            start = null;
            var full = LoadFull(path);
            if (full == null) { footText.Text = Loc.T("Файла нет"); return; }
            try { _ignoreClipUntil = DateTime.UtcNow.AddMilliseconds(800); Clipboard.SetImage(full); }
            catch (Exception ex) { App.Log("Clip.Copy", ex); return; }
            footIcon.Text = "check";
            footText.Text = Loc.T("Скопировано");
            Pop(foot, 0.7, 420);
            back?.Stop();
            back = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            back.Tick += (_, _) => { back.Stop(); footIcon.Text = "copy"; footText.Text = Loc.T("Копировать"); };
            back.Start();
        };
        return card;
    }

    private static BitmapSource? LoadFull(string path)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }
}
