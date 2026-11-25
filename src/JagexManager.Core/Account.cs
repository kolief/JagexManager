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
    public static Preference None { get; } = new();

    public DateTimeOffset? Played { get; init; }

    public IReadOnlyList<string> Groups { get; init; } = [];

    public Preference Tidy() => new(Clean(Choice), Clean(Nickname), Clean(Options), Hidden)
    {
        Played = Played,
        Groups = Grouped(Groups),
    };

    public bool InGroup(string name) => Groups.Contains(name, StringComparer.OrdinalIgnoreCase);

    public bool Equals(Preference? other) =>
        other is not null
        && Choice == other.Choice
        && Nickname == other.Nickname
        && Options == other.Options
        && Hidden == other.Hidden
        && Played == other.Played
        && Groups.SequenceEqual(other.Groups);

    public override int GetHashCode() => HashCode.Combine(Choice, Nickname, Options, Hidden, Played, Groups.Count);

    public static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static List<string> Grouped(IEnumerable<string?>? groups) =>
        (groups ?? [])
            .Select(Clean)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

public sealed record Account(string Id, string Name, string SessionId, IReadOnlyList<Character> Characters)
{
    public IReadOnlyDictionary<string, Preference> Preferences { get; init; } = FrozenDictionary<string, Preference>.Empty;

    public Credentials? Credentials { get; init; }

    public override string ToString() => $"{Name} ({Characters.Count} characters)";
}
