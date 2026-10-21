using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Island;

// Поиск в настройках (шаг 43).
// Результаты разбиты по разделам: над карточками значок и название раздела, где это лежит.
// Понимает близкие слова: «вырез» найдёт notch, «лагает» экономный режим, «обои» свой фон, «font» шрифт и т.д.
// Начать поиск можно, просто начав печатать в окне, или Ctrl+F.
public sealed partial class SettingsWindow
{
    // ключ (начало слова) -> что ещё искать. Ключи и слова в нижнем регистре
    private static readonly (string key, string[] also)[] Synonyms =
    {
        ("notch", new[] { "вырез", "notch" }), ("вырез", new[] { "notch" }), ("чёлк", new[] { "notch" }), ("челк", new[] { "notch" }),
        ("громк", new[] { "volume", "звук", "громк" }), ("звук", new[] { "громк", "sound", "volume", "микшер" }), ("volume", new[] { "громк" }),
        ("шрифт", new[] { "font" }), ("font", new[] { "шрифт" }),
        ("фон", new[] { "background", "обложк" }), ("обои", new[] { "фон", "background" }), ("background", new[] { "фон" }),
        ("тем", new[] { "theme", "оформлен", "стекло", "контур", "свечение" }), ("цвет", new[] { "обложк", "тем", "color" }),
        ("текст", new[] { "слова", "lyrics" }), ("слов", new[] { "текст", "lyrics" }), ("lyric", new[] { "текст", "слова" }), ("караоке", new[] { "загораются", "по буквам" }),
        ("клавиш", new[] { "ctrl", "горяч" }), ("горяч", new[] { "ctrl", "клавиш" }), ("hotkey", new[] { "горяч", "ctrl" }), ("сочетан", new[] { "ctrl" }),
        ("автозапуск", new[] { "вместе с windows" }), ("запуск", new[] { "вместе с windows", "приветств" }), ("startup", new[] { "вместе с windows" }),
        ("батаре", new[] { "заряд" }), ("заряд", new[] { "батаре" }),
        ("дискорд", new[] { "discord" }), ("discord", new[] { "дискорд" }),
        ("гиф", new[] { "стикер", "gif" }), ("gif", new[] { "стикер" }), ("стикер", new[] { "гиф" }), ("видео", new[] { "фон", "стикер" }), ("картинк", new[] { "фон", "стикер" }),
        ("анимац", new[] { "жидк", "плавн", "по буквам" }), ("плавн", new[] { "жидк", "анимац" }), ("капл", new[] { "жидк" }),
        ("лаг", new[] { "эконом", "производ", "кадр" }), ("тормоз", new[] { "эконом", "производ", "кадр" }), ("fps", new[] { "кадр", "эконом" }),
        ("слаб", new[] { "эконом", "производ" }), ("ноут", new[] { "эконом", "батаре", "яркость" }),
        ("язык", new[] { "language", "english", "перевод" }), ("англ", new[] { "english", "язык" }), ("language", new[] { "язык" }),
        ("монитор", new[] { "экран" }), ("экран", new[] { "монитор", "весь экран" }),
        ("уведомл", new[] { "плашк", "notification" }), ("плашк", new[] { "уведомл" }), ("notif", new[] { "уведомл", "плашк" }),
        ("плеер", new[] { "весь экран" }), ("полноэкран", new[] { "весь экран" }), ("визуал", new[] { "визуализатор", "эквалайзер" }),
        ("эквалайз", new[] { "equalizer", "полоски", "стиль" }), ("eq", new[] { "эквалайзер" }), ("полоск", new[] { "эквалайзер", "прогресс" }),
        ("буфер", new[] { "копир", "карман" }), ("копир", new[] { "буфер" }), ("заметк", new[] { "карман", "note" }), ("карман", new[] { "буфер", "заметк" }),
        ("сброс", new[] { "сбросить", "по умолчанию" }), ("бэкап", new[] { "экспорт", "импорт" }), ("backup", new[] { "экспорт", "импорт" }),
        ("ошибк", new[] { "журнал", "crash" }), ("лог", new[] { "журнал", "crash" }), ("crash", new[] { "журнал" }), ("папк", new[] { "папка shyflow" }),
        ("версия", new[] { "о программе" }), ("перевод", new[] { "translate", "язык перевода" }),
        ("док", new[] { "значки приложений" }), ("dock", new[] { "значки приложений" }), ("прилож", new[] { "значки приложений" }),
        ("программ", new[] { "значки приложений" }), ("трей", new[] { "значки приложений" }), ("меню-бар", new[] { "значки приложений" }),
        ("menu bar", new[] { "значки приложений" }), ("панель задач", new[] { "значки приложений" }), ("taskbar", new[] { "значки приложений" }),
    };

    private static List<string> SearchTerms(string q)
    {
        var res = new List<string> { q };
        if (q.Length < 2) return res;
        foreach (var (key, also) in Synonyms)
            if (q.StartsWith(key, StringComparison.Ordinal) || (q.Length >= 3 && key.StartsWith(q, StringComparison.Ordinal)))
                foreach (var a in also) if (!res.Contains(a)) res.Add(a);
        return res;
    }

    private static bool Hit(string text, List<string> terms)
    {
        foreach (var t in terms) if (text.Contains(t, StringComparison.Ordinal)) return true;
        return false;
    }

    // подпись раздела в результатах поиска
    private static FrameworkElement PageLabel(string name, string icon, Color col)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 20, 0, 0), Tag = "page" };
        row.Children.Add(new SBorder
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(col), VerticalAlignment = VerticalAlignment.Center,
            Child = new VIcon { Text = icon, FontSize = 11, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        });
        row.Children.Add(new TextBlock
        {
            Text = name, Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        return row;
    }
}
