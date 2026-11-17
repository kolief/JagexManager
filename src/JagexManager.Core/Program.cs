using System.Text.Json.Serialization;

namespace JagexManager;

public sealed record Program(string Name, string Path, [property: JsonIgnore] string Arguments = "")
{
    public const string RuneLite = "RuneLite";
    public const string OldSchool = "Old School RuneScape";
    public const string RuneScape = "RuneScape 3";

    private static readonly string Games = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Jagex Launcher", "Games");

    public static IReadOnlyList<Program> Known { get; } =
    [
        new(RuneLite, System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RuneLite", "RuneLite.exe")),
        new(OldSchool, System.IO.Path.Combine(Games, "Old School RuneScape", "Client", "osclient.exe")),
        new(RuneScape, System.IO.Path.Combine(Games, "RuneScape", "RuneScape.exe"),
            "--configURI https://www.runescape.com/k=5/l=0/jav_config.ws"),
    ];

    [JsonIgnore]
    public bool Installed => System.IO.Path.IsPathFullyQualified(Path) && File.Exists(Path);

    [JsonIgnore]
    public string Status => Installed ? Path : $"Not found: {Path}";
}
