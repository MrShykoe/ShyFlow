using System.Runtime.InteropServices;
using System.Text;

namespace Island;

internal static class AppInfo
{
    // Шаг 75: версия из Microsoft Store (пакет MSIX). У неё обновления приходят через Store, а не с GitHub,
    // и автозапуск включается через StartupTask из манифеста, а не записью в реестр.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetCurrentPackageFullName(ref int len, StringBuilder? name);
    private static bool? _packaged;
    public static bool IsPackaged
    {
        get
        {
            if (_packaged is bool b) return b;
            try { int len = 0; _packaged = GetCurrentPackageFullName(ref len, null) != 15700; }   // 15700: APPMODEL_ERROR_NO_PACKAGE
            catch { _packaged = false; }
            return _packaged.Value;
        }
    }

    // Discord Rich Presence («Слушает ShyFlow» или «Использует ShyFlow» в профиле). Как включить:
    //   1) https://discord.com/developers/applications, New Application, назови его ShyFlow (это имя увидят друзья).
    //   2) General Information, скопируй Application ID и вставь сюда между кавычками.
    //   3) Необязательно, иконка в профиле: Rich Presence, Art Assets, загрузи Island\ShyFlow.png под именем shyflow и впиши это имя в DiscordLargeImage.
    //   4) Пересобери. В Настройках, «Основное», «Discord» появится «Показывать в Discord» (и в приветствии тоже).
    // Пока ID пустой, пункты про Discord скрыты и ничего не отправляется.
    public const string DiscordAppId = "1561057527192358992";
    // Необязательно: имя картинки из Rich Presence Art Assets того же приложения (большая иконка в профиле).
    // Пока картинка с этим именем не загружена в Art Assets, Discord просто не показывает иконку.
    public const string DiscordLargeImage = "shyflow";

    // версия из сборки (задаётся в Island.csproj тегом <Version>)
    public static string CurrentVersion
    {
        get
        {
            var v = typeof(AppInfo).Assembly.GetName().Version ?? new Version(3, 0, 0);
            return $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
        }
    }
}
