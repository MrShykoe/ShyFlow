using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Island;

// Строка настроек с полем ввода (шаг 54: город для погоды). Сохраняется по Enter и при уходе из поля.
public partial class SettingsWindow
{
    private void TextRow(StackPanel card, string title, string? sub, string hint, Func<string> get, Action<string> set, double width = 170)
    {
        Divider(card);
        var g = new Grid { Margin = new Thickness(16, 9, 14, 9) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        texts.Children.Add(RowTitle(title));
        if (sub != null) texts.Children.Add(RowSub(sub));
        g.Children.Add(texts);
        var box = new Grid { Width = width, VerticalAlignment = VerticalAlignment.Center };
        var tb = new TextBox
        {
            FontSize = 12.5, Foreground = Brushes.White, Background = ChipBg, BorderThickness = new Thickness(0),
            CaretBrush = Brushes.White, Padding = new Thickness(8, 5, 8, 5), Text = get()
        };
        var ph = new TextBlock { Text = Loc.T(hint), Foreground = new SolidColorBrush(Color.FromArgb(0x70, 255, 255, 255)), FontSize = 12.5, Margin = new Thickness(10, 5, 0, 0), IsHitTestVisible = false };
        ph.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        tb.TextChanged += (_, _) => ph.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        void Commit()
        {
            string v = tb.Text.Trim();
            if (v == get()) return;
            set(v);
            _s.Save(); _changed();
        }
        tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        tb.LostFocus += (_, _) => Commit();
        box.Children.Add(tb);
        box.Children.Add(ph);
        Grid.SetColumn(box, 1);
        g.Children.Add(box);
        card.Children.Add(g);
    }
}
