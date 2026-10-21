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
    // ---------- экспорт, импорт и сброс настроек ----------
    private void ExportCfg()
    {
        var dlg = new SaveFileDialog { Filter = Loc.T("Настройки ShyFlow (*.json)|*.json"), FileName = "shyflow-settings.json" };
        if (dlg.ShowDialog(this) != true) return;
        try { File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(_s, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { MacDialog.Info(this, Loc.T("Не удалось сохранить файл: ") + ex.Message); }
    }

    private void ApplyAndReopen()
    {
        _s.Save(); _changed();
        WantsReopen = true;
        Close();
    }

    private void ImportCfg()
    {
        var dlg = new OpenFileDialog { Filter = Loc.T("Настройки ShyFlow (*.json)|*.json") };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(dlg.FileName));
            if (loaded == null) throw new InvalidDataException("пустой файл");
            CopySettings(loaded, _s);
            ApplyAndReopen();
        }
        catch (Exception ex) { MacDialog.Info(this, Loc.T("Не удалось прочитать файл: ") + ex.Message); }
    }

    private void ResetCfg()
    {
        if (!MacDialog.Ask(this, Loc.T("Вернуть все настройки к значениям по умолчанию?"), Loc.T("Сбросить", "Reset"))) return;
        CopySettings(new AppSettings(), _s);
        ApplyAndReopen();
    }
}
