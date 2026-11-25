using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JagexManager;

public sealed partial record Release(Version Version, Uri Page)
{
    private const string LatestUrl = "https://api.github.com/repos/kolief/JagexManager/releases/latest";
    private const string Pages = "https://github.com/kolief/JagexManager/releases/";

    public static async Task<Release?> FindNewerAsync(HttpClient http, Version current, CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
        request.Headers.UserAgent.ParseAdd("JagexManager");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        try
        {
            using var response = await http.SendAsync(request, cancel);
            if (!response.IsSuccessStatusCode)
                return null;

            var latest = await response.Content.ReadFromJsonAsync<Latest>(cancel);
            return Read(latest?.Tag, latest?.Page) is { } release && release.Version > Plain(current) ? release : null;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or NotSupportedException
            || (e is OperationCanceledException && !cancel.IsCancellationRequested))
        {
            return null;
        }
    }

    public static Release? Read(string? tag, string? page)
    {
        if (tag is null || Tag().Match(tag) is not { Success: true } match)
            return null;

        if (page is null || !page.StartsWith(Pages, StringComparison.Ordinal) || !Uri.TryCreate(page, UriKind.Absolute, out var uri))
            return null;

        var parts = match.Groups.Values.Skip(1).Select(g => int.TryParse(g.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1).ToArray();
        return parts.All(n => n >= 0) ? new Release(new Version(parts[0], parts[1], parts[2]), uri) : null;
    }

    private static Version Plain(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    [GeneratedRegex(@"^v(\d{1,6})\.(\d{1,6})\.(\d{1,6})$")]
    private static partial Regex Tag();

    private sealed record Latest(
        [property: JsonPropertyName("tag_name")] string? Tag,
        [property: JsonPropertyName("html_url")] string? Page);
}
