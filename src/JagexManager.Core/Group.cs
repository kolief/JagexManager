namespace JagexManager;

public sealed record Group(string Name, IReadOnlyList<CharacterView> Characters)
{
    public string Detail { get; } = string.Join(" · ", Characters.Select(c => c.Name));

    public bool Same(Group other) =>
        Name == other.Name && Detail == other.Detail && Characters.SequenceEqual(other.Characters);
}
