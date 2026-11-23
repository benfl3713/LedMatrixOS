using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.Commute;

internal enum Urgency { Relaxed, Soon, Now }

/// <summary>
/// What to tell the commuter: which train they can still catch and how long until they must leave the door.
/// <see cref="Train"/> is null when there is nothing catchable; <see cref="AllMissed"/> then says whether trains exist but are too soon.
/// <para>
/// The gaps put that train in context. <see cref="GapBeforeSeconds"/> is how long after the previous train it comes (a long gap means a
/// crowded platform, so a busy train); <see cref="GapAfterSeconds"/> is how long the wait is for the one after it if this one is missed.
/// Both are -1 when unknown. <see cref="TypicalGapSeconds"/> is the usual interval between trains on the board (-1 when there are too few
/// to tell), which is what "long" is measured against.
/// </para>
/// </summary>
internal readonly record struct CommutePlan(
    Departure? Train, int LeaveInSeconds, Urgency Urgency, bool AllMissed,
    int GapBeforeSeconds = -1, int GapAfterSeconds = -1, int TypicalGapSeconds = -1)
{
    public static readonly CommutePlan None = new(null, 0, Urgency.Relaxed, false);

    /// <summary>The train comes after an unusually long gap, so it is likely to be packed.</summary>
    public bool Busy => GapBeforeSeconds >= 0 && CommutePlanner.IsLongGap(GapBeforeSeconds, TypicalGapSeconds);

    /// <summary>Missing this train means an unusually long wait for the next.</summary>
    public bool LongWaitAfter => GapAfterSeconds >= 0 && CommutePlanner.IsLongGap(GapAfterSeconds, TypicalGapSeconds);

    /// <summary>This train is busy but the next follows close behind: worth letting it go.</summary>
    public bool NextIsQuieter => Busy && GapAfterSeconds >= 0 && CommutePlanner.IsShortGap(GapAfterSeconds, TypicalGapSeconds);
}

internal static class CommutePlanner
{
    public const int NowThresholdSeconds = 45;
    public const int SoonThresholdSeconds = 3 * 60;

    /// <summary>With no usual interval to compare against, a gap this long counts as long.</summary>
    public const int FallbackLongGapSeconds = 8 * 60;

    /// <summary>A gap must beat the usual interval by at least this much (and by 60%) to count as long.</summary>
    public const int MinExtraSeconds = 3 * 60;

    /// <summary>A gap within this of the usual interval (or under 4 minutes) counts as short.</summary>
    public const int ShortGapSeconds = 4 * 60;

    /// <summary>
    /// The first train that is at least <paramref name="walkMinutes"/> away, i.e. one you can still walk to in time, with the gaps either side.
    /// </summary>
    /// <param name="sameRouteOnly">
    /// Measure gaps only against trains on the target's route (line and direction). Use it when the board holds every route of the station;
    /// when the rider picked their routes, any of them will do, so every train on the board counts.
    /// </param>
    /// <param name="history">Trains that have recently left, for the gap before the first train on the board.</param>
    public static CommutePlan Plan(IReadOnlyList<Departure> board, TimeSpan now, int walkMinutes, bool sameRouteOnly = false, DepartureHistory? history = null)
    {
        if (board.Count == 0) return CommutePlan.None;

        double walk = Math.Max(0, walkMinutes) * 60.0;
        int target = -1;
        for (int i = 0; i < board.Count; i++)
        {
            if (board[i].RemainingSeconds(now) - walk >= 0) { target = i; break; }
        }

        if (target < 0) return new CommutePlan(null, 0, Urgency.Relaxed, true);

        var train = board[target];
        double arrival = train.RemainingSeconds(now);
        int seconds = (int)(arrival - walk);
        var urgency = seconds <= NowThresholdSeconds ? Urgency.Now : seconds <= SoonThresholdSeconds ? Urgency.Soon : Urgency.Relaxed;

        // The neighbours on the same service: the train before (on the board, or one that has just gone) and the one after.
        double before = double.NaN, after = double.NaN;
        for (int i = target - 1; i >= 0; i--)
            if (Related(board[i], train, sameRouteOnly)) { before = board[i].RemainingSeconds(now); break; }
        if (double.IsNaN(before) && history is not null && history.LastBefore(train, now, sameRouteOnly, out var left)) before = left;
        for (int i = target + 1; i < board.Count; i++)
            if (Related(board[i], train, sameRouteOnly)) { after = board[i].RemainingSeconds(now); break; }

        int gapBefore = double.IsNaN(before) ? -1 : Math.Max(0, (int)Math.Round(arrival - before));
        int gapAfter = double.IsNaN(after) ? -1 : Math.Max(0, (int)Math.Round(after - arrival));
        return new CommutePlan(train, seconds, urgency, false, gapBefore, gapAfter, TypicalGap(board, train, now, sameRouteOnly));
    }

