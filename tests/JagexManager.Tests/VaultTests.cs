using System.Security.Cryptography;
using System.Text;

namespace JagexManager.Tests;

public class VaultTests
{
    private static void Seal(string path, string json)
    {
        var entropy = Encoding.UTF8.GetBytes("JagexManager.Vault.v1");
        File.WriteAllBytes(path, ProtectedData.Protect(Encoding.UTF8.GetBytes(json), entropy, DataProtectionScope.CurrentUser));
    }

    private static readonly Account Sample = new("user-1", "Player#1234", "session-secret", [new("c1", "Main"), new("c2", "Alt")]);

    [Fact]
    public void MissingVaultIsEmpty()
    {
        using var folder = new Folder();

        Assert.Empty(new Vault(folder.At("vault.bin")).Load());
    }

    [Fact]
    public void RoundTripsAccounts()
    {
        using var folder = new Folder();
        var vault = new Vault(folder.At(@"nested\vault.bin"));

        vault.Save([Sample]);
        var loaded = Assert.Single(vault.Load());

        Assert.Equal(Sample.Id, loaded.Id);
        Assert.Equal(Sample.Name, loaded.Name);
        Assert.Equal(Sample.SessionId, loaded.SessionId);
        Assert.Equal(Sample.Characters, loaded.Characters);
    }

    [Fact]
    public void StoresNothingReadable()
    {
        using var folder = new Folder();
        var path = folder.At("vault.bin");

        new Vault(path).Save([Sample]);
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(path));

        Assert.DoesNotContain("session-secret", text);
        Assert.DoesNotContain("Player", text);
    }

    [Fact]
    public void SavingReplacesAndLeavesNoDraft()
    {
        using var folder = new Folder();
        var vault = new Vault(folder.At("vault.bin"));

        vault.Save([Sample]);
        vault.Save([]);

        Assert.Empty(vault.Load());
        Assert.Equal(["vault.bin"], Directory.GetFiles(folder.Root).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DamagedVaultIsReportedAndCanBeSetAside(bool truncate)
    {
        using var folder = new Folder();
        var path = folder.At("vault.bin");
        var vault = new Vault(path);
        vault.Save([Sample]);

        var data = File.ReadAllBytes(path);
        data[^1] ^= 0xFF;
        File.WriteAllBytes(path, truncate ? [] : data);

        Assert.Throws<InvalidDataException>(() => vault.Load());

        var aside = vault.SetAside();
        Assert.True(File.Exists(aside));
        Assert.Empty(vault.Load());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("""[{"Id":"user-1"}]""")]
    [InlineData("""[{"Id":"u","Name":"n","SessionId":"s","Characters":[null]}]""")]
    [InlineData("""[{"Id":"u","Name":"n","SessionId":"s","Characters":[{"accountId":"c1"}]}]""")]
    public void HalfFilledAccountsCountAsDamage(string json)
    {
        using var folder = new Folder();
        var path = folder.At("vault.bin");
        Seal(path, json);

        Assert.Throws<InvalidDataException>(() => new Vault(path).Load());
    }

    [Fact]
    public void KeepsEachCharactersChosenClient()
    {
        using var folder = new Folder();
        var vault = new Vault(folder.At("vault.bin"));

        vault.Save([Sample with { Choices = new Dictionary<string, string> { ["c1"] = Program.RuneScape } }]);

        Assert.Equal(Program.RuneScape, Assert.Single(vault.Load()).Choices["c1"]);
    }

    [Theory]
    [InlineData("""[{"Id":"u","Name":"n","SessionId":"s","Characters":[]}]""")]
    [InlineData("""[{"Id":"u","Name":"n","SessionId":"s","Characters":[],"Choices":null}]""")]
    [InlineData("""[{"Id":"u","Name":"n","SessionId":"s","Characters":[],"Choices":{"c1":null}}]""")]
    public void VaultWithoutClientChoicesLoadsWithNone(string json)
    {
        using var folder = new Folder();
        var path = folder.At("vault.bin");
        Seal(path, json);

        Assert.Empty(Assert.Single(new Vault(path).Load()).Choices);
    }

    [Fact]
    public void KeepsLauncherCredentialsEncrypted()
    {
        using var folder = new Folder();
        var path = folder.At("vault.bin");
        var credentials = new Credentials("access", "refresh-secret", Token.With(new { sub = "user-1", iat = 100 }));

        new Vault(path).Save([Sample with { Credentials = credentials }]);

        Assert.Equal(credentials, Assert.Single(new Vault(path).Load()).Credentials);
        Assert.DoesNotContain("refresh-secret", Encoding.UTF8.GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void StoresOnlyTheThreeTokens()
    {
        using var folder = new Folder();
        var path = folder.At("vault.bin");
        new Vault(path).Save([Sample with { Credentials = new Credentials("a", "r", Token.With(new { sub = "user-1", iat = 1 })) }]);

        var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), Encoding.UTF8.GetBytes("JagexManager.Vault.v1"), DataProtectionScope.CurrentUser));
        using var document = System.Text.Json.JsonDocument.Parse(json);

        Assert.Equal(["AccessToken", "RefreshToken", "IdToken"], document.RootElement[0].GetProperty("Credentials").EnumerateObject().Select(p => p.Name));
    }

    [Theory]
    [InlineData("""[{"Id":"u","Name":"n","SessionId":"s","Characters":[]}]""")]
    [InlineData("""[{"Id":"u","Name":"n","SessionId":"s","Characters":[],"Credentials":null}]""")]
    [InlineData("""[{"Id":"u","Name":"n","SessionId":"s","Characters":[],"Credentials":{"AccessToken":"a","IdToken":"FULL"}}]""")]
    [InlineData("""[{"Id":"u","Name":"n","SessionId":"s","Characters":[],"Credentials":{"AccessToken":"a","RefreshToken":"","IdToken":"FULL"}}]""")]
    [InlineData("""[{"Id":"u","Name":"n","SessionId":"s","Characters":[],"Credentials":{"AccessToken":"a","RefreshToken":"r"}}]""")]
    [InlineData("""[{"Id":"u","Name":"n","SessionId":"s","Characters":[],"Credentials":{"AccessToken":"a","RefreshToken":"r","IdToken":"NOIAT"}}]""")]
    [InlineData("""[{"Id":"u","Name":"n","SessionId":"s","Characters":[],"Credentials":{"AccessToken":"a","RefreshToken":"r","IdToken":"not a token"}}]""")]
    public void IncompleteCredentialsLoadAsNone(string json)
    {
        using var folder = new Folder();
        var path = folder.At("vault.bin");
        Seal(path, json.Replace("FULL", Token.With(new { sub = "u", iat = 1 })).Replace("NOIAT", Token.With(new { sub = "u" })));

        Assert.Null(Assert.Single(new Vault(path).Load()).Credentials);
    }

    [Fact]
    public void LockedVaultIsNotMistakenForDamage()
    {
        using var folder = new Folder();
        var path = folder.At("vault.bin");
        var vault = new Vault(path);
        vault.Save([Sample]);

        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<IOException>(() => vault.Load());

        Assert.Single(vault.Load());
    }
}
