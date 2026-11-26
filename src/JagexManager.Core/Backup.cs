using System.Text.Json;
using System.Text.Json.Nodes;

namespace JagexManager;

public sealed record Restored(Settings Settings, IReadOnlyDictionary<string, Preference> Characters);

public static class Backup
{
    public const long SizeLimit = 1024 * 1024;

    private const string Marker = "JagexManager";
    private const int Version = 1;

    public static string Write(Settings settings, IEnumerable<Account> accounts)
    {
        var characters = new JsonObject();
        foreach (var (id, preference) in accounts.SelectMany(a => a.Preferences))
        {
            var chosen = Preference.None.Choose(preference);
            if (chosen != Preference.None)
                characters[id] = JsonSerializer.SerializeToNode(new Choices(chosen.Choice, chosen.Nickname, chosen.Options, chosen.Hidden, chosen.Groups, chosen.Note));
        }

        var root = new JsonObject
        {
            [Marker] = Version,
            [nameof(Settings)] = JsonSerializer.SerializeToNode(settings, Settings.Format),
            ["Characters"] = characters,
        };
        return root.ToJsonString(Settings.Format);
    }

    public static Restored Load(string path)
    {
        if (new FileInfo(path).Length > SizeLimit)
            throw Unreadable();

        return Read(File.ReadAllText(path));
    }

    public static Restored Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(Marker, out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var number)
                || number != Version
                || !root.TryGetProperty(nameof(Settings), out var settings)
                || settings.ValueKind != JsonValueKind.Object)
                throw Unreadable();

            var characters = new Dictionary<string, Preference>();
            if (root.TryGetProperty("Characters", out var entries) && entries.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in entries.EnumerateObject())
                {
                    if (entry.Name.Length > 0 && entry.Value.ValueKind == JsonValueKind.Object && Chosen(entry.Value) is { } preference)
                        characters[entry.Name] = preference;
                }
            }

            return new Restored(Settings.Read(settings), characters);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            throw Unreadable();
        }
    }

    private static Preference? Chosen(JsonElement entry)
    {
        if (entry.Deserialize<Choices>() is not { } choices)
            return null;

        var preference = new Preference(choices.Choice, choices.Nickname, choices.Options, choices.Hidden)
        {
            Groups = (choices.Groups ?? []).OfType<string>().ToList(),
            Note = choices.Note,
        }.Tidy();

        return preference.Fits && preference != Preference.None ? preference : null;
    }

    private static InvalidDataException Unreadable() => new("This file is not a JagexManager backup, or it is damaged.");

    private sealed record Choices(string? Choice, string? Nickname, string? Options, bool Hidden, IReadOnlyList<string?>? Groups, string? Note);
}
