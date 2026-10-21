using System.Diagnostics;
using Microsoft.Win32;

namespace Island;

// Переход к треку в Spotify (шаг 48, кнопка в коллекции).
// Без входа в аккаунт Spotify не даёт найти точный трек, поэтому открывается поиск «исполнитель название»:
// нужный трек в нём первый. Если Spotify установлен, открывается приложение (spotify:search:…), иначе сайт.
internal static class Spotify
{
    public static void OpenTrack(string title, string artist)
    {
        string q = (artist + " " + title).Trim();
        if (q.Length == 0) return;
        try
        {
            string url = Installed()
                ? "spotify:search:" + Uri.EscapeDataString(q)
                : "https://open.spotify.com/search/" + Uri.EscapeDataString(q);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) { App.Log("Spotify.Open", ex); }
    }

    // приложение зарегистрировало ссылки spotify: (обычная установка и из Microsoft Store)
    private static bool Installed()
    {
        try
        {
            using var k = Registry.ClassesRoot.OpenSubKey("spotify");
            return k?.GetValue("URL Protocol") != null;
        }
        catch { return false; }
    }
}
