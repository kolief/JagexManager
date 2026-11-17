namespace JagexManager.Tests;

internal sealed class Folder : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("jagexmanager-tests-").FullName;

    public string At(string name) => Path.Combine(Root, name);

    public string Script(string body)
    {
        var path = At("client.cmd");
        File.WriteAllText(path, body.ReplaceLineEndings("\r\n"));
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
