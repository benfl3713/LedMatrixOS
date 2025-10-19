using LedMatrixOS.Core;

namespace LedMatrixOS.Apps;

public static class BuiltInApps
{
    public static IEnumerable<Type> GetAll()
    {
        yield return typeof(HomePageApp);
        yield return typeof(ClockApp);
        yield return typeof(SolidColorApp);
        yield return typeof(RainbowSpiralApp);
        yield return typeof(BouncingBallsApp);
        yield return typeof(MatrixRainApp);
        yield return typeof(GeometricPatternsApp);
        yield return typeof(AnimatedClockApp);
        yield return typeof(DvdLogoApp);
        yield return typeof(WeatherApp);
        yield return typeof(SpotifyApp);
        yield return typeof(FlipClockApp);
        yield return typeof(CountdownTimerApp);
        //yield return typeof(PongApp);
        yield return typeof(ScrollingTextApp);
        yield return typeof(EqualizerApp);
        yield return typeof(FireApp);
    }
}
