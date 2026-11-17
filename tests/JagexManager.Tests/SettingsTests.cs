namespace JagexManager.Tests;

public class SettingsTests
{
    private const string Hdos = @"C:\Games\HDOS\hdos.exe";

    private static readonly string RuneLitePath = Program.Known.Single(p => p.Name == Program.RuneLite).Path;

    private static Settings Load(string content)
    {
        using var folder = new Folder();
        var path = folder.At("settings.json");
        File.WriteAllText(path, content);
        return Settings.Load(path);
    }

    private static Settings With(params string[] paths) =>
        paths.Aggregate(new Settings(), (settings, path) => settings.Add(path).Settings);

    [Fact]
    public void StartsWithRuneLiteItsColoursAndTheGameFont()
    {
        var settings = new Settings();

        Assert.Equal(Program.RuneLite, settings.Default);
        Assert.Empty(settings.Custom);
        Assert.Empty(settings.Options);
        Assert.Equal(Theme.RuneLite, settings.Theme);
        Assert.True(settings.UseGameFont);
    }

    [Fact]
    public void MissingFileOrEmptyObjectGivesDefaults()
    {
        using var folder = new Folder();

        Assert.Equal(new Settings(), Settings.Load(folder.At("settings.json")));
        Assert.Equal(new Settings(), Load("{}"));
    }

    [Fact]
    public void SavesReadableNamesAndLoadsThemBack()
    {
        using var folder = new Folder();
        var path = folder.At(@"nested\settings.json");
        var settings = (With(Hdos, @"C:\Games\Other\client.exe") with
        {
            Default = "hdos",
            Theme = Theme.Dark,
            UseGameFont = false,
        }).WithOptions(Program.RuneLite, "--profile=Main");

        settings.Save(path);

        var text = File.ReadAllText(path);
        Assert.Contains("\"Theme\": \"Dark\"", text);
        Assert.DoesNotContain("Installed", text);
        Assert.DoesNotContain("Status", text);
        Assert.DoesNotContain("Arguments", text);
        Assert.Equal(settings, Settings.Load(path));
    }

    [Fact]
    public void AddedClientsAreOfferedAfterTheKnownOnes()
    {
        var settings = With(Hdos, @"C:\Games\Other\client.exe");

        Assert.Equal(
            [Program.RuneLite, Program.OldSchool, Program.RuneScape, "hdos", "client"],
            settings.Programs.Select(p => p.Name));
        Assert.Equal(Hdos, settings.Find("hdos").Path);
        Assert.True(settings.Removable("hdos"));
        Assert.False(settings.Removable(Program.RuneLite));
    }

    [Fact]
    public void AddingNamesAClientAfterItsProgram()
    {
        var (settings, name) = new Settings().Add(Hdos);

        Assert.Equal("hdos", name);
        Assert.Equal(Program.RuneLite, settings.Default);
        Assert.Equal(new Program("hdos", Hdos), Assert.Single(settings.Custom));
    }

    [Fact]
    public void AddingAKnownProgramKeepsItsName()
    {
        var (settings, name) = new Settings().Add(RuneLitePath.ToUpperInvariant());

        Assert.Equal(Program.RuneLite, name);
        Assert.Equal(new Settings(), settings);
    }

    [Theory]
    [InlineData(@"C:\Games\.exe")]
    [InlineData(@"C:\Games\ .exe")]
    public void AProgramWithoutANameIsCalledClient(string path) =>
        Assert.Equal("Client", new Settings().Add(path).Name);

    [Fact]
    public void AddingTheSameProgramTwiceKeepsOne()
    {
        var once = With(Hdos);

        var (twice, name) = once.Add(Hdos.ToUpperInvariant());

        Assert.Equal(once, twice);
        Assert.Equal("hdos", name);
    }

    [Theory]
    [InlineData(@"C:\Elsewhere\hdos.exe", "hdos 2")]
    [InlineData(@"C:\Elsewhere\RuneLite.exe", "RuneLite 2")]
    [InlineData(@"C:\Elsewhere\runescape 3.exe", "runescape 3 2")]
    public void ATakenNameGetsANumber(string path, string name) =>
        Assert.Equal(name, With(Hdos).Add(path).Name);

