using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Island;

// Ещё четыре стиля эквалайзера (шаг 41). Настройки, «Внешний вид», «Эквалайзер», «Стиль».
//   3 Зеркало: полоски растут от середины вверх и вниз, как запись голоса.
//   4 Пики: столбики, над каждым «шапочка», которая зависает на пике и медленно падает (как в старых плеерах).
//   5 Пульс: бегущая линия общей громкости, как кардиограмма, с точкой на конце.
//   6 Матрица: светодиоды 7 на 4, загораются снизу вверх; погасшие видны тускло.
// Каждый стиль это одна-две фигуры, которые пересобираются за кадр: нагрузка как у старых стилей.
// Цвет общий для всех: цвет обложки (если включено «Цвет из обложки везде»), иначе белый.
public partial class MainWindow
{
    private sealed class EqCanvas
    {
        public Path Main = null!;   // залитые полоски / светодиоды / линия
        public Path Dim = null!;    // тусклые светодиоды матрицы, «шапочки» пиков, точка на конце пульса
    }
    private EqCanvas? _eqM, _eqF;

    private const double EqW = 42, EqH = 18;

    private EqCanvas BuildEqCanvas(Panel p)
    {
        var c = new Canvas { Width = EqW, Height = EqH };
        var eq = new EqCanvas
        {
            Main = new Path { Fill = _uiBrush, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round },
            Dim = new Path { Fill = _uiBrush }
        };
        c.Children.Add(eq.Dim);
        c.Children.Add(eq.Main);
        p.Children.Add(c);
        return eq;
    }

    // ---------- состояние «пиков» и «пульса» ----------
    private readonly double[] _peak = new double[Bands], _peakHold = new double[Bands], _peakV = new double[Bands];
    private readonly double[] _pulse = new double[40];
    private int _pulseHead;
    private double _pulseLevel, _pulseAcc;
    private readonly Stopwatch _eqClock = Stopwatch.StartNew();
    private double _eqLast;

    private double Lvl(int i) => Math.Min(1.15, Math.Max(0, _bp[i])) / 1.15;

