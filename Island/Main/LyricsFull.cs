using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Island;

// Слова песни в развёрнутом острове: строка, которая поётся, и следующая.
public partial class MainWindow
{
    // ============================================================
    //  СЛОВА В РАЗВЁРНУТОМ ОСТРОВЕ: две строки (поётся сейчас и следующая)
    // ============================================================
    private bool _fullLyrOn;
    private int _fIdx = -2;
    private LinearGradientBrush? _fWave;
    private double _fWaveW;
    private double _fWaveA = -9, _fWaveB = -9;
    private readonly List<(double t0, double t1, double x0, double x1)> _fWords = new();
    private const double FullLyrH = 44;
    private bool _fullLyrGap;   // шаг 52: проигрыш без слов дольше 3 с — место под строку схлопывается

    // содержимое развёрнутого острова ровно 134 px + по 18 px полей сверху и снизу: подбородка больше нет
    private double FullH() => 170 + (_fullLyrOn && !_fullLyrGap ? FullLyrH + 8 : 0) + AppsFullH;   // + ряд значков приложений (AppsBar.cs)

    private void UpdateFullLyrVisibility()
    {
        bool on = _lyricMode && _cfg.LyricsInFull;
        if (on == _fullLyrOn) return;
        _fullLyrOn = on;
        _fullLyrGap = false;
        FullLyr.Margin = new Thickness(0, 8, 0, 0);
        _fIdx = -2;
        if (on)
        {
            FullLyr.Visibility = Visibility.Visible;
            FullLyr.BeginAnimation(HeightProperty, new DoubleAnimation(0, FullLyrH, TimeSpan.FromMilliseconds(260))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        else
        {
            var a = new DoubleAnimation(FullLyrH, 0, TimeSpan.FromMilliseconds(200));
            a.Completed += (_, _) => { if (!_fullLyrOn) FullLyr.Visibility = Visibility.Collapsed; };
            FullLyr.BeginAnimation(HeightProperty, a);
        }
        Retarget(); // остров вытягивается вниз или возвращается к прежней высоте
    }

    private void FullLyrTick(int idx, double t)
    {
        if (!_fullLyrOn || _lyr == null) return;
        if (_mode != Mode.Full) { _fIdx = -2; return; } // свёрнут: ничего не рисуем, при раскрытии строки покажутся заново
        if (idx != _fIdx)
        {
            bool animate = _fIdx != -2;
            _fIdx = idx;
            SetFullLines(idx, animate);
        }
        if (_fWave != null) UpdateFullWave(t);
        else if (_fullLetters) FullSw.Update(t);
    }

    private void SetFullLines(int idx, bool animate)
    {
        var lines = _lyr!;
        // шаг 76: без метки голоса «v2:», «Имя:» (Services\Duet.cs)
        string cur = idx >= 0 ? Duet.Strip(lines[idx].s, _curArtist) : "";
        string nxt = idx + 1 < lines.Count ? Duet.Strip(lines[idx + 1].s, _curArtist) : "";
        if (idx < 0) nxt = lines.Count > 0 ? Duet.Strip(lines[0].s, _curArtist) : "";
        bool blank = string.IsNullOrWhiteSpace(cur);
        bool interlude = !blank && LyricsEngine.IsInterlude(cur);
        // пауза без слов: долгая — прячем место под строки, короткая (меньше 3 с) — оставляем, чтобы остров не дёргался
        double gapStart = idx >= 0 ? lines[idx].t : 0;
        double gapEnd = idx + 1 < lines.Count ? lines[idx + 1].t : double.MaxValue;
        if (idx < 0) gapEnd = lines.Count > 0 ? lines[0].t : double.MaxValue;
        SetLyrGap((blank || interlude) && gapEnd - gapStart >= 3);
        double avail = Math.Max(120, _fullW - 40);

        // прошлая строка уходит вверх и гаснет
        string prevText = FullL1.Text;
        bool letters = IslandLetters;
        if (animate && !letters && !string.IsNullOrWhiteSpace(prevText) && prevText != cur)
        {
            FullL0.Text = prevText;
            FullL0.FontSize = FullL1.FontSize;
            FullL0.BeginAnimation(OpacityProperty, new DoubleAnimation(0.6, 0, TimeSpan.FromMilliseconds(320)));
            Get(FullL0).T.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -18, TimeSpan.FromMilliseconds(320))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        }

        // текущая строка: подбираем размер, чтобы влезла целиком
        double fs = 15, tw = TextW(cur, fs, FontWeights.SemiBold);
        if (tw > avail)
        {
            fs = Math.Clamp(15 * avail / tw, 11, 15);
            tw = TextW(cur, fs, FontWeights.SemiBold);
        }
        FullL1.FontSize = fs;
        FullL1.Text = blank ? " " : cur;
        FullL1.MaxWidth = avail;

        _fWords.Clear();
        _fWave = null;
        _fWaveA = _fWaveB = -9;
        FullL1.Foreground = Brushes.White;
        if (!blank && !interlude && _cfg.LyricsKaraoke && !letters)
        {
            var parts = cur.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            double t0 = lines[idx].t;
            double t1 = idx + 1 < lines.Count ? lines[idx + 1].t : t0 + 4;
            double win = LyricsEngine.LineWindow(cur, t0, t1);
            var wts = LyricsEngine.WordWeights(parts);
            double total = wts.Sum(), acc = 0;
            string prefix = "";
            _fWaveW = Math.Max(10, tw);
            for (int k = 0; k < parts.Length; k++)
            {
                double w = wts[k] / total * win;
                double x0 = TextW(prefix, fs, FontWeights.SemiBold);
                prefix += parts[k] + " ";
                double x1 = TextW(prefix.TrimEnd(), fs, FontWeights.SemiBold);
                _fWords.Add((t0 + acc, t0 + acc + Math.Max(w, 0.18), x0, x1));
                acc += w;
            }
            double len = _fWaveW + 8;
            _fWave = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(0, 0),
                EndPoint = new Point(len, 0)
            };
            _fWave.GradientStops.Add(new GradientStop(Colors.White, 0));
            _fWave.GradientStops.Add(new GradientStop(Colors.White, 0));
            _fWave.GradientStops.Add(new GradientStop(DimC, 0));
            _fWave.GradientStops.Add(new GradientStop(DimC, 1));
            FullL1.Foreground = _fWave;
            UpdateFullWave(-1);
        }
        FullL1.Effect = null;
        if (GlowFx && !blank && !letters) GlowOn(FullGlow, FullL1, 0.75, 7, animate);
        else GlowOff(FullGlow);

        // следующая строка: бледная и мелкая
        FullL2.Text = nxt;
        FullL2.MaxWidth = avail;

        if (letters)
        {
            // по буквам: старая строка улетает, новая опускается и загорается волной
            FullL1.Effect = null;
            FullLetterLine(prevText, idx, cur, blank, interlude, animate);
            FullL0.Opacity = 0;
            if (animate)
            {
                FullL2.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)));
                Get(FullL2).T.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(460))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            return;
        }

        if (animate)
        {
            // следующая «поднимается» на место текущей
            FullL1.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(360)));
            var tx = Get(FullL1);
            FullL1.RenderTransformOrigin = new Point(0, 0.5);
            tx.T.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(520))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } });
            var sc = new DoubleAnimation(0.84, 1, TimeSpan.FromMilliseconds(520))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } };
            tx.S.BeginAnimation(ScaleTransform.ScaleXProperty, sc);
            tx.S.BeginAnimation(ScaleTransform.ScaleYProperty, sc);
            if (FullGlow.Effect != null)
            {
                var gx = Get(FullGlow);
                FullGlow.RenderTransformOrigin = new Point(0, 0.5);
                gx.T.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(520))
                { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } });
                gx.S.BeginAnimation(ScaleTransform.ScaleXProperty, sc);
                gx.S.BeginAnimation(ScaleTransform.ScaleYProperty, sc);
            }
            // снизу появляется новая бледная
            FullL2.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)));
            Get(FullL2).T.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(460))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        else
        {
            FullL0.Opacity = 0;
        }
    }

    private void SetLyrGap(bool gap)
    {
        if (gap == _fullLyrGap) return;
        _fullLyrGap = gap;
        FullLyr.BeginAnimation(HeightProperty, new DoubleAnimation(gap ? 0 : FullLyrH, TimeSpan.FromMilliseconds(gap ? 240 : 300))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        FullLyr.Margin = new Thickness(0, gap ? 0 : 8, 0, 0);
        Retarget();   // остров становится ниже или возвращается
    }

    private void UpdateFullWave(double t)
    {
        if (_fWave == null || _fWords.Count == 0) return;
        double x;
        if (t <= _fWords[0].t0) x = 0;
        else if (t >= _fWords[^1].t1) x = _fWaveW;
        else
        {
            x = _fWaveW;
            foreach (var w in _fWords)
            {
                if (t > w.t1) continue;
                double fr = Math.Clamp((t - w.t0) / (w.t1 - w.t0), 0, 1);
                x = w.x0 + fr * (w.x1 - w.x0);
                break;
            }
        }
        double len = _fWaveW + 8;
        double cx = -WaveFeather + x / _fWaveW * (_fWaveW + 2 * WaveFeather);
        double fa = Math.Clamp((cx - WaveFeather) / len, 0, 1);
        double fb = Math.Clamp((cx + WaveFeather) / len, 0, 1);
        if (Math.Abs(fa - _fWaveA) < 0.0015 && Math.Abs(fb - _fWaveB) < 0.0015) return;
        _fWaveA = fa; _fWaveB = fb;
        _fWave.GradientStops[1].Offset = fa;
        _fWave.GradientStops[2].Offset = fb;
    }
}
