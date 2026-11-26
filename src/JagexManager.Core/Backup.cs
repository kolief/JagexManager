using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace JagexManager;

public sealed record Backup(Settings Settings, IReadOnlyDictionary<string, Preference> Characters)
{
    public const long SizeLimit = 1024 * 1024;

    private const string Marker = "JagexManager";
    private const int Version = 1;
    private const int NameLimit = 100;
    private const int PathLimit = 1000;

    public static string Write(Settings settings, IEnumerable<Account> accounts)
    {
        var characters = new JsonObject();
        foreach (var (id, preference) in accounts.SelectMany(a => a.Preferences))
        {
            var entry = Entry.From(preference);
            if (entry.Preference != Preference.None)
                characters[id] = JsonSerializer.SerializeToNode(entry);
        }

        var root = new JsonObject
        {
            [Marker] = Version,
            [nameof(Settings)] = JsonSerializer.SerializeToNode(settings, Settings.Format),
            [nameof(Characters)] = characters,
        };
        return root.ToJsonString(Settings.Format);
    }

    public static Backup Load(string path)
    {
        using var file = File.OpenRead(path);
        var buffer = new byte[SizeLimit + 1];
        var length = file.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        if (length > SizeLimit)
            throw Unreadable();

        return Read(Encoding.UTF8.GetString(buffer, 0, length).TrimStart('\uFEFF'));
    }

    public static Backup Read(string json)
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
                || !root.TryGetProperty(nameof(Settings), out var saved)
                || saved.ValueKind != JsonValueKind.Object)
                throw Unreadable();

            var settings = Settings.Read(saved);
            if (!settings.Custom.All(p => p.Name.Length <= NameLimit && p.Path.Length <= PathLimit && Plain(p.Name) && Plain(p.Path))
                || !settings.Options.Values.All(o => o.Length <= Preference.OptionsLimit && Plain(o)))
                throw Unreadable();

            var characters = new Dictionary<string, Preference>();
            if (root.TryGetProperty(nameof(Characters), out var entries) && entries.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in entries.EnumerateObject())
                {
                    if (entry.Name.Length > 0
                        && entry.Value.ValueKind == JsonValueKind.Object
                        && entry.Value.Deserialize<Entry>()?.Preference is { Fits: true } preference
                        && preference != Preference.None
                        && new[] { preference.Nickname, preference.Options, preference.Note }.Concat(preference.Groups).All(Plain))
                        characters[entry.Name] = preference;
                }
            }

            return new Backup(settings, characters);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            throw Unreadable();
        }
    }

    private static bool Plain(string? text) =>
        text is null || !text.Any(c => char.IsControl(c)
            || char.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);

    private static InvalidDataException Unreadable() => new("This file is not a JagexManager backup, or it is damaged.");

    private sealed record Entry(string? Choice, string? Nickname, string? Options, bool Hidden, IReadOnlyList<string?>? Groups, string? Note)
    {
        [JsonIgnore]
        public Preference Preference => new Preference(Choice, Nickname, Options, Hidden)
        {
            Groups = (Groups ?? []).OfType<string>().ToList(),
            Note = Note,
        }.Tidy();

        public static Entry From(Preference preference) =>
            new(preference.Choice, preference.Nickname, preference.Options, preference.Hidden, preference.Groups, preference.Note);
    }
}
