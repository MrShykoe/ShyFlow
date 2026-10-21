using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Island;

// Внешний вид: цвет обложки и сдвиг текста, темы, раскладка свёрнутого острова, размытый фон,
// форма обложки, тонкая полоска прогресса, стили эквалайзера.
public partial class MainWindow
{
    // ============================================================
    //  ЦВЕТ ОБЛОЖКИ (для свечения) И СДВИГ ТЕКСТА
    // ============================================================
    internal static Color AccentOf(BitmapSource bmp)
    {
        try
        {
            var small = new TransformedBitmap(bmp, new ScaleTransform(16.0 / bmp.PixelWidth, 16.0 / bmp.PixelHeight));
            var conv = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            conv.CopyPixels(px, stride, 0);
            double r = 0, g = 0, b = 0, wt = 0;
            for (int i = 0; i < px.Length; i += 4)
            {
                double B = px[i] / 255.0, G = px[i + 1] / 255.0, R = px[i + 2] / 255.0;
                double mx = Math.Max(R, Math.Max(G, B)), mn = Math.Min(R, Math.Min(G, B));
                double sat = mx <= 0 ? 0 : (mx - mn) / mx;
                double k = 0.05 + sat * sat * mx; // яркие насыщенные цвета важнее серых
                r += R * k; g += G * k; b += B * k; wt += k;
            }
            r /= wt; g /= wt; b /= wt;
            double m = Math.Max(r, Math.Max(g, b));
            if (m < 0.02) return Colors.White;
            double up = 0.95 / m; // подтягиваем яркость, чтобы свечение было заметно
            r = Math.Min(1, r * up) * 0.8 + 0.2;
            g = Math.Min(1, g * up) * 0.8 + 0.2;
            b = Math.Min(1, b * up) * 0.8 + 0.2;
            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }
        catch { return Colors.White; }
    }

