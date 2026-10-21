using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Act = (string kind, string text, double frac, System.Windows.Media.Color col, string icon, bool paused);

namespace Island;

// Стек активностей (шаг 61). Раньше пузырь показывал одну вещь: первую по списку, остальные ждали.
// Теперь всё, что идёт, собирается в стек по важности: звонок > фокус > скачивание и копирование > таймер > секундомер >
// часы, погода или батарея («Пузырь всегда»). Музыка живёт в самом острове, поэтому звонок и всё остальное идут рядом с ней.
// В пузыре видно одно, но держится до трёх сразу: справа точки, свайп по пузырю влево-вправо или колесо мыши листают.
// Появилось что-то важнее выбранного (начался звонок) — пузырь сам переключается на него; выбранное закончилось — на самое важное.
public partial class MainWindow
{
    private const int StackMax = 3;
    private string _stackSel = "";                         // выбранное свайпом; "" — самое важное
    private string[] _stackKinds = Array.Empty<string>();  // что было в стеке в прошлый раз (чтобы заметить новое)
    private double _dlFrac = -1;                           // скачивание обновления, 0..1 (Update.cs); -1 — не идёт
    private static readonly Color DlC = Color.FromRgb(0x0A, 0x84, 0xFF);
    private Point? _stackDown;
    private bool _stackSwiped;

    // шаг 62: «готово» после копирования — короткая активность со всплеском волны и галочкой
    private DateTime _doneUntil;
    private static readonly Color DoneC = Color.FromRgb(0x30, 0xD1, 0x58);
    private readonly System.Windows.Threading.DispatcherTimer _doneTimer = new() { Interval = TimeSpan.FromMilliseconds(1900) };
    private List<Act> _stackActs = new();

    private string _doneText = "", _doneIcon = "check";
    private Color _doneCol = DoneC;

    // «готово» со всплеском; шаг 65: свой текст и цвет (итог сборки)
    private void TriggerDone(string? text = null, Color? col = null, string icon = "check")
    {
        _doneText = text ?? Loc.T("Готово");
        _doneCol = col ?? DoneC;
        _doneIcon = icon;
        _doneUntil = DateTime.UtcNow + _doneTimer.Interval;
        _stackSel = "";
        UpdateBubble();
        BubbleWave.Complete();
        _doneTimer.Stop();
        _doneTimer.Start();
    }

    // прогресс жидкостью, а не кольцом: копирование, скачивание, сборка, загрузки браузера и «готово»
    private static bool IsLiquid(string kind) => kind is "copy" or "dl" or "done" or "build" or "steam" || kind.StartsWith("web:", StringComparison.Ordinal);

