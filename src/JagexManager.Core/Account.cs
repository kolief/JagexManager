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

    public Preference Tidy() => new(Clean(Choice), Clean(Nickname), Clean(Options), Hidden);

    public static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

public sealed record Account(string Id, string Name, string SessionId, IReadOnlyList<Character> Characters)
{
    public IReadOnlyDictionary<string, Preference> Preferences { get; init; } = FrozenDictionary<string, Preference>.Empty;

    public Credentials? Credentials { get; init; }

    public override string ToString() => $"{Name} ({Characters.Count} characters)";
}
