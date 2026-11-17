using System.Collections.Specialized;
using System.Web;

namespace JagexManager;

public sealed class AuthException(string message) : Exception(message)
{
    internal static AuthException Mismatch() => new("The sign-in response did not match this request. Please try again.");

    internal static AuthException Incomplete() => new("Jagex did not return the expected sign-in details.");
}

public static class Auth
{
    public const string Origin = "https://account.jagex.com";
    public const string LauncherClientId = "com_jagex_auth_desktop_launcher";
    public const string ConsentClientId = "1fddee4e-b100-4f4e-b2b0-097f9088f9d2";
    public const string LauncherRedirect = "https://secure.runescape.com/m=weblogin/launcher-redirect";
    public const string ConsentRedirect = "http://localhost";

    private const string LauncherScheme = "jagex:";

    public static Uri LoginUrl(string state, string challenge) => Page(new()
    {
        ["auth_method"] = "",
        ["login_type"] = "",
        ["flow"] = "launcher",
        ["response_type"] = "code",
        ["client_id"] = LauncherClientId,
        ["redirect_uri"] = LauncherRedirect,
        ["code_challenge"] = challenge,
        ["code_challenge_method"] = "S256",
        ["prompt"] = "login",
        ["scope"] = "openid offline gamesso.token.create user.profile.read",
        ["state"] = state,
    });

    public static Uri ConsentUrl(string idToken, string nonce, string state) => Page(new()
    {
        ["id_token_hint"] = idToken,
        ["nonce"] = nonce,
        ["prompt"] = "consent",
        ["redirect_uri"] = ConsentRedirect,
        ["response_type"] = "id_token code",
        ["state"] = state,
        ["client_id"] = ConsentClientId,
        ["scope"] = "openid offline",
    });

    public static bool IsCodeRedirect(string url) =>
        url.StartsWith(LauncherScheme, StringComparison.OrdinalIgnoreCase) || IsPage(url, LauncherRedirect);

    public static bool IsTokenRedirect(string url) => IsPage(url, ConsentRedirect);

    public static string ReadCode(string url, string state)
    {
        var values = url.StartsWith(LauncherScheme, StringComparison.OrdinalIgnoreCase)
            ? HttpUtility.ParseQueryString(url[LauncherScheme.Length..].Replace(',', '&'))
            : HttpUtility.ParseQueryString(new Uri(url).Query);

        return Read(values, "code", state);
    }

    public static string ReadIdToken(string url, string state, string nonce)
    {
        var uri = new Uri(url);
        var values = HttpUtility.ParseQueryString(uri.Fragment.TrimStart('#'));
        if (HttpUtility.ParseQueryString(uri.Query) is { } query && query["error"] is not null)
            values = query;

        var token = Read(values, "id_token", state);
        if (Jwt.Claim(token, "nonce") != nonce)
            throw AuthException.Mismatch();

        return token;
    }

    private static string Read(NameValueCollection values, string name, string state)
    {
        if (values["state"] != state)
            throw AuthException.Mismatch();

        if (values["error"] is { } error)
        {
            var code = error.Length is > 0 and <= 40 && error.All(c => c is (>= 'a' and <= 'z') or '_') ? error : "unknown_error";
            throw new AuthException($"Jagex refused the sign-in ({code}).");
        }

        return values[name] is { Length: > 0 } value ? value : throw AuthException.Incomplete();
    }

    private static bool IsPage(string url, string page)
    {
        var expected = new Uri(page);
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == expected.Scheme
            && uri.Authority == expected.Authority
            && uri.AbsolutePath == expected.AbsolutePath;
    }

    private static Uri Page(Dictionary<string, string> query) =>
        new($"{Origin}/oauth2/auth?{string.Join('&', query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"))}");
}
