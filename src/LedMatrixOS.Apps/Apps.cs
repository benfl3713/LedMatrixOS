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
        yield return typeof(DvdLogoApp);
        yield return typeof(WeatherApp);
        yield return typeof(SpotifyApp);
        yield return typeof(CountdownTimerApp);
        yield return typeof(PomodoroApp);
        yield return typeof(LifeApp);
        yield return typeof(AliveApp);
        yield return typeof(ScrollingTextApp);
        yield return typeof(EqualizerApp);
        yield return typeof(FireApp);
        yield return typeof(TubeStatusApp);
        yield return typeof(TubeLineApp);
        yield return typeof(TubeDeparturesApp);
        yield return typeof(BusArrivalsApp);
        yield return typeof(RailDeparturesApp);
        yield return typeof(CycleHubApp);
        yield return typeof(RoadDisruptionsApp);
        yield return typeof(AirQualityApp);
        yield return typeof(PlaneSpotterApp);
        yield return typeof(IssTrackerApp);
        yield return typeof(JourneyApp);
        yield return typeof(BinDayApp);
        yield return typeof(MorningBriefingApp);
        yield return typeof(CommuteApp);
        yield return typeof(SkyClockApp);
        yield return typeof(TidesApp);
        yield return typeof(HomeAssistantTilesApp);
        yield return typeof(CalendarApp);
        yield return typeof(AquariumApp);
        yield return typeof(RssTickerApp);
        yield return typeof(StocksApp);
        yield return typeof(PetApp);
        yield return typeof(InputTestApp);
        yield return typeof(QrApp);
        yield return typeof(PartyModeApp);
        yield return typeof(SnakeApp);
        yield return typeof(BreakoutApp);
        yield return typeof(TetrisApp);
        yield return typeof(PongApp);
        yield return typeof(DemosceneApp);
        yield return typeof(MediaApp);
        yield return typeof(WidgetDemoApp);
        yield return typeof(Screens.ScreenApp);
    }

    /// <summary>Retired app ids that still work in schedules, the API and persisted settings: (alias, target id, preset settings).</summary>
    public static IEnumerable<(string Alias, string TargetId, IReadOnlyDictionary<string, object> Preset)> Aliases()
    {
        yield return ("animated-clock", "clock", new Dictionary<string, object> { ["style"] = "Animated" });
        yield return ("flip-clock", "clock", new Dictionary<string, object> { ["style"] = "Flip" });
        yield return ("wifi", "qr", new Dictionary<string, object> { ["mode"] = "WiFi", ["label"] = "Join Wi-Fi" });
    }
}