    [Fact]
    public void RemovingAClientForgetsItsOptionsAndFallsBackToRuneLite()
    {
        var settings = (With(Hdos, @"C:\Games\Other\client.exe") with { Default = "hdos" })
            .WithOptions("hdos", "--fast")
            .WithOptions("client", "--kept");

        var removed = settings.Remove("hdos");

        Assert.Equal(["client"], removed.Custom.Select(p => p.Name));
        Assert.Equal(Program.RuneLite, removed.Default);
        Assert.Equal("--kept", Assert.Single(removed.Options).Value);
    }

    [Fact]
    public void RemovingLeavesAnotherDefaultAndKnownClientsAlone()
    {
        var settings = With(Hdos) with { Default = Program.OldSchool };

        Assert.Equal(Program.OldSchool, settings.Remove("hdos").Default);
        Assert.Equal(settings, settings.Remove(Program.RuneLite));
        Assert.Equal(settings, settings.Remove("No such client"));
    }

    [Fact]
    public void LaunchOptionsAreAddedToAClientsOwnArguments()
    {
        var settings = With(Hdos)
            .WithOptions(Program.RuneLite, "--profile=Main")
            .WithOptions(Program.RuneScape, "--extra")
            .WithOptions("hdos", "--fast");

        Assert.Equal("--profile=Main", settings.Find(Program.RuneLite).Arguments);
        Assert.Equal("--configURI https://www.runescape.com/k=5/l=0/jav_config.ws --extra", settings.Find(Program.RuneScape).Arguments);
        Assert.Equal("--fast", settings.Find("hdos").Arguments);
        Assert.Equal("", settings.Find(Program.OldSchool).Arguments);
    }

    [Fact]
    public void ClearingLaunchOptionsForgetsThem()
    {
        var settings = new Settings().WithOptions(Program.RuneLite, "--profile=Main").WithOptions(Program.RuneLite, "");

        Assert.Equal(new Settings(), settings);
    }