    /// <summary>The median interval between consecutive trains of the target's service on the board; -1 with fewer than two intervals.</summary>
    internal static int TypicalGap(IReadOnlyList<Departure> board, Departure train, TimeSpan now, bool sameRouteOnly)
    {
        Span<int> gaps = stackalloc int[Math.Min(board.Count, 32)];
        int count = 0;
        double previous = double.NaN;
        for (int i = 0; i < board.Count && count < gaps.Length; i++)
        {
            if (!Related(board[i], train, sameRouteOnly)) continue;
            double t = board[i].RemainingSeconds(now);
            if (!double.IsNaN(previous)) gaps[count++] = (int)Math.Round(t - previous);
            previous = t;
        }

        if (count < 2) return -1;
        var used = gaps[..count];
        used.Sort();
        return count % 2 == 1 ? used[count / 2] : (used[count / 2 - 1] + used[count / 2]) / 2;
    }

    public static bool IsLongGap(int gap, int typical) =>
        typical <= 0 ? gap >= FallbackLongGapSeconds : gap >= typical + Math.Max(MinExtraSeconds, typical * 0.6);

    public static bool IsShortGap(int gap, int typical) => gap <= Math.Max(ShortGapSeconds, typical);

    internal static bool Related(Departure a, Departure b, bool sameRouteOnly) =>
        !sameRouteOnly || string.Equals(a.RouteKey, b.RouteKey, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Remembers when the last few trains of the board left, so the gap in front of the first train on the board is still known: TfL only lists
/// trains that are yet to arrive. A train counts as gone once it is under <see cref="DepartingSeconds"/> away (TfL often drops a train from
/// its feed just before it pulls in); its predicted arrival is what is remembered. Fixed size and allocation-free.
/// </summary>
internal sealed class DepartureHistory
{
    public const double DepartingSeconds = 30;
    private const int Capacity = 8;

    private readonly Departure?[] _trains = new Departure?[Capacity];
    private readonly TimeSpan[] _arrivals = new TimeSpan[Capacity];
    private int _next;

    public void Clear()
    {
        Array.Clear(_trains);
        _next = 0;
    }

    /// <summary>Notes every train on the board that is arriving now.</summary>
    public void Observe(IReadOnlyList<Departure> board, TimeSpan now)
    {
        for (int i = 0; i < board.Count; i++)
        {
            var d = board[i];
            double remaining = d.RemainingSeconds(now);
            if (remaining > DepartingSeconds) break;   // the board is in time order
            var arrival = now + TimeSpan.FromSeconds(remaining);

            int slot = Array.IndexOf(_trains, d);
            if (slot < 0)
            {
                slot = _next;
                _next = (_next + 1) % Capacity;
                _trains[slot] = d;
            }
            _arrivals[slot] = arrival;
        }
    }

    /// <summary>The latest train (related to <paramref name="target"/>) that arrived before it, as seconds from <paramref name="now"/> (negative: in the past).</summary>
    public bool LastBefore(Departure target, TimeSpan now, bool sameRouteOnly, out double secondsFromNow)
    {
        var targetArrival = now + TimeSpan.FromSeconds(target.RemainingSeconds(now));
        TimeSpan best = TimeSpan.MinValue;
        for (int i = 0; i < Capacity; i++)
        {
            var d = _trains[i];
            if (d is null || ReferenceEquals(d, target) || !CommutePlanner.Related(d, target, sameRouteOnly)) continue;
            if (_arrivals[i] < targetArrival - TimeSpan.FromSeconds(20) && _arrivals[i] > best) best = _arrivals[i];
        }

        secondsFromNow = best == TimeSpan.MinValue ? 0 : (best - now).TotalSeconds;
        return best != TimeSpan.MinValue;
    }
}
