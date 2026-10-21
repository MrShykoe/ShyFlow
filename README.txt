ShyFlow: «Dynamic Island» для Windows
=====================================

Чёрная капсула вверху экрана. Показывает, что играет, слова песни, громкость и яркость, уведомления, загрузки,
микрофон и камеру. Раскрывается в плеер, карман для файлов и текста, быстрые действия. Есть плеер на весь экран
со словами песни, как в Apple Music.

НУЖНО: Windows 10 2004+ или Windows 11, .NET 8 SDK (для сборки) или .NET 8 Desktop Runtime (для запуска).

ЗАПУСК ИЗ ИСХОДНИКОВ:   cd Island,  dotnet run
ПРОВЕРКА СБОРКИ:        dotnet build Island\Island.csproj
ГОТОВЫЙ EXE:            publish.bat  (папка out: light нужен .NET 8, full работает везде)
УСТАНОВЩИК:             build-all.bat (нужен Inno Setup 6.3+), результат в out\installer
MICROSOFT STORE:        build-store.bat, пакет MSIX в out\store; что и где заполнить: Package\STORE.md

ГДЕ ЧТО ЛЕЖИТ (Island\):
  App.xaml(.cs), MainWindow.xaml(.cs)   запуск, одна копия, лог ошибок; главное окно острова: режимы, пружины, музыка, громкость
  AppSettings.cs, AppInfo.cs            настройки (settings.json) и версия
  Main\                                 остальные части острова: слова песни, карман, действия, плашки, уведомления, темы, жесты
  Player\                               плеер на весь экран
  Settings\                             окно настроек и приветствие
  Ui\                                   общие элементы: SBorder (мягкие углы), VIcon (иконки с морфингом), волна, буквы, диалоги
  Services\                             тексты песен, перевод, Discord, переключение звука, мониторы, звуки, SafeFile
  Localization\                         русский и английский интерфейс

ДАННЫЕ: %AppData%\Island (settings.json, pocket.json, notes.json, кэш текстов в lyrics\).
ЕСЛИ ЧТО-ТО НЕ РАБОТАЕТ: пришли %AppData%\Island\crash.log, там причина.

История изменений по шагам: CHANGELOG.txt.
