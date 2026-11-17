using System.Web;

namespace JagexManager.Tests;

public class AuthTests
{
    [Fact]
    public void LoginUrlAsksForLauncherCode()
    {
        var url = Auth.LoginUrl("st", "ch");
        var query = HttpUtility.ParseQueryString(url.Query);

        Assert.Equal("https://account.jagex.com/oauth2/auth", url.GetLeftPart(UriPartial.Path));
        Assert.Equal("code", query["response_type"]);
        Assert.Equal(Auth.LauncherClientId, query["client_id"]);
        Assert.Equal(Auth.LauncherRedirect, query["redirect_uri"]);
        Assert.Equal("ch", query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("st", query["state"]);
        Assert.Equal("openid offline gamesso.token.create user.profile.read", query["scope"]);
    }

    [Fact]
    public void ConsentUrlCarriesIdTokenAndNonce()
    {
        var query = HttpUtility.ParseQueryString(Auth.ConsentUrl("a.b.c", "n", "st").Query);

        Assert.Equal("a.b.c", query["id_token_hint"]);
        Assert.Equal("n", query["nonce"]);
        Assert.Equal("st", query["state"]);
        Assert.Equal("id_token code", query["response_type"]);
        Assert.Equal(Auth.ConsentClientId, query["client_id"]);
        Assert.Equal("http://localhost", query["redirect_uri"]);
    }

    [Theory]
    [InlineData("https://secure.runescape.com/m=weblogin/launcher-redirect?code=abc&state=st")]
    [InlineData("jagex:code=abc,state=st,intent=social_auth")]
    public void ReadsCodeFromEitherRedirect(string url)
    {
        Assert.True(Auth.IsCodeRedirect(url));
        Assert.Equal("abc", Auth.ReadCode(url, "st"));
    }

    [Theory]
    [InlineData("https://account.jagex.com/oauth2/auth?code=abc")]
    [InlineData("https://secure.runescape.com.evil.example/m=weblogin/launcher-redirect?code=abc")]
    [InlineData("https://secure.runescape.com/m=weblogin/other?code=abc")]
    [InlineData("http://secure.runescape.com/m=weblogin/launcher-redirect?code=abc")]
    [InlineData("about:blank")]
    public void OtherPagesAreNotCodeRedirects(string url) => Assert.False(Auth.IsCodeRedirect(url));

    [Fact]
    public void RejectsCodeWithWrongState()
    {
        var url = $"{Auth.LauncherRedirect}?code=abc&state=other";

        Assert.Throws<AuthException>(() => Auth.ReadCode(url, "st"));
    }

    [Fact]
    public void ReportsErrorCodeFromRedirect()
    {
        var url = $"{Auth.LauncherRedirect}?error=access_denied&state=st";

        var error = Assert.Throws<AuthException>(() => Auth.ReadCode(url, "st"));
        Assert.Contains("access_denied", error.Message);
    }

    [Theory]
    [InlineData("jagex:error=Your%20account%20is%20locked.%20Call%20555-0100,state=st")]
    [InlineData("jagex:error=access_denied,state=other")]
    public void NeverShowsWordingSuppliedByTheResponse(string url)
    {
        var error = Assert.Throws<AuthException>(() => Auth.ReadCode(url, "st"));

        Assert.DoesNotContain("locked", error.Message);
        Assert.DoesNotContain("access_denied", error.Message);
    }

    [Fact]
    public void RejectsRedirectWithoutCode() =>
        Assert.Throws<AuthException>(() => Auth.ReadCode($"{Auth.LauncherRedirect}?state=st", "st"));

    [Fact]
    public void ReadsIdTokenFromFragment()
    {
        var token = Token.With(new { nonce = "n" });
        var url = $"http://localhost/#code=x&id_token={token}&state=st";

        Assert.True(Auth.IsTokenRedirect(url));
        Assert.Equal(token, Auth.ReadIdToken(url, "st", "n"));
    }

    [Fact]
    public void IgnoresIdTokenOutsideTheFragment()
    {
        var url = $"http://localhost/?id_token={Token.With(new { nonce = "n" })}&state=st";

        Assert.Throws<AuthException>(() => Auth.ReadIdToken(url, "st", "n"));
    }

    [Fact]
    public void ReportsErrorFromConsentQuery()
    {
        var error = Assert.Throws<AuthException>(() => Auth.ReadIdToken("http://localhost/?error=consent_required&state=st", "st", "n"));

        Assert.Contains("consent_required", error.Message);
    }

    [Theory]
    [InlineData("http://localhost:8080/#id_token=x")]
    [InlineData("https://localhost/#id_token=x")]
    [InlineData("http://localhost.example/#id_token=x")]
    [InlineData("http://localhost/callback#id_token=x")]
    public void OtherPagesAreNotTokenRedirects(string url) => Assert.False(Auth.IsTokenRedirect(url));

    [Fact]
    public void RejectsIdTokenWithWrongNonce()
    {
        var url = $"http://localhost/#id_token={Token.With(new { nonce = "other" })}&state=st";

        Assert.Throws<AuthException>(() => Auth.ReadIdToken(url, "st", "n"));
    }

    [Fact]
    public void RejectsIdTokenWithWrongState()
    {
        var url = $"http://localhost/#id_token={Token.With(new { nonce = "n" })}&state=other";

        Assert.Throws<AuthException>(() => Auth.ReadIdToken(url, "st", "n"));
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("a.!!!.c")]
    [InlineData("a.bm90IGpzb24.c")]
    [InlineData("a.WzFd.c")]
    [InlineData("a.bnVsbA.c")]
    public void RejectsMalformedIdToken(string token) =>
        Assert.Throws<AuthException>(() => Auth.ReadIdToken($"http://localhost/#id_token={token}&state=st", "st", "n"));

    [Fact]
    public void ErrorsNeverEchoTokens()
    {
        var token = Token.With(new { nonce = "other" });

        var error = Assert.Throws<AuthException>(() => Auth.ReadIdToken($"http://localhost/#id_token={token}&state=st", "st", "n"));
        Assert.DoesNotContain(token, error.Message);
    }
}
