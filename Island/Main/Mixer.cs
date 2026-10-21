using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.CoreAudioApi;

namespace Island;

// Громкость каждой программы (Ctrl+Alt+M или плитка «Микшер»): как микшер громкости Windows, только в острове.
// Сверху общая громкость, ниже программы, которые сейчас выводят звук (Discord, браузер, игра...).
// У одной программы бывает несколько звуковых сессий (вкладки браузера): они собираются в одну строку.
public partial class MainWindow
{
    private bool _mixerOpen;
    private const int MixerRowsMax = 5;
    private const double MixerRowH = 44;
    private readonly DispatcherTimer _mixTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<string, BitmapSource?> _appIcons = new(StringComparer.OrdinalIgnoreCase);
    private string _mixKeys = "";

    private sealed class MixApp
    {
        public string Key = "", Name = "", Path = "";
        public bool System;
        public readonly List<AudioSessionControl> Sessions = new();
    }

    private sealed class MixRow
    {
        public MiniSlider Slider = null!;
        public TextBlock Pct = null!;
        public VIcon MuteIcon = null!;
        public Func<(double vol, bool mute)> Read = null!;
    }
    private readonly List<MixRow> _mixRows = new();

    private double MixerH() => 12 + 32 + 6 + Math.Max(1, Math.Min(MixerRowsMax, MixerList.Children.Count)) * MixerRowH + 12;

    private void ToggleMixer()
    {
        if (_mixerOpen) { _mixerOpen = false; _centerOpen = false; _trOpen = false; ApplyMode(Rest()); return; }
        OpenMixer();
    }

    private void OpenMixer()
    {
        BuildMixer();
        _mixerOpen = true; _timerOpen = false; _actionsOpen = false; _pocketOpen = false; _devicesOpen = false;
        ApplyMode(Rest());
        if (!_mixTimer.IsEnabled)
        {
            _mixTimer.Tick -= MixTick;
            _mixTimer.Tick += MixTick;
            _mixTimer.Start();
        }
    }

    private void MixerClose_Click(object s, RoutedEventArgs e) { _mixerOpen = false; _centerOpen = false; _trOpen = false; ApplyMode(Rest()); }

    // раз в секунду, пока микшер открыт: новые программы появляются, ушедшие пропадают, ползунки догоняют изменения извне
    private void MixTick(object? s, EventArgs e)
    {
        if (_mode != Mode.Mixer) { _mixTimer.Stop(); return; }
        try
        {
            if (SessionKeys() != _mixKeys) { BuildMixer(); Go(440, MixerH(), 30); return; }
            foreach (var r in _mixRows)
            {
                if (r.Slider.Dragging) continue;
                var (v, m) = r.Read();
                if (Math.Abs(v - r.Slider.Value) > 0.005) r.Slider.Value = v;
                SetRowText(r, v, m);
            }
        }
        catch (Exception ex) { App.LogOnce("Mixer.Tick", ex); }
    }

