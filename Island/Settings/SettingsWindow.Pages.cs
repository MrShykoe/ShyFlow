using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace Island;

public sealed partial class SettingsWindow
{
    // Шаг 43: настройки разложены по смыслу на 7 разделов (раньше почти всё лежало в «Остров»).
    // Наверху каждого раздела строка «что здесь», чтобы не искать по всем вкладкам.
    private static void Intro(StackPanel p, string text) => p.Children.Add(new TextBlock
    {
        Text = text, Foreground = Gray, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 4, 14, 0),
        Tag = "intro"   // поиск такие строки не показывает
    });

    private void InfoRow(StackPanel c, string text)
    {
        Divider(c);
        c.Children.Add(new Border { Padding = new Thickness(16, 8, 14, 11), Child = RowSub(text) });
    }

    // ================= ОСНОВНОЕ =================
    private void BuildGeneralPage(StackPanel p)
    {
        Intro(p, "Язык, монитор, запуск вместе с Windows и Discord");
        StackPanel c = Card(p, "ГЛАВНОЕ");
        Choice(c, "Язык интерфейса", Loc.Names.Select((n, i) => i == 0 ? Loc.T("Авто") : n).ToArray(), () => _s.Language, v => { _s.Language = v; ReopenSoon(); });   // шаг 74: 7 вариантов
        {
            var mons = Monitors.All();
            // шаг 74: первым пунктом «Где активное окно» (Monitor = -1): остров переезжает за ним (Main\Screens.cs)
            if (mons.Count > 1)
                Choice(c, "Монитор", new[] { Loc.T("Где активное окно", "Active window") }
                        .Concat(mons.Select((m, i) => (i == 0 ? Loc.T("Основной") : Loc.T("Монитор") + " " + (i + 1)) + $" ({m.Width}×{m.Height})")).ToArray(),
                    () => _s.Monitor < 0 ? 0 : _s.Monitor + 1, v => _s.Monitor = v - 1);
        }
        // шаг 74, 76: в играх и видео на весь экран (Main\Screens.cs)
        Choice(c, "В играх и видео на весь экран", new[] { "Тонкая полоска", "Прятать совсем", "Как обычно" },
            () => !_s.HideFullscreen ? 2 : Math.Clamp(_s.FsStyle, 0, 1), v => { _s.HideFullscreen = v != 2; if (v != 2) _s.FsStyle = v; });
        InfoRow(c, "Тонкая полоска: остров сжимается в короткую полупрозрачную черту, плашки и уведомления всё равно появляются. Ctrl+Alt+I показывает остров целиком");
        Row(c, "Часы, когда музыка не играет", "Время и дата в свёрнутом острове вместо «Ничего не играет»", _s.ShowClock, v => _s.ShowClock = v);
        c = Card(p, "ЗАПУСК");
        Row(c, "Запускать вместе с Windows", null, _s.Autostart, v => _s.Autostart = v);
        Row(c, "Приветствие при запуске", "Окно с «hello» и первой настройкой. Выключается кнопкой «Больше не показывать» в его конце", !_s.HideWelcome, v => _s.HideWelcome = !v);
        ActionRow(c, "Приветствие и первая настройка", "Показать ещё раз", "Открыть", () => new WelcomeWindow(_s, _changed).Show());
        Row(c, "Советы в острове", "Пара коротких подсказок прямо в острове, когда функция пригодится впервые: свайпы, колесо, ★ и меню", _s.Tips, v => _s.Tips = v);   // шаг 73 (Main\Tips.cs)
        if (AppInfo.DiscordAppId.Length > 0)
        {
            c = Card(p, "DISCORD");
            Row(c, "Показывать в Discord", "Что ты слушаешь, видно в профиле Discord. Нужен запущенный Discord на этом компьютере", _s.DiscordRpc, v => _s.DiscordRpc = v);
        }
    }

    // ================= ВНЕШНИЙ ВИД =================
    private void BuildLookPage(StackPanel p)
    {
        Intro(p, "Форма острова, notch, эквалайзер, значки приложений, пузырь, обложка, свой фон, шрифт и стикер");
        StackPanel c = Card(p, "ОСТРОВ");
        Choice(c, "Положение", new[] { "По центру", "Слева", "Справа" }, () => _s.IslandPos, v => _s.IslandPos = v);   // шаг 74 (Main\Display.cs)
        Row(c, "Режим notch", "Остров прижат к верхнему краю экрана, как вырез на iPhone и MacBook: углы плавно перетекают в рамку. Жидкие анимации в этом режиме отключаются", _s.NotchMode, v => _s.NotchMode = v);
        SliderRow(c, "Notch: переход к рамке", "Насколько плавно вырез перетекает в край экрана: слева почти прямой угол, справа широкий изгиб", () => _s.NotchEarK, v => _s.NotchEarK = v);   // шаг 76
        SliderRow(c, "Notch: скругление углов", "Нижние углы выреза: слева почти острые, справа круглые", () => _s.NotchRoundK, v => _s.NotchRoundK = v);
        Choice(c, "Стиль морфинга", new[] { "Жидкий", "Стекло", "Упругий" }, () => _s.MorphStyle, v => _s.MorphStyle = v);
        InfoRow(c, "Жидкий: тянется и деформируется, как капля. Стекло: чётко и плавно, без перехлёста. Упругий: заметный отскок и «желе» на каждую смену вида");
        Row(c, "Жидкие анимации", "Растяжения и капли в жидком стиле: раскрытие, сворачивание, плашки, нажатия", _s.LiquidFx, v => _s.LiquidFx = v);
        Choice(c, "Сила жидкости", new[] { "Мягко", "Средне", "Сильно" }, () => _s.LiquidPower, v => _s.LiquidPower = v);
        SliderRow(c, "Желеобразность", "Насколько остров колышется и отскакивает, когда меняет размер", () => _s.Jelly, v => _s.Jelly = v);
        c = Card(p, "ЗВУКИ");
        Row(c, "Звуки интерфейса", "Тихие щелчки и колокольчики: раскрытие и закрытие, уведомления, свайпы, пузырь таймера", _s.UiSounds, v => _s.UiSounds = v);
        Choice(c, "Громкость звуков", new[] { "Тихо", "Средне", "Громче" }, () => _s.UiSoundVol, v => _s.UiSoundVol = v);
        Row(c, "Цвет из обложки везде", "Прогресс, эквалайзер и переключатели красятся в цвет обложки. Выключено: всё белое", _s.AccentUi, v => _s.AccentUi = v);
        Row(c, "Размытая обложка на фоне", "Фон острова из обложки играющего трека", _s.CoverTint, v => _s.CoverTint = v);
        c = Card(p, "ЭКВАЛАЙЗЕР");
        Row(c, "Эквалайзер в острове", "Полоски справа в свёрнутом и развёрнутом острове", _s.Equalizer, v => _s.Equalizer = v);
        Choice(c, "Стиль", new[] { "Столбики", "Волна", "Круги", "Зеркало", "Пики", "Пульс", "Матрица" }, () => _s.EqStyle, v => _s.EqStyle = v);
        c = Card(p, "ЗНАЧКИ ПРИЛОЖЕНИЙ");
        Choice(c, "Где показывать", new[] { "Выкл", "Слева от острова", "Справа от острова", "В развёрнутом острове" }, () => _s.AppsPlace, v => _s.AppsPlace = v);
        Choice(c, "Док снизу", new[] { "Выкл", "Остров", "Вырез" }, () => _s.DockBottom, v => _s.DockBottom = v);   // шаг 76 (Main\Dock.cs)
        Row(c, "Плашка при запуске и закрытии", "«Открыто: …» и «Закрыто: …» с иконкой программы", _s.AppLaunchToast, v => _s.AppLaunchToast = v);
        Row(c, "Фон как у острова", "Размытая обложка или свой фон и под значками. Выключено: просто цвет темы", _s.AppsIslandBg, v => _s.AppsIslandBg = v);
        InfoRow(c, "Открытые программы, как на панели задач. Клик: переключиться (активная сворачивается), несколько окон: клик по очереди " +
                   "переключает между ними. Средний клик: закрыть. Правый клик: меню (свернуть, показать в проводнике, о программе, завершить)");
        c = Card(p, "ПУЗЫРЬ РЯДОМ С ОСТРОВОМ");
        Row(c, "Отдельный пузырь", "Кружок рядом с островом, как в iOS: таймер, копирование, микрофон в звонке, Discord, браузер, сборки и остальное. Выключено: пузыря нет совсем, таймер — внутри острова", _s.BubbleSplit, v => _s.BubbleSplit = v);
        Choice(c, "С какой стороны", new[] { "Справа", "Слева" }, () => _s.BubbleSide, v => _s.BubbleSide = v);
        Row(c, "Таймер", "Сколько осталось и кольцо", _s.BubbleTimer, v => _s.BubbleTimer = v);
        Row(c, "Секундомер", null, _s.BubbleWatch, v => _s.BubbleWatch = v);
        Row(c, "Копирование файлов", "Проценты, пока Проводник копирует. Клик по пузырю открывает окно копирования", _s.BubbleCopy, v => _s.BubbleCopy = v);
        Row(c, "Фон как у острова", "Размытая обложка или свой фон. Выключено: просто цвет темы", _s.BubbleIslandBg, v => _s.BubbleIslandBg = v);
        Row(c, "Микрофон во время звонка", "Пока микрофон кто-то использует (Discord, браузер), пузырь с его именем; клик выключает и включает микрофон", _s.BubbleMic, v => _s.BubbleMic = v);
        Row(c, "Остров понимает, что ты делаешь", "В пузыре: в VS Code и Rider ветка git и изменения, идущая сборка и её итог; в Discord быстрые mute и deafen; в браузере вкладка и живые загрузки", _s.ContextAware, v => _s.ContextAware = v);
        Row(c, "Загрузки Steam", "Пока Steam качает игру, в пузыре процент, название и скорость; конец — «Установлено». Процент примерный: Steam не сообщает его наружу", _s.SteamDownloads, v => _s.SteamDownloads = v);
        // шаг 67: режим стримера (Main\Streamer.cs)
        c = Card(p, "РЕЖИМ СТРИМЕРА");
        Row(c, "Режим стримера", "Чат Twitch под островом (мышь проходит насквозь) и «LIVE» в пузыре; клик по нему прячет чат", _s.StreamerMode, v => _s.StreamerMode = v);
        TextRow(c, "Канал Twitch", "Ник или ссылка twitch.tv/ник. Чат читается анонимно, вход не нужен", "ник канала", () => _s.TwitchChannel ?? "", v => _s.TwitchChannel = v);
        Row(c, "Прятать текст уведомлений", "Пока режим включён, уведомления всплывают без текста, чтобы личное не попало в эфир. В шторке текст остаётся", _s.StreamerHideNotifs, v => _s.StreamerHideNotifs = v);
        Row(c, "Пузырь всегда", "Когда таймер и копирование не идут, в пузыре своё. Таймер и копирование всё равно важнее", _s.BubbleAlways, v => _s.BubbleAlways = v);
        Choice(c, "Что в нём", new[] { "Время", "Дата и время", "Заряд батареи", "Погода" }, () => _s.BubbleIdle, v => _s.BubbleIdle = v);
        TextRow(c, "Город для погоды", "Пусто: примерно по интернет-адресу. Погода с open-meteo.com, без регистрации", "по IP", () => _s.WeatherCity ?? "", v => _s.WeatherCity = v);
        c = Card(p, "ОБЛОЖКА И ПРОГРЕСС");
        Choice(c, "Форма обложки в свёрнутом острове", new[] { "Круг", "Скруглённая", "Квадрат", "Лепесток", "Шестиугольник", "Ромб" }, () => _s.CoverShape, v => _s.CoverShape = v);
        Row(c, "Переворот обложки", "При смене трека обложка переворачивается на другую сторону", _s.CoverFlip, v => _s.CoverFlip = v);
        Row(c, "Полоска прогресса", "Тонкая линия внизу свёрнутого острова", _s.MiniProgress, v => _s.MiniProgress = v);
        Row(c, "Прогресс волной", "Как у голосовых в Telegram: полоски вместо линии в острове и плеере", _s.WaveSeek, v => _s.WaveSeek = v);
        c = Card(p, "ФОН ОСТРОВА");
        Row(c, "Свой фон", "Картинка, гифка или видео вместо фона темы. Лежит под текстом и кнопками и обрезается по форме острова", _s.BgEnabled, v => _s.BgEnabled = v);
        ActionRow(c, "Выбрать файл", "PNG, JPG, WEBP, GIF или видео (MP4, MOV, WEBM). Копия хранится в папке ShyFlow. У видео первые 10 секунд, без звука", "Выбрать", PickBg);
        ActionRow(c, "Убрать фон", "Вернуть обычный фон темы", "Убрать", RemoveBg);
        Choice(c, "Затемнение", new[] { "Нет", "Лёгкое", "Среднее", "Сильное" }, () => _s.BgDim, v => _s.BgDim = v);
        Choice(c, "Размытие", new[] { "Нет", "Лёгкое", "Сильное" }, () => _s.BgBlur, v => _s.BgBlur = v);
        c = Card(p, "ШРИФТ");
        {
            var fonts = MainWindow.UserFonts();
            var names = new[] { "Системный" }.Concat(fonts.Select(MainWindow.FontName)).ToArray();
            Choice(c, "Шрифт острова", names,
                () => fonts.FindIndex(f => string.Equals(f, _s.FontFile, StringComparison.OrdinalIgnoreCase)) + 1,
                v => _s.FontFile = v <= 0 || v > fonts.Count ? "" : fonts[v - 1]);
        }
        ActionRow(c, "Загрузить шрифт", "Файл .ttf, .otf или .ttc. Копия хранится в папке ShyFlow, после загрузки шрифт сразу выбран", "Загрузить", PickFont);
        Row(c, "И в плеере на весь экран", "Название, исполнитель и слова песни этим же шрифтом", _s.FontInPlayer, v => _s.FontInPlayer = v);
        ActionRow(c, "Удалить выбранный шрифт", "Убрать файл шрифта и вернуть системный", "Удалить", RemoveFont);
        c = Card(p, "СТИКЕР В ОСТРОВЕ");
        Row(c, "Стикер в развёрнутом острове", "Справа в свободном месте, рядом с эквалайзером. Он же на обороте обложки в плеере", _s.GifEnabled, v => _s.GifEnabled = v);
        ActionRow(c, "Выбрать стикер", "Гифка, картинка (PNG, JPG, WEBP) или видео (MP4, MOV, WEBM). Копия хранится в папке ShyFlow. У видео берутся первые 10 секунд, звука нет", "Выбрать", PickGif);
        ActionRow(c, "Убрать стикер", "Удалить выбранный файл", "Удалить", RemoveGif);
        GifBgRow(c);
        GifHexRow(c);
        Choice(c, "Сила вырезания", new[] { "Мало", "Средне", "Сильно" }, () => _s.GifTolerance, v => _s.GifTolerance = v);
        Row(c, "Вырезать и внутри", "Убирает цвет фона и внутри фигуры: в просветах, между руками. Без этого вырезается только то, что соединено с краем", _s.GifHoles, v => _s.GifHoles = v);
    }

    // ================= МУЗЫКА =================
    private void BuildMusicPage(StackPanel p)
    {
        Intro(p, "Слова песни: поиск, перевод и как они выглядят в острове");
        StackPanel c = Card(p, "ТЕКСТЫ ПЕСЕН");
        Row(c, "Слова песни вместо названия", "Ищем на lrclib.net по названию и исполнителю", _s.Lyrics, v => _s.Lyrics = v);
        Row(c, "Слова загораются по очереди", "Подсветка под голос. Сдвиг текста: Ctrl+Alt+, и Ctrl+Alt+.", _s.LyricsKaraoke, v => _s.LyricsKaraoke = v);
        Row(c, "Больше источников текстов", "Ещё NetEase и Kugou (неофициально, может перестать работать). У них бывает время каждого слова", _s.LyricsExtra, v => _s.LyricsExtra = v);
        Row(c, "Текст без точных таймингов", "Время строк оценивается по длине трека", _s.LyricsPlain, v => _s.LyricsPlain = v);
        c = Card(p, "ПЕРЕВОД");
        Row(c, "Перевод текста песни", "Мелким шрифтом под каждой строкой в плеере на весь экран. Строки уходят в Google Translate (неофициально)", _s.LyricsTranslate, v => _s.LyricsTranslate = v);
        Choice(c, "Язык перевода", new[] { "Русский", "English", "Deutsch", "Українська" }, () => _s.TranslateTo, v => _s.TranslateTo = v);
        c = Card(p, "СЛОВА В ОСТРОВЕ");
        Row(c, "Слова в развёрнутом острове", "Поётся сейчас и следующая строка", _s.LyricsInFull, v => _s.LyricsInFull = v);
        Row(c, "Свечение строки", "Активная строка светится цветом обложки", _s.LyricsGlow, v => _s.LyricsGlow = v);
        Row(c, "По буквам в острове", "Строка слов и смена названия и исполнителя по буквам", _s.IslandLetters, v => _s.IslandLetters = v);
        c = Card(p, "СТАТИСТИКА");
        Row(c, "Считать, что я слушаю", "Сколько и что играло, по дням и часам. Хранится только на этом компьютере, в папке ShyFlow", _s.ListenStats, v => _s.ListenStats = v);
        ActionRow(c, "Статистика прослушиваний", "Неделя, месяц, всё время, топ треков и исполнителей, твой вайб и картинка итогов для сторис", "Открыть", StatsWindow.ShowStats);
        InfoRow(c, "Карточка «Сейчас играет» для сторис: кнопка в плеере, плитка в быстрых действиях или меню острова. Сохраняется в «Изображения\\ShyFlow» и сразу копируется");
    }

    // ================= ПЛЕЕР =================
    private void BuildPlayerPage(StackPanel p)
    {
        Intro(p, "Плеер на весь экран: клик по обложке в развёрнутом острове или кнопка справа");
        StackPanel c = Card(p, "НА ВЕСЬ ЭКРАН");
        Row(c, "Визуализатор по низу", "Работает независимо от эквалайзера в острове", _s.PlayerViz, v => _s.PlayerViz = v);
        Row(c, "Свечение за обложкой", "Дышит с басами", _s.PlayerGlow, v => _s.PlayerGlow = v);
        Row(c, "Размытие дальних строк", "Как в Apple Music", _s.LyricsBlur, v => _s.LyricsBlur = v);
        Row(c, "Объёмная обложка", "Наклон за курсором, блик и перспектива при перевороте", _s.Cover3D, v => _s.Cover3D = v);
        c = Card(p, "ТЕКСТ");
        Row(c, "По буквам в плеере", "Слова загораются волной вслед за голосом. Название при смене трека улетает, новое опускается сверху", _s.LetterAnim, v => _s.LetterAnim = v);
        InfoRow(c, "Клик по строке: перемотать на неё. Правый клик: «эта строка поётся сейчас», текст подгоняется под эту секунду. " +
                   "Сбросить подгонку: Ctrl+Alt+0. Клик по обложке переворачивает её: на обороте стикер");
    }

    // ================= ПЛАШКИ =================
    private void BuildToastsPage(StackPanel p)
    {
        Intro(p, "Какие события остров показывает плашками и уведомлениями");
        StackPanel c = Card(p, "КАК ПОКАЗЫВАТЬ");
        Row(c, "Всё в пузырях", "Громкость, смена трека, уведомления и другие события появляются маленькой капсулой рядом с островом, а сам остров не раскрывается. Сторона и фон как у пузыря таймера («Внешний вид»)", _s.EventsInBubble, v => _s.EventsInBubble = v);
        c = Card(p, "МУЗЫКА И УВЕДОМЛЕНИЯ");
        Row(c, "Пауза и переключение трека", "Плашка с обложкой, когда остров свёрнут", _s.PlaybackToasts, v => _s.PlaybackToasts = v);
        Row(c, "Пропускать рекламу в Spotify", "Spotify на секунду перезапускается и играет дальше, реклама пропадает. Выключено: пропускай сам кнопкой «вперёд» в острове, свайпом или Ctrl+Alt+→", _s.AdSkip, v => _s.AdSkip = v);
        Row(c, "Показывать «Реклама» вместо трека", "Пока идёт реклама: «Реклама · 0:23», без текста песни и без статистики", _s.AdDetect, v => _s.AdDetect = v);
        Row(c, "Глушить рекламу", "Во время рекламы выключать звук только у Spotify, потом возвращать", _s.AdMute, v => _s.AdMute = v);
        Row(c, "Уведомления Windows", "Telegram, браузер и другие всплывашки", _s.Notifications, v => _s.Notifications = v);
        Row(c, "Праздники", "Раз в день: «Сегодня: Новый год» и свои события из календаря", _s.HolidayToasts, v => _s.HolidayToasts = v);
        {
            int[] secs = { 4, 7, 10, 15 };
            Choice(c, "Сколько висит уведомление", new[] { "4 с", "7 с", "10 с", "15 с" },
                () => Math.Max(0, Array.IndexOf(secs, _s.NotifSeconds)), v => _s.NotifSeconds = secs[Math.Clamp(v, 0, secs.Length - 1)]);
        }
        Row(c, "Шторка уведомлений", "Все уведомления списком: колокольчик в быстрых действиях, Ctrl+Alt+B или потянуть развёрнутый остров вниз", _s.NotifCenter, v => _s.NotifCenter = v);
        TextRow(c, "Быстрые ответы", "Кнопка «Ответить» в раскрытом уведомлении. Фразы через «;»", "Щас; Позже напишу", () => _s.QuickReplies ?? "", v => _s.QuickReplies = v, 210);
        Row(c, "Вставлять ответ сразу", "ShyFlow откроет чат и сам нажмёт Ctrl+V. Выключено: фраза просто в буфере, вставляешь сам", _s.QuickReplyPaste, v => _s.QuickReplyPaste = v);
        InfoRow(c, "Мышь над уведомлением: оно не пропадает. Клик: раскрыть, ещё клик или «Открыть»: перейти в программу, откуда оно. " +
                   "Смахни в сторону или вверх, чтобы убрать");
        Row(c, "Плашка при копировании", null, _s.CopyToast, v => _s.CopyToast = v);
        c = Card(p, "СИСТЕМА");
        Row(c, "Громкость", null, _s.VolumeToasts, v => _s.VolumeToasts = v);
        Row(c, "Яркость", "Работает на ноутбуках", _s.BrightnessToasts, v => _s.BrightnessToasts = v);
        Row(c, "Микрофон и камера", "Когда программа начинает их использовать", _s.MicCamera, v => _s.MicCamera = v);
        Row(c, "Caps Lock, Num Lock, микрофон выкл", null, _s.KeyToasts, v => _s.KeyToasts = v);
        Row(c, "Wi-Fi и Bluetooth", "Подключение и отключение", _s.NetworkToasts, v => _s.NetworkToasts = v);
        Row(c, "Батарея и зарядка", "Подключение зарядки и низкий заряд (ноутбуки)", _s.Battery, v => _s.Battery = v);
        c = Card(p, "ФАЙЛЫ");
        Row(c, "Загрузки в браузере", "Начало и конец (Chrome, Edge, Firefox)", _s.Downloads, v => _s.Downloads = v);
        Row(c, "Скриншоты и запись экрана", "Сохранение, картинка в буфере, красная точка при записи", _s.Captures, v => _s.Captures = v);
        Row(c, "Прогресс копирования файлов", "Проценты в плашке, пока Проводник копирует файлы", _s.CopyProgress, v => _s.CopyProgress = v);
    }

    // ================= УПРАВЛЕНИЕ =================
    private void BuildControlPage(StackPanel p)
    {
        Intro(p, "Мышь и жесты, карман и заметки, горячие клавиши");
        StackPanel c = Card(p, "МЫШЬ");
        Row(c, "Колесо мыши на свёрнутом острове", "Громкость, а с зажатым Shift перемотка на 5 секунд", _s.WheelControl, v => _s.WheelControl = v);
        Row(c, "Перемотка кликом", "По полоске прогресса в острове и плеере и по строке слов в плеере", _s.SeekClick, v => _s.SeekClick = v);
        Row(c, "Жесты мышью", "Свайпы по свёрнутому острову, действия ниже. Уведомление смахивается в сторону или вверх", _s.Gestures, v => _s.Gestures = v);
        // шаг 63: своё действие на каждое направление (Main\Gestures.cs)
        Choice(c, "Свайп влево", MainWindow.GestureNames, () => _s.GestureLeft, v => _s.GestureLeft = v);
        Choice(c, "Свайп вправо", MainWindow.GestureNames, () => _s.GestureRight, v => _s.GestureRight = v);
        Choice(c, "Свайп вниз", MainWindow.GestureNames, () => _s.GestureDown, v => _s.GestureDown = v);
        Choice(c, "Свайп вверх", MainWindow.GestureNames, () => _s.GestureUp, v => _s.GestureUp = v);
        Row(c, "Сворачивать при клике вне острова", null, _s.CollapseOnOutside, v => _s.CollapseOnOutside = v);
        c = Card(p, "КАРМАН И ЗАМЕТКИ");
        Row(c, "QR при броске файла", "Кинул один файл на остров — сразу QR: наведи камеру телефона, и файл скачается по Wi-Fi. Ещё есть кнопка с телефоном на карточке файла в кармане", _s.DropQr, v => _s.DropQr = v);
        Row(c, "Напоминать о заметках", "Раз в 30 минут, пока заметки есть", _s.NoteReminders, v => _s.NoteReminders = v);
        Row(c, "Звук напоминания", "Короткий «динь-динь», чтобы не пропустить. Звучит, даже если звуки интерфейса выключены", _s.ReminderSound, v => _s.ReminderSound = v);
        Row(c, "История буфера обмена", "15 последних, хранится только в памяти. Закреплённое булавкой хранится и между запусками", _s.ClipboardHistory, v => _s.ClipboardHistory = v);
        Row(c, "Картинки в истории буфера", "Скриншоты и скопированные картинки с превью. Клик: снова в буфер, перетащи: в чат или папку. Хранятся в папке ShyFlow", _s.ClipboardImages, v => _s.ClipboardImages = v);
        Row(c, "Превью ссылок", "У скопированной ссылки заголовок страницы и значок сайта. ShyFlow сам заходит на этот сайт; адреса в локальной сети не открывает", _s.LinkPreviews, v => _s.LinkPreviews = v);
        c = Card(p, "ФОКУС (ПОМОДОРО)");
        {
            int[] fm = { 15, 25, 45, 60 }, bm = { 5, 10, 15 };
            Choice(c, "Работа", new[] { "15 мин", "25 мин", "45 мин", "60 мин" }, () => Math.Max(0, Array.IndexOf(fm, _s.FocusMin)), v => _s.FocusMin = fm[Math.Clamp(v, 0, 3)]);
            Choice(c, "Перерыв", new[] { "5 мин", "10 мин", "15 мин" }, () => Math.Max(0, Array.IndexOf(bm, _s.BreakMin)), v => _s.BreakMin = bm[Math.Clamp(v, 0, 2)]);
        }
        Row(c, "Не беспокоить во время работы", "Уведомления не всплывают, а тихо копятся в шторке. В перерыве остров скажет, сколько пришло", _s.FocusQuiet, v => _s.FocusQuiet = v);
        InfoRow(c, "Включается плиткой «Фокус» в быстрых действиях. Время и кольцо — в пузыре рядом с островом, клик по нему: пауза, пропустить, остановить");
        c = Card(p, "ПЕРЕВОДЧИК");
        InfoRow(c, "Выдели текст в любой программе и нажми Ctrl+Alt+Q: остров переведёт его, язык определится сам. Перевод можно скопировать или вставить вместо выделенного. " +
                   "Плитка «Переводчик» открывает пустое поле. Сервис: Google Translate (неофициально)");
        c = Card(p, "ГОРЯЧИЕ КЛАВИШИ: CTRL + ALT + …");
        Row(c, "Горячие клавиши", "Ctrl+Alt+I работает всегда, даже если выключено", _s.Hotkeys, v => _s.Hotkeys = v);
        HotKeyRow(c, "I", "Скрыть и показать остров");
        HotKeyRow(c, "S", "Настройки");
        HotKeyRow(c, "Пробел", "Пауза и пуск");
        HotKeyRow(c, "←  →", "Предыдущий и следующий трек");
        HotKeyRow(c, "E", "Развернуть и свернуть");
        HotKeyRow(c, "P", "Карман");
        HotKeyRow(c, "N", "Новая заметка");
        HotKeyRow(c, "A", "Быстрые действия");
        HotKeyRow(c, "D", "Звук и микрофон");
        HotKeyRow(c, "M", "Громкость программ");
        HotKeyRow(c, "T", "Таймер и секундомер");
        HotKeyRow(c, ",  .", "Текст песни раньше и позже");
        HotKeyRow(c, "0", "Сбросить подгонку текста");
        HotKeyRow(c, "B", "Шторка уведомлений");
        HotKeyRow(c, "Q", "Перевести выделенный текст");
        HotKeyRow(c, "V", "Выключить и включить микрофон");
        HotKeyRow(c, "F", "Счётчик кадров");
    }

    // ================= СИСТЕМА =================
    private void BuildSystemPage(StackPanel p)
    {
        Intro(p, "Версия, экономный режим, папка с данными, экспорт и сброс настроек");
        StackPanel c = Card(p, "О ПРОГРАММЕ");
        {
            var updVer = new Grid { Margin = new Thickness(16, 12, 14, 12) };
            updVer.ColumnDefinitions.Add(new ColumnDefinition());
            updVer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            updVer.Children.Add(new TextBlock { Text = "ShyFlow", Foreground = Brushes.White, FontSize = 13.5, FontWeight = FontWeights.Medium });
            var verTb = new TextBlock { Text = "Версия " + AppInfo.CurrentVersion, Foreground = Gray, FontSize = 13 };
            Grid.SetColumn(verTb, 1);
            AttachEgg(verTb);   // пасхалка: пять кликов по версии (SettingsWindow.Egg.cs)
            updVer.Children.Add(verTb);
            c.Children.Add(updVer);
        }
        // шаг 55: автообновление (Services\Updater.cs)
        if (AppInfo.IsPackaged)   // шаг 75: версия из Microsoft Store обновляется через Store
            InfoRow(c, Loc.T("Обновления приходят через Microsoft Store", "Updates come through the Microsoft Store"));
        else
        {
            Row(c, "Обновляться автоматически", "Раз в 6 часов ShyFlow смотрит, нет ли новой версии, и предлагает её уведомлением", _s.AutoUpdate, v => _s.AutoUpdate = v);
            ActionRow(c, "Обновления", "Новые версии выходят на GitHub (MrShykoe/ShyFlow)", "Проверить", () => { if (MainWindow.Instance is { } m) _ = m.CheckUpdatesNow(this); });
        }
        ActionRow(c, "Что нового", "Презентация всех новинок ShyFlow 3", "Показать", () => WhatsNewWindow.ShowSince(new Version(3, 0, 0), _s.UiSounds));
        Row(c, "«Что нового» после обновления", "Показывать презентацию новинок при первом запуске новой версии", _s.ShowWhatsNew, v => _s.ShowWhatsNew = v);
        c = Card(p, "ПРОИЗВОДИТЕЛЬНОСТЬ");
        Choice(c, "Экономный режим", new[] { "Выкл", "Авто", "Всегда" }, () => _s.PerfMode, v => _s.PerfMode = v);
        InfoRow(c, "30 кадров в секунду вместо 60, без жидких анимаций, свечения, размытия строк, визуализатора и анимации по буквам. " +
                   "Авто: пока ноутбук от батареи или когда остров не успевает рисовать (например, в игре). Настройки при этом не меняются. " +
                   "Счётчик кадров: Ctrl+Alt+F");
        c = Card(p, "ДАННЫЕ");
        ActionRow(c, "Папка ShyFlow", "Настройки, карман, заметки, фон, стикер, шрифты и кэш текстов", "Открыть", () => OpenPath(IslandDir));
        ActionRow(c, "Журнал ошибок", "crash.log: если что-то не работает, пришли этот файл", "Открыть", () => OpenPath(Path.Combine(IslandDir, "crash.log")));
        c = Card(p, "КОНФИГ");
        ActionRow(c, "Экспорт настроек", "Сохранить все настройки в файл", "Экспорт", ExportCfg);
        ActionRow(c, "Импорт настроек", "Загрузить настройки из файла", "Импорт", ImportCfg);
        ActionRow(c, "Сбросить настройки", "Вернуть всё как по умолчанию. Карман, заметки и закреплённое не трогаются", "Сбросить", ResetCfg);
    }

    private void OpenPath(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                MacDialog.Info(this, Loc.T("Пока пусто: файла ещё нет"));
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { MacDialog.Info(this, ex.Message); }
    }
}