using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using NAudio.CoreAudioApi;
using Windows.Media.Control;

namespace Island;

// Связь острова с полноэкранным плеером: плеер сам ничего не знает про остров,
// он только читает текущий трек, обложку, слова и управляет воспроизведением.
public partial class MainWindow
{
    private PlayerWindow? _player;

    internal string CurTitle => _curTitle;
    internal string CurArtist => _curArtist;
    internal BitmapSource? CurCover => _lastCover;
    internal List<(double t, string s)>? CurLyrics => _lyr;
    internal List<WordTimes?>? CurWords => _lyrWords;   // точное время слов (того же размера, что CurLyrics) или null
    internal bool IsPlaying => _playing;
    internal double LyricOffsetNow => _lyrOffset;
    // в экономном режиме тяжёлые эффекты выключены, даже если включены в настройках (см. Performance.cs)
    internal bool LyricBlur => _cfg.LyricsBlur && !_eco;
    internal bool LyricGlow => _cfg.LyricsGlow && !_eco;
    internal System.Windows.Media.Color AccentNow => _accent;
    internal GlobalSystemMediaTransportControlsSession? CurSession => _session;
    internal MMDevice? VolumeDevice => _dev;
    internal double[] EqLevels => _bp;
    internal bool PlayerViz => _cfg.PlayerViz && !_eco;
    internal bool PlayerGlow => _cfg.PlayerGlow && !_eco;
    internal bool LetterAnim => _cfg.LetterAnim && !_eco;
    internal bool CoverFlip => _cfg.CoverFlip;
    internal bool Cover3D => _cfg.Cover3D && !_eco;
    internal bool Eco => _eco;
    internal bool SeekClick => _cfg.SeekClick;
    internal bool WaveSeek => _cfg.WaveSeek;

    // направление последнего переключения (для анимации смены трека)
    internal int SkipDirNow => (DateTime.UtcNow - _skipAt).TotalSeconds < 4 ? _skipDir : 1;
    internal void NoteSkip(int dir) { _skipDir = dir; _skipAt = DateTime.UtcNow; }

    private void Expand_Click(object s, RoutedEventArgs e)
    {
        if (s is FrameworkElement b) Pop(b, 0.7, 450);
        OpenPlayer();
    }

    // клик по обложке в развёрнутом острове открывает плеер на весь экран (как кнопка справа)
    private void CoverBig_Click(object s, MouseButtonEventArgs e)
    {
        e.Handled = true;   // остров при этом не сворачивается
        Pop(CoverBig, 0.85, 450);
        OpenPlayer();
    }

    internal void OpenPlayer()
    {
        if (_player != null) { _player.Activate(); return; }
        _player = new PlayerWindow(this);
        _player.Show();
        HideForPlayer(true);
        if (_playing) StartCapture(); // звук для визуализатора нужен, пока открыт плеер
    }

    internal void OnPlayerClosed()
    {
        _player = null;
        HideForPlayer(false);
        if (!NeedCapture()) StopCapture();
    }

    // Пока открыт плеер на весь экран, остров уезжает вверх (как Ctrl+Alt+I) и возвращается, когда плеер закрыли.
    // Если остров уже был спрятан руками, он так и остаётся спрятанным. Ctrl+Alt+I при открытом плеере возвращает его.
    private bool _hiddenForPlayer;

    private void HideForPlayer(bool hide)
    {
        if (hide)
        {
            if (_hidden) return;
            _hiddenForPlayer = true;
            ToggleHidden();
        }
        else
        {
            if (_hiddenForPlayer && _hidden) ToggleHidden();
            _hiddenForPlayer = false;
        }
    }

    // сообщение для человека: плашкой в острове, а если остров спрятан под плеером, то подсказкой в самом плеере
    private void Tell(string icon, string text, string kind = "key")
    {
        if (_player != null && _hidden) _player.ShowHint(icon, Loc.T(text));
        else ShowToast(icon, text, null, kind);
    }

    // меню острова (правый клик): «Коллекция карточек»
    private void Collection_Click(object sender, RoutedEventArgs e) => CollectionWindow.ShowCollection();

    // стикер из острова (гифка, картинка, видео) для обратной стороны обложки в плеере
    internal IReadOnlyList<BitmapSource> StickerFrames => _gifFrames;
    internal IReadOnlyList<int> StickerDelays => _gifDelays;
}
