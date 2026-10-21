# ShyFlow в Microsoft Store: пошагово

Всё бесплатно: регистрация разработчика-частного лица в Store ничего не стоит, подписывает пакет сам Microsoft.

## 1. Аккаунт разработчика (один раз)

1. Открой https://storedeveloper.microsoft.com и выбери регистрацию как **Individual** (частное лицо).
2. Войди аккаунтом Microsoft. Microsoft может попросить подтвердить личность — это нормально.
3. После регистрации ты попадаешь в **Partner Center**.

## 2. Резерв имени и данные пакета

1. Partner Center → **Apps and games** → **New product** → **MSIX or PWA app**.
2. Имя: **ShyFlow** → Reserve.
3. Открой **Product management → Product identity**. Там три строки — скопируй их в `Package\store.json`:
   - `Package/Identity/Name` → `IdentityName`
   - `Package/Identity/Publisher` → `Publisher` (начинается с `CN=`)
   - `Package/Properties/PublisherDisplayName` → `PublisherDisplayName`

## 3. Собрать пакет

Нужен только .NET 8 SDK (тот же, что для обычной сборки). Запусти `build-store.bat` в корне проекта.
Скрипт сам скачает бесплатные инструменты Microsoft (makeappx), соберёт ShyFlow со всем нужным внутри и положит
`out\store\ShyFlow-3.4.0.0.msix`.

Проверить у себя до отправки: `build-store.bat -TestSign` — пакет подпишется тестовым сертификатом,
в конце скрипт напишет одну команду, чтобы Windows ему доверяла. Перед установкой закрой обычный ShyFlow.

## 4. Заявка (Start submission)

**Pricing and availability:** Free, все рынки.

**Properties:**
- Category: **Personalization** (или Utilities & tools).
- Privacy policy URL: обязательна. Выложи `Package\PRIVACY.md` в репозиторий на GitHub и вставь ссылку на файл.
- Website / support: https://github.com/MrShykoe/ShyFlow

**Age ratings:** анкета IARC. Ответы: нет насилия, нет азартных игр, нет покупок; пользователи **не общаются** между собой
внутри приложения. Важно: приложение показывает уведомления других программ и открытый чат Twitch (если включить режим стримера) —
отвечай честно, если анкета об этом спросит.

**Packages:** перетащи `ShyFlow-*.msix`. Устройства: Windows 10/11 Desktop.

**Store listings:** добавь **English (United States)** и **Russian** — тексты ниже. Скриншоты: минимум 1, лучше 4–6,
размер 1920×1080 или 1366×768. Логотип 300×300 лежит в `Package\StoreListing\StoreLogo-300x300.png`.

**Submission options → Restricted capabilities:** Store спросит, зачем `runFullTrust`. Ответ:

> ShyFlow is a WPF desktop application (not UWP) packaged with the Desktop Bridge. runFullTrust is required to run as a regular Win32/.NET desktop app: it shows a topmost overlay window, reads Windows media session info, volume and notifications, and registers global hotkeys.

Отправляй. Проверка обычно занимает от нескольких часов до нескольких дней. Если откажут — пришли мне текст отказа.

## Возможные вопросы на проверке

- **Пропуск рекламы Spotify** (перезапуск Spotify) по умолчанию выключен. Если проверка сочтёт его нарушением правил,
  проще всего спрятать эту настройку в версии из Store.
- **Обновления:** в версии из Store встроенное обновление с GitHub отключено (так требуют правила), обновления идут через Store.
- **Автозапуск** в версии из Store включается через задачу «ShyFlowStartup» — её видно в Диспетчере задач на вкладке «Автозагрузка».
- Данные версии из Store хранятся отдельно от обычной версии: Windows кладёт их в папку пакета.

---

# Тексты для страницы в Store

## English (United States)

**Short description** (до 100 символов):
> Dynamic Island for Windows: music, lyrics, notifications and a cover card collection.

**Description:**