    [Fact]
    public void SwitchingClientKeepsWhatWasTypedForTheOneLeft()
    {
        var settings = new Settings().WithOptions(Program.RuneScape, "--kept");

        var switched = settings.Switch(Program.RuneScape, "--profile=Main");

        Assert.Equal(Program.RuneScape, switched.Default);
        Assert.Equal("--profile=Main", switched.Options[Program.RuneLite]);
        Assert.Equal("--kept", switched.Options[Program.RuneScape]);
        Assert.Equal(settings, settings.Switch(Program.RuneLite, ""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("No such client")]
    public void UnknownClientNameMeansTheDefault(string? name)
    {
        var settings = new Settings { Default = Program.OldSchool };

        Assert.Equal(Program.OldSchool, settings.Find(name).Name);
    }

    [Fact]
    public void ADefaultThatNamesNoClientFallsBackToRuneLite()
    {
        Assert.Equal(Program.RuneLite, new Settings { Default = "No such client" }.Find(null).Name);
        Assert.Equal(Program.RuneLite, Load("""{"Default":"runelite"}""").Default);
    }

    [Fact]
    public void SettingsAreEqualWhenTheirContentsAre()
    {
        var one = With(Hdos).WithOptions(Program.RuneLite, "--profile=Main");
        var same = With(Hdos).WithOptions(Program.RuneLite, "--profile=Main");

        Assert.Equal(one, same);
        Assert.Equal(one.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(one, one.WithOptions(Program.RuneLite, "--profile=Alt"));
        Assert.NotEqual(one, one.WithOptions(Program.RuneScape, "--x"));
        Assert.NotEqual(one, one.Remove("hdos"));
    }

    [Fact]
    public void OlderSettingsWithRuneLiteStayOnRuneLite()
    {
        var path = RuneLitePath.ToUpperInvariant().Replace(@"\", @"\\");

        var settings = Load($$"""{"ClientPath":"{{path}}","Arguments":"--profile=Main","Theme":"Dark"}""");

        Assert.Equal(new Settings { Theme = Theme.Dark }.WithOptions(Program.RuneLite, "--profile=Main"), settings);
    }

    [Fact]
    public void OlderSettingsWithAnotherProgramBecomeAnAddedClient()
    {
        var settings = Load("""{"ClientPath":"C:\\Games\\HDOS\\hdos.exe","Arguments":"--fast"}""");

        Assert.Equal("hdos", settings.Default);
        Assert.Equal(Hdos, settings.Find(null).Path);
        Assert.Equal("--fast", settings.Find(null).Arguments);
    }

    [Fact]
    public void OlderSettingsWithAnOfficialClientUseItByName()
    {
        var path = Program.Known.Single(p => p.Name == Program.OldSchool).Path.Replace(@"\", @"\\");

        var settings = Load($$"""{"ClientPath":"{{path}}","Arguments":"--x"}""");

        Assert.Equal(new Settings { Default = Program.OldSchool }.WithOptions(Program.OldSchool, "--x"), settings);
    }

    [Fact]
    public void OptionsForAClientThatIsNotThereAreDropped()
    {
        var settings = Load("""{"Options":{"ghost":"--later","RuneLite":" --profile=Main "}}""");

        Assert.Equal(new Settings().WithOptions(Program.RuneLite, "--profile=Main"), settings);
    }

    [Fact]
    public void OlderSettingsWithoutAPathKeepTheirArgumentsForRuneLite()
    {
        var settings = Load("""{"ClientPath":"  ","Arguments":" --profile=Main "}""");

        Assert.Equal(new Settings().WithOptions(Program.RuneLite, "--profile=Main"), settings);
    }

    [Fact]
    public void OldMembersAreIgnoredOnceNewOnesAreWritten()
    {
        var settings = Load("""{"Default":"Old School RuneScape","ClientPath":"C:\\Games\\other.exe","Arguments":"--old","Options":{"RuneLite":"--keep"}}""");

        Assert.Equal(new Settings { Default = Program.OldSchool }.WithOptions(Program.RuneLite, "--keep"), settings);
    }

    [Theory]
    [InlineData("""{"Custom":[{"Name":"hdos","Path":"C:\\Games\\HDOS\\hdos.exe"}]}""")]
    [InlineData("""{"Custom":[{"Name":"hdos","Path":"C:\\Games\\HDOS\\hdos.exe","Arguments":"ignored"}],"Theme":"Sepia","UseGameFont":null,"Options":7}""")]
    [InlineData("""{"Custom":[{"Name":"hdos","Path":"C:\\Games\\HDOS\\hdos.exe"}],"Theme":"3"}""")]
    [InlineData("""{"Custom":[{"Name":"hdos","Path":"C:\\Games\\HDOS\\hdos.exe"}],"Theme":"Light, Dark"}""")]
    [InlineData("""{"Custom":[{"Name":"hdos","Path":"C:\\Games\\HDOS\\hdos.exe"}],"UseGameFont":"yes","Default":"No such client"}""")]
    [InlineData("""{"Custom":[{"Name":"hdos","Path":"C:\\Games\\HDOS\\hdos.exe"}],"Default":"\uD800","Options":{"RuneLite":7,"hdos":"","RuneScape 3":"\uD800"}}""")]
    public void OneUnreadableMemberDoesNotCostTheOthers(string content) =>
        Assert.Equal(With(Hdos), Load(content));

    [Theory]
    [InlineData("7")]
    [InlineData("null")]
    [InlineData("""{"Name":"hdos"}""")]
    [InlineData("""{"Path":"C:\\x.exe"}""")]
    [InlineData("""{"Name":" ","Path":"C:\\x.exe"}""")]
    [InlineData("""{"Name":"runelite ","Path":"C:\\x.exe"}""")]
    [InlineData("""{"Name":"HDOS","Path":"C:\\twice.exe"}""")]
    public void AnAddedClientWithoutItsOwnNameAndPathIsSkipped(string entry) =>
        Assert.Equal(With(Hdos), Load($$"""{"Custom":[{"Name":"hdos","Path":"C:\\Games\\HDOS\\hdos.exe"},{{entry}}]}"""));

    [Fact]
    public void AddingReadsAPathHoweverItIsWritten()
    {
        var (settings, name) = new Settings().Add($" {RuneLitePath.Replace('\\', '/')} ");

        Assert.Equal(Program.RuneLite, name);
        Assert.Equal(new Settings(), settings);
    }

    [Fact]
    public void NamesListEveryOfferedClient() =>
        Assert.Equal([Program.RuneLite, Program.OldSchool, Program.RuneScape, "hdos"], With(Hdos).Names);

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{"Custom":"hdos"}""")]
    public void UnusableFileGivesDefaults(string content) =>
        Assert.Equal(new Settings(), Load(content));
}
