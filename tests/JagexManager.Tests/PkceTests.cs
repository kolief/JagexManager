namespace JagexManager.Tests;

public class PkceTests
{
    [Fact]
    public void ChallengeMatchesRfc7636Example()
    {
        var pkce = Pkce.From("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk");

        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", pkce.Challenge);
    }

    [Fact]
    public void CreatesFreshVerifierOfValidLength()
    {
        var pkce = Pkce.Create();

        Assert.InRange(pkce.Verifier.Length, 43, 128);
        Assert.Equal(Pkce.From(pkce.Verifier).Challenge, pkce.Challenge);
        Assert.NotEqual(pkce.Verifier, Pkce.Create().Verifier);
    }
}
