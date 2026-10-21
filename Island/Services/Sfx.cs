using System.IO;
using NAudio.Wave;

namespace Island;

// Звуки приветствия. Файлов нет: всё синтезируется в памяти при первом проигрывании (колокольчики, «вжух», эхо)
// и играется через NAudio. Если звуковой карты нет или что-то пошло не так, просто тишина.
internal static class Sfx
{
    private const int SR = 44100;
    private static readonly Dictionary<string, byte[]> Cache = new();

    // C5 D5 E5 G5 A5 C6 D6 E6: пентатоника, любые две ноты рядом звучат красиво
    private static readonly double[] Penta = { 523.25, 587.33, 659.25, 783.99, 880.00, 1046.50, 1174.66, 1318.51 };

    // ---------- что играем ----------

    // начало приветствия: нарастающее «вжух» и искрящееся арпеджио с эхом
    public static void Intro() => Play("intro", 0.9f, MakeIntro);

    private static float[] MakeIntro()
    {
        var b = new float[(int)(4.2 * SR)];
        Whoosh(b, 0.0, 1.15, 0.55);
        double[] arp = { 523.25, 659.25, 783.99, 1046.50, 1318.51 };
        for (int i = 0; i < arp.Length; i++) Bell(b, 0.62 + i * 0.095, arp[i], 0.30, 1.6);
        Bell(b, 1.12, 783.99, 0.16, 2.6);
        Bell(b, 1.12, 1046.50, 0.16, 2.6);
        Bell(b, 1.12, 1318.51, 0.14, 2.6);
        Bell(b, 1.12, 2093.00, 0.06, 2.0);
        Echo(b, 0.26, 0.38, 4);
        return b;
    }

    // каждое новое «hello»: следующая нота пентатоники
    public static void Greeting(int i) => Play("greet" + (i % Penta.Length), 0.75f, () =>
    {
        var b = new float[(int)(2.4 * SR)];
        double f = Penta[i % Penta.Length];
        Bell(b, 0, f, 0.42, 1.0);
        Bell(b, 0.05, f * 2, 0.10, 0.7);
        Echo(b, 0.22, 0.30, 3);
        return b;
    });

    // «шаг вперёд» и «шаг назад» в первой настройке
    public static void Next() => Play("next", 0.8f, () =>
    {
        var b = new float[(int)(1.4 * SR)];
        Bell(b, 0, 783.99, 0.34, 0.5);
        Bell(b, 0.075, 1174.66, 0.34, 0.7);
        Echo(b, 0.16, 0.25, 2);
        return b;
    });

    public static void Back() => Play("back", 0.7f, () =>
    {
        var b = new float[(int)(1.4 * SR)];
        Bell(b, 0, 1174.66, 0.28, 0.45);
        Bell(b, 0.075, 783.99, 0.28, 0.6);
        Echo(b, 0.16, 0.22, 2);
        return b;
    });

    // маленький «поп» на выбор плитки или переключатель
    public static void Pop() => Play("pop", 0.6f, () =>
    {
        var b = new float[(int)(0.5 * SR)];
        for (int i = 0; i < b.Length; i++)
        {
            double t = (double)i / SR;
            double f = 520 + 900 * Math.Exp(-t * 38);   // быстро падающий тон: «блип»
            b[i] = (float)(Math.Sin(2 * Math.PI * f * t) * Math.Exp(-t * 26) * 0.5);
        }
        return b;
    });

    // конец настройки: восходящее арпеджио и долгий аккорд
    public static void Done() => Play("done", 0.9f, () =>
    {
        var b = new float[(int)(4.0 * SR)];
        double[] arp = { 523.25, 659.25, 783.99, 1046.50, 1318.51, 1567.98 };
        for (int i = 0; i < arp.Length; i++) Bell(b, i * 0.075, arp[i], 0.30, 1.5);
        Bell(b, 0.5, 523.25, 0.20, 3.0);
        Bell(b, 0.5, 783.99, 0.18, 3.0);
        Bell(b, 0.5, 1046.50, 0.18, 3.0);
        Bell(b, 0.5, 1318.51, 0.14, 3.0);
        Echo(b, 0.24, 0.36, 4);
        return b;
    });

    // чтобы первый звук не ждал синтеза: считаем заранее в фоне
    public static void Warmup() => Task.Run(() =>
    {
        try { Prepare("intro", MakeIntro); }
        catch { }
    });

