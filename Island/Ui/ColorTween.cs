using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Island;

// Плавная смена цвета без ColorAnimation (шаг 48).
// Анимированный цвет WPF передаёт в поток отрисовки отдельным ресурсом, и в WPF есть ошибка: этот ресурс иногда
// сообщает об изменении уже уничтоженной кисти или эффекту, и поток отрисовки падает (_purecall в
// CMilSlaveResource::NotifyOnChanged, процесс закрывается без исключения и без crash.log). Так падал ShyFlow.
// Здесь цвет просто выставляется на каждом кадре: для WPF это обычные изменения значения, без анимационного ресурса.
// Вызов повторяет BeginAnimation: ColorTween.To(кисть, SolidColorBrush.ColorProperty, new ColorAnimation(цвет, время)).
internal static class ColorTween
{
    private sealed class Run
    {
        public required DependencyObject Target;
        public required DependencyProperty Prop;
        public Color From, To, Last;
        public double Start, Delay, Dur;
        public IEasingFunction? Ease;
    }

    private static readonly Dictionary<(DependencyObject, DependencyProperty), Run> Runs = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static bool _hooked;

    public static void To(DependencyObject target, DependencyProperty prop, ColorAnimation a)
    {
        if (target is Freezable { IsFrozen: true }) return;
        // старая анимация через часы держала бы значение поверх нашего
        if (target is IAnimatable an && an.HasAnimatedProperties) an.BeginAnimation(prop, null);
        var cur = (Color)target.GetValue(prop);
        var to = a.To ?? cur;
        double dur = a.Duration.HasTimeSpan ? a.Duration.TimeSpan.TotalMilliseconds : 0;
        double delay = a.BeginTime?.TotalMilliseconds ?? 0;
        if (dur <= 1 && delay <= 0) { Set(target, prop, to); return; }
        var run = new Run
        {
            Target = target, Prop = prop, From = a.From ?? cur, To = to, Last = cur,
            Start = Clock.Elapsed.TotalMilliseconds, Delay = delay, Dur = Math.Max(1, dur), Ease = a.EasingFunction
        };
        Runs[(target, prop)] = run;
        if (!_hooked) { CompositionTarget.Rendering += Tick; _hooked = true; }
    }

    // сразу выставить цвет и остановить переход
    public static void Set(DependencyObject target, DependencyProperty prop, Color c)
    {
        Runs.Remove((target, prop));
        if (target is Freezable { IsFrozen: true }) return;
        if (target is IAnimatable an && an.HasAnimatedProperties) an.BeginAnimation(prop, null);
        target.SetValue(prop, c);
    }

    private static void Tick(object? sender, EventArgs e)
    {
        double now = Clock.Elapsed.TotalMilliseconds;
        List<(DependencyObject, DependencyProperty)>? done = null;
        foreach (var (key, r) in Runs)
        {
            try
            {
                // цвет поменяли в обход перехода: он главнее, переход больше не трогаем
                if ((Color)r.Target.GetValue(r.Prop) != r.Last) { (done ??= new()).Add(key); continue; }
                double t = (now - r.Start - r.Delay) / r.Dur;
                if (t < 0) continue;
                if (t >= 1) t = 1;
                double k = r.Ease?.Ease(t) ?? t;
                var c = t >= 1 ? r.To : Lerp(r.From, r.To, k);
                r.Target.SetValue(r.Prop, c);
                r.Last = c;
                if (t >= 1) (done ??= new()).Add(key);
            }
            catch (Exception ex) { App.LogOnce("ColorTween", ex); (done ??= new()).Add(key); }
        }
        if (done != null) foreach (var k in done) Runs.Remove(k);
        if (Runs.Count == 0) { CompositionTarget.Rendering -= Tick; _hooked = false; }
    }

    private static Color Lerp(Color a, Color b, double k)
    {
        byte L(byte x, byte y) => (byte)Math.Clamp(Math.Round(x + (y - x) * k), 0, 255);
        return Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }
}
