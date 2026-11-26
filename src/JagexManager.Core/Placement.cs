namespace JagexManager;

public readonly record struct Area(int Left, int Top, int Width, int Height);

public sealed record Placement(Area Normal, bool Maximised);
