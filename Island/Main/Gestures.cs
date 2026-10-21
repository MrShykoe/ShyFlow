namespace Island;

// Свои жесты (шаг 63): свайп по свёрнутому острову влево, вправо, вниз и вверх делает то, что выбрано в настройках
// («Управление», «Жесты»). По умолчанию как раньше: влево следующий трек, вправо предыдущий, вниз раскрыть; вверх пауза.
public partial class MainWindow
{
    // порядок важен: номера хранятся в настройках
    internal static readonly string[] GestureNames =
    {
        "Ничего", "Следующий трек", "Предыдущий трек", "Пауза и play", "Раскрыть остров", "Карман", "Быстрые действия",
        "Шторка уведомлений", "Таймер", "Громкость программ", "Микрофон", "Скрыть остров",
    };

    private void DoGesture(int action)
    {
        switch (action)
        {
            case 1: Swipe(1); return;
            case 2: Swipe(-1); return;
            case 4: SwipeDown(); return;
        }
        _swiped = true;   // дальше мышь не слушаем, но держим до отпускания (как в Swipe)
        ReleaseRubber();
        if (action == 0) return;
        Sfx.Ui("swipe");
        switch (action)
        {
            case 3: OnHotkey(2); break;    // как Ctrl+Alt+Пробел
            case 5: OnHotkey(5); break;    // карман
            case 6: OnHotkey(7); break;    // быстрые действия
            case 7: ToggleCenter(); break;
            case 8: ToggleTimerPanel(); break;
            case 9: ToggleMixer(); break;
            case 10: ToggleMic(); break;
            case 11: ToggleHidden(); break;
        }
    }
}
