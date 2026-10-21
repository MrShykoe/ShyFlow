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
    // ---------- стикер в острове: выбор файла и вырезание фона ----------
    private void PickGif()
    {
        var dlg = new OpenFileDialog
        {
            Filter = Loc.T("Гифки, картинки и видео|*.gif;*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tif;*.tiff;*.mp4;*.m4v;*.mov;*.webm;*.mkv;*.avi;*.wmv|Все файлы|*.*"),
            Title = Loc.T("Выбери гифку, картинку или видео")
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dlg.FileName).Length > 300L * 1024 * 1024)
            {
                MacDialog.Info(this, Loc.T("Файл слишком большой (больше 300 МБ)"));
                return;
            }
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island");
            Directory.CreateDirectory(dir);
            string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
            if (ext.Length == 0 || ext.Length > 6) ext = ".bin";
            string dst = Path.Combine(dir, "sticker-" + DateTime.Now.Ticks + ext);
            File.Copy(dlg.FileName, dst, true);   // копия в папке программы: стикер не пропадёт, если исходный файл удалят
            if (!string.IsNullOrEmpty(_s.GifPath) && _s.GifPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                try { File.Delete(_s.GifPath); } catch { }
            bool wasOff = !_s.GifEnabled;
            _s.GifPath = dst; _s.GifEnabled = true;
            if (wasOff) ApplyAndReopen();   // переключатель в окне надо перерисовать
            else { _s.Save(); _changed(); }
        }
        catch (Exception ex) { MacDialog.Info(this, Loc.T("Не удалось сохранить файл: ") + ex.Message); }
    }

    private void RemoveGif()
    {
        if (string.IsNullOrEmpty(_s.GifPath)) return;
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island");
            if (_s.GifPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) File.Delete(_s.GifPath);
        }
        catch (Exception ex) { App.Log("Sticker.Remove", ex); }
        _s.GifPath = "";
        _s.Save(); _changed();
    }

    // ряд цветов для вырезания фона гифки: крестик (не вырезать), «А» (цвет из угла), готовые цвета и свой HEX
    private void GifBgRow(StackPanel card)
    {
        Divider(card);
        var g = new Grid { Margin = new Thickness(16, 10, 14, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        tp.Children.Add(RowTitle("Убрать фон стикера"));
        tp.Children.Add(RowSub("Вырезается выбранный цвет от краёв картинки. «А» берёт цвет с рамки кадра, крестик оставляет как есть"));
        g.Children.Add(tp);

        var presets = new (string key, string hex)[]
        {
            ("off", ""), ("auto", ""), ("#FFFFFF", "#FFFFFF"), ("#000000", "#000000"), ("#00FF00", "#00FF00"),
            ("#0000FF", "#0000FF"), ("#FF00FF", "#FF00FF"), ("#FF0000", "#FF0000")
        };
        var host = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var rings = new List<(Border b, string key)>();
        string Current() => !_s.GifRemoveBg ? "off" : (_s.GifKeyColor ?? "auto").Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? "auto" : (_s.GifKeyColor ?? "").ToUpperInvariant();
        void Mark()
        {
            string cur = Current();
            foreach (var (b2, key) in rings)
                b2.BorderBrush = key == cur ? Brushes.White : Br(0x30, 255, 255, 255);
        }
        foreach (var (key, hex) in presets)
        {
            var sw = new SBorder
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 6, 0),
                BorderThickness = new Thickness(2), BorderBrush = Br(0x30, 255, 255, 255), Cursor = Cursors.Hand,
                Background = hex.Length > 0 ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) : (Brush)ChipBg
            };
            if (key == "off")
                sw.Child = new TextBlock { Text = "✕", Foreground = Brushes.White, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            else if (key == "auto")
                sw.Child = new TextBlock { Text = "А", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            rings.Add((sw, key));
            string k = key;
            sw.MouseLeftButtonDown += (_, e) =>
            {
                if (k == "off") _s.GifRemoveBg = false;
                else { _s.GifRemoveBg = true; _s.GifKeyColor = k; }
                _s.Save(); _changed(); Mark(); e.Handled = true;
            };
            host.Children.Add(sw);
        }
        Mark();
        Grid.SetColumn(host, 1);
        g.Children.Add(host);
        card.Children.Add(g);
    }

    private void GifHexRow(StackPanel card)
    {
        Divider(card);
        var g = new Grid { Margin = new Thickness(16, 9, 14, 9) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = RowTitle("Свой цвет фона (HEX)");
        t.VerticalAlignment = VerticalAlignment.Center;
        t.Margin = new Thickness(0, 0, 14, 0);
        g.Children.Add(t);
        var tb = new TextBox
        {
            Width = 96, FontSize = 12.5, Foreground = Brushes.White, Background = ChipBg, BorderThickness = new Thickness(0),
            CaretBrush = Brushes.White, Padding = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center,
            Text = _s.GifKeyColor is string hex && hex.StartsWith("#") ? hex : "#"
        };
        void Commit()
        {
            string v = tb.Text.Trim();
            if (!v.StartsWith("#")) v = "#" + v;
            if (!System.Text.RegularExpressions.Regex.IsMatch(v, "^#[0-9A-Fa-f]{6}$")) return;
            _s.GifRemoveBg = true; _s.GifKeyColor = v.ToUpperInvariant();
            _s.Save(); _changed();
        }
        tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        tb.LostFocus += (_, _) => Commit();
        Grid.SetColumn(tb, 1);
        g.Children.Add(tb);
        card.Children.Add(g);
    }
}
