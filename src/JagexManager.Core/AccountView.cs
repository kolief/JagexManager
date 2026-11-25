using System.Collections.ObjectModel;

namespace JagexManager;

public sealed class AccountView : View
{
    private Account account;
    private bool expired;
    private bool showHidden;
    private bool byPlayed;
    private string? filter;

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

    public IReadOnlyList<CharacterView> Playable => Characters.Where(c => !c.Hidden && !c.Running && c.Ready).ToList();

    public int HiddenCount => Characters.Count(c => c.Hidden);

    public bool HiddenOffered => HiddenCount > 0 && filter is null;

    public bool Shown => filter is null || Named(filter) || Characters.Any(c => c.Matches(filter));

    public string? Filter
    {
        get => filter;
        set => Set(ref filter, Preference.Clean(value), nameof(Filter), nameof(Shown), nameof(HiddenOffered));
    }

    public bool ByPlayed
    {
        get => byPlayed;
        set
        {
            if (byPlayed == value)
                return;

            byPlayed = value;
            Update(account);
        }
    }

    public bool Named(string text) => Name.Contains(text, StringComparison.CurrentCultureIgnoreCase);

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

    public void Update(Account latest)
    {
        account = latest;

        foreach (var gone in Characters.Where(row => latest.Characters.All(c => c.Id != row.Character.Id)).ToList())
        {
            gone.Forget();
            Characters.Remove(gone);
        }

        var ordered = Ordered(latest);
        for (var position = 0; position < ordered.Count; position++)
        {
            var character = ordered[position];
            var current = Characters.Select(row => row.Character.Id).ToList().IndexOf(character.Id);

            if (current < 0)
            {
                Characters.Insert(position, new CharacterView(this, character));
                continue;
            }

            Characters[current].Character = character;
            if (current != position)
                Characters.Move(current, position);
        }

        Raise(nameof(Account), nameof(Name), nameof(Summary), nameof(HiddenCount), nameof(HiddenLink), nameof(HiddenOffered), nameof(Shown));
        if (HiddenCount == 0)
            ShowHidden = false;
    }

    private IReadOnlyList<Character> Ordered(Account latest) =>
        byPlayed
            ? latest.Characters.OrderByDescending(c => latest.Preferences.GetValueOrDefault(c.Id)?.Played ?? DateTimeOffset.MinValue).ToList()
            : latest.Characters;
}
