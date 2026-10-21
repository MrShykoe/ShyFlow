using System.IO;

namespace Island;

// Файл на телефон по QR (шаг 66): кинул файл на остров — сразу окно с QR (настройка «QR при броске файла»),
// или кнопка с телефоном на карточке файла в кармане. Раздача и окно: Services\LanShare.cs, Ui\QrWindow.cs.
public partial class MainWindow
{
    private QrWindow? _qrWin;
    private bool _qrHooked;

    private void ShowQr(string path)
    {
        if (Directory.Exists(path)) { ShowToast("smartphone", "Папку так не отправить: только файл", null, "key"); return; }
        if (!_qrHooked)
        {
            _qrHooked = true;
            LanShare.Downloaded += it => Dispatcher.BeginInvoke(() =>
            {
                if (_qrWin?.Token == it.Token) _qrWin.Downloaded();
                TriggerDone(Loc.T("На телефоне"));
                Sfx.Ui("bubble");
            });
        }
        string? url = LanShare.Share(path, out var item);
        if (url == null || item == null)
        {
            ShowToast("wifi-off", "Нет Wi-Fi или сети: QR для телефона не получится", null, "net");
            return;
        }
        _qrWin?.Close();
        _qrWin = new QrWindow(url, item, MonitorDip);
        _qrWin.Closed += (_, _) => _qrWin = null;
        _qrWin.Show();
        _qrWin.Activate();
    }
}
