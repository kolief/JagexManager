namespace JagexManager;

public sealed class CharacterView(AccountView owner, Character character) : View
{
    private Character character = character;
    private Client? client;
    private bool ready = true;

    public AccountView Owner { get; } = owner;

    public Character Character
    {
        get => character;
        set
        {
            character = value;
            Raise(nameof(Character), nameof(Name), nameof(Choice));
        }
    }

    public string Name => character.Name.Length > 0 ? character.Name : "Unnamed character";

    public string? Choice => Owner.Account.Choices.GetValueOrDefault(character.Id);

    public bool Running => client is not null;

    public bool Ready
    {
        get => ready;
        set => Set(ref ready, value, nameof(Ready));
    }

    public void Attach(Client started) => Set(ref client, started, nameof(Running));

    public void Stop() => client?.Stop();

    public void DropClosed()
    {
        if (client is { Running: false })
            Forget();
    }

    public void Forget()
    {
        client?.Dispose();
        Set(ref client, null, nameof(Running));
    }
}
