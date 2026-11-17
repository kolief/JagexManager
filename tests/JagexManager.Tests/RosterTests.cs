using System.Net;

namespace JagexManager.Tests;

public sealed class RosterTests : IDisposable
{
    private static readonly Character Main = new("c1", "Main");
    private static readonly Character Alt = new("c2", "Alt");
    private static readonly Account Player = new("user-1", "Player", "session-1", [Main, Alt]);
    private static readonly Account Other = new("user-2", "Other", "session-2", [new("c3", "Third")]);

    private readonly Folder folder = new();
    private readonly Web web = new();
    private readonly byte[] key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
    private readonly Vault vault;
    private readonly Roster roster;
    private readonly Launcher launcher;

    public RosterTests()
    {
        vault = new Vault(folder.At("vault.bin"));
        roster = new Roster(vault, web.Jagex());
        launcher = new Launcher(folder.At("launcher"), key);
    }

    public void Dispose()
    {
        foreach (var character in roster.Accounts.SelectMany(a => a.Characters))
            character.Stop();

        folder.Dispose();
    }

    private static string Json(params Character[] characters) =>
        System.Text.Json.JsonSerializer.Serialize(characters.Select(c => new { accountId = c.Id, displayName = c.Name }));

    private string Idle() => folder.Script("@ping -n 120 127.0.0.1 > nul");

    private static Credentials Signed(string subject, long issued, string refresh = "refresh") =>
        new("access", refresh, Token.With(new { sub = subject, iat = issued }));

    private AccountView Add(Account account)
    {
        roster.Add(account);
        return roster.Accounts.Single(a => a.Account.Id == account.Id);
    }

    [Fact]
    public void HandingOverPassesTheTokensOn()
    {
        var tokens = Signed("user-1", 100);
        var account = Add(Player with { Credentials = tokens });

        roster.HandOver(account, launcher, replace: false);

        Assert.Equal(tokens, launcher.Read());
        Assert.Null(account.Account.Credentials);
        Assert.Null(Assert.Single(vault.Load()).Credentials);
    }

    [Fact]
    public void HandingOverWithoutTokensWritesNothing()
    {
        var account = Add(Player);

        Assert.Throws<PlayException>(() => roster.HandOver(account, launcher, replace: false));
        Assert.Null(launcher.Read());
    }

    [Fact]
    public void AFailedHandOverKeepsTheTokens()
    {
        var tokens = Signed("user-1", 100);
        File.WriteAllText(folder.At("blocked"), "a file where the folder should be");
        var account = Add(Player with { Credentials = tokens });

        Assert.ThrowsAny<IOException>(() => roster.HandOver(account, new Launcher(folder.At("blocked"), key), replace: false));
        Assert.Equal(tokens, account.Account.Credentials);
        Assert.Equal(tokens, Assert.Single(vault.Load()).Credentials);
    }

    [Fact]
    public void TheLaunchersTokensGoBackToTheirAccountFirst()
    {
        var rotated = Signed("user-1", 200, "rotated");
        var player = Add(Player);
        var other = Add(Other with { Credentials = Signed("user-2", 100) });
        launcher.Write(rotated, "session-1");

        roster.HandOver(other, launcher, replace: false);

        Assert.Equal(rotated, player.Account.Credentials);
        Assert.Equal("user-2", launcher.Read()!.Subject);
    }

    [Fact]
    public void AFreshSignInIsNotReplacedByOlderLauncherTokens()
    {
        var fresh = Signed("user-1", 300, "fresh");
        var account = Add(Player with { Credentials = fresh });
        launcher.Write(Signed("user-1", 100, "revoked"), "session-1");

        roster.HandOver(account, launcher, replace: false);

        Assert.Equal(fresh, launcher.Read());
    }

