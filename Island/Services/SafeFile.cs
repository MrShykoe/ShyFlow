using System.IO;

namespace Island;

// Запись файла «целиком или никак». Сначала пишем во временный файл рядом, потом подменяем им старый.
// Если программа упадёт или выключат свет посреди записи, старый файл останется целым
// (раньше settings.json мог оказаться обрезанным, и все настройки молча сбрасывались).
internal static class SafeFile
{
    public static void WriteAllText(string path, string text)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        if (!File.Exists(path)) { File.Move(tmp, path); return; }
        try { File.Replace(tmp, path, null, true); }
        catch (IOException)
        {
            // Replace не везде работает (сетевые и некоторые облачные папки): тогда обычная перезапись
            File.Copy(tmp, path, true);
            File.Delete(tmp);
        }
    }
}
