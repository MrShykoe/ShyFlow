using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Island;

// Маленький ползунок 0..1 для острова (микшер громкости): клик и перетаскивание, колесо мыши по 5 %.
internal sealed class MiniSlider : Grid
{
    private readonly SBorder _fill;
    private readonly Ellipse _thumb;
    private double _value;

    public event Action<double>? Changed;
    public bool Dragging { get; private set; }

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 1); Relayout(); }
    }

    public Brush Accent { set { _fill.Background = value; } }

    public MiniSlider()
    {
        Height = 24;
        Background = Brushes.Transparent;   // чтобы ловить мышь по всей высоте, а не только по полоске
        Cursor = Cursors.Hand;
        Children.Add(new SBorder
        {
            Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center
        });
        _fill = new SBorder
        {
            Height = 4, CornerRadius = new CornerRadius(2), Background = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
        };
        Children.Add(_fill);
        _thumb = new Ellipse
        {
            Width = 12, Height = 12, Fill = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
        };
        Children.Add(_thumb);
        SizeChanged += (_, _) => Relayout();
    }

    private void Relayout()
    {
        double w = ActualWidth;
        _fill.Width = Math.Max(0, w * _value);
        _thumb.Margin = new Thickness(Math.Clamp(w * _value - 6, 0, Math.Max(0, w - 12)), 0, 0, 0);
    }

    private void SetFrom(MouseEventArgs e)
    {
        double w = ActualWidth;
        if (w <= 0) return;
        Value = e.GetPosition(this).X / w;
        Changed?.Invoke(_value);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Dragging = true;
        CaptureMouse();
        SetFrom(e);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Dragging) SetFrom(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!Dragging) return;
        Dragging = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Value = _value + (e.Delta > 0 ? 0.05 : -0.05);
        Changed?.Invoke(_value);
        e.Handled = true;
    }
}
