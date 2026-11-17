using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JagexManager;

public sealed class JagexException(string message, HttpStatusCode status) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;

    public bool Rejected => Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}

public sealed record Tokens(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("id_token")] string IdToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken = null)
{
    public override string ToString() => nameof(Tokens);
}

public sealed class Jagex(HttpClient http)
{
    private const string TokenUrl = $"{Auth.Origin}/oauth2/token";
    private const string SessionUrl = "https://auth.jagex.com/game-session/v1";
    private const string UserUrl = "https://api.jagex.com/v1/users";

    public static Jagex Connect()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        return new Jagex(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) });
    }

    public async Task<Tokens> ExchangeAsync(string code, string verifier, CancellationToken cancel = default)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = Auth.LauncherClientId,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = Auth.LauncherRedirect,
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl) { Content = form };

        var tokens = await SendAsync<Tokens>(request, cancel);
        return tokens is { AccessToken.Length: > 0, IdToken.Length: > 0 } ? tokens : throw AuthException.Incomplete();
    }

    public async Task<string> OpenSessionAsync(string idToken, CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{SessionUrl}/sessions")
        {
            Content = JsonContent.Create(new { idToken }),
        };

        var session = await SendAsync<Session>(request, cancel);
        return session.SessionId is { Length: > 0 } id ? id : throw AuthException.Incomplete();
    }

    public async Task<IReadOnlyList<Character>> ListCharactersAsync(string sessionId, CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{SessionUrl}/accounts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var characters = await SendAsync<List<Character?>>(request, cancel);
        return characters
            .Where(c => c is { Id.Length: > 0 })
            .Select(c => c! with { Name = c.Name ?? "" })
            .DistinctBy(c => c.Id)
            .ToList();
    }

    public async Task<string?> FindNameAsync(string subject, string accessToken, CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{UserUrl}/{Uri.EscapeDataString(subject)}/displayName");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var user = await SendAsync<User>(request, cancel);
            if (string.IsNullOrWhiteSpace(user.DisplayName))
                return null;

            return string.IsNullOrWhiteSpace(user.Suffix) ? user.DisplayName : $"{user.DisplayName}#{user.Suffix}";
        }
        catch (Exception e) when (e is JagexException or HttpRequestException
            || (e is OperationCanceledException && !cancel.IsCancellationRequested))
        {
            return null;
        }
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancel)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, cancel);
        if (!response.IsSuccessStatusCode)
            throw new JagexException($"Jagex answered {(int)response.StatusCode} for {request.RequestUri!.AbsolutePath}.", response.StatusCode);

        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancel)
                ?? throw new JagexException("Jagex sent an empty response.", response.StatusCode);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            throw new JagexException("Jagex sent a response this version does not understand.", response.StatusCode);
        }
    }

    private sealed record Session(string? SessionId);

    private sealed record User(string? DisplayName, string? Suffix);
}
