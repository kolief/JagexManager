using System.Net;
using System.Web;

namespace JagexManager.Tests;

public class LoginTests
{
    private const string NamePath = "/v1/users/user-1/displayName";

    private static readonly object[] Characters =
    [
        new { accountId = "c1", displayName = "Main", userHash = "h1" },
        new { accountId = "c2", displayName = "Alt", userHash = "h2" },
    ];

    private static Web Online() => new Web()
        .Reply(Web.TokenPath, new { access_token = "access", id_token = Token.With(new { sub = "user-1", iat = 100 }), refresh_token = "refresh" })
        .Reply(Web.SessionPath, new { sessionId = "session-1" })
        .Reply(Web.AccountsPath, Characters)
        .Reply(NamePath, new { displayName = "Player", suffix = "1234" });

    private static string Query(Uri page, string name) => HttpUtility.ParseQueryString(page.Query)[name]!;

    private static string CodeRedirect(Login login) =>
        $"{Auth.LauncherRedirect}?code=the-code&state={Query(login.StartUrl, "state")}";

    private static async Task<string> ReachConsentAsync(Login login)
    {
        var consent = await login.ContinueAsync(CodeRedirect(login));
        var idToken = Token.With(new { nonce = Query(consent!, "nonce") });
        return $"http://localhost/#code=x&id_token={idToken}&state={Query(consent!, "state")}";
    }

    [Fact]
    public async Task SignsInThroughBothSteps()
    {
        var login = new Login(Online().Jagex());

        var redirect = await ReachConsentAsync(login);
        Assert.Null(login.Account);
        Assert.Null(await login.ContinueAsync(redirect));

        var account = login.Account!;
        Assert.Equal("user-1", account.Id);
        Assert.Equal("Player#1234", account.Name);
        Assert.Equal("session-1", account.SessionId);
        Assert.Equal([new Character("c1", "Main"), new Character("c2", "Alt")], account.Characters);
        Assert.Equal("refresh", account.Credentials!.RefreshToken);
        Assert.Equal("user-1", account.Credentials.Subject);
    }

    [Fact]
    public async Task WithoutAnIssueTimeThereIsNothingForTheLauncher()
    {
        var web = Online().Reply(Web.TokenPath, new { access_token = "access", id_token = Token.With(new { sub = "user-1" }), refresh_token = "refresh" });
        var login = new Login(web.Jagex());

        await login.ContinueAsync(await ReachConsentAsync(login));

        Assert.NotNull(login.Account);
        Assert.Null(login.Account.Credentials);
    }

    [Fact]
    public async Task WithoutARefreshTokenThereIsNothingForTheLauncher()
    {
        var web = Online().Reply(Web.TokenPath, new { access_token = "access", id_token = Token.With(new { sub = "user-1" }) });
        var login = new Login(web.Jagex());

        await login.ContinueAsync(await ReachConsentAsync(login));

        Assert.Null(login.Account!.Credentials);
    }

    [Fact]
    public async Task ExchangesCodeWithVerifier()
    {
        var web = Online();
        var login = new Login(web.Jagex());

        await ReachConsentAsync(login);

        var form = HttpUtility.ParseQueryString(web.Sent[0].Body);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("the-code", form["code"]);
        Assert.Equal(Auth.LauncherClientId, form["client_id"]);
        Assert.Equal(Auth.LauncherRedirect, form["redirect_uri"]);
        Assert.Equal(Query(login.StartUrl, "code_challenge"), Pkce.From(form["code_verifier"]!).Challenge);
    }

    [Fact]
    public async Task EachStepHasItsOwnState()
    {
        var login = new Login(Online().Jagex());

        var consent = await login.ContinueAsync(CodeRedirect(login));

        Assert.NotEqual(Query(login.StartUrl, "state"), Query(consent!, "state"));
    }

    [Fact]
    public async Task InterceptsOnlyTheRedirectOfTheCurrentStep()
    {
        var login = new Login(Online().Jagex());
        var code = CodeRedirect(login);

        Assert.True(login.Intercepts(code));
        Assert.False(login.Intercepts("http://localhost/#id_token=x"));
        Assert.False(login.Intercepts(login.StartUrl.ToString()));

        var redirect = await ReachConsentAsync(login);
        Assert.True(login.Intercepts(redirect));
        Assert.False(login.Intercepts(code));

        await login.ContinueAsync(redirect);
        Assert.False(login.Intercepts(redirect));
    }

    [Fact]
    public async Task NamesAccountAfterFirstNamedCharacterWhenNameIsUnavailable()
    {
        var web = Online()
            .Reply(NamePath, "", HttpStatusCode.InternalServerError)
            .Reply(Web.AccountsPath, new object[] { new { accountId = "c0" }, new { accountId = "c1", displayName = "Main" } });
        var login = new Login(web.Jagex());

        await login.ContinueAsync(await ReachConsentAsync(login));

        Assert.Equal("Main", login.Account!.Name);
    }

    [Fact]
    public async Task SignsInAccountWithoutCharacters()
    {
        var web = Online().Reply(Web.AccountsPath, "[]").Reply(NamePath, "", HttpStatusCode.NotFound);
        var login = new Login(web.Jagex());

        await login.ContinueAsync(await ReachConsentAsync(login));

        Assert.Empty(login.Account!.Characters);
        Assert.Equal("Jagex account", login.Account.Name);
    }

    [Fact]
    public async Task FailsWhenExchangeIsRefused()
    {
        var web = Online().Reply(Web.TokenPath, new { error = "invalid_grant", secret = "leak" }, HttpStatusCode.BadRequest);
        var login = new Login(web.Jagex());

        var error = await Assert.ThrowsAsync<JagexException>(() => ReachConsentAsync(login));
        Assert.Equal(HttpStatusCode.BadRequest, error.Status);
        Assert.DoesNotContain("leak", error.Message);
    }

    [Fact]
    public async Task FailsWhenIdTokenNamesNoSubject()
    {
        var web = Online().Reply(Web.TokenPath, new { access_token = "access", id_token = Token.With(new { }) });
        var login = new Login(web.Jagex());

        await Assert.ThrowsAsync<AuthException>(() => ReachConsentAsync(login));
        Assert.False(login.Intercepts("http://localhost/#id_token=x"));
    }

    [Fact]
    public async Task FailsWhenSessionIsMissing()
    {
        var web = Online().Reply(Web.SessionPath, new { });
        var login = new Login(web.Jagex());

        await Assert.ThrowsAsync<AuthException>(async () => await login.ContinueAsync(await ReachConsentAsync(login)));
        Assert.Null(login.Account);
    }

    [Fact]
    public async Task AbandonedSignInYieldsNoAccount()
    {
        var web = Online();
        var login = new Login(web.Jagex());
        var redirect = await ReachConsentAsync(login);

        using var closing = new CancellationTokenSource();
        web.Gate = new TaskCompletionSource();
        var pending = login.ContinueAsync(redirect, closing.Token);
        closing.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Null(login.Account);
    }
}
