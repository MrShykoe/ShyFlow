using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Island;

// Свой фон острова и свой шрифт (Настройки, «Внешний вид», «Фон острова» и «Шрифт»).
//
// Фон: картинка, гифка или видео (первые 10 секунд, 12 кадров в секунду, без звука) вместо фона темы и размытой обложки.
//   Файл читается тем же кодом, что и стикер (GifSticker.cs), только крупнее: до 480 px по большей стороне.
//   Лежит под всем содержимым острова и обрезается по его мягкому контуру (вместе с ушами в режиме notch).
//   Затемнение (нет, лёгкое, среднее, сильное), чтобы текст читался на ярких картинках, и размытие (нет, лёгкое, сильное).
//   Анимация идёт, только пока остров виден.
// Шрифт: свои .ttf, .otf, .ttc (копия в %AppData%\Island\fonts). Применяется ко всему тексту острова
//   (ширина острова тоже считается этим шрифтом) и, если включено, к плееру на весь экран. Значки остаются значками.
public partial class MainWindow
{
    private const int BgMaxSide = 480, BgMaxFrames = 150;
    // свойство, а не поле: порядок статических полей в разных partial-файлах не определён, DataDir мог быть ещё не задан
    internal static string FontsDir => Path.Combine(DataDir, "fonts");

    private List<BitmapSource> _bgFrames = new();
    private List<int> _bgDelays = new();
    private int _bgIdx, _bgGen;
    private string _bgSig = "", _bgLoading = "";
    private readonly ImageBrush _bgBrush = new() { Stretch = Stretch.UniformToFill };
    private DispatcherTimer? _bgTimer;

