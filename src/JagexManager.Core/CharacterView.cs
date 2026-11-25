using System.Globalization;

namespace JagexManager;

public sealed class CharacterView : View
{
    public static readonly TimeSpan LapseWarning = TimeSpan.FromDays(3);

    private Character character;
    private Client? client;
    private bool ready = true;

    public CharacterView(AccountView owner, Character character)
    {
        Owner = owner;
        this.character = character;
        owner.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AccountView.ShowHidden) or nameof(AccountView.Search))
                Raise(nameof(Visible));
        };
    }

    public AccountView Owner { get; }

    public Character Character
    {
        get => character;
        set
        {
            character = value;
            Raise(nameof(Character), nameof(Name), nameof(Nickname), nameof(RealName), nameof(Choice), nameof(Options),
                nameof(Detail), nameof(Hidden), nameof(Visible), nameof(Groups));
            Tick();
        }
    }

    public string Name => Nickname ?? RealName;

    public string? Nickname => Preference.Nickname;

    public string RealName => character.Name.Length > 0 ? character.Name : "Unnamed character";

    public string? Choice => Preference.Choice;

    public string? Options => Preference.Options;

    public bool Hidden => Preference.Hidden;

    public IReadOnlyList<string> Groups => Preference.Groups;

    public DateTimeOffset? Played => Preference.Played;

    public bool Listed => !Hidden || Owner.ShowHidden || Running;

    public bool Visible => Owner.Search is { } search ? Matches(search) || (Listed && Owner.Named(search)) : Listed;

    public string? Detail
    {
        get
        {
            string?[] parts = [Nickname is null ? null : RealName, Choice, Options, Ago(Played)];
            var shown = parts.OfType<string>().ToList();
            return shown.Count > 0 ? string.Join(" · ", shown) : null;
        }
    }

    public bool Member => character.Membership is { Active: true };

    public string? Membership => character.Membership switch
    {
        null => null,
        { Active: true } => "P2P",
        _ => "F2P",
    };

    public string? DaysLeft => character.Membership is { Active: true, Ends: { } ends } ? Days(ends) : null;

    public string? MembershipLeft => DaysLeft is { } left ? $"{left} left" : null;

    public bool Lapsing =>
        character.Membership is { Active: true, Renews: false, Ends: { } ends } && ends - DateTimeOffset.UtcNow <= LapseWarning;

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

    public bool InGroup(string group) => Preference.InGroup(group);

    public bool Matches(string text) =>
        Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
        || RealName.Contains(text, StringComparison.CurrentCultureIgnoreCase);

    public void Tick() =>
        Raise(nameof(Member), nameof(Membership), nameof(DaysLeft), nameof(MembershipLeft), nameof(MembershipNote), nameof(Lapsing), nameof(Detail));

    public void Attach(Client started) => Set(ref client, started, nameof(Running), nameof(Visible));

    public void Stop() => client?.Stop();

    public bool Focus() => client?.Focus() ?? false;

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

    private Preference Preference => Owner.Account.Preferences.GetValueOrDefault(character.Id, Preference.None);

    private static string Days(DateTimeOffset ends) =>
        (int)Math.Ceiling((ends - DateTimeOffset.UtcNow).TotalDays) is var days && days == 1 ? "1 day" : $"{days} days";

    private static string? Ago(DateTimeOffset? played)
    {
        if (played is not { } when)
            return null;

        var days = (DateTime.Today - when.ToLocalTime().Date).Days;
        return days switch
        {
            <= 0 => "Played today",
            1 => "Played yesterday",
            < 7 => $"Played {days} days ago",
            _ => $"Played on {Day(when)}",
        };
    }

    private static string Day(DateTimeOffset date) =>
        date.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.CurrentCulture);
}
