using System.Collections.Frozen;
using System.Text.Json.Serialization;

namespace JagexManager;

public sealed record Character(
    [property: JsonPropertyName("accountId")] string Id,
    [property: JsonPropertyName("displayName")] string Name);

public sealed record Account(string Id, string Name, string SessionId, IReadOnlyList<Character> Characters)
{
    public IReadOnlyDictionary<string, string> Choices { get; init; } = FrozenDictionary<string, string>.Empty;

    public Credentials? Credentials { get; init; }

    public override string ToString() => $"{Name} ({Characters.Count} characters)";
}
