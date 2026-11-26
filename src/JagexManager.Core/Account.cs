using System.Collections.Frozen;
using System.Text.Json.Serialization;

namespace JagexManager;

public sealed record Character(
    [property: JsonPropertyName("accountId")] string Id,
    [property: JsonPropertyName("displayName")] string Name)
{
    public Membership? Membership { get; init; }
}

public sealed record Membership(DateTimeOffset? Ends, bool Renews)
{
    public bool Active => Ends > DateTimeOffset.UtcNow;
}

public sealed record Preference(string? Choice = null, string? Nickname = null, string? Options = null, bool Hidden = false)
{
    public const int NicknameLimit = 32;
    public const int OptionsLimit = 1000;
    public const int GroupLimit = 32;
    public const int NoteLimit = 200;

    public static Preference None { get; } = new();

    public DateTimeOffset? Played { get; init; }

    public IReadOnlyList<string> Groups { get; init; } = [];

    public string? Note { get; init; }

    public Placement? Placement { get; init; }

    public IReadOnlyList<PlayDay> Playtime { get; init; } = [];

    public bool Fits =>
        (Nickname?.Length ?? 0) <= NicknameLimit
        && (Options?.Length ?? 0) <= OptionsLimit
        && (Note?.Length ?? 0) <= NoteLimit
        && Groups.All(g => g.Length <= GroupLimit);

    public Preference Tidy() => this with
    {
        Choice = Clean(Choice),
        Nickname = Clean(Nickname),
        Options = Clean(Options),
        Note = Clean(Note),
        Groups = Grouped(Groups),
        Placement = Placement is { Normal: { Width: > 0, Height: > 0 } } ? Placement : null,
        Playtime = Days(Playtime),
    };

    public Preference Choose(Preference chosen) => this with
    {
        Choice = chosen.Choice,
        Nickname = chosen.Nickname,
        Options = chosen.Options,
        Hidden = chosen.Hidden,
        Groups = chosen.Groups,
        Note = chosen.Note,
    };

    public bool InGroup(string name) => Groups.Contains(name, Group.NameComparer);

    public bool Equals(Preference? other) =>
        other is not null
        && Choice == other.Choice
        && Nickname == other.Nickname
        && Options == other.Options
        && Hidden == other.Hidden
        && Played == other.Played
        && Note == other.Note
        && Placement == other.Placement
        && Groups.SequenceEqual(other.Groups)
        && Playtime.SequenceEqual(other.Playtime);

    public override int GetHashCode() => HashCode.Combine(Choice, Nickname, Options, Hidden, Played, Note, Groups.Count, Playtime.Count);

    public static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static List<string> Grouped(IEnumerable<string?>? groups) =>
        (groups ?? [])
            .Select(Clean)
            .OfType<string>()
            .Distinct(Group.NameComparer)
            .Order(Group.NameComparer)
            .ToList();

    private static List<PlayDay> Days(IEnumerable<PlayDay?>? days) =>
        (days ?? []).OfType<PlayDay>().Where(d => d.Seconds > 0).ToList();
}

public sealed record Account(string Id, string Name, string SessionId, IReadOnlyList<Character> Characters)
{
    public IReadOnlyDictionary<string, Preference> Preferences { get; init; } = FrozenDictionary<string, Preference>.Empty;

    public Credentials? Credentials { get; init; }

    public override string ToString() => $"{Name} ({Characters.Count} characters)";
}
