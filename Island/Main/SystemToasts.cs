using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Networking.Connectivity;

namespace Island;

// Системные плашки: микрофон и камера, Caps Lock и Num Lock, Wi-Fi и Bluetooth,
// скриншоты и запись экрана, батарея, загрузки в браузере.
public partial class MainWindow
{
    // ============================================================
    //  МИКРОФОН И КАМЕРА (окно приватности Windows)
    // ============================================================
    private readonly DispatcherTimer _privTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private string _micKey = "", _camKey = "";
    private bool _dotOn;

    private static string AppName(string key)
    {
        if (key.Contains('#')) return Path.GetFileNameWithoutExtension(key.Split('#').Last());
        int u = key.IndexOf('_');
        return u > 0 ? key[..u] : key;
    }

    private static void ScanKey(RegistryKey k, List<string> res)
    {
        foreach (var n in k.GetSubKeyNames())
        {
            if (n == "NonPackaged") continue;
            using var s = k.OpenSubKey(n);
            if (s == null) continue;
            var start = s.GetValue("LastUsedTimeStart") as long?;
            var stop = s.GetValue("LastUsedTimeStop") as long?;
            if (start.HasValue && start.Value > 0 && stop.HasValue && stop.Value == 0) res.Add(AppName(n));
        }
    }

