using System.IO;
using Microsoft.Win32;

namespace Island;

// Настройки «Фон острова» и «Шрифт»: выбор файлов. Сами фон и шрифт применяются в Main\Customize.cs.
public sealed partial class SettingsWindow
{
    private static string IslandDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island");

    private void PickBg()
    {
        var dlg = new OpenFileDialog
        {
            Filter = Loc.T("Картинки, гифки и видео|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tif;*.tiff;*.mp4;*.m4v;*.mov;*.webm;*.mkv;*.avi;*.wmv|Все файлы|*.*"),
            Title = Loc.T("Выбери фон для острова")
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dlg.FileName).Length > 300L * 1024 * 1024)
            {
                MacDialog.Info(this, Loc.T("Файл слишком большой (больше 300 МБ)"));
                return;
            }
            Directory.CreateDirectory(IslandDir);
            string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
            if (ext.Length == 0 || ext.Length > 6) ext = ".bin";
            string dst = Path.Combine(IslandDir, "background-" + DateTime.Now.Ticks + ext);
            File.Copy(dlg.FileName, dst, true);   // копия: фон не пропадёт, если исходный файл удалят
            DeleteOwnCopy(_s.BgPath);
            bool wasOff = !_s.BgEnabled;
            _s.BgPath = dst;
            _s.BgEnabled = true;
            if (wasOff) ApplyAndReopen();   // переключатель в окне надо перерисовать
            else { _s.Save(); _changed(); }
        }
        catch (Exception ex) { MacDialog.Info(this, Loc.T("Не удалось сохранить файл: ") + ex.Message); }
    }

    private void RemoveBg()
    {
        if (string.IsNullOrEmpty(_s.BgPath)) return;
        DeleteOwnCopy(_s.BgPath);
        _s.BgPath = "";
        _s.Save(); _changed();
    }

    // удаляем только свои копии в папке ShyFlow, чужие файлы не трогаем
    private static void DeleteOwnCopy(string? path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith(IslandDir, StringComparison.OrdinalIgnoreCase)) return;
        try { File.Delete(path); }
        catch (Exception ex) { App.Log("Settings.DeleteCopy", ex); }
    }

    private void PickFont()
    {
        var dlg = new OpenFileDialog { Filter = Loc.T("Шрифты|*.ttf;*.otf;*.ttc"), Title = Loc.T("Выбери файл шрифта") };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            Directory.CreateDirectory(MainWindow.FontsDir);
            string dst = Path.Combine(MainWindow.FontsDir, Path.GetFileName(dlg.FileName));
            if (!string.Equals(dst, dlg.FileName, StringComparison.OrdinalIgnoreCase)) File.Copy(dlg.FileName, dst, true);
            if (MainWindow.LoadFontFile(dst) == null)
            {
                try { File.Delete(dst); } catch { /* не удалился: просто останется лежать */ }
                MacDialog.Info(this, Loc.T("Это не похоже на файл шрифта"));
                return;
            }
            _s.FontFile = dst;
            ApplyAndReopen();   // список шрифтов в окне надо пересобрать
        }
        catch (Exception ex) { MacDialog.Info(this, Loc.T("Не удалось сохранить файл: ") + ex.Message); }
    }

    private void RemoveFont()
    {
        string f = _s.FontFile ?? "";
        if (f.Length == 0) return;
        _s.FontFile = "";
        // файл шрифта может быть ещё занят (шрифт прямо сейчас на экране): тогда он просто останется в папке, это не страшно
        try { if (f.StartsWith(MainWindow.FontsDir, StringComparison.OrdinalIgnoreCase)) File.Delete(f); }
        catch (Exception ex) { App.Log("Settings.DeleteFont", ex); }
        ApplyAndReopen();
    }
}
