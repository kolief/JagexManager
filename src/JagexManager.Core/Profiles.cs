using System.Text.Json;
using System.Text.RegularExpressions;

namespace JagexManager;

public static partial class Profiles
{
    private const int NameLimit = 64;

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

    public static string? Pick(string? own, string? shared, string profile)
    {
        var picked = With(own ?? shared, profile);
        return picked == Preference.Clean(shared) ? null : picked;
    }

    private static string With(string? options, string profile)
    {
        var rest = Flag().Replace(options ?? "", "").Trim();
        var flag = profile.Any(char.IsWhiteSpace) ? $"--profile=\"{profile}\"" : $"--profile={profile}";
        return $"{flag} {rest}".Trim();
    }

    private static string? Name(JsonElement profile) =>
        profile.ValueKind == JsonValueKind.Object
        && profile.TryGetProperty("name", out var name)
        && name.ValueKind == JsonValueKind.String
            ? name.GetString()
            : null;

    private static bool Usable(string name) =>
        name.Length is > 0 and <= NameLimit
        && name == name.Trim()
        && !name.StartsWith('$')
        && !name.EndsWith('\\')
        && !name.Any(c => c == '"' || char.IsControl(c));

    [GeneratedRegex("(?<=^|\\s)--profile(?:=|\\s+)(?:\"(?<quoted>[^\"]*)\"|(?<plain>[^\\s\"]+))\\s*")]
    private static partial Regex Flag();
}