    private List<MixApp> ReadApps()
    {
        var apps = new Dictionary<string, MixApp>(StringComparer.OrdinalIgnoreCase);
        var dev = _dev;
        if (dev == null) return new List<MixApp>();
        var mgr = dev.AudioSessionManager;
        mgr.RefreshSessions();
        var all = mgr.Sessions;
        for (int i = 0; i < all.Count; i++)
        {
            var ses = all[i];
            try
            {
                if (ses.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired) continue;
                bool sys = ses.IsSystemSoundsSession;
                uint pid = ses.GetProcessID;
                string path = sys ? "" : ProcessPath(pid);
                string key = sys ? "#system" : path.Length > 0 ? path : "pid:" + pid;
                if (!apps.TryGetValue(key, out var app))
                {
                    app = new MixApp { Key = key, Path = path, System = sys, Name = sys ? Loc.T("Системные звуки") : AppName(pid, path, ses.DisplayName) };
                    apps[key] = app;
                }
                app.Sessions.Add(ses);
            }
            catch (Exception ex) { App.LogOnce("Mixer.Session", ex); }
        }
        // программы по имени, системные звуки в конце
        return apps.Values.OrderBy(a => a.System).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private string SessionKeys()
    {
        try { return string.Join("|", ReadApps().Select(a => a.Key)); }
        catch { return _mixKeys; }
    }

    private void BuildMixer()
    {
        MixerList.Children.Clear();
        _mixRows.Clear();
        if (_dev == null)
        {
            MixerList.Children.Add(new TextBlock { Text = Loc.T("Нет устройства вывода звука"), Foreground = Brushes.Gray, FontSize = 13, Margin = new Thickness(4, 12, 0, 0) });
            _mixKeys = "";
            return;
        }
        var ev = _dev.AudioEndpointVolume;
        AddMixRow(null, Loc.T("Общая громкость"), true,
            () => (ev.MasterVolumeLevelScalar, ev.Mute),
            v => ev.MasterVolumeLevelScalar = (float)v,
            m => ev.Mute = m);
        List<MixApp> apps;
        try { apps = ReadApps(); }
        catch (Exception ex) { App.Log("Mixer.Read", ex); apps = new(); }
        _mixKeys = string.Join("|", apps.Select(a => a.Key));
        foreach (var app in apps)
        {
            var list = app.Sessions;
            AddMixRow(app, app.Name, false,
                () => (list[0].SimpleAudioVolume.Volume, list[0].SimpleAudioVolume.Mute),
                v => { foreach (var x in list) x.SimpleAudioVolume.Volume = (float)v; },
                m => { foreach (var x in list) x.SimpleAudioVolume.Mute = m; });
        }
        if (apps.Count == 0)
            MixerList.Children.Add(new TextBlock
            {
                Text = Loc.T("Сейчас ни одна программа не выводит звук"), Foreground = new SolidColorBrush(Color.FromArgb(0x88, 255, 255, 255)),
                FontSize = 12, Margin = new Thickness(40, 2, 0, 0)
            });
    }

    private void AddMixRow(MixApp? app, string name, bool master, Func<(double, bool)> read, Action<double> setVol, Action<bool> setMute)
    {
        var row = new Grid { Height = MixerRowH - 4, Margin = new Thickness(0, 0, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });

        // значок: у программы её иконка, у общей громкости и системных звуков кружок со значком
        FrameworkElement icon;
        var bmp = app != null && app.Path.Length > 0 ? AppIcon(app.Path) : null;
        if (bmp != null)
        {
            var img = new Image { Source = bmp, Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            icon = img;
        }
        else
            icon = new SBorder
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(master ? Colors.White : Color.FromArgb(0x26, 255, 255, 255)),
                Child = new VIcon
                {
                    Text = master ? "volume-2" : app?.System == true ? "bell" : "app-window", FontSize = 12,
                    Foreground = master ? new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E)) : Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
        row.Children.Add(icon);

        var title = new TextBlock
        {
            Text = name, Foreground = Brushes.White, FontSize = 12.5, FontWeight = master ? FontWeights.SemiBold : FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0), ToolTip = name
        };
        Grid.SetColumn(title, 1);
        row.Children.Add(title);

        var r = new MixRow { Read = read };
        r.Slider = new MiniSlider { VerticalAlignment = VerticalAlignment.Center, Accent = _uiBrush };
        Grid.SetColumn(r.Slider, 2);
        row.Children.Add(r.Slider);
        r.Pct = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255)), FontSize = 11.5, TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center, Typography = { NumeralAlignment = FontNumeralAlignment.Tabular }
        };
        Grid.SetColumn(r.Pct, 3);
        row.Children.Add(r.Pct);
        r.MuteIcon = new VIcon { Text = "volume-2", FontSize = 13, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var mute = new Button { Style = (Style)FindResource("Ghost"), Width = 30, Height = 30, Content = r.MuteIcon, ToolTip = Loc.T("Без звука") };
        Grid.SetColumn(mute, 4);
        row.Children.Add(mute);

        try
        {
            var (v, m) = read();
            r.Slider.Value = v;
            SetRowText(r, v, m);
        }
        catch (Exception ex) { App.LogOnce("Mixer.Row", ex); }
        r.Slider.Changed += v =>
        {
            try
            {
                if (master) _tileMuteAt = DateTime.UtcNow;   // своя плашка громкости не нужна: ползунок и так перед глазами
                setVol(v);
                SetRowText(r, v, read().Item2);
            }
            catch (Exception ex) { App.LogOnce("Mixer.Set", ex); }
        };
        mute.Click += (_, _) =>
        {
            try
            {
                var (v, m) = read();
                if (master) _tileMuteAt = DateTime.UtcNow;
                setMute(!m);
                SetRowText(r, v, !m);
                Pop(mute, 0.7, 420);
            }
            catch (Exception ex) { App.LogOnce("Mixer.Mute", ex); }
        };
        _mixRows.Add(r);
        MixerList.Children.Add(row);
    }

    private static void SetRowText(MixRow r, double v, bool mute)
    {
        r.Pct.Text = mute ? "—" : ((int)Math.Round(v * 100)).ToString();
        string icon = mute || v < 0.005 ? "volume-x" : v < 0.5 ? "volume-1" : "volume-2";
        if (r.MuteIcon.Text != icon) r.MuteIcon.Text = icon;
        r.Slider.Opacity = mute ? 0.45 : 1;
    }

    // ---------- имя и значок программы ----------
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, int n);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);

    // путь к exe: так работает и для программ, запущенных от администратора (MainModule там падает)
    private static string ProcessPath(uint pid)
    {
        if (pid == 0) return "";
        var h = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return "";
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : "";
        }
        finally { CloseHandle(h); }
    }

    private static string AppName(uint pid, string path, string? display)
    {
        if (!string.IsNullOrWhiteSpace(display) && !display.StartsWith("@")) return display;
        if (path.Length > 0)
        {
            try
            {
                var d = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(d)) return d.Trim();
            }
            catch { /* нет доступа к файлу: возьмём имя exe */ }
            return Path.GetFileNameWithoutExtension(path);
        }
        try { return Process.GetProcessById((int)pid).ProcessName; }
        catch { return "PID " + pid; }
    }

    private BitmapSource? AppIcon(string path)
    {
        if (_appIcons.TryGetValue(path, out var cached)) return cached;
        BitmapSource? res = null;
        var large = new IntPtr[1];
        try
        {
            if (ExtractIconEx(path, 0, large, null, 1) > 0 && large[0] != IntPtr.Zero)
            {
                res = Imaging.CreateBitmapSourceFromHIcon(large[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                res.Freeze();
            }
        }
        catch (Exception ex) { App.LogOnce("Mixer.Icon", ex); }
        finally { if (large[0] != IntPtr.Zero) DestroyIcon(large[0]); }
        _appIcons[path] = res;
        return res;
    }
}
