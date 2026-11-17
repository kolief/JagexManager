namespace JagexManager.Tests;

public class ClientTests
{
    private static readonly Character Main = new("c1", "Main Character");

    [Fact]
    public async Task PassesLoginToClient()
    {
        using var folder = new Folder();
        var script = folder.Script("""
            @echo %JX_SESSION_ID%/%JX_CHARACTER_ID%/%JX_DISPLAY_NAME%/%JX_ACCESS_TOKEN%> "%~dp0out.txt"
            """);

        Environment.SetEnvironmentVariable("JX_ACCESS_TOKEN", "inherited");
        try
        {
            using var client = Client.Start(script, "", "session-1", Main);

            Assert.True(await Wait.ForAsync(() => !client.Running));
            Assert.Equal("session-1/c1/Main Character/", File.ReadAllText(folder.At("out.txt")).Trim());
        }
        finally
        {
            Environment.SetEnvironmentVariable("JX_ACCESS_TOKEN", null);
        }
    }

    [Fact]
    public async Task StopClosesEverythingTheClientStarted()
    {
        using var folder = new Folder();
        var script = folder.Script("""
            @start "" /b ping -n 120 127.0.0.1 > nul
            @echo handed over> "%~dp0handed.txt"
            @exit
            """);

        using var client = Client.Start(script, "", "session-1", Main);

        Assert.True(await Wait.ForAsync(() => File.Exists(folder.At("handed.txt"))));
        await Task.Delay(500);
        Assert.True(client.Running, "the handed-over process should still count as running");

        client.Stop();

        Assert.True(await Wait.ForAsync(() => !client.Running));
    }

    [Fact]
    public async Task DisposeLeavesClientRunning()
    {
        using var folder = new Folder();
        var script = folder.Script("""
            @echo started> "%~dp0started.txt"
            @ping -n 3 127.0.0.1 > nul
            @echo finished> "%~dp0finished.txt"
            """);

        var client = Client.Start(script, "", "session-1", Main);
        Assert.True(await Wait.ForAsync(() => File.Exists(folder.At("started.txt"))));
        client.Dispose();

        Assert.False(client.Running);
        Assert.True(await Wait.ForAsync(() => File.Exists(folder.At("finished.txt"))));
    }

    [Fact]
    public async Task PassesArgumentsToClient()
    {
        using var folder = new Folder();
        var script = folder.Script("""
            @echo %*> "%~dp0out.txt"
            """);
        using var client = Client.Start(script, "--profile=Main --developer-mode", "session-1", Main);

        Assert.True(await Wait.ForAsync(() => !client.Running));
        Assert.Equal("--profile=Main --developer-mode", File.ReadAllText(folder.At("out.txt")).Trim());
    }

    [Fact]
    public void MissingClientIsReported()
    {
        using var folder = new Folder();

        Assert.Throws<FileNotFoundException>(() => Client.Start(folder.At("missing.exe"), "", "session-1", Main));
    }
}
