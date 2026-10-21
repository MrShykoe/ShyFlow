using System.Windows;

namespace Island;

// Режим notch: остров прижат к верхнему краю экрана, как вырез на iPhone 13 и MacBook.
//
// Шаг 39: остров и «уши» это одна фигура (Smooth.Shape с параметром ear). Раньше уши были отдельными фигурами
// поверх острова: на стыке был виден шов, а заливка ушей лишь подбиралась под цвет острова (градиенты тем, обложка на фоне
// и рамка темы на ушах не совпадали). Теперь остров просто шире на два уха, его фон, обложка на фоне, рамка темы и свечение
// идут по общему контуру без единого стыка, а содержимое сдвинуто внутрь на ширину уха (Padding).
//
// Переход плавный и непрерывный: остров поднимается к краю, выпуклые верхние углы распрямляются в ноль ровно к середине пути,
// и оттуда же вырастают вогнутые уши. Форма ни в какой момент не прыгает.
public partial class MainWindow
{
    // размер уха: на сколько оно уходит от острова вбок и вниз. Шаг 60: маленькое, как у чёлки MacBook (было 22,
    // большие уши заходили на нижние углы и остров «свисал»)
    // Шаг 76: оба размера настраиваются («Внешний вид», «Режим notch»: «Переход к рамке» и «Скругление углов»).
    // По умолчанию как раньше: уши 10 px, нижние углы 15 px.
    private double NotchEar => 2 + 22 * Math.Clamp(_cfg.NotchEarK, 0, 1);         // 2..24 px
    private double NotchBottomR => 4 + 15 * Math.Clamp(_cfg.NotchRoundK, 0, 1);   // 4..19 px
    private const double FloatGap = 8;       // отступ обычного острова от верхнего края

    private Spring _nk = new() { K = 110, D = 23 };   // 0 обычный остров, 1 вырез; почти критическое затухание: без отскока
    private double _notchTopApplied = -1;
    private double _earNow;                           // текущий размер ушей, px (0, пока остров не у края)

    private void InitNotch()
    {
        _nk.P = _nk.T = _cfg.NotchMode ? 1 : 0;
        NotchLayout();
    }

    // вызывается из ApplySettings: куда плыть
    private void NotchRetarget()
    {
        // шаг 76: размеры ушей и углов могли поменяться в настройках — перерисовать
        _capsK = -1; _earNow = -1;
        NotchLayout();
        EnsureLoop();
        double t = _cfg.NotchMode ? 1 : 0;
        if (Math.Abs(_nk.T - t) < 0.5) return;
        _nk.T = t;
        EnsureLoop();
    }

    // шаг пружины перехода, true пока что-то движется
    private bool NotchStep(double dt)
    {
        if (NkSettled) { _nk.P = _nk.T; _nk.V = 0; }
        else _nk.Step(dt);
        NotchLayout();
        return !NkSettled;
    }

    private void NotchSnap()
    {
        _nk.P = _nk.T; _nk.V = 0;
        NotchLayout();
    }

    // порог строже, чем у остальных пружин: при шкале 0..1 «0.05» было бы заметным скачком
    private bool NkSettled => Math.Abs(_nk.P - _nk.T) < 0.002 && Math.Abs(_nk.V) < 0.01;

    private double NotchK => Math.Clamp(_nk.P, 0, 1);

