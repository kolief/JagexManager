namespace JagexManager.Tests;

public class ProgramTests
{
    [Fact]
    public void KnownClientsHaveFullPaths()
    {
        Assert.Equal([Program.RuneLite, Program.OldSchool, Program.RuneScape], Program.Known.Select(p => p.Name));
        Assert.All(Program.Known, p =>
        {
            Assert.True(Path.IsPathFullyQualified(p.Path));
            Assert.EndsWith(".exe", p.Path);
        });
    }

    [Fact]
    public void ClientNamesAreStoredSoTheyMustNotChange()
    {
        Assert.Equal("RuneLite", Program.RuneLite);
        Assert.Equal("Old School RuneScape", Program.OldSchool);
        Assert.Equal("RuneScape 3", Program.RuneScape);
    }

    [Fact]
    public void OnlyRuneScape3IsToldWhereItsConfigurationIs()
    {
        Assert.All(Program.Known.Where(p => p.Name != Program.RuneScape), p => Assert.Equal("", p.Arguments));
        Assert.Equal(
            "--configURI https://www.runescape.com/k=5/l=0/jav_config.ws",
            Program.Known.Single(p => p.Name == Program.RuneScape).Arguments);
    }

    [Fact]
    public void InstalledMeansAFileAtAFullPath()
    {
        using var folder = new Folder();
        var path = folder.At("client.exe");

        Assert.False(new Program("Test", path).Installed);
        Assert.False(new Program("Test", "").Installed);

        File.WriteAllText(path, "");
        Assert.True(new Program("Test", path).Installed);
    }

    [Theory]
    [InlineData("notepad.exe")]
    [InlineData(@"C:notepad.exe")]
    [InlineData(@"\Windows\notepad.exe")]
    [InlineData(@"..\notepad.exe")]
    public void APathThatIsNotFullIsNeverInstalled(string path)
    {
        Assert.False(new Program("Test", path).Installed);
    }

    [Fact]
    public void StatusSaysWhatIsWrongBeforeThePath()
    {
        using var folder = new Folder();
        var path = folder.At("client.exe");

        Assert.Equal($"Not found: {path}", new Program("Test", path).Status);

        File.WriteAllText(path, "");
        Assert.Equal(path, new Program("Test", path).Status);
    }
}
