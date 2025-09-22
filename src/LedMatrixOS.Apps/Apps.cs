using LedMatrixOS.Core;

namespace LedMatrixOS.Apps;

public static class BuiltInApps
{
    public static IEnumerable<IMatrixApp> GetAll()
    {
        yield return new ClockApp();
        yield return new SolidColorApp();
        // TODO: Weather, Spotify, etc.
    }
}
