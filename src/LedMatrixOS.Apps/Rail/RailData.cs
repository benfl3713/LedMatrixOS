namespace LedMatrixOS.Apps.Rail;

public enum RailStatus { OnTime, Delayed, Cancelled }

/// <summary>One departure from a station. <see cref="ExpectedTime"/> only matters for delayed services.</summary>
public sealed record RailService(
    string Key,
    DateTimeOffset ScheduledTime,
    DateTimeOffset? ExpectedTime,
    RailStatus Status,
    string Destination,
    string Platform,
    string Operator = "",
    string CallingPoints = "",
    int? Coaches = null,
    bool IsLastTrain = false)
{
    /// <summary>When the train is actually going: the expected time if it is delayed, otherwise the schedule.</summary>
    public DateTimeOffset DepartsAt => Status == RailStatus.Delayed && ExpectedTime is { } e ? e : ScheduledTime;
}

/// <summary>Where departures come from. A real integration implements this and replaces <see cref="HardcodedRailSource"/>.</summary>
public interface IRailDepartureSource
{
    /// <summary>Upcoming services from the station, soonest first.</summary>
    Task<RailService[]> GetDeparturesAsync(string stationCode, CancellationToken cancellationToken);

    /// <summary>The display name of a station code, or null to show the code itself.</summary>
    string? StationName(string stationCode) => null;
}