    private static List<string> InUse(string kind)
    {
        var res = new List<string>();
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\" + kind);
            if (root == null) return res;
            ScanKey(root, res);
            using var np = root.OpenSubKey("NonPackaged");
            if (np != null) ScanKey(np, res);
        }
        catch (Exception ex) { App.LogOnce("Privacy", ex); }
        return res;
    }

    private bool _micOn, _camOn, _recActive;

    private void UpdateDot()
    {
        bool any = _micOn || _camOn || _recActive;
        if (any)
        {
            RecDot.Fill = _recActive ? Brushes.Red : _camOn ? Brushes.LimeGreen : Brushes.Orange;
            if (!_dotOn)
            {
                _dotOn = true;
                RecDot.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(800))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            }
        }
        else if (_dotOn)
        {
            _dotOn = false;
            RecDot.BeginAnimation(OpacityProperty, null);
            RecDot.Opacity = 0;
        }
    }

    private void PollPrivacy()
    {
        if (!_cfg.MicCamera)
        {
            _micOn = _camOn = false;
            UpdateDot();
            return;
        }
        var mic = InUse("microphone");
        var cam = InUse("webcam");
        string mk = string.Join("|", mic), ck = string.Join("|", cam);
        if (_ready)
        {
            if (mk != _micKey && mic.Count > 0) ShowToast("\uE720", "Микрофон: " + mic[0], null, "mic");
            if (ck != _camKey && cam.Count > 0) ShowToast("\uE722", "Камера: " + cam[0], null, "cam");
        }
        bool micWas = _micOn;
        _micKey = mk; _camKey = ck;
        _micOn = mic.Count > 0; _camOn = cam.Count > 0;
        _micApp = mic.Count > 0 ? mic[0] : "";
        if (micWas != _micOn) UpdateBubble();   // пузырь звонка с кнопкой микрофона (MicToggle.cs)
        UpdateDot();
    }

    private MMDevice? _mic;
    private bool _lastMicMute;

    private void InitMicMute()
    {
        try
        {
            var en = new MMDeviceEnumerator();
            _mic = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            _lastMicMute = _mic.AudioEndpointVolume.Mute;
            _mic.AudioEndpointVolume.OnVolumeNotification += d =>
                Dispatcher.BeginInvoke(() =>
                {
                    if (d.Muted == _lastMicMute) return;
                    _lastMicMute = d.Muted;
                    UpdateMicUi();
                    if (_ready && _cfg.KeyToasts)
                        ShowToast(d.Muted ? "mic-off" : "mic", d.Muted ? "Микрофон выключен" : "Микрофон включён", null, "mic");
                });
        }
        catch (Exception ex) { App.Log("MicMute.Init", ex); }
    }

    // ============================================================
    //  CAPS LOCK, NUM LOCK
    // ============================================================
    private readonly DispatcherTimer _keyTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _caps, _num;

    private void PollKeys()
    {
        bool caps = (GetKeyState(0x14) & 1) != 0, num = (GetKeyState(0x90) & 1) != 0;
        if (caps != _caps)
        {
            _caps = caps;
            if (_ready && _cfg.KeyToasts) ShowToast("arrow-big-up", caps ? "Caps Lock включён" : "Caps Lock выключен", null, "key");
        }
        if (num != _num)
        {
            _num = num;
            if (_ready && _cfg.KeyToasts) ShowToast("hash", num ? "Num Lock включён" : "Num Lock выключен", null, "key");
        }
    }

    // ============================================================
    //  WI-FI И BLUETOOTH
    // ============================================================
    private string _netKey = "";
    private DeviceWatcher? _btw;
    private readonly Dictionary<string, string> _btNames = new();
    private bool _btReady;

    private static string NetKey()
    {
        try
        {
            var p = NetworkInformation.GetInternetConnectionProfile();
            if (p == null) return "none";
            var lvl = p.GetNetworkConnectivityLevel();
            if (lvl == NetworkConnectivityLevel.None || lvl == NetworkConnectivityLevel.LocalAccess) return "none";
            return (p.IsWlanConnectionProfile ? "wifi:" : "eth:") + p.ProfileName;
        }
        catch { return "none"; }
    }

    private void InitNetwork()
    {
        try
        {
            _netKey = NetKey();
            NetworkInformation.NetworkStatusChanged += _ => Dispatcher.BeginInvoke(new Action(OnNet));
        }
        catch (Exception ex) { App.Log("Network.Init", ex); }
    }

    private void OnNet()
    {
        string k = NetKey();
        if (k == _netKey) return;
        _netKey = k;
        if (!_ready || !_cfg.NetworkToasts) return;
        if (k == "none") ShowToast("wifi-off", "Нет подключения", null, "net");
        else if (k.StartsWith("wifi:")) ShowToast("\uE701", "Wi-Fi: " + k[5..], null, "net");
        else ShowToast("\uE839", "Сеть: " + k[4..], null, "net");
    }

    private void InitBluetooth()
    {
        try
        {
            var sel = BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            _btw = DeviceInformation.CreateWatcher(sel);
            _btw.Added += (w, d) =>
            {
                _btNames[d.Id] = d.Name;
                if (_btReady) Dispatcher.BeginInvoke(() => ToastBt(d.Name, true));
            };
            _btw.Removed += (w, u) =>
            {
                if (_btNames.TryGetValue(u.Id, out var n))
                {
                    _btNames.Remove(u.Id);
                    if (_btReady) Dispatcher.BeginInvoke(() => ToastBt(n, false));
                }
            };
            _btw.EnumerationCompleted += (w, o) => _btReady = true;
            _btw.Start();
        }
        catch (Exception ex) { App.Log("Bluetooth.Init", ex); }
    }

    private void ToastBt(string name, bool connected)
    {
        if (_ready && _cfg.NetworkToasts)
            ShowToast("\uE702", (connected ? "Подключено: " : "Отключено: ") + name, null, "bt");
    }

    // ============================================================
    //  СКРИНШОТЫ И ЗАПИСЬ ЭКРАНА
    // ============================================================
    private readonly List<FileSystemWatcher> _capW = new();
    private readonly Dictionary<string, (DateTime first, DateTime last)> _recFiles = new();
    private readonly DispatcherTimer _recTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private void InitCaptures()
    {
        try
        {
            string pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string vids = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            WatchCapture(Path.Combine(pics, "Screenshots"), "*.png", true);
            WatchCapture(Path.Combine(vids, "Screen Recordings"), "*.mp4", false);
            WatchCapture(Path.Combine(vids, "Captures"), "*.mp4", false);
            _recTimer.Tick += (_, _) => RecTick();
            _recTimer.Start();
        }
        catch (Exception ex) { App.Log("Captures.Init", ex); }
    }

    private void WatchCapture(string dir, string filter, bool shot)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            var w = new FileSystemWatcher(dir, filter)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            if (shot) w.Created += (s, e) => OnShot(e.FullPath);
            else { w.Created += (s, e) => RecTouch(e.FullPath); w.Changed += (s, e) => RecTouch(e.FullPath); }
            _capW.Add(w);
        }
        catch (Exception ex) { App.Log("Captures.Watch", ex); }
    }

    private void OnShot(string path)
    {
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            await Task.Delay(400); // файл ещё дописывается
            if (!_ready || !_cfg.Captures) return;
            ShowNotif("Скриншот", "Скриншот сохранён. Клик: в карман", Path.GetFileName(path));
            _notifClick = () => AddToPocket(path);
        }));
    }

    private void RecTouch(string path)
    {
        lock (_recFiles)
        {
            var now = DateTime.UtcNow;
            _recFiles[path] = _recFiles.TryGetValue(path, out var v) ? (v.first, now) : (now, now);
        }
    }

    private void RecTick()
    {
        if (!_cfg.Captures)
        {
            if (_recActive) { _recActive = false; UpdateDot(); }
            return;
        }
        var done = new List<string>();
        bool active = false;
        lock (_recFiles)
        {
            var now = DateTime.UtcNow;
            foreach (var kv in _recFiles)
            {
                if ((now - kv.Value.last).TotalSeconds > 3) done.Add(kv.Key);
                else if ((kv.Value.last - kv.Value.first).TotalSeconds >= 2) active = true;
            }
            foreach (var d in done) _recFiles.Remove(d);
        }
        if (active && !_recActive)
        {
            _recActive = true; UpdateDot();
            if (_ready) ShowToast("\uE714", "Идёт запись экрана", null, "cam");
        }
        else if (!active && _recActive) { _recActive = false; UpdateDot(); }
        foreach (var p in done)
        {
            if (!_ready) continue;
            ShowNotif("Запись экрана", "Запись сохранена. Клик: в карман", Path.GetFileName(p));
            _notifClick = () => AddToPocket(p);
        }
    }

    private void AddToPocket(string path)
    {
        _files.Remove(path);
        _files.Insert(0, path);
        SaveFiles();
        _tab = 0; _pocketOpen = true; _actionsOpen = false; _devicesOpen = false; _timerOpen = false; _mixerOpen = false; _centerOpen = false; _trOpen = false; _autoOpened = false;
        RenderPocket();
        ApplyMode(Rest());
    }

    // ============================================================
    //  БАТАРЕЯ И ЗАРЯДКА
    // ============================================================
    private readonly DispatcherTimer _batTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool? _online;
    private int _lastLow = 101;

    private void PollBattery()
    {
        try
        {
            var rep = Windows.Devices.Power.Battery.AggregateBattery.GetReport();
            if (rep.Status == Windows.System.Power.BatteryStatus.NotPresent) return; // настольный ПК
            bool online = SystemParameters.PowerLineStatus == PowerLineStatus.Online;
            int pct = -1;
            if (rep.FullChargeCapacityInMilliwattHours.HasValue && rep.RemainingCapacityInMilliwattHours.HasValue &&
                rep.FullChargeCapacityInMilliwattHours.Value > 0)
                pct = (int)(100.0 * rep.RemainingCapacityInMilliwattHours.Value / rep.FullChargeCapacityInMilliwattHours.Value);
            if (_online == null) { _online = online; return; }
            if (online != _online)
            {
                _online = online;
                if (_ready && _cfg.Battery)
                    ShowToast("zap", (online ? "Зарядка подключена" : "Зарядка отключена") + (pct >= 0 ? $", {pct}%" : ""), null, "bat");
            }
            if (!online && pct >= 0 && _ready && _cfg.Battery)
            {
                if (pct <= 10 && _lastLow > 10) { _lastLow = 10; ShowToast("triangle-alert", $"Заряд {pct}%. Подключи зарядку", null, "bat"); }
                else if (pct <= 20 && _lastLow > 20) { _lastLow = 20; ShowToast("triangle-alert", $"Заряд {pct}%", null, "bat"); }
            }
            if (pct > 25 || online) _lastLow = 101;
        }
        catch (Exception ex) { App.LogOnce("Battery", ex); }
    }

    // ============================================================
    //  ЗАГРУЗКИ В БРАУЗЕРЕ
    // ============================================================
    private FileSystemWatcher? _dlw;
    private readonly HashSet<string> _dlShown = new();
    private static readonly string[] PartExt = { ".crdownload", ".part", ".download", ".opdownload" };

    private static string FmtSize(long b) =>
        b >= 1_000_000_000 ? (b / 1e9).ToString("0.0", Loc.Culture) + Loc.T(" ГБ") :
        b >= 1_000_000 ? (b / 1e6).ToString("0.0", Loc.Culture) + Loc.T(" МБ") :
        Math.Max(1, b / 1000) + Loc.T(" КБ");

    private void InitDownloads()
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(dir)) return;
            _dlw = new FileSystemWatcher(dir) { NotifyFilter = NotifyFilters.FileName, EnableRaisingEvents = true };
            _dlw.Created += (s, e) => DlStart(e.Name ?? "", e.FullPath);
            _dlw.Renamed += (s, e) =>
            {
                string oldExt = Path.GetExtension(e.OldName ?? "").ToLowerInvariant();
                string newExt = Path.GetExtension(e.Name ?? "").ToLowerInvariant();
                if (PartExt.Contains(oldExt) && !PartExt.Contains(newExt)) DlDone(e.Name ?? "", e.FullPath);
                else if (PartExt.Contains(newExt)) DlStart(e.Name ?? "", e.FullPath);
            };
        }
        catch (Exception ex) { App.Log("Downloads.Init", ex); }
    }

    private void DlStart(string name, string fullPath)
    {
        if (!PartExt.Contains(Path.GetExtension(name).ToLowerInvariant())) return;
        string b = Path.GetFileNameWithoutExtension(name);
        if (b.StartsWith("Unconfirmed", StringComparison.OrdinalIgnoreCase)) return;
        lock (_dlShown) { if (!_dlShown.Add(b)) return; }
        Dispatcher.BeginInvoke(() => WebDlStart(b, fullPath));   // шаг 65: живая активность с волной (Context.cs)
        Dispatcher.BeginInvoke(() => { if (_ready && _cfg.Downloads) ShowToast("\uE896", "Скачивание: " + b, null, "dl"); });
    }

    private void DlDone(string name, string path)
    {
        lock (_dlShown) _dlShown.Remove(name);
        long size = 0;
        try { size = new FileInfo(path).Length; } catch { }
        Dispatcher.BeginInvoke(() =>
        {
            WebDlDone(name);
            if (_ready && _cfg.Downloads)
                ShowToast("\uE73E", "Загружено: " + name + (size > 0 ? ", " + FmtSize(size) : ""), null, "dl");
        });
    }
}