    [Fact]
    public void AnUnknownLauncherAccountIsOnlyReplacedWhenAllowed()
    {
        var stranger = Signed("user-9", 100);
        var account = Add(Player with { Credentials = Signed("user-1", 100) });
        launcher.Write(stranger, "session-9");

        Assert.Throws<PlayException>(() => roster.HandOver(account, launcher, replace: false));
        Assert.Equal(stranger, launcher.Read());

        roster.HandOver(account, launcher, replace: true);
        Assert.Equal("user-1", launcher.Read()!.Subject);
    }

    [Fact]
    public void OlderTokensGiveWayToTheLaunchersNewerOnes()
    {
        var renewed = Signed("user-1", 200, "renewed");
        var player = Add(Player with { Credentials = Signed("user-1", 100, "old") });
        var other = Add(Other with { Credentials = Signed("user-2", 100) });
        launcher.Write(renewed, "session-1");

        roster.HandOver(other, launcher, replace: false);

        Assert.Equal(renewed, player.Account.Credentials);
    }

    [Fact]
    public void ATieKeepsTheLaunchersCopy()
    {
        var player = Add(Player with { Credentials = Signed("user-1", 100, "used") });
        var other = Add(Other with { Credentials = Signed("user-2", 100) });
        launcher.Write(Signed("user-1", 100, "rotated"), "session-1");

        roster.HandOver(other, launcher, replace: false);

        Assert.Equal("rotated", player.Account.Credentials!.RefreshToken);
    }