    // первая половина пути: углы распрямляются; вторая: растут уши (S-кривая, чтобы рост начинался и кончался мягко)
    private static double Ease01(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
    private double EarK => Ease01(NotchK * 2 - 1);
    private double CornerK => 1 - Ease01(NotchK * 2);

    private void NotchLayout()
    {
        double k = NotchK;
        double top = FloatGap * (1 - k);
        if (Math.Abs(top - _notchTopApplied) > 0.01)
        {
            _notchTopApplied = top;
            Pill.Margin = new Thickness(0, top, 0, 0);
        }
        NotchCapsules();
        double ear = Pill.Visibility == Visibility.Visible ? NotchEar * EarK : 0;
        if (Math.Abs(ear - _earNow) < 0.01) return;
        _earNow = ear;
        // остров шире на два уха: содержимое остаётся над самим островом, а фон с обложкой растянут на всю фигуру
        Pill.Ears = ear;
        Pill.Padding = new Thickness(ear, 0, ear, 0);
        BgImg.Ears = ear;
        BgImg.Margin = BgCustom.Margin = BgShade.Margin = new Thickness(-ear, 0, -ear, 0);
        Pill.Width = Math.Max(24, _w.P) + 2 * ear;
    }

    // ширина самого острова без ушей
    private double PillBodyW => Math.Max(0, Pill.ActualWidth - 2 * _earNow);

    // Шаг 60: пузырь таймера, пузырь событий и док программ в режиме notch тоже прилипают к краю экрана, каждый своей
    // маленькой чёлкой: те же уши и углы, что у острова, и тот же плавный переход. Верх у них уже идёт за островом
    // (PlaceBubble, PlaceEvent, PlaceAppsBar берут Pill.Margin.Top).
    private double _capsK = -1;

    private void NotchCapsules()
    {
        double k = NotchK;
        if (Math.Abs(k - _capsK) < 0.0005) return;
        _capsK = k;
        double ear = NotchEar * EarK;
        NotchCapsule(Bubble, BubbleBgLayers, 19, ear);
        NotchCapsule(EvBubble, EvBgLayers, 19, ear);
        NotchCapsule(AppsBar, AppsBgLayers, 17, ear);
        // появляются и прячутся масштабом: у края экрана растут от него, а не от середины (иначе отрываются от рамки)
        var origin = new Point(0.5, k > 0.5 ? 0 : 0.5);
        Bubble.RenderTransformOrigin = EvBubble.RenderTransformOrigin = origin;
        PlaceAppsBar();   // ширина капсул выросла на уши: раздвинуть
    }

    private void NotchCapsule(SBorder b, FrameworkElement bg, double r, double ear)
    {
        b.CornerRadius = NotchCr(new CornerRadius(r));
        b.Ears = ear;
        b.Padding = new Thickness(ear, 0, ear, 0);
        // фон с обложкой растянут на уши и обрезан по общему контуру, как у острова
        bg.Margin = new Thickness(-ear, 0, -ear, 0);
        ClipCapsuleBg(b, bg);
    }

    // контур фона капсулы: со скруглением минус рамка, в режиме notch с ушами
    private static void ClipCapsuleBg(SBorder b, FrameworkElement bg)
    {
        double w = bg.ActualWidth, h = bg.ActualHeight;
        if (w <= 0 || h <= 0) return;
        double t = b.BorderThickness.Left;
        var cr = b.CornerRadius;
        static double S(double v, double d) => Math.Max(0, v - d);
        bg.Clip = Smooth.Shape(w, h, new CornerRadius(S(cr.TopLeft, t), S(cr.TopRight, t), S(cr.BottomRight, t), S(cr.BottomLeft, t)),
            0, 0, 0, 0, b.Ears);
    }

    // верхние углы распрямляются (до нуля к середине перехода), нижние у маленького острова чуть меньше (как у выреза)
    private CornerRadius NotchCr(CornerRadius c)
    {
        double k = NotchK;
        if (k <= 0) return c;
        double top = CornerK;
        // маленький остров (r до 22): нижние углы к NotchBottomR; раскрытый: в той же пропорции, что и настройка к 15 по умолчанию
        double nb = NotchBottomR;
        double Bot(double r, double kk) => r + ((r <= 22 ? nb : r * nb / 15) - r) * kk;
        return new CornerRadius(c.TopLeft * top, c.TopRight * top, Bot(c.BottomRight, k), Bot(c.BottomLeft, k));
    }
}
