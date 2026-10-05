namespace LedMatrixOS.Apps.Pet;

public enum PetMood { Sleeping, Hungry, Sad, Content, Happy }

/// <summary>How many calendar events have finished so far today. The default says none; a calendar-backed implementation can feed the pet.</summary>
public interface ICalendarProgress
{
    int EventsCompletedToday(DateTimeOffset localNow);
}

/// <summary>Whether the pet's room is dark (so the pet sleeps).</summary>
public interface IDaylight
{
    bool IsNight(DateTimeOffset localNow);
}

/// <summary>Whether it is raining outside the window.</summary>
public interface IRainSensor
{
    bool IsRaining { get; }
}

public sealed class NoCalendarProgress : ICalendarProgress
{
    public int EventsCompletedToday(DateTimeOffset localNow) => 0;
}

/// <summary>Night between 22:00 and 06:00 local time.</summary>
public sealed class ClockDaylight(int nightStartHour = 22, int nightEndHour = 6) : IDaylight
{
    public bool IsNight(DateTimeOffset localNow) => localNow.Hour >= nightStartHour || localNow.Hour < nightEndHour;
}

public sealed class NoRain : IRainSensor
{
    public bool IsRaining => false;
}

/// <summary>The pet's rules, kept free of time and drawing so they can be tested directly.</summary>
public static class PetRules
{
    public const int MaxFullness = 100;
    public const int HungryBelow = 25;
    public const int FoodPerEvent = 12;

    /// <summary>Night means asleep; a low belly means hungry; otherwise fullness, finished events and the weather set the mood.</summary>
    public static PetMood MoodOf(int fullness, int eventsCompleted, bool isNight, bool isRaining)
    {
        if (isNight) return PetMood.Sleeping;
        if (fullness < HungryBelow) return PetMood.Hungry;

        int score = fullness + 5 * Math.Min(eventsCompleted, 6) - (isRaining ? 25 : 0);
        return score >= 85 ? PetMood.Happy : score >= 45 ? PetMood.Content : PetMood.Sad;
    }

    public static int Clamp(int fullness) => Math.Clamp(fullness, 0, MaxFullness);

    public static string Label(PetMood mood) => mood switch
    {
        PetMood.Sleeping => "SLEEPING",
        PetMood.Hungry => "HUNGRY",
        PetMood.Sad => "GLOOMY",
        PetMood.Content => "CONTENT",
        _ => "HAPPY",
    };
}