    private void LoadOffsets()
    {
        try
        {
            if (File.Exists(OffsetsFile))
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(OffsetsFile));
                if (d != null) foreach (var kv in d) _offsets[kv.Key] = kv.Value;
            }
        }
        catch (Exception ex) { App.Log("Offsets.Load", ex); }
    }

    private void AdjustLyricOffset(double delta)
    {
        if (_curTitle == "" || (_lyr == null && !_lyricMode)) return;
        _lyrOffset = Math.Round(Math.Clamp(_lyrOffset + delta, -10, 10), 2);
        string key = _curArtist + "|" + _curTitle;
        if (Math.Abs(_lyrOffset) < 0.001) _offsets.Remove(key); else _offsets[key] = _lyrOffset;
        try { SafeFile.WriteAllText(OffsetsFile, JsonSerializer.Serialize(_offsets)); }
        catch (Exception ex) { App.Log("Offsets.Save", ex); }
        _lyrIdx = -2; // пересчитать строку и волну сразу
        string msg = Math.Abs(_lyrOffset) < 0.001
            ? "Текст: без сдвига"
            : "Текст " + (_lyrOffset > 0 ? "позже" : "раньше") + " на " +
              Math.Abs(_lyrOffset).ToString("0.##", Loc.Culture) + " с";
        Tell("clock", msg, "lyr");   // \u043F\u043E\u0434 \u043F\u043B\u0435\u0435\u0440\u043E\u043C \u043D\u0430 \u0432\u0435\u0441\u044C \u044D\u043A\u0440\u0430\u043D \u043E\u0441\u0442\u0440\u043E\u0432 \u0441\u043F\u0440\u044F\u0442\u0430\u043D: \u0442\u043E\u0433\u0434\u0430 \u043F\u043E\u0434\u0441\u043A\u0430\u0437\u043A\u0430 \u0432 \u0441\u0430\u043C\u043E\u043C \u043F\u043B\u0435\u0435\u0440\u0435
    }

    // ============================================================
    //  ВИД ОСТРОВА
    // ============================================================
    // Шаг 60: темы убраны (стекло, контур, свечение, графит, аврора выглядели криво), остров всегда чёрный,
    // без рамки и тени. Метод остался: его зовут при смене обложки и настроек.
    private void ApplyTheme()
    {
        if (Pill.Background is not SolidColorBrush { Color: { A: 255, R: 0, G: 0, B: 0 } }) Pill.Background = Brushes.Black;
        Pill.BorderBrush = null;
        Pill.BorderThickness = new Thickness(0);
        Pill.Effect = AppsBar.Effect = null;
    }

    // ============================================================
    //  РОВНАЯ РАСКЛАДКА СВЁРНУТОГО ОСТРОВА (с эквалайзером и без него)
    // ============================================================
    private bool MiniEqOn => _cfg.Equalizer && !_clockMode;

    // Поля подобраны так, чтобы слева и справа от содержимого было одинаково, нужен эквалайзер или нет.
    private void ApplyMiniLayout()
    {
        // шаг 72: часы по центру острова: если остался лишний пиксель-другой, он делится поровну, а не копится справа
        MiniTitle.HorizontalAlignment = MiniTitleOld.HorizontalAlignment = _clockMode ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        if (_clockMode)
        {
            Mini.Margin = new Thickness(12, 0, 12, 0);
            MiniTitle.Margin = MiniTitleOld.Margin = new Thickness(0);
        }
        else if (MiniEqOn)
        {
            Mini.Margin = new Thickness(8, 0, 12, 0);
            MiniTitle.Margin = MiniTitleOld.Margin = new Thickness(10, 0, 8, 0);
        }
        else
        {
            Mini.Margin = new Thickness(8, 0, 12, 0);
            MiniTitle.Margin = MiniTitleOld.Margin = new Thickness(10, 0, 0, 0);
        }
    }

    // сколько места занимает всё, кроме самого текста (запас 6 px, чтобы текст не обрезался)
    private double MiniFixed()
    {
        // + плашка идущего таймера или секундомера справа (Timers.cs)
        if (_clockMode) return 24 + 6 + MiniTimerW;
        return (MiniEqOn ? 8 + 12 + 22 + 10 + 8 + 42 + 6 : 8 + 12 + 22 + 10 + 0 + 6) + MiniTimerW;
    }

    // ============================================================
    //  ЗВУК ДЛЯ ПОЛОСОК: нужен острову (если включён его эквалайзер) или плееру на весь экран
    // ============================================================
    // звук нужен эквалайзеру видимого острова или плееру на весь экран (визуализатор, свечение)
    private bool NeedCapture() => (_cfg.Equalizer && !_hidden) || (_player != null && (PlayerViz || PlayerGlow));

    // ============================================================
    //  ГЛАДКИЙ РАЗМЫТЫЙ ФОН ИЗ ОБЛОЖКИ
    //  Обложка сжимается до N x N, слегка насыщается, затемняется и плавно (по кривой smoothstep)
    //  растягивается до OW x OH. Получается мягкий градиент без видимых квадратов.
    // ============================================================
    private static BitmapSource? SmoothBg(BitmapSource bmp, int N, int OW, int OH, double k)
    {
        try
        {
            var small = new TransformedBitmap(bmp, new ScaleTransform((double)N / bmp.PixelWidth, (double)N / bmp.PixelHeight));
            var conv = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            conv.CopyPixels(px, stride, 0);
            for (int i = 0; i < px.Length; i += 4)
            {
                double b = px[i], g = px[i + 1], r = px[i + 2];
                double l = 0.299 * r + 0.587 * g + 0.114 * b;
                r = l + (r - l) * 1.3; g = l + (g - l) * 1.3; b = l + (b - l) * 1.3;
                px[i] = (byte)Math.Clamp(b * k, 0, 255);
                px[i + 1] = (byte)Math.Clamp(g * k, 0, 255);
                px[i + 2] = (byte)Math.Clamp(r * k, 0, 255);
                px[i + 3] = 255;
            }
            var o = new byte[OW * OH * 4];
            for (int y = 0; y < OH; y++)
            {
                double fy = (y + 0.5) / OH * h - 0.5;
                int y0 = (int)Math.Floor(fy);
                double ty = fy - y0; ty = ty * ty * (3 - 2 * ty);
                int ya = Math.Clamp(y0, 0, h - 1), yb = Math.Clamp(y0 + 1, 0, h - 1);
                for (int x = 0; x < OW; x++)
                {
                    double fx = (x + 0.5) / OW * w - 0.5;
                    int x0 = (int)Math.Floor(fx);
                    double tx = fx - x0; tx = tx * tx * (3 - 2 * tx);
                    int xa = Math.Clamp(x0, 0, w - 1), xb = Math.Clamp(x0 + 1, 0, w - 1);
                    for (int c = 0; c < 3; c++)
                    {
                        double top = px[ya * stride + xa * 4 + c] * (1 - tx) + px[ya * stride + xb * 4 + c] * tx;
                        double bot = px[yb * stride + xa * 4 + c] * (1 - tx) + px[yb * stride + xb * 4 + c] * tx;
                        o[(y * OW + x) * 4 + c] = (byte)(top * (1 - ty) + bot * ty);
                    }
                    o[(y * OW + x) * 4 + 3] = 255;
                }
            }
            var res = BitmapSource.Create(OW, OH, 96, 96, PixelFormats.Bgra32, null, o, OW * 4);
            res.Freeze();
            return res;
        }
        catch { return null; }
    }

    // ============================================================
    //  ФОРМА ОБЛОЖКИ В СВЁРНУТОМ ОСТРОВЕ
    // ============================================================
    private void ApplyCoverShape()
    {
        CoverMini.Clip = null;
        switch (_cfg.CoverShape)
        {
            case 1: CoverMini.CornerRadius = new CornerRadius(6); break;                 // скруглённая
            case 2: CoverMini.CornerRadius = new CornerRadius(1.5); break;               // квадрат
            case 3: CoverMini.CornerRadius = new CornerRadius(11, 3, 11, 3); break;      // лепесток
            case 4:                                                                       // шестиугольник
                CoverMini.CornerRadius = new CornerRadius(0);
                CoverMini.Clip = Geometry.Parse("M11,0 L20.5,5.5 L20.5,16.5 L11,22 L1.5,16.5 L1.5,5.5 Z");
                break;
            case 5:                                                                       // ромб
                CoverMini.CornerRadius = new CornerRadius(0);
                CoverMini.Clip = Geometry.Parse("M11,0 L22,11 L11,22 L0,11 Z");
                break;
            default: CoverMini.CornerRadius = new CornerRadius(11); break;               // круг
        }
    }

    // ============================================================
    //  ТОНКАЯ ПОЛОСКА ПРОГРЕССА ВНУТРИ СВЁРНУТОГО ОСТРОВА
    // ============================================================
    private void UpdateMiniProgress()
    {
        try
        {
            if (!_cfg.MiniProgress || _mode != Mode.Mini || _clockMode || _session == null || !_playing)
            {
                if (MiniProg.Opacity > 0) MiniProg.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
                return;
            }
            var tl = _session.GetTimelineProperties();
            double total = (tl.EndTime - tl.StartTime).TotalSeconds;
            if (total <= 1) return;
            var pos = tl.Position + (DateTimeOffset.Now - tl.LastUpdatedTime);
            double cur = Math.Clamp((pos - tl.StartTime).TotalSeconds, 0, total);
            double avail = Math.Max(10, PillBodyW - 28);
            double now = avail * cur / total, nxt = avail * Math.Min(total, cur + 1) / total;
            // за секунду линия плавно доезжает до следующей точки
            MiniProg.Width = now;
            MiniProg.BeginAnimation(WidthProperty, new DoubleAnimation(now, nxt, TimeSpan.FromMilliseconds(1000)));
            if (MiniProg.Opacity < 1) MiniProg.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(300)));
        }
        catch (Exception ex) { App.LogOnce("MiniProgress", ex); }
    }

    // ============================================================
    //  СТИЛИ ЭКВАЛАЙЗЕРА: столбики, плавная волна, круги
    // ============================================================
    private int _eqStyleBuilt = -1;
    private System.Windows.Shapes.Path? _waveM, _waveF;
    private readonly List<System.Windows.Shapes.Ellipse> _dotsM = new(), _dotsF = new();

    private void RebuildEq()
    {
        _eqStyleBuilt = _cfg.EqStyle;
        _bars.Clear(); _dotsM.Clear(); _dotsF.Clear();
        _waveM = _waveF = null;
        _eqM = _eqF = null;
        MiniEq.Children.Clear();
        FullEq.Children.Clear();
        switch (_cfg.EqStyle)
        {
            case 1:
                _waveM = BuildWave(MiniEq);
                _waveF = BuildWave(FullEq);
                break;
            case 2:
                BuildDots(MiniEq, _dotsM);
                BuildDots(FullEq, _dotsF);
                break;
            case >= 3 and <= 6:
                _eqM = BuildEqCanvas(MiniEq);
                _eqF = BuildEqCanvas(FullEq);
                break;
            default:
                BuildBars(MiniEq);
                BuildBars(FullEq);
                break;
        }
        UpdateEqVisuals();
    }

    private System.Windows.Shapes.Path BuildWave(Panel p)
    {
        var c = new Canvas { Width = 42, Height = 18 };
        var path = new System.Windows.Shapes.Path
        {
            Stroke = _uiBrush, StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
        };
        c.Children.Add(path);
        p.Children.Add(c);
        return path;
    }

    private void BuildDots(Panel p, List<System.Windows.Shapes.Ellipse> list)
    {
        var c = new Canvas { Width = 42, Height = 18 };
        for (int i = 0; i < Bands; i++)
        {
            var e = new System.Windows.Shapes.Ellipse { Width = 3, Height = 3, Fill = _uiBrush };
            c.Children.Add(e);
            list.Add(e);
        }
        p.Children.Add(c);
    }

    private void UpdateEqVisuals()
    {
        switch (_cfg.EqStyle)
        {
            case 1:
                DrawWave(_waveM); DrawWave(_waveF);
                break;
            case 2:
                DrawDots(_dotsM); DrawDots(_dotsF);
                break;
            case >= 3 and <= 6:
                DrawEqExtra(_cfg.EqStyle);
                break;
            default:
                for (int j = 0; j < _bars.Count; j++)
                    _bars[j].Height = 3 + Math.Min(1.15, _bp[j % Bands]) * 15;
                break;
        }
    }

    // плавная кривая через 7 точек (Catmull-Rom, переведённый в кривые Безье)
    private void DrawWave(System.Windows.Shapes.Path? path)
    {
        if (path == null) return;
        var pts = new Point[Bands];
        for (int i = 0; i < Bands; i++)
            pts[i] = new Point(1 + i * (40.0 / (Bands - 1)), 16 - Math.Min(1.15, _bp[i]) * 13);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], false, false);
            for (int i = 0; i < Bands - 1; i++)
            {
                var p0 = i > 0 ? pts[i - 1] : pts[i];
                var p1 = pts[i];
                var p2 = pts[i + 1];
                var p3 = i + 2 < Bands ? pts[i + 2] : pts[i + 1];
                var c1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
                var c2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
                c.BezierTo(c1, c2, p2, true, true);
            }
        }
        g.Freeze();
        path.Data = g;
    }

    private void DrawDots(List<System.Windows.Shapes.Ellipse> dots)
    {
        for (int i = 0; i < dots.Count; i++)
        {
            double d = 3 + Math.Min(1.15, _bp[i]) * 8;
            dots[i].Width = dots[i].Height = d;
            Canvas.SetLeft(dots[i], 3 + i * 6 - d / 2);
            Canvas.SetTop(dots[i], 9 - d / 2);
        }
    }
}
