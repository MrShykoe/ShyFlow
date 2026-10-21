using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Island;

// Шаг 16: текст в самом острове по буквам (отдельный выключатель «По буквам в острове»).
// Смена названия и исполнителя, строка слов в свёрнутом острове и текущая строка в развёрнутом.
public partial class MainWindow
{
    private LetterSwap? _miniSw, _titleSw, _artistSw, _fullSw;
    private LetterSwap MiniSw => _miniSw ??= new LetterSwap(MiniTitle, MiniFx);
    private LetterSwap TitleSw => _titleSw ??= new LetterSwap(TitleText, TitleFx);
    private LetterSwap ArtistSw => _artistSw ??= new LetterSwap(ArtistText, ArtistFx);
    private LetterSwap FullSw => _fullSw ??= new LetterSwap(FullL1, FullFx);

    private bool _swM, _swT, _swA;                // сработала ли побуквенная смена (иначе остаётся обычный «слайд»)
    private bool _miniLetters, _fullLetters;      // строка слов сейчас рисуется живыми буквами
    private bool _lettersApplied;

    private bool IslandLetters => _cfg.IslandLetters && !_eco;

    // новый трек: название и исполнитель меняются по буквам
    private void LettersOnTrack(string oldTitle, string oldArtist, string title, string artist)
    {
        _swM = _swT = _swA = false;
        if (!IslandLetters || oldTitle == "") return;
        if (!_clockMode && !_lyricMode) _swM = MiniSw.Run(oldTitle, title, 0, 2000);
        _swT = TitleSw.Run(oldTitle, title);
        _swA = ArtistSw.Run(oldArtist, artist, 90);
    }

    // выключили в настройках или вышли из режима слов: везде возвращается обычный текст
    private void LettersOff()
    {
        _miniLetters = _fullLetters = false;
        _miniSw?.Cancel();
        _fullSw?.Cancel();
        MiniFx.Effect = null;
        FullFx.Effect = null;
    }

    // вызывается из ApplyFeatureSettings
    private void LettersApplySettings()
    {
        if (_lettersApplied == IslandLetters) return;
        _lettersApplied = IslandLetters;
        LettersOff();
        _titleSw?.Cancel();
        _artistSw?.Cancel();
        if (_lyricMode) { _lyrIdx = -2; _fIdx = -2; } // строки перерисуются в новом режиме
    }

    private static DropShadowEffect Glow(Color c, double blur, double op)
    {
        var g = new DropShadowEffect { Color = c, BlurRadius = blur, ShadowDepth = 0, Opacity = 0 };
        g.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0, op, TimeSpan.FromMilliseconds(500)));
        return g;
    }

    // строка слов в свёрнутом острове. false: побуквенно не вышло, вызывающий делает обычный переход
    private bool MiniLetterLine(string prevText, int idx, string line)
    {
        bool blank = string.IsNullOrWhiteSpace(line);
        string shown = blank ? _curTitle : line;
        if (blank && prevText == shown) return true; // название уже на экране: заново его не анимируем
        double[]? starts = null;
        double smooth = 0.5;
        if (!blank && _cfg.LyricsKaraoke && !LyricsEngine.IsInterlude(line) && _lyr != null && idx >= 0)
        {
            double t0 = _lyr[idx].t;
            double t1 = idx + 1 < _lyr.Count ? _lyr[idx + 1].t : t0 + 4;
            starts = LetterTiming.Flat(line, t0, LyricsEngine.LineWindow(line, t0, t1), out smooth);
        }
        string oldText = prevText == shown ? "" : prevText;
        bool ok = MiniSw.Run(oldText, shown, 0, 2000, starts, smooth, 3.5);
        _miniLetters = ok && starts != null;
        if (!ok) { MiniSw.Cancel(); MiniFx.Effect = null; return false; }
        MiniFx.Effect = _miniLetters && GlowFx ? Glow(_accent, 14, 0.6) : null;
        return true;
    }

    // текущая строка слов в развёрнутом острове
    private void FullLetterLine(string prevText, int idx, string cur, bool blank, bool interlude, bool animate)
    {
        _fullLetters = false;
        FullFx.Effect = null;
        double[]? starts = null;
        double smooth = 0.5;
        if (!blank && !interlude && _cfg.LyricsKaraoke && _lyr != null && idx >= 0)
        {
            double t0 = _lyr[idx].t;
            double t1 = idx + 1 < _lyr.Count ? _lyr[idx + 1].t : t0 + 4;
            starts = LetterTiming.Flat(cur, t0, LyricsEngine.LineWindow(cur, t0, t1), out smooth);
        }
        string target = blank ? "" : cur;
        string oldText = !animate || string.IsNullOrWhiteSpace(prevText) || prevText == target ? "" : prevText;
        if (blank && oldText == "") { FullSw.Cancel(); return; } // пустая строка: старая просто улетает, если была
        bool ok = FullSw.Run(oldText, target, 0, Math.Max(120, _fullW - 40), starts, smooth, 4.5, animate);
        if (!ok) { FullSw.Cancel(); return; }
        _fullLetters = starts != null;
        if (_fullLetters && GlowFx) FullFx.Effect = Glow(_accent, 12, 0.55);
    }
}
