namespace LedMatrixOS.Apps.Rail;

/// <summary>
/// A fixed sample timetable for a fictional station. Times are generated relative to the clock it is given, so the board looks live
/// and trains depart as time passes. One service in a handful is delayed, one in a few is cancelled, and the 23:52 is the last train.
/// </summary>
public sealed class HardcodedRailSource(Func<DateTimeOffset> now) : IRailDepartureSource
{
    public const string SampleStationCode = "HVB";
    public const string SampleStationName = "Havenbridge";

    private sealed record Pattern(int Minute, string Destination, string Platform, string Operator, string Calling, int Coaches);

    private static readonly Pattern[] Hourly =
    [
        new(4, "London Bridge", "2", "Southern", "Clapham Junction, Balham, Streatham Hill, East Croydon, London Bridge", 8),
        new(17, "Brighton", "1", "Southern", "Gatwick Airport, Haywards Heath, Burgess Hill, Preston Park, Brighton", 10),
        new(31, "Ashford International", "3", "Southeastern", "Tonbridge, Paddock Wood, Marden, Staplehurst, Ashford International", 6),
        new(44, "Gatwick Airport", "1", "Thameslink", "Redhill, Merstham, Gatwick Airport", 12),
        new(52, "London Victoria", "2", "Southern", "East Croydon, Norwood Junction, Clapham Junction, London Victoria", 8),
    ];

    private const int FirstHour = 5, LastHour = 23, LastMinute = 52;

    public Task<RailService[]> GetDeparturesAsync(string stationCode, CancellationToken cancellationToken) =>
        Task.FromResult(Generate(now(), stationCode));

    public string? StationName(string stationCode) => stationCode.Equals(SampleStationCode, StringComparison.OrdinalIgnoreCase) ? SampleStationName : null;

    /// <summary>The services leaving within the next three hours (and any that left under two minutes ago), soonest first.</summary>
    public static RailService[] Generate(DateTimeOffset at, string stationCode = SampleStationCode)
    {
        var result = new List<RailService>();
        var from = at.AddMinutes(-2);
        var to = at.AddHours(3);
        var midnight = new DateTimeOffset(at.Year, at.Month, at.Day, 0, 0, 0, at.Offset);

        for (int day = 0; day <= 1; day++)
        {
            for (int hour = FirstHour; hour <= LastHour; hour++)
            {
                for (int i = 0; i < Hourly.Length; i++)
                {
                    var p = Hourly[i];
                    if (hour == LastHour && p.Minute > LastMinute) continue;

                    var scheduled = midnight.AddDays(day).AddHours(hour).AddMinutes(p.Minute);
                    if (scheduled < from || scheduled > to) continue;

                    bool last = hour == LastHour && p.Minute == LastMinute;
                    int ordinal = hour * Hourly.Length + i;
                    var status = last ? RailStatus.OnTime
                        : ordinal % 17 == 3 ? RailStatus.Cancelled
                        : ordinal % 6 == 1 ? RailStatus.Delayed
                        : RailStatus.OnTime;

                    DateTimeOffset? expected = status == RailStatus.Delayed ? scheduled.AddMinutes(4 + ordinal % 5) : null;
                    result.Add(new RailService($"{scheduled:yyyyMMddHHmm}{stationCode}", scheduled, expected, status, p.Destination, p.Platform,
                        p.Operator, p.Calling, p.Coaches, last));
                }
            }
        }

        result.Sort((a, b) => a.DepartsAt.CompareTo(b.DepartsAt));
        return result.ToArray();
    }
}
