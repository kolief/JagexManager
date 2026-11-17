namespace JagexManager;

internal static class Disk
{
    public static void Replace(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var draft = path + ".tmp";
        try
        {
            using (var file = new FileStream(draft, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                file.Write(data);
                file.Flush(flushToDisk: true);
            }

            File.Move(draft, path, overwrite: true);
        }
        catch
        {
            Discard(draft);
            throw;
        }
    }

    private static void Discard(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
