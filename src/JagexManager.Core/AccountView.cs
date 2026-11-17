using System.Collections.ObjectModel;

namespace JagexManager;

public sealed class AccountView : View
{
    private Account account;
    private bool expired;

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

    public bool Expired
    {
        get => expired;
        set => Set(ref expired, value, nameof(Expired));
    }

    public void Update(Account latest)
    {
        account = latest;

        foreach (var gone in Characters.Where(row => latest.Characters.All(c => c.Id != row.Character.Id)).ToList())
        {
            gone.Forget();
            Characters.Remove(gone);
        }

        for (var position = 0; position < latest.Characters.Count; position++)
        {
            var character = latest.Characters[position];
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

        Raise(nameof(Account), nameof(Name), nameof(Summary));
    }
}
