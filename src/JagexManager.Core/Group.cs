namespace JagexManager;

public sealed class Group(string name, IReadOnlyList<CharacterView> characters)
{
    public static StringComparer Names { get; } = StringComparer.OrdinalIgnoreCase;

    public string Name { get; } = name;

    public IReadOnlyList<CharacterView> Characters { get; } = characters;

    public string Detail { get; } = string.Join(" · ", characters.Select(c => c.Name));

    public IReadOnlyList<CharacterView> Playable => Characters.Where(c => !c.Running && c.Ready).ToList();

    public bool Same(Group other) =>
        Name == other.Name && Detail == other.Detail && Characters.SequenceEqual(other.Characters);
}
