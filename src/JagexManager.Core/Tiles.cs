namespace JagexManager;

public static class Tiles
{
    public static IReadOnlyList<Area> Lay(int count, IReadOnlyList<Area> screens)
    {
        var tiles = new List<Area>();
        for (var screen = 0; screen < screens.Count; screen++)
        {
            var share = (count / screens.Count) + (screen < count % screens.Count ? 1 : 0);
            tiles.AddRange(Split(screens[screen], share));
        }

        return tiles;
    }

    private static IEnumerable<Area> Split(Area screen, int count)
    {
        if (count == 0)
            yield break;

        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (count + columns - 1) / columns;
        var width = screen.Width / columns;
        var height = screen.Height / rows;
        for (var tile = 0; tile < count; tile++)
            yield return new Area(screen.Left + (tile % columns * width), screen.Top + (tile / columns * height), width, height);
    }
}
