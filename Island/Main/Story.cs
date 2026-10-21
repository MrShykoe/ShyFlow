using System.Windows;

namespace Island;

// Карточка «Сейчас играет» для сторис и достижения коллекции (шаг 49).
// Карточка: меню острова, плитка в быстрых действиях, кнопка в плеере. Картинка сохраняется в «Изображения\ShyFlow»
// и копируется в буфер; остров показывает уведомление, клик по нему открывает папку с картинкой.
public partial class MainWindow
{
    private void Story_Click(object s, RoutedEventArgs e) => MakeStory();
    private void Notepad_Click(object s, RoutedEventArgs e) { if (_mode == Mode.Pocket) { _pocketOpen = false; ApplyMode(Rest()); } NotepadWindow.ShowNotepad(); }

    internal void MakeStory()
    {
        if (_curTitle.Length == 0 || _session == null) { Tell("music", "Сейчас ничего не играет"); return; }
        try
        {
            string? line = _lyr != null && _lyrIdx >= 0 && _lyrIdx < _lyr.Count ? _lyr[_lyrIdx].s : null;
            if (string.IsNullOrWhiteSpace(line)) line = null;
            var bmp = StoryCards.NowPlaying(_lastCover, _curTitle, _curArtist, line, _accent, TrackProgress());
            _ignoreClipUntil = DateTime.UtcNow.AddSeconds(2);   // своя картинка в буфере: не показывать «скопировано» и не класть в карман
            string path = StoryCards.Save(bmp, "ShyFlow");
            Sfx.Ui("unlock");
            if (_player != null && _hidden) { _player.ShowHint("share", Loc.T("Карточка сохранена в «Изображения\\ShyFlow» и скопирована")); StoryCards.ShowInFolder(path); return; }
            ShowNotif("ShyFlow", Loc.T("Карточка для сторис готова"), Loc.T("Уже в буфере: вставь в сторис или чат. Клик: показать файл"));
            _notifClick = () => { StoryCards.ShowInFolder(path); ApplyMode(Rest()); };
        }
        catch (Exception ex) { App.Log("Story", ex); Tell("triangle-alert", "Не получилось сделать карточку"); }
    }

    private double TrackProgress()
    {
        try
        {
            var tl = _session?.GetTimelineProperties();
            if (tl == null) return 0;
            double total = (tl.EndTime - tl.StartTime).TotalSeconds;
            if (total <= 1) return 0;
            var pos = tl.Position;
            if (_playing) pos += DateTimeOffset.Now - tl.LastUpdatedTime;
            return (pos - tl.StartTime).TotalSeconds / total;
        }
        catch { return 0; }
    }

    // новые достижения: одна плашка (если сразу несколько, «и ещё N») и звук
    internal void CheckAchievements()
    {
        List<AchDef> fresh;
        try { fresh = Achievements.Check(); }
        catch (Exception ex) { App.LogOnce("Ach.Check", ex); return; }
        if (fresh.Count == 0) return;
        var a = fresh[^1];
        string more = fresh.Count > 1 ? "  +" + (fresh.Count - 1) : "";
        Sfx.Ui("unlock");
        string text = Loc.T("Достижение: ") + Loc.T(a.Title) + more;
        if (_player != null && _hidden) _player.ShowHint("trophy", text);
        else ShowToast("trophy", text, null, "ach");
    }
}