    [Fact]
    public void AFailedHandOverLeavesTheLaunchersTokensWithTheLauncher()
    {
        var held = Signed("user-1", 100);
        var player = Add(Player);
        var mine = Signed("user-2", 100);
        var other = Add(Other with { Credentials = mine });
        launcher.Write(held, "session-1");
        var cache = folder.At(@"launcher\cache.bin");
        File.WriteAllText(cache, "characters");
        File.SetAttributes(cache, FileAttributes.ReadOnly);

        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => roster.HandOver(other, launcher, replace: false));
            Assert.Throws<UnauthorizedAccessException>(() => roster.HandOver(player, launcher, replace: false));
        }
        finally
        {
            File.SetAttributes(cache, FileAttributes.Normal);
        }

        Assert.Equal(held, launcher.Read());
        Assert.Null(player.Account.Credentials);
        Assert.Equal(mine, other.Account.Credentials);
    }

    [Fact]
    public void AFailedSignOutLeavesTheLaunchersTokensWithTheLauncher()
    {
        var held = Signed("user-1", 100);
        var account = Add(Player);
        launcher.Write(held, "session-1");
        var creds = folder.At(@"launcher\auth\creds");
        File.SetAttributes(creds, FileAttributes.ReadOnly);

        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => roster.SignOut(launcher));
        }
        finally
        {
            File.SetAttributes(creds, FileAttributes.Normal);
        }

        Assert.Equal(held, launcher.Read());
        Assert.Null(account.Account.Credentials);
    }

    [Fact]
    public void HandingOverARemovedAccountWritesNothing()
    {
        var account = Add(Player with { Credentials = Signed("user-1", 100) });
        roster.Remove(account);

        Assert.Throws<PlayException>(() => roster.HandOver(account, launcher, replace: false));
        Assert.Null(launcher.Read());
    }

    [Fact]
    public void AnAccountWithTokensIsReady()
    {
        var account = Add(Player with { Credentials = Signed("user-1", 100) });

        Assert.Equal(Handover.Ready, roster.Check(account, null));
        Assert.Equal(Handover.Ready, roster.Check(account, Signed("user-1", 50)));
    }

    [Fact]
    public void ALauncherHoldingAnUnknownAccountHoldsAStranger()
    {
        var account = Add(Player with { Credentials = Signed("user-1", 100) });

        Assert.Equal(Handover.Stranger, roster.Check(account, Signed("user-9", 100)));
    }

    [Fact]
    public void AnAccountWithoutTokensCanOnlyReopenWhatTheLauncherHolds()
    {
        var account = Add(Player);
        Add(Other);

        Assert.Equal(Handover.Ready, roster.Check(account, Signed("user-1", 100)));
        Assert.Equal(Handover.SignIn, roster.Check(account, null));
        Assert.Equal(Handover.SignIn, roster.Check(account, Signed("user-2", 100)));
        Assert.Equal(Handover.SignIn, roster.Check(account, Signed("user-9", 100)));
    }

    [Fact]
    public void SigningTheLauncherOutKeepsItsTokens()
    {
        var held = Signed("user-1", 100);
        var account = Add(Player);
        launcher.Write(held, "session-1");

        roster.SignOut(launcher);

        Assert.Null(launcher.Read());
        Assert.Equal(held, account.Account.Credentials);
    }

    [Fact]
    public void SigningTheLauncherOutOfAnUnknownAccountClearsIt()
    {
        Add(Player);
        launcher.Write(Signed("user-9", 100), "session-9");

        roster.SignOut(launcher);

        Assert.Null(launcher.Read());
        Assert.Null(Assert.Single(vault.Load()).Credentials);
    }

    [Fact]
    public void AnUnfamiliarLauncherFileIsNotSignedOut()
    {
        var creds = folder.At(@"launcher\auth\creds");
        Directory.CreateDirectory(Path.GetDirectoryName(creds)!);
        File.WriteAllText(creds, "not the launcher's format");

        Assert.Throws<InvalidDataException>(() => roster.SignOut(launcher));
        Assert.True(File.Exists(creds));
    }

    [Fact]
    public void LoadsWhatWasSaved()
    {
        roster.Add(Player);

        var reopened = new Roster(vault, web.Jagex());
        reopened.Load();

        var account = Assert.Single(reopened.Accounts);
        Assert.Equal("Player", account.Name);
        Assert.Equal(["Main", "Alt"], account.Characters.Select(c => c.Name));
    }

    [Fact]
    public void SigningInAgainRenewsTheStoredAccount()
    {
        roster.Add(Player);
        var account = roster.Accounts[0];
        account.Expired = true;

        roster.Add(Player with { SessionId = "session-2" });

        Assert.Same(account, Assert.Single(roster.Accounts));
        Assert.False(account.Expired);
        Assert.Equal("session-2", Assert.Single(vault.Load()).SessionId);
    }

    [Fact]
    public void ChosenClientIsRememberedPerCharacter()
    {
        roster.Add(Player);
        var main = roster.Accounts[0].Characters[0];
        var alt = roster.Accounts[0].Characters[1];

        roster.Choose(main, Program.RuneScape);

        Assert.Equal(Program.RuneScape, main.Choice);
        Assert.Null(alt.Choice);
        Assert.Equal(Program.RuneScape, Assert.Single(vault.Load()).Choices["c1"]);

        roster.Choose(main, null);

        Assert.Null(main.Choice);
        Assert.Empty(Assert.Single(vault.Load()).Choices);
    }

    [Fact]
    public async Task ChosenClientSurvivesRefreshAndSigningInAgain()
    {
        roster.Add(Player);
        roster.Choose(roster.Accounts[0].Characters[0], Program.OldSchool);

        web.Reply(Web.AccountsPath, Json(Alt, Main));
        await roster.RefreshAsync(roster.Accounts[0]);
        roster.Add(Player with { SessionId = "session-2" });

        Assert.Equal(Program.OldSchool, roster.Accounts[0].Characters.Single(c => c.Name == "Main").Choice);

        var stored = Assert.Single(vault.Load());
        Assert.Equal("session-2", stored.SessionId);
        Assert.Equal(Program.OldSchool, stored.Choices["c1"]);
    }

    [Fact]
    public async Task ChoicesOfCharactersThatLeftAreDropped()
    {
        roster.Add(Player);
        var main = roster.Accounts[0].Characters[0];
        var alt = roster.Accounts[0].Characters[1];
        roster.Choose(main, Program.RuneScape);

        web.Reply(Web.AccountsPath, Json(Alt));
        await roster.RefreshAsync(roster.Accounts[0]);
        Assert.Empty(Assert.Single(vault.Load()).Choices);

        roster.Choose(main, Program.OldSchool);
        Assert.Empty(Assert.Single(vault.Load()).Choices);

        roster.Choose(alt, Program.OldSchool);
        Assert.Equal(["c2"], Assert.Single(vault.Load()).Choices.Keys);
    }

    [Fact]
    public async Task PlayRefusesAClientThatIsNotInstalled()
    {
        roster.Add(Player);
        var character = roster.Accounts[0].Characters[0];

        var error = await Assert.ThrowsAsync<PlayException>(() => roster.PlayAsync(character, new Program(Program.RuneScape, folder.At("missing.exe"))));

        Assert.Contains(Program.RuneScape, error.Message);
        Assert.Empty(web.Sent);
        Assert.False(character.Running);
    }

    [Fact]
    public void ChoicesOfClientsNoLongerOfferedAreDropped()
    {
        roster.Add(Player);
        roster.Add(Player with { Id = "user-2", Name = "Second", Characters = [new Character("c9", "Other")] });
        roster.Choose(roster.Accounts[0].Characters[0], "hdos");
        roster.Choose(roster.Accounts[0].Characters[1], Program.OldSchool);
        roster.Choose(roster.Accounts[1].Characters[0], "hdos");

        roster.Prune([Program.RuneLite, Program.OldSchool]);

        Assert.Null(roster.Accounts[0].Characters[0].Choice);
        Assert.Equal(Program.OldSchool, roster.Accounts[0].Characters[1].Choice);
        Assert.Null(roster.Accounts[1].Characters[0].Choice);
        Assert.Equal([Program.OldSchool], vault.Load().SelectMany(a => a.Choices.Values));
    }

    [Fact]
    public void KeepingEveryChoiceWritesNothing()
    {
        roster.Add(Player);
        roster.Choose(roster.Accounts[0].Characters[0], Program.OldSchool);
        var written = File.GetLastWriteTimeUtc(folder.At("vault.bin"));
        File.SetLastWriteTimeUtc(folder.At("vault.bin"), written.AddDays(-1));

        roster.Prune([Program.RuneLite, Program.OldSchool]);

        Assert.Equal(written.AddDays(-1), File.GetLastWriteTimeUtc(folder.At("vault.bin")));
        Assert.Equal(Program.OldSchool, roster.Accounts[0].Characters[0].Choice);
    }

    [Fact]
    public void ChoosingAClientAnnouncesIt()
    {
        roster.Add(Player);
        var main = roster.Accounts[0].Characters[0];
        var changed = new List<string?>();
        main.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        roster.Choose(main, Program.RuneScape);

        Assert.Contains("Choice", changed);
    }

    [Fact]
    public void RemovingForgetsTheAccount()
    {
        roster.Add(Player);
        roster.Add(Player with { Id = "user-2", Name = "Other" });

        roster.Remove(roster.Accounts[0]);

        Assert.Equal("Other", Assert.Single(roster.Accounts).Name);
        Assert.Equal("user-2", Assert.Single(vault.Load()).Id);
    }

    [Fact]
    public void MovedAccountsKeepTheirOrderOnceSaved()
    {
        roster.Add(Player);
        roster.Add(Player with { Id = "user-2", Name = "Second" });
        roster.Add(Player with { Id = "user-3", Name = "Third" });

        Assert.True(roster.Move(roster.Accounts[0], 2));
        Assert.Equal(["Second", "Third", "Player"], roster.Accounts.Select(a => a.Name));
        Assert.Equal(["user-1", "user-2", "user-3"], vault.Load().Select(a => a.Id));

        roster.Save();
        Assert.Equal(["user-2", "user-3", "user-1"], vault.Load().Select(a => a.Id));
    }

    [Theory]
    [InlineData(-5, "Second,Player,Third")]
    [InlineData(99, "Player,Third,Second")]
    public void MovingPastAnEndStopsAtThatEnd(int position, string order)
    {
        roster.Add(Player);
        roster.Add(Player with { Id = "user-2", Name = "Second" });
        roster.Add(Player with { Id = "user-3", Name = "Third" });

        Assert.True(roster.Move(roster.Accounts[1], position));

        Assert.Equal(order.Split(','), roster.Accounts.Select(a => a.Name));
    }

    [Fact]
    public void MovingToWhereItAlreadyIsReportsNoMove()
    {
        roster.Add(Player);
        roster.Add(Player with { Id = "user-2", Name = "Second" });

        Assert.False(roster.Move(roster.Accounts[0], 0));
        Assert.False(roster.Move(roster.Accounts[0], -5));
        Assert.Equal(["Player", "Second"], roster.Accounts.Select(a => a.Name));
    }

    [Fact]
    public void MovingAnAccountThatIsNotListedDoesNothing()
    {
        var stranger = new AccountView(Player with { Id = "user-9", Name = "Stranger" });

        Assert.False(roster.Move(stranger, 0));
        Assert.Empty(roster.Accounts);

        roster.Add(Player);
        Assert.False(roster.Move(stranger, 0));
        Assert.Equal("Player", Assert.Single(roster.Accounts).Name);
    }

    [Fact]
    public async Task RefreshPicksUpChangedCharacters()
    {
        roster.Add(Player);
        web.Reply(Web.AccountsPath, Json(Alt, Main with { Name = "Renamed" }, new Character("c3", "New")));

        await roster.RefreshAsync(roster.Accounts[0]);

        Assert.Equal(["Alt", "Renamed", "New"], roster.Accounts[0].Characters.Select(c => c.Name));
        Assert.Equal(["c2", "c1", "c3"], Assert.Single(vault.Load()).Characters.Select(c => c.Id));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public async Task OnlyARejectedSessionExpiresTheAccount(HttpStatusCode status, bool expired)
    {
        roster.Add(Player);
        web.Reply(Web.AccountsPath, "", status);

        await roster.RefreshAsync(roster.Accounts[0]);

        Assert.Equal(expired, roster.Accounts[0].Expired);
        Assert.Equal(2, roster.Accounts[0].Characters.Count);
    }

    [Fact]
    public async Task RefreshClearsExpiryOnceTheSessionWorks()
    {
        roster.Add(Player);
        roster.Accounts[0].Expired = true;
        web.Reply(Web.AccountsPath, Json(Main, Alt));

        await roster.RefreshAsync(roster.Accounts[0]);

        Assert.False(roster.Accounts[0].Expired);
    }

    [Fact]
    public async Task AnswerAboutAReplacedSessionIsIgnored()
    {
        roster.Add(Player);
        web.Reply(Web.AccountsPath, "", HttpStatusCode.Unauthorized);
        web.Gate = new TaskCompletionSource();

        var stale = roster.RefreshAsync(roster.Accounts[0]);
        roster.Add(Player with { SessionId = "session-2" });
        web.Gate.SetResult();
        await stale;

        Assert.False(roster.Accounts[0].Expired);
    }

    [Fact]
    public async Task PlayStartsTheClientOnce()
    {
        roster.Add(Player);
        web.Reply(Web.AccountsPath, Json(Main, Alt));
        var character = roster.Accounts[0].Characters[0];

        await roster.PlayAsync(character, new Program("Test", Idle()));
        await roster.PlayAsync(character, new Program("Test", folder.At("missing.exe")));

        Assert.True(character.Running);

        character.Stop();
        Assert.True(await Wait.ForAsync(() =>
        {
            roster.DropClosed();
            return !character.Running;
        }));
    }

    [Fact]
    public async Task PlayPassesArgumentsToTheClient()
    {
        roster.Add(Player);
        web.Reply(Web.AccountsPath, Json(Main, Alt));
        var script = folder.Script("""
            @echo %*> "%~dp0out.txt"
            """);

        await roster.PlayAsync(roster.Accounts[0].Characters[0], new Program("Test", script, "--profile=Main"));

        var character = roster.Accounts[0].Characters[0];
        Assert.True(await Wait.ForAsync(() =>
        {
            roster.DropClosed();
            return !character.Running;
        }));
        Assert.Equal("--profile=Main", File.ReadAllText(folder.At("out.txt")).Trim());
    }

    [Fact]
    public async Task PlayWorksOffline()
    {
        roster.Add(Player);
        web.Reply(Web.AccountsPath, "", HttpStatusCode.BadGateway);
        var character = roster.Accounts[0].Characters[0];

        await roster.PlayAsync(character, new Program("Test", Idle()));

        Assert.True(character.Running);
        character.Stop();
    }

    [Fact]
    public async Task PlayRefusesAnExpiredSession()
    {
        roster.Add(Player);
        web.Reply(Web.AccountsPath, "", HttpStatusCode.Unauthorized);
        var character = roster.Accounts[0].Characters[0];

        var error = await Assert.ThrowsAsync<PlayException>(() => roster.PlayAsync(character, new Program("Test", Idle())));

        Assert.Contains("Sign in again", error.Message);
        Assert.DoesNotContain("session-1", error.Message);
        Assert.False(character.Running);
    }

    [Fact]
    public async Task PlayRefusesACharacterThatIsGone()
    {
        roster.Add(Player);
        web.Reply(Web.AccountsPath, Json(Alt));
        var character = roster.Accounts[0].Characters[0];

        await Assert.ThrowsAsync<PlayException>(() => roster.PlayAsync(character, new Program("Test", Idle())));

        Assert.False(character.Running);
    }

    [Fact]
    public async Task PlayRefusesAnAccountRemovedMeanwhile()
    {
        roster.Add(Player);
        web.Reply(Web.AccountsPath, Json(Main, Alt));
        web.Gate = new TaskCompletionSource();
        var character = roster.Accounts[0].Characters[0];

        var play = roster.PlayAsync(character, new Program("Test", Idle()));
        roster.Remove(roster.Accounts[0]);
        web.Gate.SetResult();

        await Assert.ThrowsAsync<PlayException>(() => play);
        Assert.False(character.Running);
    }

    [Fact]
    public async Task RunningClientSurvivesRefreshAndSigningInAgain()
    {
        roster.Add(Player);
        web.Reply(Web.AccountsPath, Json(Main, Alt));
        var character = roster.Accounts[0].Characters[0];
        await roster.PlayAsync(character, new Program("Test", Idle()));

        web.Reply(Web.AccountsPath, Json(Alt, Main));
        await roster.RefreshAsync(roster.Accounts[0]);
        roster.Add(Player with { SessionId = "session-2" });

        Assert.Same(character, roster.Accounts[0].Characters.Single(c => c.Name == "Main"));
        Assert.True(character.Running);
        character.Stop();
    }

    [Fact]
    public async Task ForgetStopsTrackingWithoutClosing()
    {
        roster.Add(Player);
        web.Reply(Web.AccountsPath, Json(Main, Alt));
        var character = roster.Accounts[0].Characters[0];
        var script = folder.Script("""
            @ping -n 3 127.0.0.1 > nul
            @echo finished> "%~dp0finished.txt"
            """);
        await roster.PlayAsync(character, new Program("Test", script));

        roster.Forget();

        Assert.False(character.Running);
        Assert.True(await Wait.ForAsync(() => File.Exists(folder.At("finished.txt"))));
    }
}
