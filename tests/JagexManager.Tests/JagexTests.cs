using System.Net;

namespace JagexManager.Tests;

public class JagexTests
{
    [Fact]
    public async Task ListsCharactersWithSessionAsBearer()
    {
        var web = new Web().Reply(Web.AccountsPath, new[] { new { accountId = "c1", displayName = "Main" } });

        var characters = await web.Jagex().ListCharactersAsync("session-1");

        Assert.Equal([new Character("c1", "Main")], characters);
        Assert.Equal("Bearer session-1", web.Sent[0].Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task KeepsUnnamedCharactersAndSkipsUnusableOrRepeatedEntries()
    {
        var web = new Web().Reply(Web.AccountsPath, """[{"accountId":"c1"},null,{"displayName":"No id"},{"accountId":""},{"accountId":"c1","displayName":"Twice"}]""");

        var characters = await web.Jagex().ListCharactersAsync("session-1");

        Assert.Equal([new Character("c1", "")], characters);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    public async Task OnlyAuthFailuresCountAsRejected(HttpStatusCode status, bool rejected)
    {
        var web = new Web().Reply(Web.AccountsPath, "", status);

        var error = await Assert.ThrowsAsync<JagexException>(() => web.Jagex().ListCharactersAsync("session-1"));

        Assert.Equal(rejected, error.Rejected);
        Assert.DoesNotContain("session-1", error.Message);
    }

    [Theory]
    [InlineData("<html>maintenance</html>", "text/html")]
    [InlineData("<html>maintenance</html>", "application/json")]
    [InlineData("""{"unexpected":"shape"}""", "application/json")]
    public async Task ReportsUnreadableResponse(string body, string type)
    {
        var web = new Web().Reply(Web.AccountsPath, body, type: type);

        var error = await Assert.ThrowsAsync<JagexException>(() => web.Jagex().ListCharactersAsync("session-1"));

        Assert.False(error.Rejected);
    }

    [Fact]
    public async Task OpensSessionFromIdToken()
    {
        var web = new Web().Reply("/game-session/v1/sessions", new { sessionId = "session-1" });

        Assert.Equal("session-1", await web.Jagex().OpenSessionAsync("id-token"));
        Assert.Equal("""{"idToken":"id-token"}""", web.Sent[0].Body);
    }

    [Theory]
    [InlineData("""{"access_token":"access"}""")]
    [InlineData("""{"access_token":"","id_token":"id"}""")]
    public async Task ExchangeNeedsBothTokens(string body)
    {
        var web = new Web().Reply("/oauth2/token", body);

        await Assert.ThrowsAsync<AuthException>(() => web.Jagex().ExchangeAsync("code", "verifier"));
    }

    [Fact]
    public async Task NameIsOptional()
    {
        Assert.Null(await new Web().Jagex().FindNameAsync("user-1", "access"));
    }

    [Theory]
    [InlineData("""{"displayName":"Player","suffix":"1234"}""", "Player#1234")]
    [InlineData("""{"displayName":"Player"}""", "Player")]
    [InlineData("""{"suffix":"1234"}""", null)]
    public async Task NameCarriesItsSuffixWhenThereIsOne(string body, string? name)
    {
        var web = new Web().Reply("/v1/users/user-1/displayName", body);

        Assert.Equal(name, await web.Jagex().FindNameAsync("user-1", "access"));
        Assert.Equal("Bearer access", web.Sent[0].Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task NameLookupTimeoutIsNotAnError()
    {
        var web = new Web { Gate = new TaskCompletionSource() };
        var jagex = new Jagex(new HttpClient(web) { Timeout = TimeSpan.FromMilliseconds(50) });

        Assert.Null(await jagex.FindNameAsync("user-1", "access"));
    }
}