> ShyFlow puts a living black capsule at the top of your screen, like the Dynamic Island on iPhone — and makes it the best place to control music on Windows.
>
> 🎵 Music, beautifully. Works with Spotify, YouTube in your browser, Apple Music and any player that shows in Windows media controls. Waveform progress, an equalizer that moves with the beat, and a full-screen player with synced lyrics, like Apple Music.
>
> ★ Collect covers. Turn the cover of any track into a collectible card with a rarity and a score out of 10. The first card of the day has better odds of being rare. Complete sets, unlock achievements and share your best cards to stories in one click.
>
> 📊 Your month in music. Listening stats and a monthly recap image with your top tracks and card of the month.
>
> 🔔 Everything in one place. Windows notifications, volume and brightness, a pocket for files and clipboard history, quick actions, timers, focus mode, a translator and more.
>
> 🎮 Stays out of the way. Hides by itself in full-screen games and videos, can sit in the center, left or right, and follows you across monitors.
>
> 🌊 Feels alive. Liquid, springy animations everywhere. Themes, notch mode, your own background, font and sticker.
>
> Free, no ads, no accounts. Your data stays on your PC.
> Languages: English, Русский, Українська, Español, Deutsch, Português.

**Features** (по одной строке в поле Product features):
- Dynamic Island-style capsule at the top of the screen
- Synced song lyrics in the island and a full-screen player
- Collectible cards from album covers, with rarities, sets and achievements
- Card of the day with better odds of a rare card
- Share cards and monthly recaps to stories in one click
- Windows notifications, volume and brightness in the island
- Pocket for files, notes and clipboard history
- Quick actions, timer, stopwatch and focus mode
- Hides automatically in full-screen games and videos
- Center, left or right position; follows the active monitor
- Liquid animations, themes, notch mode, custom background and font
- 6 languages, no ads, no accounts

**Keywords** (до 7): dynamic island, notch, music player, lyrics, spotify, widgets, customization

## Russian

**Short description:**
> Dynamic Island для Windows: музыка, тексты песен, уведомления и коллекция карточек.

**Description:**

> ShyFlow — живая чёрная капсула вверху экрана, как Dynamic Island на iPhone, и лучшее место, чтобы управлять музыкой в Windows.
>
> 🎵 Музыка красиво. Работает со Spotify, YouTube в браузере, Яндекс Музыкой и любым плеером, который виден в медиа-управлении Windows. Прогресс волной, эквалайзер в такт и плеер на весь экран со словами песни под голос, как в Apple Music.
>
> ★ Собирай обложки. Обложка любого трека становится коллекционной карточкой с редкостью и оценкой из 10. Первая карточка за день — карточка дня, у неё больше шансов оказаться редкой. Собирай наборы, получай достижения и делись лучшими карточками в сторис в один клик.
>
> 📊 Твой месяц в музыке. Статистика прослушиваний и картинка с итогами месяца: топ треков и карточка месяца.
>
> 🔔 Всё в одном месте. Уведомления Windows, громкость и яркость, карман для файлов и история буфера, быстрые действия, таймеры, режим фокуса, переводчик и не только.
>
> 🎮 Не мешает. Сам прячется в играх и видео на весь экран, стоит по центру, слева или справа и переезжает за тобой между мониторами.
>
> 🌊 Как живой. Жидкие, пружинистые анимации везде. Темы, режим выреза, свой фон, шрифт и стикер.
>
> Бесплатно, без рекламы и аккаунтов. Твои данные остаются на твоём компьютере.
> Языки: English, Русский, Українська, Español, Deutsch, Português.

**Features:**
- Капсула в стиле Dynamic Island вверху экрана
- Слова песен под голос в острове и плеере на весь экран
- Коллекционные карточки из обложек: редкости, наборы, достижения
- Карточка дня с повышенным шансом на редкую
- Карточки и итоги месяца в сторис в один клик
- Уведомления Windows, громкость и яркость в острове
- Карман для файлов, заметок и истории буфера
- Быстрые действия, таймер, секундомер и режим фокуса
- Сам прячется в играх и видео на весь экран
- По центру, слева или справа; монитор с активным окном
- Жидкие анимации, темы, режим выреза, свой фон и шрифт
- 6 языков, без рекламы и аккаунтов

**Keywords:** dynamic island, остров, музыка, тексты песен, spotify, виджеты, кастомизация

## Какие скриншоты сделать (1920×1080)

1. Развёрнутый остров с плеером и волной поверх красивых обоев — главный, он первый в списке.
2. Плеер на весь экран со словами песни.
3. Плашка «Карточка дня!» в острове + окно коллекции с карточками разной редкости.
4. Карман (файлы и буфер) или быстрые действия.
5. Уведомление в острове.
6. Картинка «Итоги месяца» или карточка для сторис.

Подписи на скриншотах не обязательны, но если делать — короткие: «Музыка красиво», «Собирай обложки», «Всё в одном месте».
