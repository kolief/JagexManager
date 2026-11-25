using System.Text.Json;
using System.Text.RegularExpressions;

namespace JagexManager;

public static partial class Profiles
{
    public static string Saved { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".runelite", "profiles2", "profiles.json");

    public static IReadOnlyList<string> Read(string path)
    {
        try
        {
            if (!File.Exists(path))
                return [];

            using var file = JsonDocument.Parse(File.ReadAllText(path));
            if (file.RootElement.ValueKind != JsonValueKind.Object
                || !file.RootElement.TryGetProperty("profiles", out var profiles)
                || profiles.ValueKind != JsonValueKind.Array)
                return [];

            return profiles.EnumerateArray()
                .Select(Name)
                .OfType<string>()
                .Where(Usable)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static string? Of(string? options)
    {
        if (options is null || Flag().Match(options) is not { Success: true } match)
            return null;

        return match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["plain"].Value;
    }

    public static string? With(string? options, string? profile)
    {
        var rest = Flag().Replace(options ?? "", "").Trim();
        if (profile is null)
            return Preference.Clean(rest);

        var flag = profile.Contains(' ', StringComparison.Ordinal) ? $"--profile=\"{profile}\"" : $"--profile={profile}";
        return $"{flag} {rest}".Trim();
    }

    private static string? Name(JsonElement profile) =>
        profile.ValueKind == JsonValueKind.Object
        && profile.TryGetProperty("name", out var name)
        && name.ValueKind == JsonValueKind.String
            ? name.GetString()
            : null;

    private static bool Usable(string name) =>
        name.Length > 0 && name == name.Trim() && !name.Any(c => c == '"' || char.IsControl(c));

    [GeneratedRegex("(?<=^|\\s)--profile(?:=|\\s+)(?:\"(?<quoted>[^\"]*)\"|(?<plain>[^\\s\"]+))\\s*")]
    private static partial Regex Flag();
}
