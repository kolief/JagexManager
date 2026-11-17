using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace JagexManager.Tests;

public sealed class LauncherTests : IDisposable
{
    private static readonly Credentials Player = new("access", "refresh", Token.With(new { sub = "user-1", iat = 100 }));

    private readonly Folder folder = new();
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
    private readonly Launcher launcher;

    public LauncherTests() => launcher = new Launcher(folder.Root, key);

    public void Dispose() => folder.Dispose();

    private string Creds => folder.At(@"auth\creds");

    private void Seal(string json)
    {
        var plain = Encoding.UTF8.GetBytes(json);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16))
            aes.Encrypt(nonce, plain, cipher, tag);

        Directory.CreateDirectory(folder.At("auth"));
        File.WriteAllText(Creds, $"{Convert.ToBase64String(nonce)}:{Convert.ToBase64String([.. cipher, .. tag])}");
    }

    private JsonNode Unseal()
    {
        var parts = File.ReadAllText(Creds).Split(':');
        var nonce = Convert.FromBase64String(parts[0]);
        var data = Convert.FromBase64String(parts[1]);
        var plain = new byte[data.Length - 16];
        using (var aes = new AesGcm(key, 16))
            aes.Decrypt(nonce, data[..^16], data[^16..], plain);

        return JsonNode.Parse(plain)!;
    }

    [Fact]
    public void ReadsBackWhatItWrote()
    {
        launcher.Write(Player, "session-1");

        Assert.Equal(Player, launcher.Read());
        Assert.Equal("user-1", launcher.Read()!.Subject);
    }

    [Fact]
    public void WritesTheLaunchersOwnFormatWithAnExpiredAccessToken()
    {
        launcher.Write(Player, "session-1");

        var stored = Unseal();
        var entry = Assert.Single(stored["credentialsList"]!.AsArray())!;
        Assert.Equal("refresh", (string?)entry["refreshToken"]);
        Assert.Equal(0, (int)entry["accessTokenExpiry"]!);
        Assert.Equal("session-1", (string?)stored["gameSessionId"]);
        Assert.Equal(12, Convert.FromBase64String(File.ReadAllText(Creds).Split(':')[0]).Length);
    }

    [Fact]
    public void WritingForgetsTheCachedCharactersAndLeavesNoDraft()
    {
        File.WriteAllText(folder.At("cache.bin"), "other account");

        launcher.Write(Player, "session-1");

        Assert.False(File.Exists(folder.At("cache.bin")));
        Assert.Equal(["creds"], Directory.GetFiles(folder.At("auth")).Select(Path.GetFileName));
    }

    [Fact]
    public void ReadsTheLaunchersOwnFile()
    {
        Seal($$"""{"credentialsList":[{"accessToken":"access","refreshToken":"refresh","idToken":"{{Player.IdToken}}","accessTokenExpiry":1794954880}],"gameSessionId":"session-1"}""");

        Assert.Equal(Player, launcher.Read());
    }

    [Theory]
    [InlineData("""{"credentialsList":[]}""")]
    [InlineData("""{"credentialsList":[],"gameSessionId":"session-1"}""")]
    public void ASignedOutLauncherHoldsNothing(string json)
    {
        Seal(json);

        Assert.Null(launcher.Read());
    }

    [Fact]
    public void NoFileHoldsNothing() => Assert.Null(launcher.Read());

    [Theory]
    [InlineData("""{"credentialsList":[{"accessToken":"a","refreshToken":"r","idToken":"ID"}],"provider":"steam"}""")]
    [InlineData("""{"credentialsList":[{"accessToken":"a","refreshToken":"r","idToken":"ID","scope":"x"}]}""")]
    [InlineData("""{"credentialsList":[{"accessToken":"a","refreshToken":"r","idToken":"ID"},{"accessToken":"a","refreshToken":"r","idToken":"ID"}]}""")]
    [InlineData("""{"credentialsList":[{"accessToken":"a","idToken":"ID"}]}""")]
    [InlineData("""{"credentialsList":[{"accessToken":"a","refreshToken":7,"idToken":"ID"}]}""")]
    [InlineData("""{"credentialsList":[{"accessToken":"a","refreshToken":"r","idToken":"not a token"}]}""")]
    [InlineData("""{"credentialsList":[{"accessToken":"a","refreshToken":"r","idToken":"NOSUB"}]}""")]
    [InlineData("""{"credentialsList":[{"accessToken":"a","refreshToken":"r","idToken":"NOIAT"}]}""")]
    [InlineData("""{"credentialsList":{}}""")]
    [InlineData("""[]""")]
    [InlineData("""not json""")]
    public void AnUnfamiliarFileIsRefused(string json)
    {
        Seal(json.Replace("NOSUB", Token.With(new { iat = 1 })).Replace("NOIAT", Token.With(new { sub = "user-1" })).Replace("ID", Player.IdToken));

        Assert.Throws<InvalidDataException>(launcher.Read);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no separator")]
    [InlineData("AAAA:AAAA")]
    [InlineData("AAAAAAAAAAAAAAAA:not base64!")]
    public void AnUnreadableFileIsRefused(string text)
    {
        Directory.CreateDirectory(folder.At("auth"));
        File.WriteAllText(Creds, text);

        Assert.Throws<InvalidDataException>(launcher.Read);
    }

    [Fact]
    public void AFileSealedWithAnotherKeyIsRefused()
    {
        launcher.Write(Player, "session-1");

        Assert.Throws<InvalidDataException>(new Launcher(folder.Root, RandomNumberGenerator.GetBytes(32)).Read);
    }

    [Fact]
    public void SigningOutRemovesTheSignInAndCachedCharacters()
    {
        launcher.Write(Player, "session-1");
        File.WriteAllText(folder.At("cache.bin"), "characters");

        launcher.Clear();

        Assert.Null(launcher.Read());
        Assert.False(File.Exists(folder.At("cache.bin")));
    }

    [Fact]
    public void SigningOutWithNothingThereIsHarmless()
    {
        launcher.Clear();

        Assert.Null(launcher.Read());
    }

    [Fact]
    public void ACacheThatCannotBeDeletedLeavesTheSignInAlone()
    {
        launcher.Write(Player, "session-1");
        var cache = folder.At("cache.bin");
        File.WriteAllText(cache, "characters");
        File.SetAttributes(cache, FileAttributes.ReadOnly);

        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => launcher.Write(Player with { RefreshToken = "other" }, "session-1"));
            Assert.Equal(Player, launcher.Read());
        }
        finally
        {
            File.SetAttributes(cache, FileAttributes.Normal);
        }
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(100.7, 100)]
    public void AnIssueTimeMayBeFractional(double iat, long issued)
    {
        var credentials = new Credentials("a", "r", Token.With(new { sub = "user-1", iat }));

        Assert.Equal(issued, credentials.Issued);
        Assert.True(credentials.Complete);
    }

    [Fact]
    public void CredentialsKeepTheirValuesOutOfText() =>
        Assert.DoesNotContain("refresh", Player.ToString());
}
