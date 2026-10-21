using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Island;

// Язык интерфейса и выбор монитора.
public partial class MainWindow
{
    // ============================================================
    //  ЯЗЫК
    // ============================================================
    private int _langBuilt = -1;

    private void ApplyLanguage()
    {
        _langBuilt = _cfg.Language;
        Loc.Init(_cfg.Language);
        Loc.ApplyTree(this);
        if (Pill.ContextMenu != null) Loc.ApplyTree(Pill.ContextMenu);
        BuildActions();
        RenderPocket();
        if (_devicesOpen) RenderDevices();
        if (_session == null) EnterIdle();   // «Ничего не играет» и часы на нужном языке
        else _ = RefreshAsync();
    }

    // ============================================================
    //  МОНИТОР
    // ============================================================
    internal Rect MonitorDip
    {
        get
        {
            var list = Monitors.All();
            var m = list[Math.Clamp(CurMonitor, 0, list.Count - 1)];   // шаг 74: -1 = где активное окно (Screens.cs)
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX; // общий масштаб Windows
            return new Rect(m.Left / scale, m.Top / scale, m.Width / scale, m.Height / scale);
        }
    }

    private bool _displayHooked;

    // Шаг 60: окно больше не во всю ширину монитора, а полоса по центру под остров, пузыри и док.
    // Прозрачное окно WPF перерисовывается целиком, поэтому лишняя ширина (на 4K 3840 px) стоила кадров и батареи.
    // Ширина меняется редко: растёт сразу, как только чему-то стало тесно, а сужается с задержкой и ступенями,
    // чтобы окно не пересоздавало поверхность на каждом кадре анимации.
    private const double WinMinW = 1100;    // запас под жидкие растяжения, тени и смахивание уведомления в сторону
    private const double WinSideGap = 120;  // поле за самым крайним элементом
    private const double WinStep = 160;     // ступень ширины
    private double _winW;
    private DispatcherTimer? _winShrink;

    // сколько нужно окну, чтобы всё влезло: остров (с учётом того, куда он растёт) и капсулы по бокам
    private double NeededWinW()
    {
        double pill = Math.Max(Pill.Width, Math.Max(24, _w.T) + 2 * _earNow);
        double bubbles = BubbleSpace + EvSpace;
        double apps = AppsBar.Visibility == Visibility.Visible ? AppsBar.ActualWidth + 8 : 0;
        bool appsRight = !AppsLeft, bubblesRight = !BubbleLeft;
        double right = (bubblesRight ? bubbles : 0) + (appsRight ? apps : 0);
        double left = (bubblesRight ? 0 : bubbles) + (appsRight ? 0 : apps);
        double need = pill + 2 * (Math.Max(left, right) + 8 + WinSideGap);
        need = Math.Ceiling(Math.Max(need, WinMinW) / WinStep) * WinStep;
        return Math.Min(need, MonitorDip.Width);
    }

    // вызывается из PlaceAppsBar: при каждом изменении ширины острова и капсул
    private void FitWindow()
    {
        if (_winW <= 0) return;   // окно ещё не поставлено
        double need = NeededWinW();
        if (need > _winW + 0.5) { _winShrink?.Stop(); SetWinW(need); }
        else if (need < _winW - WinStep - 0.5)
        {
            if (_winShrink == null)
            {
                _winShrink = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                _winShrink.Tick += (_, _) => { _winShrink.Stop(); double n = NeededWinW(); if (n < _winW - 0.5) SetWinW(n); };
            }
            if (!_winShrink.IsEnabled) _winShrink.Start();
        }
    }

    private void SetWinW(double w)
    {
        var r = MonitorDip;
        _winW = w;
        // шаг 74: остров слева или справа — окно прижато к этому краю монитора, сам остров сдвигается в PlaceStage
        Left = _cfg.IslandPos switch
        {
            1 => r.Left,
            2 => r.Left + r.Width - w,
            _ => r.Left + Math.Round((r.Width - w) / 2)
        };
        Width = w;
        PlaceStage();
    }

    // ============================================================
    //  ПОЛОЖЕНИЕ (шаг 74): по центру, слева, справа
    //  Всё в окне (остров, пузыри, капсула значков) выложено от центра. Слева и справа окно прижато к краю монитора,
    //  а вся сцена сдвигается так, чтобы край острова был в Edge px от края экрана; остров растёт от края.
    //  Пузыри и значки тогда всегда с внутренней стороны, иначе они ушли бы за край экрана.
    // ============================================================
    private const double Edge = 18;
    private readonly TranslateTransform _stageMove = new();
    private bool BubbleLeft => _cfg.IslandPos == 1 ? false : _cfg.IslandPos == 2 || _cfg.BubbleSide == 1;
    private bool AppsLeft => _cfg.IslandPos == 1 ? false : _cfg.IslandPos == 2 ? _cfg.AppsPlace is 1 or 2 : _cfg.AppsPlace == 1;

    private void PlaceStage()
    {
        if (!ReferenceEquals(Stage.RenderTransform, _stageMove)) Stage.RenderTransform = _stageMove;
        double w = _winW > 0 ? _winW : Width;
        double half = Math.Max(24, Pill.Width) / 2;
        double x = _cfg.IslandPos switch
        {
            1 => Edge + half - w / 2,
            2 => w / 2 - Edge - half,
            _ => 0
        };
        if (Math.Abs(_stageMove.X - x) > 0.25) _stageMove.X = Math.Round(x);
    }

    private void PlaceOnMonitor()
    {
        var r = MonitorDip;
        Top = r.Top;
        _winW = 0;
        SetWinW(NeededWinW());
        Height = 420;   // шаг 49: с запасом под шторку уведомлений и четыре ряда быстрых действий (лишнее прозрачное и пропускает клики)
        if (!_displayHooked)
        {
            _displayHooked = true;
            // монитор отключили или поменяли разрешение: остров ставится на место заново
            SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(new Action(PlaceOnMonitor));
        }
    }
}
