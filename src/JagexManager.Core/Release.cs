using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JagexManager;

public sealed partial record Release(Version Version, Uri Page)
{
    private const string LatestUrl = "https://api.github.com/repos/kolief/JagexManager/releases/latest";
    private const string Host = "github.com";
    private const string ReleasesPath = "/kolief/JagexManager/releases/";

    public static async Task<Release?> FindNewerAsync(HttpClient http, Version current)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
        request.Headers.UserAgent.ParseAdd("JagexManager");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        try
        {
            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return null;

            var latest = await response.Content.ReadFromJsonAsync<Latest>();
            return Read(latest?.Tag, latest?.Page) is { } release && release.Version > Plain(current) ? release : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    public static Release? Read(string? tag, string? page)
    {
        if (tag is null || Tag().Match(tag) is not { Success: true } match)
            return null;

        if (!Uri.TryCreate(page, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.Host != Host
            || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0
            || !uri.AbsolutePath.StartsWith(ReleasesPath, StringComparison.Ordinal))
            return null;

        int Part(int group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
        return new Release(new Version(Part(1), Part(2), Part(3)), uri);
    }

    private static Version Plain(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    [GeneratedRegex("^v([0-9]{1,6})\\.([0-9]{1,6})\\.([0-9]{1,6})$")]
    private static partial Regex Tag();

    private sealed record Latest(
        [property: JsonPropertyName("tag_name")] string? Tag,
        [property: JsonPropertyName("html_url")] string? Page);
}