    // остальные прогрессы стека рядом маленькими волнами (скачивание и копирование сразу)
    private void MultiWaves(string shown)
    {
        var rest = _stackActs.Where(a => IsLiquid(a.kind) && a.kind != "done" && a.kind != shown).ToList();
        BubbleMulti.Visibility = rest.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        while (BubbleMulti.Children.Count > rest.Count) BubbleMulti.Children.RemoveAt(BubbleMulti.Children.Count - 1);
        for (int i = 0; i < rest.Count; i++)
        {
            if (BubbleMulti.Children.Count <= i)
                BubbleMulti.Children.Add(new WaveFill { Width = 14, Height = 14, Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0) });
            var wv = (WaveFill)BubbleMulti.Children[i];
            wv.Color = rest[i].col;
            wv.Value = rest[i].frac;
        }
    }

    private void InitActivities()
    {
        _doneTimer.Tick += (_, _) => { _doneTimer.Stop(); UpdateBubble(); };
        Bubble.MouseMove += (_, e) =>
        {
            if (_stackDown is not { } p0 || e.LeftButton != MouseButtonState.Pressed) return;
            double dx = e.GetPosition(this).X - p0.X;
            if (Math.Abs(dx) < 22) return;
            _stackDown = null;
            _stackSwiped = true;
            StackSwitch(dx < 0 ? 1 : -1);
        };
        Bubble.MouseWheel += (_, e) =>
        {
            if (_stackKinds.Length < 2) return;
            e.Handled = true;
            StackSwitch(e.Delta < 0 ? 1 : -1);
        };
    }

    private void StackPress(MouseButtonEventArgs e)
    {
        _stackDown = e.GetPosition(this);
        _stackSwiped = false;
        Bubble.CaptureMouse();
    }

    // true: отпустили после свайпа, клик не нужен
    private bool StackRelease()
    {
        _stackDown = null;
        if (Bubble.IsMouseCaptured) Bubble.ReleaseMouseCapture();
        bool swiped = _stackSwiped;
        _stackSwiped = false;
        return swiped;
    }

    // всё, что идёт сейчас, по убыванию важности
    private List<Act> Activities()
    {
        var list = new List<Act>();
        // шаг 76: «Отдельный пузырь» выключен — пузыря нет совсем. Раньше выключались только таймер и копирование,
        // а микрофон в Discord, вкладка браузера, сборка, Steam и остальное продолжали всплывать рядом с островом
        if (!_cfg.BubbleSplit) return list;
        if (DateTime.UtcNow < _doneUntil) list.Add(("done", _doneText, 1, _doneCol, _doneIcon, false));
        if (MicState() is { } mic) list.Add(mic);
        if (FocusState() is { } focus) list.Add(focus);
        if (_dlFrac >= 0) list.Add(("dl", (int)(_dlFrac * 100) + "%", _dlFrac, DlC, "download", false));
        if (SteamState() is { } steam) list.Add(steam);   // шаг 68: загрузка игры в Steam (SteamDl.cs)
        var ctx = ContextStates().ToList();   // шаг 65: сборка и загрузки браузера — как скачивание, программа — ниже таймеров (Context.cs)
        list.AddRange(ctx.Where(a => a.kind != "ctx"));
        if (StreamState() is { } live) list.Add(live);   // шаг 67: чат стрима (Streamer.cs)
        if (_cfg.BubbleSplit) list.AddRange(BubbleStates());
        list.AddRange(ctx.Where(a => a.kind == "ctx"));
        if (_cfg.BubbleAlways && IdleState() is { } idle) list.Add(idle);
        return list.Count > StackMax ? list.GetRange(0, StackMax) : list;
    }

    // что показать в пузыре; заодно точки стека
    private Act? PickActivity()
    {
        var list = Activities();
        _stackActs = list;
        var kinds = list.Select(a => a.kind).ToArray();
        if (_stackSel.Length > 0)
        {
            int cur = Array.IndexOf(kinds, _stackSel);
            // выбранное закончилось или появилось что-то важнее: к самому важному
            if (cur < 0 || kinds.Take(cur).Any(k => !_stackKinds.Contains(k))) _stackSel = "";
        }
        _stackKinds = kinds;
        int idx = Math.Max(0, Array.IndexOf(kinds, _stackSel));
        StackDots(kinds.Length, idx);
        return list.Count == 0 ? null : list[idx];
    }

    private void StackSwitch(int dir)
    {
        int n = _stackKinds.Length;
        if (n < 2) return;
        int cur = Math.Max(0, Array.IndexOf(_stackKinds, _stackSel));
        int next = (cur + dir + n) % n;
        _stackSel = next == 0 ? "" : _stackKinds[next];
        Sfx.Ui("tap");
        UpdateBubble();
        // содержимое въезжает с той стороны, куда листали
        var d = TimeSpan.FromMilliseconds(380);
        BubbleBodyMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dir * 14, 0, d) { EasingFunction = Spr() });
        BubbleBody.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(220)));
    }

    private void StackDots(int n, int sel)
    {
        BubbleDots.Visibility = n > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (n < 2) return;
        if (BubbleDots.Children.Count != n)
        {
            BubbleDots.Children.Clear();
            for (int i = 0; i < n; i++)
                BubbleDots.Children.Add(new Ellipse { Width = 4, Height = 4, Margin = new Thickness(0, 1.5, 0, 1.5) });
        }
        for (int i = 0; i < n; i++)
            ((Ellipse)BubbleDots.Children[i]).Fill = new SolidColorBrush(i == sel ? Colors.White : Color.FromArgb(0x55, 255, 255, 255));
    }
}