    // напоминание о заметке (шаг 53): заметный, но мягкий «динь-динь» из трёх нот, звучит даже без звуков интерфейса
    public static void Reminder() => Play("reminder", 0.7f, () =>
    {
        var b = new float[(int)(2.2 * SR)];
        Bell(b, 0, 1318.51, 0.34, 1.0);
        Bell(b, 0.16, 1046.50, 0.30, 1.0);
        Bell(b, 0.32, 1567.98, 0.34, 1.6);
        Bell(b, 0.32, 783.99, 0.14, 1.6);
        Echo(b, 0.2, 0.28, 2);
        return b;
    });

    // ---------- звуки интерфейса острова (шаг 49) ----------
    // Короткие и тихие: раскрытие и закрытие, уведомление, свайп, пузырь, нажатие. Громкость из настроек (0 = выкл).
    public static float UiLevel = 0.22f;
    private static readonly Dictionary<string, DateTime> UiLast = new();

    public static void Ui(string kind)
    {
        if (UiLevel <= 0) return;
        lock (UiLast)
        {
            // один и тот же звук не чаще раза в 70 мс: быстрые клики не превращаются в треск
            if (UiLast.TryGetValue(kind, out var t) && (DateTime.UtcNow - t).TotalMilliseconds < 70) return;
            UiLast[kind] = DateTime.UtcNow;
        }
        Func<float[]>? make = kind switch
        {
            "open" => () => Tones(0.45, (0, 1318.51, 0.30, 0.32), (0.045, 1760.0, 0.26, 0.38)),
            "close" => () => Tones(0.4, (0, 1567.98, 0.26, 0.28), (0.045, 1174.66, 0.24, 0.32)),
            "notif" => () => { var b = Tones(1.1, (0, 1046.50, 0.34, 0.7), (0.11, 1567.98, 0.30, 0.9)); Echo(b, 0.13, 0.22, 2); return b; },
            "tap" => MakeTap,
            "swipe" => () => MakeSwoosh(0.2, true),
            "dismiss" => () => MakeSwoosh(0.16, false),
            "bubble" => MakeBloop,
            "unlock" => () => { var b = Tones(1.6, (0, 1046.50, 0.3, 0.9), (0.08, 1318.51, 0.3, 0.9), (0.16, 1567.98, 0.3, 1.0), (0.24, 2093.0, 0.22, 1.2)); Echo(b, 0.16, 0.25, 2); return b; },
            _ => null
        };
        if (make == null) return;
        // «колокольчики» громче шумов: выравниваем на слух
        float k = kind is "tap" or "swipe" or "dismiss" ? 0.8f : 0.55f;
        Play("ui-" + kind, UiLevel * k, make);
    }

    private static float[] Tones(double len, params (double at, double f, double amp, double decay)[] notes)
    {
        var b = new float[(int)(len * SR)];
        foreach (var n in notes) Bell(b, n.at, n.f, n.amp, n.decay);
        return b;
    }

    // мягкий щелчок: очень короткий высокий тон и щепотка шума
    private static float[] MakeTap()
    {
        var b = new float[(int)(0.09 * SR)];
        var rnd = new Random(3);
        for (int i = 0; i < b.Length; i++)
        {
            double t = (double)i / SR;
            double tone = Math.Sin(2 * Math.PI * 2200 * t) * Math.Exp(-t * 90);
            double noise = (rnd.NextDouble() * 2 - 1) * Math.Exp(-t * 260) * 0.35;
            b[i] = (float)((tone + noise) * 0.5);
        }
        return b;
    }

    // «шух»: шум через фильтр, который едет вверх (свайп) или вниз (смахнуть)
    private static float[] MakeSwoosh(double dur, bool up)
    {
        var b = new float[(int)((dur + 0.05) * SR)];
        var rnd = new Random(11);
        int n = (int)(dur * SR);
        double y = 0, y2 = 0;
        for (int i = 0; i < n; i++)
        {
            double u = (double)i / n;
            double cutoff = up ? 700 + 5200 * u : 5200 - 4300 * u;
            double a = 1 - Math.Exp(-2 * Math.PI * cutoff / SR);
            y += a * ((rnd.NextDouble() * 2 - 1) - y);
            y2 += a * (y - y2);
            b[i] = (float)(y2 * 4.0 * Math.Pow(Math.Sin(Math.PI * u), 1.5));
        }
        return b;
    }

    // «буль»: тон быстро скользит вверх, как пузырёк
    private static float[] MakeBloop()
    {
        var b = new float[(int)(0.22 * SR)];
        double phase = 0;
        for (int i = 0; i < b.Length; i++)
        {
            double t = (double)i / SR;
            double f = 380 + 900 * (1 - Math.Exp(-t * 28));
            phase += 2 * Math.PI * f / SR;
            b[i] = (float)(Math.Sin(phase) * Math.Min(1, t / 0.006) * Math.Exp(-t * 22) * 0.6);
        }
        return b;
    }

