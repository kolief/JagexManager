using System.Buffers.Text;
using System.Text.Json;

namespace JagexManager.Tests;

internal static class Token
{
    public static string With(object claims) =>
        $"{Part(new { typ = "JWT" })}.{Part(claims)}.signature";

    private static string Part(object value) => Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(value));
}
