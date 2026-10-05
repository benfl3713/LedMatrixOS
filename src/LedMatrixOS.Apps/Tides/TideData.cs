using System.Globalization;
using System.Text.Json;
using LedMatrixOS.Apps.Weather;

namespace LedMatrixOS.Apps.Tides;

public enum TideKind { High, Low }

/// <summary>A high or low water, with the height in metres.</summary>
public sealed record TideEvent(DateTimeOffset Time, TideKind Kind, double Height);

/// <summary>
/// Evenly spaced tide heights (metres) for a place. Heights between samples are interpolated smoothly, and the highs and lows are found
/// from the samples once, up front. <see cref="IsModel"/> marks a made-up sinusoidal forecast rather than real data.
/// </summary>
public sealed class TideForecast
{
    private readonly double[] _levels;

    public TideForecast(string place, DateTimeOffset start, TimeSpan step, double[] levels, bool isModel = false)
    {
        if (levels.Length < 4) throw new ArgumentException("A tide forecast needs at least four samples", nameof(levels));
        Place = place;
        Start = start;
        Step = step;
        _levels = levels;
        IsModel = isModel;
        Low = levels.Min();
        High = levels.Max();
        Events = FindEvents();
    }

    public string Place { get; }
    public DateTimeOffset Start { get; }
    public TimeSpan Step { get; }
    public bool IsModel { get; }
    public double Low { get; }
    public double High { get; }
    public TideEvent[] Events { get; }
    public DateTimeOffset End => Start + Step * (_levels.Length - 1);

    /// <summary>Whether <paramref name="when"/> has samples on both sides (a couple of steps' margin) and so is trustworthy.</summary>
    public bool Covers(DateTimeOffset when) => when >= Start + Step && when <= End - Step;

    /// <summary>The water height at a moment, in metres (Catmull-Rom through the samples; clamped to the ends).</summary>
    public double LevelAt(DateTimeOffset when)
    {
        double pos = (when - Start) / Step;
        if (pos <= 0) return _levels[0];
        if (pos >= _levels.Length - 1) return _levels[^1];
        int i = (int)pos;
        double f = pos - i;
        double p0 = _levels[Math.Max(0, i - 1)], p1 = _levels[i], p2 = _levels[i + 1], p3 = _levels[Math.Min(_levels.Length - 1, i + 2)];
        return p1 + 0.5 * f * (p2 - p0 + f * (2 * p0 - 5 * p1 + 4 * p2 - p3 + f * (3 * (p1 - p2) + p3 - p0)));
    }

    /// <summary>The next two highs/lows after <paramref name="when"/> (soonest first). Returns false when there is not even one.</summary>
    public bool TryNext(DateTimeOffset when, out TideEvent first, out TideEvent? second)
    {
        first = null!;
        second = null;
        foreach (var e in Events)
        {
            if (e.Time <= when) continue;
            if (first is null) first = e;
            else { second = e; break; }
        }

        return first is not null;
    }

    private TideEvent[] FindEvents()
    {
        var found = new List<TideEvent>();
        for (int i = 1; i < _levels.Length - 1; i++)
        {
            double a = _levels[i - 1], b = _levels[i], c = _levels[i + 1];
            bool high = b > a && b >= c, low = b < a && b <= c;
            if (!high && !low) continue;
            double denom = a - 2 * b + c;
            double shift = Math.Abs(denom) < 1e-9 ? 0 : Math.Clamp(0.5 * (a - c) / denom, -0.5, 0.5);
            found.Add(new TideEvent(Start + Step * (i + shift), high ? TideKind.High : TideKind.Low, b - 0.25 * (a - c) * shift));
        }

        return [.. found];
    }
}

/// <summary>Where tide heights come from. The real source needs the network; <see cref="ModelTideSource"/> never fails.</summary>
public interface ITideSource
{
    Task<TideForecast> GetAsync(string location, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>
/// A deterministic stand-in: the main lunar semidiurnal tide (12h25m) plus a smaller solar one, which gives spring and neap tides over a fortnight.
/// It is not real for any place, but the same moment always gives the same forecast, so it works offline and in tests.
/// </summary>
public sealed class ModelTideSource : ITideSource
{
    private static readonly DateTimeOffset Epoch = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const double LunarHours = 12.4206012, SolarHours = 12.0, Mean = 3.0, LunarAmp = 2.2, SolarAmp = 0.7;

    public Task<TideForecast> GetAsync(string location, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(Create(location, now));

    public static TideForecast Create(string location, DateTimeOffset now)
    {
        var start = new DateTimeOffset(now.UtcDateTime.Date.AddHours(now.UtcDateTime.Hour), TimeSpan.Zero) - TimeSpan.FromHours(13);
        var step = TimeSpan.FromMinutes(15);
        var levels = new double[4 * 50];
        for (int i = 0; i < levels.Length; i++)
        {
            double h = (start + step * i - Epoch).TotalHours;
            levels[i] = Mean + LunarAmp * Math.Cos(2 * Math.PI * h / LunarHours) + SolarAmp * Math.Cos(2 * Math.PI * h / SolarHours);
        }

        return new TideForecast(PlaceName(location), start, step, levels, isModel: true);
    }

    internal static string PlaceName(string location)
    {
        int bar = location.IndexOf('|');
        return (bar > 0 ? location[..bar] : location).Trim();
    }
}

/// <summary>Real data: Open-Meteo's marine API (free, no key) sea level height including tides, hourly. Inland places have no value and fail.</summary>
public sealed class OpenMeteoTideSource(HttpClient http, PlaceResolver? places = null) : ITideSource
{
    private readonly PlaceResolver _places = places ?? new PlaceResolver(http);

    public async Task<TideForecast> GetAsync(string location, DateTimeOffset now, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var place = await _places.ResolveAsync(location, timeout.Token).ConfigureAwait(false);
        var json = await http.GetStringAsync(Url(place.Lat, place.Lon), timeout.Token).ConfigureAwait(false);
        return Parse(json, place.Name);
    }

    public static string Url(double lat, double lon) =>
        "https://marine-api.open-meteo.com/v1/marine" +
        $"?latitude={lat.ToString(CultureInfo.InvariantCulture)}&longitude={lon.ToString(CultureInfo.InvariantCulture)}" +
        "&hourly=sea_level_height_msl&timezone=UTC&past_days=1&forecast_days=3";

    public static TideForecast Parse(string json, string place)
    {
        using var doc = JsonDocument.Parse(json);
        var hourly = doc.RootElement.GetProperty("hourly");
        var times = hourly.GetProperty("time");
        var heights = hourly.GetProperty("sea_level_height_msl");
        int n = Math.Min(times.GetArrayLength(), heights.GetArrayLength());
        var levels = new List<double>(n);
        for (int i = 0; i < n; i++)
        {
            if (heights[i].ValueKind != JsonValueKind.Number) break;   // the forecast ends before the requested range does
            levels.Add(heights[i].GetDouble());
        }

        if (levels.Count < 4) throw new FormatException("No sea level data for this place");
        var start = DateTime.SpecifyKind(DateTime.Parse(times[0].GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.None), DateTimeKind.Utc);
        return new TideForecast(place, new DateTimeOffset(start), TimeSpan.FromHours(1), [.. levels]);
    }
}
