using System.Text;

namespace JagexManager;

public enum CommandKind
{
    Open,
    Play,
    PlayGroup,
}

public sealed record Command(CommandKind Kind, string? Target = null)
{
    public const string PlayFlag = "--play";
    public const string PlayGroupFlag = "--play-group";

    private const int TargetLimit = 256;

    public static Command Open { get; } = new(CommandKind.Open);

    public string Arguments => Kind switch
    {
        CommandKind.Play => $"{PlayFlag} {Quote(Target ?? "")}",
        CommandKind.PlayGroup => $"{PlayGroupFlag} {Quote(Target ?? "")}",
        _ => "",
    };

    public static Command? Parse(IReadOnlyList<string> args)
    {
        for (var position = 0; position + 1 < args.Count; position++)
        {
            CommandKind? kind = args[position] switch
            {
                PlayFlag => CommandKind.Play,
                PlayGroupFlag => CommandKind.PlayGroup,
                _ => null,
            };

            if (kind is { } found && Usable(args[position + 1]) is { } target)
                return new Command(found, target);
        }

        return null;
    }

    public string Write() => $"{Kind}\n{Target}";

    public static Command? Read(string text)
    {
        var parts = text.Split('\n', 2);
        if (!Enum.GetNames<CommandKind>().Contains(parts[0]))
            return null;

        var kind = Enum.Parse<CommandKind>(parts[0]);
        var target = parts.Length > 1 ? Usable(parts[1]) : null;
        return kind == CommandKind.Open || target is not null ? new Command(kind, target) : null;
    }

    private static string? Usable(string text) =>
        Preference.Clean(text) is { Length: <= TargetLimit } target && !target.Any(char.IsControl) ? target : null;

    private static string Quote(string text)
    {
        var quoted = new StringBuilder("\"");
        var slashes = 0;
        foreach (var letter in text)
        {
            if (letter == '\\')
            {
                slashes++;
                continue;
            }

            quoted.Append('\\', letter == '"' ? (slashes * 2) + 1 : slashes);
            quoted.Append(letter);
            slashes = 0;
        }

        return quoted.Append('\\', slashes * 2).Append('"').ToString();
    }
}
