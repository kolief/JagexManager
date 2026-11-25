using System.Globalization;

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
            Raise(nameof(Character), nameof(Name), nameof(Choice), nameof(Options), nameof(Detail), nameof(Hidden), nameof(Visible));
            Recount();
        }
    }

    public string Name => Preference.Nickname ?? RealName;

    public string? Choice => Owner.Account.Choices.GetValueOrDefault(character.Id);

    public string? Options => Preference.Options;

    public bool Hidden => Preference.Hidden;

    public bool Visible => !Hidden || Owner.ShowHidden || Running;

    public string? Detail
    {
        get
        {
            string?[] parts = [Preference.Nickname is null ? null : RealName, Choice, Options];
            var shown = parts.OfType<string>().ToList();
            return shown.Count > 0 ? string.Join(" · ", shown) : null;
        }
    }

    private string RealName => character.Name.Length > 0 ? character.Name : "Unnamed character";

    private Preference Preference => Owner.Account.Preferences.GetValueOrDefault(character.Id) ?? Preference.None;

    public bool Member => character.Membership is { Active: true };

    public string? Membership => character.Membership switch
    {
        null => null,
        { Active: true } => "P2P",
        _ => "F2P",
    };

    public string? MembershipLeft => character.Membership is { Active: true, Ends: { } ends } ? Left(ends) : null;

    public bool Lapsing => character.Membership is { Active: true, Renews: false, Ends: { } ends } && ends - DateTimeOffset.UtcNow <= TimeSpan.FromDays(3);

    public string? MembershipNote => character.Membership switch
    {
        { Ends: { } ends, Active: true, Renews: true } => $"Renews on {Day(ends)}",
        { Ends: { } ends, Active: true } => $"Ends on {Day(ends)}",
        { Ends: { } ends } => $"Ended on {Day(ends)}",
        _ => null,
    };

    public bool Running => client is not null;

    public bool Ready
    {
        get => ready;
        set => Set(ref ready, value, nameof(Ready));
    }

    public void Reveal() => Raise(nameof(Visible));

    public void Recount() =>
        Raise(nameof(Member), nameof(Membership), nameof(MembershipLeft), nameof(MembershipNote), nameof(Lapsing));

    public void Attach(Client started) => Set(ref client, started, nameof(Running), nameof(Visible));

    public void Stop() => client?.Stop();

    public void DropClosed()
    {
        if (client is { Running: false })
            Forget();
    }

    public void Forget()
    {
        client?.Dispose();
        Set(ref client, null, nameof(Running), nameof(Visible));
    }

    private static string Left(DateTimeOffset ends) =>
        (int)Math.Ceiling((ends - DateTimeOffset.UtcNow).TotalDays) is var days && days == 1 ? "1 day left" : $"{days} days left";

    private static string Day(DateTimeOffset date) =>
        date.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.CurrentCulture);
}
