using System.Buffers.Text;
using System.Text.Json;

namespace JagexManager;

public static class Jwt
{
    public static string? Claim(string token, string name) =>
        Read(token, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    public static long? Time(string token, string name) =>
        Read(token, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetDouble(out var time) ? (long)time : null;

    private static JsonElement? Read(string token, string name)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
            throw Malformed();

        try
        {
            using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
            if (payload.RootElement.ValueKind != JsonValueKind.Object)
                throw Malformed();

            return payload.RootElement.TryGetProperty(name, out var value) ? value.Clone() : null;
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            throw Malformed();
        }
    }

    private static AuthException Malformed() => new("Jagex returned a malformed ID token.");
}
