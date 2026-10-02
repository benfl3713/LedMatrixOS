namespace LedMatrixOS.Apps.Weather;

/// <summary>The handful of looks the weather screen can have.</summary>
public enum WeatherKind { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Snow, Thunderstorm }

/// <summary>WMO weather interpretation codes (as used by Open-Meteo) mapped to a look, an intensity and a caption.</summary>
public static class WeatherCodes
{
    public static WeatherKind KindOf(int code) => code switch
    {
        0 or 1 => WeatherKind.Clear,
        2 => WeatherKind.PartlyCloudy,
        3 => WeatherKind.Cloudy,
        45 or 48 => WeatherKind.Fog,
        51 or 53 or 55 or 56 or 57 => WeatherKind.Drizzle,
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => WeatherKind.Rain,
        71 or 73 or 75 or 77 or 85 or 86 => WeatherKind.Snow,
        95 or 96 or 99 => WeatherKind.Thunderstorm,
        _ => WeatherKind.Cloudy,
    };

    /// <summary>1 (light) to 3 (heavy).</summary>
    public static int IntensityOf(int code) => code switch
    {
        51 or 61 or 71 or 77 or 80 or 85 => 1,
        55 or 57 or 65 or 67 or 75 or 82 or 86 or 99 => 3,
        _ => 2,
    };

    public static string Describe(int code, bool isDay = true) => code switch
    {
        0 => isDay ? "Sunny" : "Clear",
        1 => "Mainly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 => "Light drizzle",
        53 => "Drizzle",
        55 => "Heavy drizzle",
        56 or 57 => "Icy drizzle",
        61 => "Light rain",
        63 => "Rain",
        65 => "Heavy rain",
        66 or 67 => "Freezing rain",
        71 => "Light snow",
        73 => "Snow",
        75 => "Heavy snow",
        77 => "Snow grains",
        80 => "Light showers",
        81 => "Showers",
        82 => "Heavy showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunder and hail",
        _ => "Cloudy",
    };
}

public sealed record HourlyPoint(DateTime LocalTime, double Temp, int Code, bool IsDay, int PrecipChance);

public sealed record DailyPoint(DateTime Date, double High, double Low, int Code, int PrecipChance);

/// <summary>Everything the weather screen shows. Temperatures and wind are in the unit the query asked for.</summary>
public sealed record WeatherSnapshot(
    string Location,
    DateTimeOffset ObservedAt,
    double Temp,
    double Feels,
    int Code,
    bool IsDay,
    double WindSpeed,
    int WindDirection,
    int PrecipChance,
    double High,
    double Low,
    TimeSpan Sunrise,
    TimeSpan Sunset,
    bool Fahrenheit,
    IReadOnlyList<HourlyPoint> Hourly,
    IReadOnlyList<DailyPoint> Daily)
{
    public WeatherKind Kind => WeatherCodes.KindOf(Code);
    public TimeSpan UtcOffset => ObservedAt.Offset;
}

public sealed record WeatherQuery(string Location, bool Fahrenheit);

/// <summary>A place the weather could not be fetched for (unknown name), as opposed to a network failure.</summary>
public sealed class LocationNotFoundException(string location) : Exception($"Location not found: {location}");

public interface IWeatherSource
{
    Task<WeatherSnapshot> GetAsync(WeatherQuery query, CancellationToken cancellationToken);
}
