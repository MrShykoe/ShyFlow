using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Island;

// Переводчик в острове (шаг 54).
//   Ctrl+Alt+Q в любой программе: остров копирует выделенный текст (сам нажимает Ctrl+C), раскрывается и переводит.
//   Буфер обмена потом возвращается как был. «Вставить вместо выделенного» кладёт перевод обратно в ту программу.
//   Плитка «Переводчик» в быстрых действиях: пустое поле, можно печатать или вставить; перевод идёт по мере ввода.
// Язык определяется сам. Если текст уже на языке перевода, переводим на запасной (русский <-> английский).
// Сервис — неофициальный Google Translate, как у перевода текстов песен.
public partial class MainWindow
{
    private const double TrH = 270;
    private static readonly (string code, string name)[] TrLangs =
    {
        ("ru", "Русский"), ("en", "Английский"), ("uk", "Украинский"), ("de", "Немецкий"), ("es", "Испанский"), ("fr", "Французский"),
        ("it", "Итальянский"), ("pl", "Польский"), ("tr", "Турецкий"), ("ja", "Японский"), ("ko", "Корейский"), ("zh-CN", "Китайский"),
    };

    private bool _trOpen;
    private IntPtr _trSrc;
    private int _trGen;
    private readonly DispatcherTimer _trDebounce = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool _trHooked;

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();

    private int TrTo => Math.Clamp(_cfg.TranslateQuickTo, 0, TrLangs.Length - 1);

    private void OpenTranslator(string? text, IntPtr src)
    {
        if (!_trHooked) { _trHooked = true; _trDebounce.Tick += (_, _) => { _trDebounce.Stop(); _ = DoTranslate(); }; }
        _trSrc = src;
        _pocketOpen = false; _actionsOpen = false; _devicesOpen = false; _timerOpen = false; _mixerOpen = false; _centerOpen = false; _trOpen = false; _autoOpened = false;
        _trOpen = true;
        TrDir.Content = Loc.T("Авто") + "  →  " + Loc.T(TrLangs[TrTo].name);
        TrPaste.Visibility = src != IntPtr.Zero && !string.IsNullOrWhiteSpace(text) ? Visibility.Visible : Visibility.Collapsed;
        TrOut.Text = "";
        TrState.Text = "";
        TrIn.Text = text ?? "";
        ApplyMode(Rest());
        Activate();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { TrIn.Focus(); TrIn.CaretIndex = TrIn.Text.Length; }));
        if (!string.IsNullOrWhiteSpace(text)) _ = DoTranslate();
    }

    private void TrClose_Click(object s, RoutedEventArgs e) { _trOpen = false; ApplyMode(Rest()); }

    private void TrIn_TextChanged(object s, TextChangedEventArgs e)
    {
        TrHint.Visibility = TrIn.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!_trOpen) return;
        _trDebounce.Stop();
        if (TrIn.Text.Trim().Length == 0) { TrOut.Text = ""; TrState.Text = ""; return; }
        _trDebounce.Start();
    }

    private async Task DoTranslate()
    {
        string text = TrIn.Text.Trim();
        if (text.Length == 0) return;
        int gen = ++_trGen;
        TrState.Text = Loc.T("перевожу…");
        string to = TrLangs[TrTo].code;
        var r = await TranslateEngine.TranslateText(text, to);
        // текст уже на нужном языке: на запасной
        if (r is { } x && x.from.Length > 0 && x.from.StartsWith(to.Split('-')[0], StringComparison.OrdinalIgnoreCase))
            r = await TranslateEngine.TranslateText(text, to == "ru" ? "en" : "ru");
        if (gen != _trGen) return;   // пока ждали, текст поменяли
        if (r == null) { TrState.Text = Loc.T("нет связи с переводчиком"); return; }
        TrOut.Text = r.Value.text;
        string from = TrLangs.FirstOrDefault(l => l.code.StartsWith(r.Value.from, StringComparison.OrdinalIgnoreCase)).name ?? r.Value.from;
        TrState.Text = from.Length > 0 ? Loc.T("с языка: ") + Loc.T(from) : "";
    }

    private void TrDir_Click(object s, RoutedEventArgs e)
    {
        var m = new ContextMenu();
        for (int i = 0; i < TrLangs.Length; i++)
        {
            int k = i;
            var it = new MenuItem { Header = Loc.T(TrLangs[i].name), IsChecked = i == TrTo };
            it.Click += (_, _) =>
            {
                _cfg.TranslateQuickTo = k;
                _cfg.Save();
                TrDir.Content = Loc.T("Авто") + "  →  " + Loc.T(TrLangs[k].name);
                _ = DoTranslate();
            };
            m.Items.Add(it);
        }
        m.PlacementTarget = TrDir;
        m.IsOpen = true;
    }

    private void TrCopy_Click(object s, RoutedEventArgs e)
    {
        if (TrOut.Text.Length == 0) return;
        CopyBack(TrOut.Text);
        TrCopy.Content = Loc.T("Скопировано");
        Sfx.Ui("tap");
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
        t.Tick += (_, _) => { t.Stop(); TrCopy.Content = Loc.T("Копировать"); };
        t.Start();
    }

    // перевод на место выделенного в исходной программе
    private async void TrPaste_Click(object s, RoutedEventArgs e)
    {
        if (TrOut.Text.Length == 0 || _trSrc == IntPtr.Zero) return;
        string before = SafeClipText();
        CopyBack(TrOut.Text);
        _trOpen = false;
        ApplyMode(Rest());
        SetForegroundWindow(_trSrc);
        await Task.Delay(120);
        SendCombo(0x56);   // Ctrl+V
        await Task.Delay(250);
        if (before.Length > 0) CopyBack(before);   // буфер как был
    }

    private static string SafeClipText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : ""; } catch { return ""; }
    }

    private static void SendCombo(byte key)
    {
        keybd_event(0x11, 0, 0, UIntPtr.Zero);           // Ctrl
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 2, UIntPtr.Zero);
        keybd_event(0x11, 0, 2, UIntPtr.Zero);
    }

    // Ctrl+Alt+Q: забрать выделенное из активной программы
    private async void QuickTranslate()
    {
        try
        {
            var fg = GetForegroundWindow();
            GetWindowThreadProcessId(fg, out uint pid);
            if (pid == OwnPid) { OpenTranslator(null, IntPtr.Zero); return; }
            // ждём, пока отпустят Ctrl, Alt и Q, иначе наш Ctrl+C превратится в Ctrl+Alt+C
            for (int i = 0; i < 40 && ((GetAsyncKeyState(0x12) & 0x8000) != 0 || (GetAsyncKeyState(0x11) & 0x8000) != 0 || (GetAsyncKeyState(0x51) & 0x8000) != 0); i++)
                await Task.Delay(25);
            string before = SafeClipText();
            uint seq = GetClipboardSequenceNumber();
            _ignoreClipUntil = DateTime.UtcNow.AddSeconds(2);   // в историю буфера это не попадает
            SendCombo(0x43);   // Ctrl+C
            string? sel = null;
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(25);
                if (GetClipboardSequenceNumber() != seq) { await Task.Delay(30); sel = SafeClipText(); break; }
            }
            if (sel != null && before.Length > 0) { _ignoreClipUntil = DateTime.UtcNow.AddSeconds(1); CopyBack(before); }
            OpenTranslator(string.IsNullOrWhiteSpace(sel) ? null : sel.Trim(), fg);
        }
        catch (Exception ex) { App.LogOnce("QuickTranslate", ex); OpenTranslator(null, IntPtr.Zero); }
    }
}