    // ---------- фон ----------
    private void ApplyIslandBg()
    {
        string path = _cfg.BgPath ?? "";
        bool want = _cfg.BgEnabled && path.Length > 0 && File.Exists(path);
        if (!want) { BgOff(); return; }

        // затемнение и размытие меняются без перечитывания файла
        BgShade.Background = new SolidColorBrush(Color.FromArgb(_cfg.BgDim switch { 0 => (byte)0, 2 => (byte)0x80, 3 => (byte)0xB0, _ => (byte)0x50 }, 0, 0, 0));
        double blur = _cfg.BgBlur switch { 1 => 8, 2 => 20, _ => 0 };
        BgCustom.Effect = blur > 0 && !_eco ? new BlurEffect { Radius = blur, RenderingBias = RenderingBias.Performance } : null;
        AppsBgCustom.Effect = blur > 0 && !_eco ? new BlurEffect { Radius = blur, RenderingBias = RenderingBias.Performance } : null;   // капсула программ: своя копия
        BubbleBgCustom.Effect = blur > 0 && !_eco ? new BlurEffect { Radius = blur, RenderingBias = RenderingBias.Performance } : null;   // и пузырь
        BubbleBgCustom.RenderTransform = new ScaleTransform(blur > 0 ? 1.12 : 1, blur > 0 ? 1.12 : 1);
        EvBgCustom.Effect = blur > 0 && !_eco ? new BlurEffect { Radius = blur, RenderingBias = RenderingBias.Performance } : null;   // и пузырь событий
        EvBgCustom.RenderTransform = new ScaleTransform(blur > 0 ? 1.12 : 1, blur > 0 ? 1.12 : 1);
        // размытие тянет прозрачность с краёв: чуть увеличиваем картинку, края уходят под обрезку
        BgCustom.RenderTransform = new ScaleTransform(blur > 0 ? 1.12 : 1, blur > 0 ? 1.12 : 1);
        AppsBgCustom.RenderTransform = new ScaleTransform(blur > 0 ? 1.12 : 1, blur > 0 ? 1.12 : 1);

        string sig = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
        if (sig == _bgSig || sig == _bgLoading) { ShowBgLayers(_bgFrames.Count > 0); return; }
        _bgLoading = sig;
        int gen = ++_bgGen;
        Task.Run(() => DecodeMedia(path, BgMaxSide, BgMaxFrames)).ContinueWith(t =>
        {
            var raw = t.IsCompletedSuccessfully ? t.Result : null;
            Dispatcher.BeginInvoke(() =>
            {
                if (gen != _bgGen) return;
                _bgLoading = "";
                if (raw == null || raw.Frames.Count == 0)
                {
                    BgOff();
                    if (_settingsWin != null)
                        MacDialog.Info(_settingsWin, Loc.T("Не удалось открыть файл. Возможно, этот формат не поддерживается вашей версией Windows"));
                    return;
                }
                var frames = new List<BitmapSource>(raw.Frames.Count);
                foreach (var px in raw.Frames)
                {
                    var bs = BitmapSource.Create(raw.W, raw.H, 96, 96, PixelFormats.Pbgra32, null, px, raw.W * 4);
                    bs.Freeze();
                    frames.Add(bs);
                }
                _bgFrames = frames;
                _bgDelays = raw.Delays;
                _bgIdx = 0;
                _bgSig = sig;
                _bgBrush.ImageSource = frames[0];
                RenderOptions.SetBitmapScalingMode(_bgBrush, BitmapScalingMode.HighQuality);
                BgCustom.Background = _bgBrush;
                ShowBgLayers(true);
                _bgTimer ??= MakeBgTimer();
                _bgTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(20, _bgDelays[0]));
                if (frames.Count > 1) _bgTimer.Start(); else _bgTimer.Stop();
            });
        });
    }

    private DispatcherTimer MakeBgTimer()
    {
        var t = new DispatcherTimer(DispatcherPriority.Render);
        t.Tick += (_, _) =>
        {
            if (_bgFrames.Count < 2) { t.Stop(); return; }
            // остров спрятан или под плеером: кадры не меняем, только изредка проверяем
            if (_hidden || !IsVisible) { t.Interval = TimeSpan.FromMilliseconds(300); return; }
            _bgIdx = (_bgIdx + 1) % _bgFrames.Count;
            _bgBrush.ImageSource = _bgFrames[_bgIdx];
            t.Interval = TimeSpan.FromMilliseconds(Math.Max(_eco ? 66 : 20, _bgDelays[_bgIdx]));   // в экономном режиме не чаще 15 раз в секунду
        };
        return t;
    }

    // свой фон вместо размытой обложки (она под ним всё равно не видна)
    private void ShowBgLayers(bool on)
    {
        var vis = on ? Visibility.Visible : Visibility.Collapsed;
        BgCustom.Visibility = BgShade.Visibility = vis;
        BgImg.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BgOff()
    {
        _bgGen++;
        _bgLoading = _bgSig = "";
        _bgTimer?.Stop();
        _bgFrames = new();
        _bgDelays = new();
        _bgBrush.ImageSource = null;
        BgCustom.Background = null;
        ShowBgLayers(false);
    }

    // ---------- шрифт ----------
    private static readonly FontFamily SystemFont = new("Segoe UI Variable Text, Segoe UI");
    private FontFamily _islandFont = SystemFont;

    // Шрифт из конкретного файла. Fonts.GetFontFamilies(путь) не подходит: он перебирает всю папку и отдаёт первый попавшийся шрифт.
    // Поэтому имя семейства читаем из самого файла, а файл указываем явно: «папка/» + «./файл.ttf#Имя».
    internal static FontFamily? LoadFontFile(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            string? fam = FamilyOf(path);
            if (fam == null) return null;
            var dir = new Uri(Path.GetDirectoryName(path)! + Path.DirectorySeparatorChar);
            return new FontFamily(dir, "./" + Path.GetFileName(path) + "#" + fam);
        }
        catch (Exception ex) { App.Log("Font", ex); return null; }
    }

    private static string? FamilyOf(string path)
    {
        try
        {
            var gt = new GlyphTypeface(new Uri(path));
            var en = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            if (gt.FamilyNames.TryGetValue(en, out var n) && !string.IsNullOrWhiteSpace(n)) return n;
            if (gt.Win32FamilyNames.TryGetValue(en, out n) && !string.IsNullOrWhiteSpace(n)) return n;
            return gt.FamilyNames.Values.FirstOrDefault() ?? gt.Win32FamilyNames.Values.FirstOrDefault();
        }
        catch { return null; }   // не шрифт или битый файл
    }

    // имя шрифта из файла для списка в настройках
    internal static string FontName(string path) => FamilyOf(path) ?? Path.GetFileNameWithoutExtension(path);

    internal static List<string> UserFonts()
    {
        try
        {
            if (!Directory.Exists(FontsDir)) return new List<string>();
            return Directory.GetFiles(FontsDir).Where(p => p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch { return new List<string>(); }
    }

    private void ApplyFont()
    {
        var ff = LoadFontFile(_cfg.FontFile ?? "") ?? SystemFont;
        if (ReferenceEquals(ff, _islandFont) || ff.Source == _islandFont.Source) return;
        _islandFont = ff;
        FontFamily = ff;   // весь текст острова наследует шрифт окна
        UpdateWidths(MiniTitle.Text, ArtistText.Text);
    }

    // шрифт для плеера на весь экран (null: его собственный)
    internal FontFamily? PlayerFont => _cfg.FontInPlayer && !ReferenceEquals(_islandFont, SystemFont) ? _islandFont : null;
}