    // ---------- синтез ----------

    // колокольчик: несколько обертонов, высокие затухают быстрее; decay: через столько секунд звука почти нет
    private static void Bell(float[] buf, double start, double freq, double amp, double decay)
    {
        double[] ratio = { 1.0, 2.0, 3.01, 4.17, 5.43 };
        double[] gain = { 1.0, 0.42, 0.24, 0.11, 0.05 };
        double[] speed = { 1.0, 1.4, 2.0, 2.8, 3.6 };
        int s0 = (int)(start * SR);
        int len = (int)(decay * SR);
        double tau = decay / 4.5;
        for (int i = 0; i < len; i++)
        {
            int o = s0 + i;
            if (o >= buf.Length) break;
            double t = (double)i / SR;
            double att = Math.Min(1.0, t / 0.004);
            double v = 0;
            for (int p = 0; p < ratio.Length; p++)
            {
                double fr = freq * ratio[p];
                if (fr > 11000) break;
                v += gain[p] * Math.Exp(-t * speed[p] / tau) * Math.Sin(2 * Math.PI * fr * t);
            }
            buf[o] += (float)(v * amp * att);
        }
    }

    // «вжух»: шум, который открывается снизу вверх, плюс тихий растущий тон
    private static void Whoosh(float[] buf, double start, double dur, double amp)
    {
        var rnd = new Random(7);
        int s0 = (int)(start * SR);
        int n = (int)(dur * SR);
        double y = 0, y2 = 0, phase = 0;
        for (int i = 0; i < n; i++)
        {
            int o = s0 + i;
            if (o >= buf.Length) break;
            double u = (double)i / n;
            double cutoff = 260 + 5600 * u * u;
            double a = 1 - Math.Exp(-2 * Math.PI * cutoff / SR);
            double noise = rnd.NextDouble() * 2 - 1;
            y += a * (noise - y);
            y2 += a * (y - y2);   // два каскада: мягче срез
            double env = Math.Pow(Math.Sin(Math.PI * Math.Pow(u, 0.8)), 2);
            double f = 220 * Math.Pow(4, u);
            phase += 2 * Math.PI * f / SR;
            buf[o] += (float)((y2 * 5.0 + Math.Sin(phase) * 0.12) * env * amp);
        }
    }

    // эхо: несколько затухающих повторов исходного звука, каждый тише предыдущего
    private static void Echo(float[] buf, double delaySec, double feedback, int repeats)
    {
        int d = (int)(delaySec * SR);
        var dry = (float[])buf.Clone();
        for (int r = 1; r <= repeats; r++)
        {
            double g = Math.Pow(feedback, r);
            int off = d * r;
            for (int i = off; i < buf.Length; i++) buf[i] += (float)(dry[i - off] * g);
        }
    }

    // ---------- проигрывание ----------

    private static byte[] ToPcm(float[] f)
    {
        float peak = 0;
        foreach (var v in f) peak = Math.Max(peak, Math.Abs(v));
        float k = peak > 0.0001f ? 0.8f / peak : 1f;
        var bytes = new byte[f.Length * 2];
        int fade = (int)(0.04 * SR);
        for (int i = 0; i < f.Length; i++)
        {
            float v = f[i] * k;
            int left = f.Length - i;
            if (left < fade) v *= (float)left / fade;   // без щелчка на конце
            short s = (short)Math.Clamp((int)(v * 32767), -32768, 32767);
            bytes[i * 2] = (byte)(s & 0xFF);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return bytes;
    }

    private static byte[] Prepare(string key, Func<float[]> make)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var have)) return have;
            var data = ToPcm(make());
            Cache[key] = data;
            return data;
        }
    }

    private static void Play(string key, float volume, Func<float[]> make)
    {
        Task.Run(() =>
        {
            try
            {
                var data = Prepare(key, make);
                var raw = new RawSourceWaveStream(new MemoryStream(data), new WaveFormat(SR, 16, 1));
                // звуки интерфейса должны звучать сразу после нажатия: короткий буфер вместо обычных 300 мс
                var wo = key.StartsWith("ui-") ? new WaveOutEvent { DesiredLatency = 80, NumberOfBuffers = 2 } : new WaveOutEvent();
                wo.Init(raw);
                wo.Volume = Math.Clamp(volume, 0f, 1f);
                wo.PlaybackStopped += (_, _) =>
                {
                    try { wo.Dispose(); } catch { }
                    try { raw.Dispose(); } catch { }
                };
                wo.Play();
            }
            catch { /* нет звуковой карты: без звука */ }
        });
    }
}
