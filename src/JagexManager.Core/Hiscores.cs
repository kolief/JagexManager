using System.Net;
using System.Text.Json;

namespace JagexManager;

public sealed record Levels(int Total, int? Combat)
{
    public string Text => Combat is { } combat ? $"Combat {combat} · Total {Total:N0}" : $"Total {Total:N0}";
}

public sealed class Hiscores(HttpClient http)
{
    private const string Url = "https://secure.runescape.com/m=hiscore_oldschool/index_lite.json?player=";
    private const int ResponseLimit = 256 * 1024;

    public static Hiscores Connect() =>
        new(new HttpClient { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = ResponseLimit });

    public async Task<Levels?> FindAsync(string name, CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url + Uri.EscapeDataString(name));
        request.Headers.UserAgent.ParseAdd("JagexManager");
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await http.SendAsync(request, cancel);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        return Read(document.RootElement);
    }

    public static Levels? Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("skills", out var skills)
            || skills.ValueKind != JsonValueKind.Array)
            return null;

        var levels = new Dictionary<string, int>();
        foreach (var skill in skills.EnumerateArray())
        {
            if (skill.ValueKind == JsonValueKind.Object
                && skill.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                && Number(skill, "rank") > 0
                && Number(skill, "level") is > 0 and var level)
                levels.TryAdd(name.GetString()!, level);
        }

        if (!levels.TryGetValue("Overall", out var total))
            return null;

        int? Level(string skill) => levels.TryGetValue(skill, out var found) ? found : null;

        var combat = (Level("Attack"), Level("Strength"), Level("Defence"), Level("Hitpoints"), Level("Ranged"), Level("Prayer"), Level("Magic")) is
            ({ } attack, { } strength, { } defence, { } hitpoints, { } ranged, { } prayer, { } magic)
            ? Combat(attack, strength, defence, hitpoints, ranged, prayer, magic)
            : (int?)null;

        return new Levels(total, combat);
    }

    public static int Combat(int attack, int strength, int defence, int hitpoints, int ranged, int prayer, int magic) =>
        ((10 * (defence + hitpoints + (prayer / 2))) + (13 * Math.Max(attack + strength, Math.Max(ranged * 3 / 2, magic * 3 / 2)))) / 40;

    private static int? Number(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;
}
