namespace JagexManager.Tests;

public class SecretTests
{
    [Fact]
    public void SecretsAreUrlSafeAndFresh()
    {
        var secret = Secret.Create(64);

        Assert.Matches("^[A-Za-z0-9_-]+$", secret);
        Assert.NotEqual(secret, Secret.Create(64));
    }

    [Fact]
    public void TypesHoldingSecretsDoNotPrintThem()
    {
        Assert.DoesNotContain("session-1", new Account("id", "Name", "session-1", []).ToString());
        Assert.DoesNotContain("access", new Tokens("access", "id-token").ToString());
        Assert.DoesNotContain("the-verifier", Pkce.From("the-verifier").ToString());
    }
}
