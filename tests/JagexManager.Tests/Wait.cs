using System.Diagnostics;

namespace JagexManager.Tests;

internal static class Wait
{
    public static async Task<bool> ForAsync(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (condition())
                return true;
            await Task.Delay(50);
        }

        return condition();
    }
}
