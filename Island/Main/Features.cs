using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Island;

// Запуск всех дополнительных возможностей острова и применение их настроек.
public partial class MainWindow
{
    private static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island");

    [DllImport("user32.dll")] private static extern short GetKeyState(int vk);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam,
        uint flags, uint timeout, out UIntPtr result);

    private void StartFeatures()
    {
        _clockTimer.Tick += (_, _) => { if (_clockMode) TickClock(); UpdateMiniProgress(); };
        _clockTimer.Start();

        _privTimer.Tick += (_, _) => PollPrivacy();
        _privTimer.Start();

        _caps = (GetKeyState(0x14) & 1) != 0;
        _num = (GetKeyState(0x90) & 1) != 0;
        _keyTimer.Tick += (_, _) => PollKeys();
        _keyTimer.Start();

        _lyrTimer.Tick += (_, _) => LyricTick();

        InitMicMute();
        InitNetwork();
        InitBluetooth();
        InitDownloads();
        InitCopyProgress();
        InitCaptures();
        _batTimer.Tick += (_, _) => PollBattery();
        _batTimer.Start();

        LoadOffsets();
        LoadNotes();
        _noteTimer.Tick += (_, _) => NoteReminder();
        _noteTimer.Start();

        BuildActions();
        ApplyFeatureSettings();
        StartPerformance();
        StartTimers();
        StartApps();
    }

    private void ApplyFeatureSettings()
    {
        ApplyTheme();
        RegisterHotkeys();
        if (_lyricMode)
        {
            bool hasLine = _lyr != null && _lyrIdx >= 0 && _lyrIdx < _lyr.Count && !string.IsNullOrWhiteSpace(_lyr[_lyrIdx].s);
            if (GlowFx && hasLine && !IslandLetters) GlowOn(MiniGlow, MiniTitle, 0.8, 7, false);
            else GlowOff(MiniGlow);
        }
        ApplyGif();
        ApplyIslandBg();
        if (_appsTimer.IsEnabled) { _appsSig = ""; ScanApps(); }   // поменяли, где показывать значки приложений
        ApplyFont();
        if (!_cfg.Lyrics && _lyricMode) SetLyricMode(false);
        else if (_cfg.Lyrics && !_lyricMode && _curTitle != "") _ = LoadLyricsAsync(_curTitle, _curArtist);
        LettersApplySettings();
        if (_session == null) EnterIdle();
    }
}
