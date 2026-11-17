namespace JagexManager;

public sealed class Login(Jagex jagex)
{
    private readonly Pkce pkce = Pkce.Create();
    private readonly string loginState = Secret.Create(16);
    private readonly string consentState = Secret.Create(16);
    private readonly string nonce = Secret.Create(16);

    private Tokens? tokens;
    private string? subject;

    public Uri StartUrl => Auth.LoginUrl(loginState, pkce.Challenge);

    public Account? Account { get; private set; }

    public bool Intercepts(string url) =>
        Account is null && (tokens is null ? Auth.IsCodeRedirect(url) : Auth.IsTokenRedirect(url));

    public async Task<Uri?> ContinueAsync(string url, CancellationToken cancel = default)
    {
        if (tokens is null || subject is null)
        {
            var code = Auth.ReadCode(url, loginState);
            var issued = await jagex.ExchangeAsync(code, pkce.Verifier, cancel);

            subject = Jwt.Claim(issued.IdToken, "sub") ?? throw AuthException.Incomplete();
            tokens = issued;
            return Auth.ConsentUrl(tokens.IdToken, nonce, consentState);
        }

        var idToken = Auth.ReadIdToken(url, consentState, nonce);
        var sessionId = await jagex.OpenSessionAsync(idToken, cancel);
        var characters = await jagex.ListCharactersAsync(sessionId, cancel);
        var name = await jagex.FindNameAsync(subject, tokens.AccessToken, cancel)
            ?? characters.Select(c => c.Name).FirstOrDefault(n => n.Length > 0)
            ?? "Jagex account";

        cancel.ThrowIfCancellationRequested();

        Account = new Account(subject, name, sessionId, characters)
        {
            Credentials = tokens.RefreshToken is { } refresh
                && new Credentials(tokens.AccessToken, refresh, tokens.IdToken) is { Complete: true } credentials
                    ? credentials
                    : null,
        };
        return null;
    }
}
