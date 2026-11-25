using System.Collections.ObjectModel;

namespace JagexManager;

public sealed class AccountView : View
{
    private Account account;
    private bool expired;
    private bool showHidden;
    private Sort sort;
    private string? search;

    public AccountView(Account account)
    {
        this.account = account;
        Update(account);
    }

    public ObservableCollection<CharacterView> Characters { get; } = [];

    public Account Account => account;

    public string Name => account.Name;

    public string Summary => Characters.Count switch
    {
        0 => "No characters yet",
        1 => "1 character",
        var count => $"{count} characters",
    };

    public IReadOnlyList<CharacterView> Playable => Characters.Where(c => !c.Hidden && c.Visible && !c.Running && c.Ready).ToList();

    public int HiddenCount => Characters.Count(c => c.Hidden);

    public bool HiddenOffered => HiddenCount > 0 && search is null;

    public bool Matched => search is null || Characters.Any(c => c.Visible);

    public string? Search
    {
        get => search;
        set => Set(ref search, Preference.Clean(value), nameof(Search), nameof(Matched), nameof(HiddenOffered));
    }

    public Sort Sort
    {
        get => sort;
        set
        {
            if (sort == value)
                return;

            sort = value;
            Arrange();
        }
    }

    public IEnumerable<CharacterView> InJagexOrder =>
        account.Characters.Select(c => Characters.First(row => row.Character.Id == c.Id));

    public bool Matches(string text) => Name.Contains(text, StringComparison.CurrentCultureIgnoreCase);

    public string HiddenLink => (ShowHidden, HiddenCount) switch
    {
        (true, _) => "Hide them again",
        (false, 1) => "Show 1 hidden character",
        (false, var count) => $"Show {count} hidden characters",
    };

    public bool ShowHidden
    {
        get => showHidden;
        set => Set(ref showHidden, value, nameof(ShowHidden), nameof(HiddenLink));
    }

    public bool Expired
    {
        get => expired;
        set => Set(ref expired, value, nameof(Expired));
    }

    public void Tick()
    {
        foreach (var character in Characters)
            character.Tick();
    }

    public void Arrange() => Place(account, sort == Sort.Played ? ByPlayed(account) : account.Characters);

    public void Update(Account latest) => Place(latest, sort == Sort.Played ? Kept(latest) : latest.Characters);

    private void Place(Account latest, IReadOnlyList<Character> ordered)
    {
        account = latest;

        foreach (var gone in Characters.Where(row => latest.Characters.All(c => c.Id != row.Character.Id)).ToList())
        {
            gone.Forget();
            Characters.Remove(gone);
        }

        for (var position = 0; position < ordered.Count; position++)
        {
            var character = ordered[position];
            var current = Characters.Select(row => row.Character.Id).ToList().IndexOf(character.Id);

            if (current < 0)
            {
                Characters.Insert(position, Row(character));
                continue;
            }

            Characters[current].Character = character;
            if (current != position)
                Characters.Move(current, position);
        }

        Raise(nameof(Account), nameof(Name), nameof(Summary), nameof(HiddenCount), nameof(HiddenLink), nameof(HiddenOffered), nameof(Matched));
        if (HiddenCount == 0)
            ShowHidden = false;
    }

    private CharacterView Row(Character character)
    {
        var row = new CharacterView(this, character);
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CharacterView.Visible))
                Raise(nameof(Matched));
        };
        return row;
    }

    private static List<Character> ByPlayed(Account latest) =>
        latest.Characters.OrderByDescending(c => latest.Preferences.GetValueOrDefault(c.Id)?.Played ?? DateTimeOffset.MinValue).ToList();

    private List<Character> Kept(Account latest)
    {
        var current = Characters.Select(row => row.Character.Id).ToList();
        return latest.Characters.OrderBy(c => current.IndexOf(c.Id) is var at and >= 0 ? at : int.MaxValue).ToList();
    }
}
