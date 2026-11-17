using System.IO.Compression;

namespace JagexManager.Tests;

public class GameFontTests
{
    private const string Entry = "net/runelite/client/ui/runescape.ttf";

    private static void Jar(Folder folder, string name, int day, string entry, byte[] font)
    {
        var path = folder.At(name);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var stream = archive.CreateEntry(entry).Open();
            stream.Write(font);
        }

        File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, day));
    }

    [Fact]
    public void CopiesTheFontFromTheNewestClient()
    {
        using var folder = new Folder();
        var target = folder.At(@"out\runescape.ttf");
        Jar(folder, "client-1.0.1.jar", day: 1, Entry, [1]);
        Jar(folder, "client-1.0.2.jar", day: 2, Entry, [2]);
        Jar(folder, "runelite-api-1.0.3.jar", day: 3, Entry, [3]);

        Assert.True(GameFont.Available(folder.Root));
        Assert.True(GameFont.Sync(wanted: true, folder.Root, target));
        Assert.Equal([2], File.ReadAllBytes(target));
    }

    [Fact]
    public void SkipsClientsWithoutAReadableFont()
    {
        using var folder = new Folder();
        var target = folder.At("runescape.ttf");
        Jar(folder, "client-1.0.1.jar", day: 1, Entry, [1]);
        Jar(folder, "client-1.0.2.jar", day: 2, "net/runelite/client/ui/other.ttf", [2]);
        File.WriteAllText(folder.At("client-1.0.3.jar"), "half downloaded");

        Assert.True(GameFont.Available(folder.Root));
        Assert.True(GameFont.Sync(wanted: true, folder.Root, target));
        Assert.Equal([1], File.ReadAllBytes(target));

        foreach (var jar in Directory.GetFiles(folder.Root, "*.jar"))
            File.Open(jar, FileMode.Open, FileAccess.Read, FileShare.None).Dispose();
    }

    [Fact]
    public void DeletesTheCopyWhenTheFontIsNotWanted()
    {
        using var folder = new Folder();
        var target = folder.At("runescape.ttf");
        Jar(folder, "client-1.0.1.jar", day: 1, Entry, [1]);
        GameFont.Sync(wanted: true, folder.Root, target);

        Assert.False(GameFont.Sync(wanted: false, folder.Root, target));
        Assert.False(File.Exists(target));
        Assert.False(GameFont.Sync(wanted: false, folder.Root, folder.At(@"missing\runescape.ttf")));
    }

    [Fact]
    public void KeepsWorkingFromItsCopyAfterRuneLiteIsGone()
    {
        using var folder = new Folder();
        var target = folder.At("runescape.ttf");
        File.WriteAllBytes(target, [9]);

        Assert.False(GameFont.Available(folder.At("missing")));
        Assert.True(GameFont.Sync(wanted: true, folder.At("missing"), target));
        Assert.Equal([9], File.ReadAllBytes(target));

        Assert.False(GameFont.Sync(wanted: false, folder.At("missing"), target));
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void ReportsAWantedFontItCannotCopyAndLeavesNothingBehind()
    {
        using var folder = new Folder();
        Jar(folder, "client-1.0.1.jar", day: 1, Entry, [1]);

        var target = folder.At(@"out\runescape.ttf");
        Directory.CreateDirectory(target);

        Assert.False(GameFont.Sync(wanted: true, folder.Root, target));
        Assert.Empty(Directory.GetFiles(folder.At("out")));
    }

    [Fact]
    public void OffersNothingWithoutARuneLiteClient()
    {
        using var folder = new Folder();
        Jar(folder, "runelite-api-1.0.1.jar", day: 1, Entry, [1]);

        Assert.False(GameFont.Available(folder.Root));
        Assert.False(GameFont.Available(folder.At("missing")));
        Assert.False(GameFont.Sync(wanted: true, folder.Root, folder.At("runescape.ttf")));
        Assert.Empty(Directory.GetFiles(folder.Root, "*.ttf*"));
    }
}
