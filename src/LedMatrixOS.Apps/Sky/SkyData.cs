using LedMatrixOS.Apps.Weather;

namespace LedMatrixOS.Apps.Sky;

/// <summary>Where the sky is looked at, and what the weather there is doing (<see cref="Kind"/> falls back to clear when the forecast is unavailable).</summary>
public sealed record SkyData(string Place, double Latitude, double Longitude, WeatherKind Kind);

/// <summary>Resolves a location and its current weather. Failure to get the weather is not an error: the sky is just clear.</summary>
public interface ISkySource
{
    Task<SkyData> GetAsync(string location, CancellationToken cancellationToken);
}

public sealed class OpenMeteoSkySource(PlaceResolver places, IWeatherSource weather) : ISkySource
{
    public async Task<SkyData> GetAsync(string location, CancellationToken cancellationToken)
    {
        var place = await places.ResolveAsync(location, cancellationToken).ConfigureAwait(false);
        var kind = WeatherKind.Clear;
        try
        {
            kind = (await weather.GetAsync(new WeatherQuery(location, false), cancellationToken).ConfigureAwait(false)).Kind;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Weather is decoration here; keep the sky.
        }

        return new SkyData(place.Name, place.Lat, place.Lon, kind);
    }
}
