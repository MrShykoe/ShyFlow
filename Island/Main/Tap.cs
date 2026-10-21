using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Island;

// Шаг 70: клик по плитке или карточке больше не теряется.
// Плитки быстрых действий и карточки кармана при нажатии сжимаются (до 0.93 и 0.95). Если нажать у самого края,
// после сжатия курсор оказывался уже за плиткой: отпускание доставалось соседу или панели, и клик пропадал.
// С пером графического планшета так было особенно часто: кончик чуть съезжает между касанием и отрывом.
// Теперь элемент на время нажатия держит мышь. CaptureMode.SubTree: крестик, булавка и другие дочерние
// элементы по-прежнему получают свои события, а всё, что вне элемента, приходит к нему самому.
// Клик засчитывается, если отпустили на элементе или рядом (TapSlop); увели дальше — это отмена.
public partial class MainWindow
{
    private const double TapSlop = 14;

    private void HoldOnPress(FrameworkElement el)
    {
        el.PreviewMouseLeftButtonDown += (_, _) =>
        {
            if (Mouse.Captured == null) Mouse.Capture(el, CaptureMode.SubTree);
        };
        el.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!ReferenceEquals(Mouse.Captured, el)) return;
            var p = e.GetPosition(el);
            bool near = p.X >= -TapSlop && p.Y >= -TapSlop && p.X <= el.ActualWidth + TapSlop && p.Y <= el.ActualHeight + TapSlop;
            if (!near)
            {
                // отмена: клик не засчитываем, плитка пружинит обратно
                e.Handled = true;
                var t = Get(el);
                var a = new DoubleAnimation(1, TimeSpan.FromMilliseconds(380)) { EasingFunction = Spr() };
                t.S.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                t.S.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
            // мышь отпускаем, когда отработают все обработчики этого отпускания (и открытие файла, и действие плитки)
            el.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (ReferenceEquals(Mouse.Captured, el)) el.ReleaseMouseCapture();
            }));
        };
    }
}