    private void DrawEqExtra(int style)
    {
        double now = _eqClock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _eqLast, 0, 0.1);
        _eqLast = now;
        if (style == 4) StepPeaks(dt);
        if (style == 5) StepPulse(dt);
        foreach (var eq in new[] { _eqM, _eqF })
        {
            if (eq == null) continue;
            switch (style)
            {
                case 3: DrawMirror(eq); break;
                case 4: DrawPeaks(eq); break;
                case 5: DrawPulse(eq); break;
                case 6: DrawMatrix(eq); break;
            }
        }
    }

    private static double BarX(int i) => 1 + i * 6;   // 7 полосок по 3 px с шагом 6: от 1 до 40

    private static void AddBar(StreamGeometryContext c, double x, double y, double w, double h)
    {
        // скруглённый прямоугольник (капсула для узких полосок)
        double r = Math.Min(w, h) / 2;
        c.BeginFigure(new Point(x + r, y), true, true);
        c.LineTo(new Point(x + w - r, y), true, false);
        c.ArcTo(new Point(x + w, y + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        c.LineTo(new Point(x + w, y + h - r), true, false);
        c.ArcTo(new Point(x + w - r, y + h), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        c.LineTo(new Point(x + r, y + h), true, false);
        c.ArcTo(new Point(x, y + h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        c.LineTo(new Point(x, y + r), true, false);
        c.ArcTo(new Point(x + r, y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
    }

    private static Geometry Geo(Action<StreamGeometryContext> draw)
    {
        var g = new StreamGeometry();
        using (var c = g.Open()) draw(c);
        g.Freeze();
        return g;
    }

    // 3: от середины вверх и вниз
    private void DrawMirror(EqCanvas eq)
    {
        eq.Main.Data = Geo(c =>
        {
            for (int i = 0; i < Bands; i++)
            {
                double h = 3 + Lvl(i) * (EqH - 3);
                AddBar(c, BarX(i), (EqH - h) / 2, 3, h);
            }
        });
        eq.Dim.Data = null;
    }

    // 4: столбики и падающие «шапочки»
    private void StepPeaks(double dt)
    {
        for (int i = 0; i < Bands; i++)
        {
            double v = Lvl(i);
            if (v >= _peak[i]) { _peak[i] = v; _peakV[i] = 0; _peakHold[i] = 0.35; }   // новый пик: держим треть секунды
            else if (_peakHold[i] > 0) _peakHold[i] -= dt;
            else
            {
                _peakV[i] += 2.2 * dt;                          // потом падает с ускорением, как настоящая шапочка
                _peak[i] = Math.Max(v, _peak[i] - _peakV[i] * dt);
            }
        }
    }

    private void DrawPeaks(EqCanvas eq)
    {
        double room = EqH - 4;   // сверху место для шапочки
        eq.Main.Data = Geo(c =>
        {
            for (int i = 0; i < Bands; i++)
            {
                double h = 2 + Lvl(i) * (room - 2);
                AddBar(c, BarX(i), EqH - h, 3, h);
            }
        });
        eq.Dim.Data = Geo(c =>
        {
            for (int i = 0; i < Bands; i++)
            {
                double y = EqH - 2 - _peak[i] * (room - 2) - 2.5;
                AddBar(c, BarX(i), Math.Max(0, y), 3, 1.6);
            }
        });
        eq.Dim.Opacity = 0.75;
    }

    // 5: бегущая линия громкости (басы весят больше)
    private void StepPulse(double dt)
    {
        double sum = 0, wsum = 0;
        for (int i = 0; i < Bands; i++) { double w = i < 2 ? 2.0 : 1.0; sum += Lvl(i) * w; wsum += w; }
        double target = sum / wsum;
        _pulseLevel += (target - _pulseLevel) * Math.Min(1, dt * 18);
        // новая точка раз в 40 мс: скорость бега не зависит от частоты кадров (и в экономном режиме тоже)
        _pulseAcc += dt;
        while (_pulseAcc >= 0.04)
        {
            _pulseAcc -= 0.04;
            _pulseHead = (_pulseHead + 1) % _pulse.Length;
            _pulse[_pulseHead] = _pulseLevel;
        }
    }

    private void DrawPulse(EqCanvas eq)
    {
        int n = _pulse.Length;
        var pts = new Point[n];
        for (int k = 0; k < n; k++)
        {
            double v = _pulse[(_pulseHead + 1 + k) % n];   // от старых к новым, слева направо
            pts[k] = new Point(1 + k * ((EqW - 5) / (n - 1)), EqH - 2.5 - v * (EqH - 5));
        }
        eq.Main.Fill = null;
        eq.Main.Stroke = _uiBrush;
        eq.Main.StrokeThickness = 1.6;
        eq.Main.Data = Geo(c =>
        {
            c.BeginFigure(pts[0], false, false);
            for (int k = 1; k < n; k++) c.LineTo(pts[k], true, true);
        });
        var head = pts[^1];
        eq.Dim.Data = new EllipseGeometry(head, 2.2, 2.2);   // точка на конце линии
        eq.Dim.Opacity = 1;
    }

    // 6: светодиоды 7 на 4
    private void DrawMatrix(EqCanvas eq)
    {
        const int rows = 4;
        const double size = 3, gap = 1.6;
        double top = (EqH - rows * size - (rows - 1) * gap) / 2;
        var lit = new List<(double x, double y)>();
        var off = new List<(double x, double y)>();
        for (int i = 0; i < Bands; i++)
        {
            double v = Lvl(i);
            for (int r = 0; r < rows; r++)
            {
                double y = top + (rows - 1 - r) * (size + gap);    // ряд 0 внизу
                ((v > (r + 0.35) / rows) ? lit : off).Add((BarX(i), y));
            }
        }
        eq.Main.Data = Geo(c => { foreach (var (x, y) in lit) AddBar(c, x, y, size, size); });
        eq.Dim.Data = Geo(c => { foreach (var (x, y) in off) AddBar(c, x, y, size, size); });
        eq.Dim.Opacity = 0.18;
    }
}
