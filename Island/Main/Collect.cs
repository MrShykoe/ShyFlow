using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Island;

// Шаг 71: коллекция карточек прямо из острова.
// Раньше карточку можно было забрать только звездой в плеере на весь экран, а сама коллекция пряталась среди плиток.
// Теперь ★ стоит в развёрнутом острове рядом с «на весь экран»:
//   трека ещё нет в коллекции — карточка выпадает прямо в острове: он перетекает в плашку «Новая карточка!»
//   цвета редкости, с редкостью и оценкой; редкая и выше — с «желе» и звуком. Клик по плашке открывает коллекцию;
//   трек уже в коллекции — ★ золотая, клик открывает коллекцию.
public partial class MainWindow
{
    private static readonly Color StarGold = Color.FromRgb(0xFF, 0xC8, 0x3A);
    private Color _cardCol = StarGold;   // цвет плашки «Новая карточка!» (ToastColor, kind "card")
    private string _coverFor = "";       // название трека, чья обложка сейчас в _lastCover: у трека без обложки остаётся старая

    private bool CoverIsCurrent => _lastCover != null && _curTitle.Length > 0 && _coverFor == _curTitle;

    private void InitCollect()
    {
        CardCollection.Changed += () => Dispatcher.BeginInvoke(new Action(UpdateIslandStar));
    }

    private void UpdateIslandStar()
    {
        bool can = CoverIsCurrent;
        bool have = can && CardCollection.Find(_curTitle, _curArtist) != null;
        // шаг 73: карточку дня сегодня ещё не забирали — вместо звезды солнце: «первая за день, шанс на редкую выше»
        bool daily = can && !have && CardCollection.DailyAvailable;
        StarIcon.Text = daily ? "sun" : "star";
        StarIcon.Foreground = have ? new SolidColorBrush(StarGold) : daily ? new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)) : Brushes.White;
        StarBtn.Opacity = can ? 1 : 0.4;
        StarBtn.ToolTip = daily
            ? Loc.T("Карточка дня: первая за сегодня, шанс на редкую выше", "Card of the day: first one today, better odds of a rare")
            : Loc.T(have ? "Уже в коллекции: открыть коллекцию" : can ? "Забрать обложку в коллекцию" : "У этого трека нет обложки",
                    have ? "Already collected: open collection" : can ? "Add the cover to your collection" : "This track has no cover");
    }

    private void Star_Click(object s, RoutedEventArgs e)
    {
        Press(StarBtn, 0);
        if (!CoverIsCurrent || _lastCover == null) { Sfx.Ui("tap"); return; }
        var have = CardCollection.Find(_curTitle, _curArtist);
        if (have != null)
        {
            CollectionWindow.ShowCollection();
            return;
        }
        CardEntry card;
        try { card = CardCollection.Add(_lastCover, _curTitle, _curArtist).card; }
        catch (Exception ex)
        {
            App.Log("Collection.AddIsland", ex);
            ShowToast("triangle-alert", Loc.T("Не получилось сохранить карточку", "Couldn't save the card"), null, "key");
            return;
        }
        UpdateIslandStar();
        CardDrop(card);
        // шаг 73: только что собран набор — следом вторая плашка; потом достижения (Story.cs)
        bool setDone = CardCollection.ArtistCount(card.Artist) == CardCollection.SetSize;
        var d = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(setDone ? 3400 : 1600) };
        d.Tick += (_, _) =>
        {
            d.Stop();
            if (setDone)
            {
                _cardCol = CollectibleCard.Accent(4);
                ShowToast("layout-grid", Loc.T("Набор собран", "Set complete") + ": " + card.Artist, null, "card");
                _osdClick = () => { _toastTimer.Stop(); ApplyMode(Rest()); CollectionWindow.ShowCollection(); };
                try { Sfx.Done(); } catch { /* нет звука */ }
                var a = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2400) };
                a.Tick += (_, _) => { a.Stop(); CheckAchievements(); };
                a.Start();
            }
            else CheckAchievements();
        };
        d.Start();
    }

    // карточка «выпадает» в острове: плашка цвета редкости, звезда крутится, у редких — «желе» и звук
    private void CardDrop(CardEntry c)
    {
        _cardCol = CollectibleCard.Accent(c.Rarity);
        string rarity = Loc.T(CardCollection.RarityNames[c.Rarity]) + (c.Foil ? " · " + Loc.T("голографическая") : "");
        string head = c.Daily ? Loc.T("Карточка дня!", "Card of the day!") : Loc.T("Новая карточка!", "New card!");
        ShowToast(c.Daily ? "sun" : "star", head + ": " + rarity + " · " + c.Score + "/10", null, "card");
        _osdClick = () => { _toastTimer.Stop(); ApplyMode(Rest()); CollectionWindow.ShowCollection(); };
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromMilliseconds(3200);   // подольше, чтобы успеть прочитать и нажать
        _toastTimer.Start();
        if (!EventsToBubble)
        {
            Spin(OsdIcon, -220, 900);
            Pop(OsdBadge, 0.25, 700);
        }
        if (c.Rarity >= 3)
        {
            Wobble();
            try { Sfx.Done(); } catch { /* нет звука */ }
        }
        else { try { Sfx.Pop(); } catch { /* нет звука */ } }
    }
}
