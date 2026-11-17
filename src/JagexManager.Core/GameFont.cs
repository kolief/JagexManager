using System.IO.Compression;

namespace JagexManager;

public static class GameFont
{
    public const string Family = "RuneScape";

    private const string Entry = "net/runelite/client/ui/runescape.ttf";

    public static string Repository { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".runelite", "repository2");

    public static bool Available(string repository)
    {
        using var archive = Find(repository)?.Archive;
        return archive is not null;
    }

    public static bool Sync(bool wanted, string repository, string target)
    {
        if (!wanted)
        {
            Delete(target);
            return false;
        }

        if (File.Exists(target))
            return true;

        var font = Find(repository);
        if (font is null)
            return false;

        var draft = target + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
            font.ExtractToFile(draft, overwrite: true);
            File.Move(draft, target);
            return true;
        }
        catch (Exception e) when (Unreadable(e))
        {
            return false;
        }
        finally
        {
            font.Archive.Dispose();
            Delete(draft);
        }
    }

    private static ZipArchiveEntry? Find(string repository)
    {
        FileInfo[] clients;
        try
        {
            clients = new DirectoryInfo(repository).GetFiles("client-*.jar");
        }
        catch (Exception e) when (Unreadable(e))
        {
            return null;
        }

        foreach (var client in clients.OrderByDescending(f => f.LastWriteTimeUtc))
        {
            ZipArchive? archive = null;
            try
            {
                archive = ZipFile.OpenRead(client.FullName);
                if (archive.GetEntry(Entry) is { } font)
                    return font;
            }
            catch (Exception e) when (Unreadable(e))
            {
            }

            archive?.Dispose();
        }

        return null;
    }

    private static void Delete(string path)
    {
        if (!File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (Unreadable(e))
        {
        }
    }

    private static bool Unreadable(Exception e) =>
        e is IOException or UnauthorizedAccessException or InvalidDataException;
}
