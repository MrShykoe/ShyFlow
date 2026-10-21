using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Island;

// Плашки и уведомления в острове (шаг 40, новый вид).
//
// Плашка (громкость, яркость, сеть, Bluetooth, клавиши, зарядка, загрузки, микрофон и камера, трек...):
//   значок в маленьком цветном квадратике, цвет по смыслу (синий сеть, оранжевый микрофон, зелёный камера и зарядка,
//   красный «нет сети» и низкий заряд, жёлтый яркость, фиолетовый буфер, у громкости цвет обложки).
//   Текст вида «Wi-Fi: Дом» делится на заголовок и подпись. У смены трека вместо значка обложка, подпись исполнитель.
//   У громкости и яркости тонкая полоска и проценты; значок громкости меняется по уровню.
// Уведомление: значок приложения его цветом (Telegram, Discord, WhatsApp...; у остальных цвет по имени, всегда один и тот же),
//   сверху имя приложения и «сейчас», ниже заголовок и текст до двух строк. Длинный текст делает остров чуть выше.
public partial class MainWindow
{
    private double _notifH = 76;
    private string _osdKind = "";

    private static Color Rgb(uint hex) => Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);

    private Color ToastColor(string glyph, string kind, bool muted)
    {
        Color accent = UiColor;
        return kind switch
        {
            "vol" => muted ? Rgb(0x8E8E93) : accent,
            "sun" => Rgb(0xFFD60A),
            "net" => glyph == "wifi-off" ? Rgb(0xFF453A) : Rgb(0x0A84FF),
            "bt" => Rgb(0x0A84FF),
            "mic" => glyph == "mic-off" ? Rgb(0xFF453A) : Rgb(0xFF9F0A),
            "cam" => glyph == "\uE714" ? Rgb(0xFF453A) : Rgb(0x30D158),
            "bat" => glyph == "triangle-alert" ? Rgb(0xFF453A) : Rgb(0x30D158),
            "dl" => glyph == "\uE73E" ? Rgb(0x30D158) : Rgb(0x0A84FF),
            "copy" => Rgb(0xBF5AF2),
            "key" => glyph switch
            {
                "sun" => Rgb(0xFFD60A),
                "moon" => Rgb(0x5E5CE6),
                "gauge" => Rgb(0xFF9F0A),
                "clock" => accent,
                _ => Rgb(0xC7C7CC)
            },
            "lyr" => accent,
            "card" => _cardCol,   // шаг 71: новая карточка, цвет редкости (Collect.cs)
            _ => accent
        };
    }

    private static bool IsWhiteish(Color c) => c.R > 235 && c.G > 235 && c.B > 235;

    private void ShowToast(string glyph, string? text, double? value, string kind = "", BitmapSource? art = null)
    {
        if (text != null) text = Loc.T(text);
        _osdClick = null;   // у новой плашки свой клик (SmartClip.cs ставит его после показа)
        bool vol = kind == "vol";
        bool muted = vol && value is double mv && mv <= 0.001;
        // значок громкости по уровню: перечёркнутый, тихо, громко
        if (vol && value is double lv) glyph = lv <= 0.001 ? "volume-x" : lv < 0.5 ? "volume-1" : "volume-2";
        var col = ToastColor(glyph, kind, muted);
        bool track = kind is "next" or "prev" or "play";

        // шаг 52: «Всё в пузырях» — плашка в капсуле рядом с островом, сам остров не меняется (EventBubble.cs)
        if (EventsToBubble)
        {
            string t0 = text ?? "", s0 = "";
            int c0 = t0.IndexOf(": ", StringComparison.Ordinal);
            if (track) s0 = _curArtist;
            else if (c0 > 0 && c0 < 32) { s0 = t0[(c0 + 2)..]; t0 = t0[..c0]; }
            if (vol && value is double vv) glyph = vv <= 0.001 ? "volume-x" : vv < 0.5 ? "volume-1" : "volume-2";
            ShowEventToast(glyph, t0, s0, value, col, art ?? (track ? _lastCover : null), art == null && track);
            _osdKind = kind;
            return;
        }

        // значок в цветном квадратике; у трека вместо значка обложка
        // своя картинка (иконка запущенной программы) или обложка у трека
        if (art != null || (track && _lastCover != null))
        {
            var br = new ImageBrush(art ?? _lastCover) { Stretch = art != null ? Stretch.Uniform : Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(br, BitmapScalingMode.HighQuality);
            OsdArt.Background = br;
            OsdArt.Margin = new Thickness(art != null ? 5 : 0);
            OsdArt.Visibility = Visibility.Visible;
            OsdIcon.Visibility = Visibility.Collapsed;
            OsdBadge.Background = art != null ? new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)) : Brushes.Transparent;
        }
        else
        {
            OsdArt.Visibility = Visibility.Collapsed;
            OsdIcon.Visibility = Visibility.Visible;
            OsdIcon.Text = glyph;   // иконка плавно перетекает из предыдущей (VIcon)
            OsdIcon.Foreground = new SolidColorBrush(IsWhiteish(col) ? Colors.White : col);
            OsdBadge.Background = new SolidColorBrush(Color.FromArgb(IsWhiteish(col) ? (byte)0x26 : (byte)0x38, col.R, col.G, col.B));
        }

        double w;
        if (value is double v)
        {
            v = Math.Clamp(v, 0, 1);
            OsdTrack.Visibility = Visibility.Visible;
            OsdFill.Visibility = Visibility.Visible;
            OsdTextBox.Visibility = Visibility.Collapsed;
            OsdFill.Background = vol && !muted ? _uiBrush : new SolidColorBrush(IsWhiteish(col) ? Colors.White : col);
            OsdFill.BeginAnimation(WidthProperty, new DoubleAnimation(OsdBarW * v, TimeSpan.FromMilliseconds(160))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            OsdValue.Text = muted ? "—" : ((int)Math.Round(v * 100)).ToString();
            OsdValue.Visibility = Visibility.Visible;
            // шаг 62: копирование и скачивание наливают квадратик значка жидкостью (Ui\WaveFill.cs)
            bool liquid = kind is "copy" or "dl";
            OsdWave.Visibility = liquid ? Visibility.Visible : Visibility.Collapsed;
            if (liquid) { OsdWave.Color = IsWhiteish(col) ? Color.FromRgb(0x0A, 0x84, 0xFF) : col; OsdWave.Value = v; OsdIcon.Foreground = Brushes.White; }
            w = 280;
        }
        else
        {
            OsdTrack.Visibility = Visibility.Collapsed;
            OsdFill.Visibility = Visibility.Collapsed;
            OsdTextBox.Visibility = Visibility.Visible;
            // шаг 60: пустое число справа (MinWidth 30) съедало место у текста: «Микрофон вк…» при свободном острове
            OsdValue.Text = "";
            OsdValue.Visibility = Visibility.Collapsed;
            OsdWave.Visibility = Visibility.Collapsed;
            // «Wi-Fi: Дом» -> заголовок «Wi-Fi», подпись «Дом»; у трека подпись исполнитель
            string title = text ?? "", sub = "";
            int colon = title.IndexOf(": ", StringComparison.Ordinal);
            if (track) sub = _curArtist;
            else if (colon > 0 && colon < 32) { sub = title[(colon + 2)..]; title = title[..colon]; }
            OsdText.Text = title;
            OsdSub.Text = sub;
            OsdSub.Visibility = sub.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            double tw = Math.Max(TextW(title, 13, FontWeights.SemiBold), sub.Length > 0 ? TextW(sub, 11.5, FontWeights.Normal) : 0);
            w = Math.Clamp(11 + 32 + 11 + tw + 8 + 16 + 6, 190, 440);
        }
        ApplyMode(Mode.Osd, w);

        if ((DateTime.UtcNow - _lastIconAt).TotalMilliseconds > 350)
        {
            switch (kind)
            {
                case "next": Slide(OsdBadge, -20); Pop(OsdBadge, 0.7); break;   // обложка вылетает вправо
                case "prev": Slide(OsdBadge, 20); Pop(OsdBadge, 0.7); break;    // и влево
                case "play": Pop(OsdBadge, 0.6); break;
                case "sun": Spin(OsdIcon, -120); Pop(OsdBadge, 0.6); break;
                default: Pop(OsdBadge, kind == _osdKind ? 0.85 : 0.45); break;   // та же плашка подряд: лёгкий толчок, новая: заметный
            }
        }
        _osdKind = kind;
        _lastIconAt = DateTime.UtcNow;
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromMilliseconds(track || value == null ? 2000 : 1700);
        _toastTimer.Start();
    }

    // ---------- уведомления ----------
    private static readonly (string key, uint color)[] AppColors =
    {
        ("telegram", 0x2AABEE), ("discord", 0x5865F2), ("whatsapp", 0x25D366), ("вконтакте", 0x0077FF), ("vk", 0x0077FF),
        ("chrome", 0x4285F4), ("edge", 0x1B9DE2), ("firefox", 0xFF7139), ("opera", 0xFF1B2D), ("yandex", 0xFC3F1D), ("яндекс", 0xFC3F1D),
        ("steam", 0x2A475E), ("outlook", 0x0078D4), ("почта", 0x0078D4), ("mail", 0x0078D4), ("gmail", 0xEA4335), ("youtube", 0xFF0000),
        ("spotify", 0x1DB954), ("teams", 0x5059C9), ("slack", 0x611F69), ("twitch", 0x9146FF), ("viber", 0x7360F2), ("skype", 0x00AFF0),
        ("таймер", 0xFF9F0A), ("timer", 0xFF9F0A), ("заметка", 0xFFD60A), ("note", 0xFFD60A),
        ("скриншот", 0x30D158), ("screenshot", 0x30D158), ("запись экрана", 0xFF453A), ("screen record", 0xFF453A)
    };

    private static readonly uint[] Palette = { 0xFF453A, 0xFF9F0A, 0x30D158, 0x64D2FF, 0x0A84FF, 0x5E5CE6, 0xBF5AF2, 0xFF375F, 0xAC8E68 };

    private static Color AppColor(string name)
    {
        string n = name.ToLowerInvariant();
        foreach (var (key, c) in AppColors) if (n.Contains(key)) return Rgb(c);
        uint h = 2166136261;   // стабильный хэш имени: у приложения всегда один и тот же цвет
        foreach (char ch in n) h = (h ^ ch) * 16777619;
        return Rgb(Palette[h % (uint)Palette.Length]);
    }

    // буквы на значке: «Google Chrome» -> «GC», «Telegram» -> «Te»
    private static string Initials(string name)
    {
        var words = name.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2 && char.IsLetterOrDigit(words[0][0]) && char.IsLetterOrDigit(words[1][0]))
            return (words[0][..1] + words[1][..1]).ToUpper(Loc.Culture);
        string w = name.Trim();
        return w.Length <= 1 ? w.ToUpper(Loc.Culture) : char.ToUpper(w[0], Loc.Culture) + w[1..2].ToLower(Loc.Culture);
    }

    // уведомление от самого острова (таймер, напоминание, скриншот...): в шторку не попадает
    private void ShowNotif(string app, string title, string body) =>
        ShowNotifItem(new NotifItem { App = app, Title = title, Body = body });
}