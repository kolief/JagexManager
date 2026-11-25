using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JagexManager;

public enum Theme
{
    RuneLite,
    System,
    Light,
    Dark,
}

public sealed record Settings
{
    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Default { get; init; } = Program.RuneLite;

    public IReadOnlyList<Program> Custom { get; init; } = [];

    public IReadOnlyDictionary<string, string> Options { get; init; } = FrozenDictionary<string, string>.Empty;

    public Theme Theme { get; init; } = Theme.RuneLite;

    public bool UseGameFont { get; init; } = true;

    public bool CheckUpdates { get; init; } = true;

    public bool UseTray { get; init; }

    [JsonIgnore]
    public IReadOnlyList<Program> Programs =>
        Offered.Select(p => p with { Arguments = $"{p.Arguments} {Options.GetValueOrDefault(p.Name)}".Trim() }).ToList();

    [JsonIgnore]
    public IReadOnlyList<Program> Installed => Programs.Where(p => p.Installed).ToList();

    [JsonIgnore]
    public IEnumerable<string> Names => Offered.Select(p => p.Name);

    private IEnumerable<Program> Offered => Program.Known.Concat(Custom);

    public Program Find(string? name)
    {
        var programs = Programs;
        return programs.FirstOrDefault(p => p.Name == name)
            ?? programs.FirstOrDefault(p => p.Name == Default)
            ?? programs[0];
    }

    public Program Find(string? name, string? options)
    {
        var program = Find(name);
        if (options is null)
            return program;

        var plain = Offered.First(p => p.Name == program.Name);
        return program with { Arguments = $"{plain.Arguments} {options}".Trim() };
    }

    public bool Removable(string name) => Custom.Any(p => p.Name == name);

    public (Settings Settings, string Name) Add(string path)
    {
        path = path.Trim().Replace('/', '\\');
        var same = Offered.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
        if (same is not null)
            return (this, same.Name);

        var name = FreeName(Path.GetFileNameWithoutExtension(path));
        return (this with { Custom = [.. Custom, new Program(name, path)] }, name);
    }

    public Settings Remove(string name)
    {
        if (!Removable(name))
            return this;

        return WithOptions(name, "") with
        {
            Custom = Custom.Where(p => p.Name != name).ToList(),
            Default = Default == name ? Program.RuneLite : Default,
        };
    }

    public Settings Switch(string name, string typed) =>
        WithOptions(Default, typed) with { Default = name };

    public Settings WithOptions(string name, string options)
    {
        var all = Options.Where(o => o.Key != name).ToDictionary();
        var text = options.Trim();
        if (text.Length > 0)
            all[name] = text;

        return this with { Options = all };
    }

    public bool Equals(Settings? other) =>
        other is not null
        && Default == other.Default
        && Theme == other.Theme
        && UseGameFont == other.UseGameFont
        && CheckUpdates == other.CheckUpdates
        && UseTray == other.UseTray
        && Custom.SequenceEqual(other.Custom)
        && Options.Count == other.Options.Count
        && Options.All(o => other.Options.GetValueOrDefault(o.Key) == o.Value);

    public override int GetHashCode() => HashCode.Combine(Default, Theme, UseGameFont, CheckUpdates, UseTray, Custom.Count, Options.Count);

    public static Settings Load(string path)
    {
        var defaults = new Settings();
        try
        {
            if (!File.Exists(path))
                return defaults;

            using var file = JsonDocument.Parse(File.ReadAllText(path));
            var root = file.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return defaults;

            var theme = Text(root, nameof(Theme));

            var settings = new Settings
            {
                Default = Text(root, nameof(Default)) ?? defaults.Default,
                Custom = CustomFrom(root),
                Options = Texts(root, nameof(Options)),
                Theme = Enum.GetNames<Theme>().Contains(theme) ? Enum.Parse<Theme>(theme!) : defaults.Theme,
                UseGameFont = Flag(root, nameof(UseGameFont)) ?? defaults.UseGameFont,
                CheckUpdates = Flag(root, nameof(CheckUpdates)) ?? defaults.CheckUpdates,
                UseTray = Flag(root, nameof(UseTray)) ?? defaults.UseTray,
            };

            if (Legacy(root))
                settings = Upgrade(settings, Text(root, "ClientPath"), Text(root, "Arguments"));

            var names = settings.Names.ToHashSet();
            return settings with
            {
                Default = settings.Find(settings.Default).Name,
                Options = settings.Options
                    .Where(o => names.Contains(o.Key) && o.Value.Trim().Length > 0)
                    .ToDictionary(o => o.Key, o => o.Value.Trim()),
            };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return defaults;
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Format));
    }

    private string FreeName(string wanted)
    {
        var name = wanted.Trim().Length > 0 ? wanted.Trim() : "Client";
        var taken = Names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name))
            return name;

        var number = 2;
        while (taken.Contains($"{name} {number}"))
            number++;

        return $"{name} {number}";
    }

    private static bool Legacy(JsonElement root) =>
        !new[] { nameof(Default), nameof(Custom), nameof(Options) }.Any(name => root.TryGetProperty(name, out _));

    private static Settings Upgrade(Settings settings, string? path, string? arguments)
    {
        if (string.IsNullOrWhiteSpace(path))
            return settings.WithOptions(Program.RuneLite, arguments ?? "");

        var (changed, name) = settings.Add(path);
        return (changed with { Default = name }).WithOptions(name, arguments ?? "");
    }

    private static List<Program> CustomFrom(JsonElement root)
    {
        var custom = new List<Program>();
        if (!root.TryGetProperty(nameof(Custom), out var entries) || entries.ValueKind != JsonValueKind.Array)
            return custom;

        var taken = Program.Known.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
        {
            var name = Text(entry, nameof(Program.Name))?.Trim();
            var path = Text(entry, nameof(Program.Path));
            if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(path) || !taken.Add(name))
                continue;

            custom.Add(new Program(name, path));
        }

        return custom;
    }

    private static bool? Flag(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        }
        : null;

    private static string? Text(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) ? Text(value) : null;

    private static string? Text(JsonElement value)
    {
        try
        {
            return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> Texts(JsonElement parent, string name)
    {
        var texts = new Dictionary<string, string>();
        if (!parent.TryGetProperty(name, out var members) || members.ValueKind != JsonValueKind.Object)
            return texts;

        foreach (var member in members.EnumerateObject())
        {
            if (Text(member.Value) is { } text)
                texts[member.Name] = text;
        }

        return texts;
    }
}
