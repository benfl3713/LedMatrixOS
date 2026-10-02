using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.Commute;

internal enum Urgency { Relaxed, Soon, Now }

/// <summary>
/// What to tell the commuter: which train they can still catch and how long until they must leave the door.
/// <see cref="Train"/> is null when there is nothing catchable; <see cref="AllMissed"/> then says whether trains exist but are too soon.
/// </summary>
internal readonly record struct CommutePlan(Departure? Train, int LeaveInSeconds, Urgency Urgency, bool AllMissed)
{
    public static readonly CommutePlan None = new(null, 0, Urgency.Relaxed, false);
}

internal static class CommutePlanner
{
    public const int NowThresholdSeconds = 45;
    public const int SoonThresholdSeconds = 3 * 60;

    /// <summary>The first train that is at least <paramref name="walkMinutes"/> away, i.e. one you can still walk to in time.</summary>
    public static CommutePlan Plan(IReadOnlyList<Departure> board, TimeSpan now, int walkMinutes)
    {
        if (board.Count == 0) return CommutePlan.None;

        double walk = Math.Max(0, walkMinutes) * 60.0;
        for (int i = 0; i < board.Count; i++)
        {
            double leaveIn = board[i].RemainingSeconds(now) - walk;
            if (leaveIn < 0) continue;

            int seconds = (int)leaveIn;
            var urgency = seconds <= NowThresholdSeconds ? Urgency.Now : seconds <= SoonThresholdSeconds ? Urgency.Soon : Urgency.Relaxed;
            return new CommutePlan(board[i], seconds, urgency, false);
        }

        return new CommutePlan(null, 0, Urgency.Relaxed, true);
    }
}
