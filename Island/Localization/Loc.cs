using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Island;

// Язык интерфейса. Авто: по языку Windows (русский, белорусский, казахский: русский; украинский, испанский, немецкий,
// португальский: свои; остальное: английский).
// Шаг 74: кроме русского и английского — украинский, испанский, немецкий и португальский. Они переводятся с английского
// (LocExtra.cs): T(ru) сначала даёт английский, как раньше, потом английский ищется в словаре языка. Нет перевода — английский.
internal static partial class Loc
{
    public static bool En { get; private set; }   // любой язык, кроме русского (базовый текст английский)
    public static string Lang { get; private set; } = "ru";

    // номер в настройке Language -> код языка; 0 = авто
    public static readonly string[] Codes = { "", "ru", "en", "uk", "es", "de", "pt" };
    public static readonly string[] Names = { "Авто", "Русский", "English", "Українська", "Español", "Deutsch", "Português" };
    public static CultureInfo Culture { get; private set; } = new("ru-RU");

    private static bool _hooked;
    private static readonly ConditionalWeakTable<DependencyObject, string[]> OrigText = new();
    private static readonly ConditionalWeakTable<DependencyObject, string[]> OrigTip = new();

    // 0 авто, 1 русский, 2 английский
    public static void Init(int setting)
    {
        string sys = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        string auto = sys is "ru" or "be" or "kk" ? "ru" : sys is "uk" or "es" or "de" or "pt" ? sys : "en";
        Lang = setting > 0 && setting < Codes.Length ? Codes[setting] : auto;
        En = Lang != "ru";
        Culture = new CultureInfo(Lang switch { "ru" => "ru-RU", "uk" => "uk-UA", "es" => "es-ES", "de" => "de-DE", "pt" => "pt-BR", _ => "en-US" });
        if (!_hooked)
        {
            _hooked = true;
            // любой элемент, который появляется на экране (в том числе созданный кодом), переводится сам
            EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((s, _) => Translate(s)));
        }
    }

    // английский текст -> выбранный язык (украинский, испанский, немецкий, португальский); английский как есть
    private static string X(string en) =>
        Lang != "en" && Extra.TryGetValue(Lang, out var d) && d.TryGetValue(en, out var t) ? t : en;

    public static string T(string? ru)
    {
        if (string.IsNullOrEmpty(ru)) return ru ?? "";
        if (!En) return ru;
        return X(ToEn(ru));
    }

    private static string ToEn(string ru)
    {
        if (Map.TryGetValue(ru, out var e)) return e;
        foreach (var (rx, rep) in Patterns)
        {
            var m = rx.Match(ru);
            if (m.Success) return m.Result(rep);
        }
        foreach (var (p, e2) in Prefixes)
            if (ru.StartsWith(p, StringComparison.Ordinal)) return e2 + ru[p.Length..];
        string r = ru;
        foreach (var (a, b) in Subs)
            if (r.Contains(a)) r = r.Replace(a, b);
        return r;
    }

    public static string T(string ru, string en) => En ? X(en) : ru;

    private static void Apply(ConditionalWeakTable<DependencyObject, string[]> table, DependencyObject d, string cur, Action<string> set)
    {
        if (string.IsNullOrWhiteSpace(cur)) return;
        string orig = cur;
        // если сейчас стоит наш же перевод, переводим заново с исходного русского (так работает переключение языка)
        if (table.TryGetValue(d, out var pair) && cur == pair[1]) orig = pair[0];
        string tr = T(orig);
        if (tr != cur) set(tr);
        table.Remove(d);
        table.Add(d, new[] { orig, tr });
    }

    private static void Translate(object? o)
    {
        switch (o)
        {
            case TextBlock tb when tb.Inlines.Count <= 1:
                Apply(OrigText, tb, tb.Text, v => tb.Text = v);
                break;
            case MenuItem mi when mi.Header is string h:
                Apply(OrigText, mi, h, v => mi.Header = v);
                break;
            case Button b when b.Content is string c:
                Apply(OrigText, b, c, v => b.Content = v);
                break;
        }
        if (o is FrameworkElement fe && fe.ToolTip is string tt)
            Apply(OrigTip, fe, tt, v => fe.ToolTip = v);
    }

    public static void ApplyTree(object? root)
    {
        if (root == null) return;
        Translate(root);
        if (root is not DependencyObject d) return;
        foreach (var ch in LogicalTreeHelper.GetChildren(d))
            if (ch is DependencyObject) ApplyTree(ch);
    }
}
