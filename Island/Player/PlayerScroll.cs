using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Island;

// Ручная прокрутка текста песни в плеере (шаг 58), как в Apple Music.
// Колесо мыши или перетаскивание по тексту листают строки; пока листаешь, все строки видны ярче.
// Через 3,5 секунды без прокрутки (или по кнопке «К текущей строке») текст плавно возвращается к строке, которая поётся.
// Клик по строке по-прежнему перематывает на неё; перетаскивание клик не вызывает.
public sealed partial class PlayerWindow
{
    private bool _manualScroll;
    private double _scrollOff, _scrollBase;
    private readonly DispatcherTimer _scrollBack = new() { Interval = TimeSpan.FromSeconds(3.5) };
    private bool _scrollHooked, _dragScroll;
    private Point _dragFrom;
    private double _dragOff0;
    private Border? _backPill;

    private void HookLyricScroll()
    {
        if (_scrollHooked) return;
        _scrollHooked = true;
        _lyrPanel.Background = Brushes.Transparent;   // чтобы колесо и мышь ловились и между строк
        _scrollBack.Tick += (_, _) => EndManualScroll();
        _lyrPanel.PreviewMouseWheel += (_, e) =>
        {
            if (_lines.Count == 0) return;
            e.Handled = true;
            BeginManualScroll();
            SetScroll(_scrollOff + e.Delta * 0.55, true);
        };
        _lyrPanel.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_lines.Count == 0) return;
            _dragFrom = e.GetPosition(this);
            _dragOff0 = _manualScroll ? _scrollOff : 0;
            _dragScroll = false;
        };
        _lyrPanel.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _lines.Count == 0) return;
            double dy = e.GetPosition(this).Y - _dragFrom.Y;
            if (!_dragScroll && Math.Abs(dy) < 8) return;
            if (!_dragScroll) { _dragScroll = true; BeginManualScroll(); _lyrPanel.CaptureMouse(); }
            SetScroll(_dragOff0 + dy, false);
        };
        _lyrPanel.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!_dragScroll) return;
            _dragScroll = false;
            _lyrPanel.ReleaseMouseCapture();
            e.Handled = true;   // это было перетаскивание, а не клик по строке
            _scrollBack.Stop(); _scrollBack.Start();
        };

        // «К текущей строке»: видна, пока листаешь
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new VIcon { Text = "chevrons-up-down", FontSize = 13, Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = Loc.T("К текущей строке"), Foreground = Brushes.Black, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        _backPill = new SBorder
        {
            Child = row, CornerRadius = new CornerRadius(18), Padding = new Thickness(14, 8, 16, 9), Background = new SolidColorBrush(Color.FromArgb(0xE6, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.Hand,
            Opacity = 0, IsHitTestVisible = false
        };
        _backPill.PreviewMouseLeftButtonDown += (_, e) => e.Handled = true;
        _backPill.PreviewMouseLeftButtonUp += (_, e) => { e.Handled = true; EndManualScroll(); };
        _lyrPanel.Children.Add(_backPill);
        // низ панели затухает (маска прозрачности): кнопку ставим чуть выше затухания
        _lyrPanel.SizeChanged += (_, e) => _backPill.Margin = new Thickness(0, 0, 0, e.NewSize.Height * 0.15);
    }

    private void BeginManualScroll()
    {
        if (!_manualScroll)
        {
            _manualScroll = true;
            _lyrStack.UpdateLayout();
            _scrollBase = TargetFor(Math.Max(_idx, 0));
            _scrollOff = 0;
            // пока листаешь, все строки читаются: ярче и без размытия
            for (int i = 0; i < _lines.Count; i++) ManualLook(i);
            if (_backPill != null) { _backPill.IsHitTestVisible = true; _backPill.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200))); }
        }
        _scrollBack.Stop();
        _scrollBack.Start();
    }

    // вид строки, пока листают: текущая белая и со свечением, остальные ярче и без размытия.
    // Запомненные яркость и размытие сбрасываем, чтобы после прокрутки строки снова оформились как обычно
    private void ManualLook(int i)
    {
        if (i >= _lineOp.Length) return;
        _lineOp[i] = -1;
        if (i != _idx) { _lines[i].Effect = null; _lineBlur[i] = -1; }
        _lines[i].BeginAnimation(OpacityProperty, new DoubleAnimation(i == _idx ? 1 : 0.7, TimeSpan.FromMilliseconds(220)));
    }

    // сдвиг относительно положения «как надо», с пределами: первая и последняя строки не уезжают за середину
    private void SetScroll(double off, bool animate)
    {
        if (_wraps.Count == 0) return;
        double max = TargetFor(0) - _scrollBase, min = TargetFor(_wraps.Count - 1) - _scrollBase;
        _scrollOff = Math.Clamp(off, min, max);
        double y = _scrollBase + _scrollOff;
        foreach (var t in _lt)
        {
            if (animate) t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            else { t.BeginAnimation(TranslateTransform.YProperty, null); t.Y = y; }
        }
    }

    private void ResetManualScroll()
    {
        _scrollBack.Stop();
        _manualScroll = false;
        _dragScroll = false;
        if (_backPill != null) { _backPill.IsHitTestVisible = false; _backPill.BeginAnimation(OpacityProperty, null); _backPill.Opacity = 0; }
    }

    private void EndManualScroll()
    {
        _scrollBack.Stop();
        if (!_manualScroll) return;
        _manualScroll = false;
        if (_backPill != null) { _backPill.IsHitTestVisible = false; _backPill.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200))); }
        SetIndex(_idx, true);   // яркость, размытие и прокрутка как обычно, к строке, которая поётся
    }
}
