using System.Globalization;
using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.BinDay;

/// <summary>One bin's recurrence: every <see cref="EveryNWeeks"/> weeks on <see cref="Weekday"/>, aligned to <see cref="Anchor"/> (a known collection date).</summary>
public sealed record BinRule(string Name, Pixel Colour, DayOfWeek Weekday, int EveryNWeeks, DateOnly? Anchor, IReadOnlyList<DateOnly> Skips)
{
    private const int MaxSkipSearch = 600;

    /// <summary>The first collection on or after <paramref name="from"/> that is not skipped, or null if none within about ten years.</summary>
    public DateOnly? NextOnOrAfter(DateOnly from)
    {
        int step = 7 * Math.Max(1, EveryNWeeks);
        DateOnly d;
        if (Anchor is { } anchor)
        {
            int diff = from.DayNumber - anchor.DayNumber;
            int k = (int)Math.Ceiling(diff / (double)step);
            d = anchor.AddDays(k * step);
        }
        else
        {
            d = from.AddDays(((int)Weekday - (int)from.DayOfWeek + 7) % 7);
            step = 7;
        }

        for (int i = 0; i < MaxSkipSearch; i++, d = d.AddDays(step))
            if (!Skips.Contains(d)) return d;
        return null;
    }
}

/// <summary>A bin (or one-off reminder from the calendar) collected on a date.</summary>
public readonly record struct Collection(string Name, Pixel Colour, DateOnly Date);

/// <summary>Pure computation of upcoming bin collections from rules plus optional one-off dates.</summary>
public sealed class CollectionSchedule(IReadOnlyList<BinRule> rules, IReadOnlyList<Collection>? extras = null)
{
    public IReadOnlyList<BinRule> Rules { get; } = rules;
    public IReadOnlyList<Collection> Extras { get; } = extras ?? [];

    /// <summary>All collections from <paramref name="today"/> through <paramref name="days"/> days ahead, by date then rule order.</summary>
    public List<Collection> Upcoming(DateOnly today, int days)
    {
        var last = today.AddDays(days);
        var result = new List<Collection>();
        foreach (var rule in Rules)
        {
            var d = rule.NextOnOrAfter(today);
            while (d is { } date && date <= last)
            {
                result.Add(new Collection(rule.Name, rule.Colour, date));
                d = rule.NextOnOrAfter(date.AddDays(1));
            }
        }
        foreach (var e in Extras)
            if (e.Date >= today && e.Date <= last) result.Add(e);
        return result.OrderBy(c => c.Date).ToList();   // stable: rule order within a day
    }

    /// <summary>The next collection of each bin (one-offs included), soonest first.</summary>
    public List<Collection> NextPerBin(DateOnly today)
    {
        var result = new List<Collection>();
        foreach (var rule in Rules)
            if (rule.NextOnOrAfter(today) is { } d) result.Add(new Collection(rule.Name, rule.Colour, d));
        foreach (var e in Extras.Where(e => e.Date >= today)) result.Add(e);
        return result.OrderBy(c => c.Date).ToList();
    }

    /// <summary>Everything collected on <paramref name="date"/>.</summary>
    public List<Collection> On(DateOnly date) => Upcoming(date, 0);

    /// <summary>True from <paramref name="eveningHour"/> the evening before a collection until the end of the collection day.</summary>
    public bool IsDue(DateTime local, int eveningHour)
    {
        var today = DateOnly.FromDateTime(local);
        if (On(today).Count > 0) return true;
        return local.Hour >= eveningHour && On(today.AddDays(1)).Count > 0;
    }
}

/// <summary>A text reminder shown while the local time is inside its window on the chosen days.</summary>
public sealed record Reminder(string Text, TimeOnly Start, TimeOnly End, byte DayMask)
{
    public bool RunsOn(DayOfWeek day) => (DayMask & (1 << (int)day)) != 0;

    /// <summary>True inside [Start, End). A window that ends before it starts wraps past midnight and belongs to the day it started on.</summary>
    public bool IsActive(DateTime local)
    {
        var t = TimeOnly.FromDateTime(local);
        if (Start < End) return RunsOn(local.DayOfWeek) && t >= Start && t < End;
        return (RunsOn(local.DayOfWeek) && t >= Start) || (RunsOn(local.AddDays(-1).DayOfWeek) && t < End);
    }
}

/// <summary>Parses the <c>Bins</c> and <c>Reminders</c> settings. Entries split on ';' or newline, fields on '|'. Bad entries are reported, not thrown.</summary>
public static class BinParser
{
    public const string BinSyntax = "Name|#colour|Day|EveryNWeeks|AnchorDate[|skip,skip]; e.g. Black|#3a3a3a|Mon|2|2026-10-05; Garden|#2ea043|Wed|1|2026-10-07";
    public const string ReminderSyntax = "Text|HH:mm|HH:mm|Mon,Tue,...; e.g. Take pills|08:00|20:00|Mon,Tue,Wed,Thu,Fri,Sat,Sun";

    private static readonly string[] Days = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    private static IEnumerable<string[]> Entries(string? text) =>
        (text ?? "").Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.Split('|', StringSplitOptions.TrimEntries));

    public static bool TryParseDay(string? s, out DayOfWeek day)
    {
        day = default;
        if (s is not { Length: >= 3 }) return false;
        int i = Array.IndexOf(Days, s[..3].ToLowerInvariant());
        if (i < 0) return false;
        day = (DayOfWeek)i;
        return true;
    }

    public static bool TryParseColour(string? s, out Pixel colour)
    {
        colour = default;
        if (s is null) return false;
        s = s.TrimStart('#');
        if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return false;
        colour = new Pixel((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static (List<BinRule> Rules, List<string> Errors) ParseBins(string? text)
    {
        var rules = new List<BinRule>();
        var errors = new List<string>();
        foreach (var f in Entries(text))
        {
            string label = f[0].Length == 0 ? "?" : f[0];
            if (f.Length < 3 || f[0].Length == 0) { errors.Add($"{label}: needs Name|#colour|Day"); continue; }
            if (!TryParseColour(f[1], out var colour)) { errors.Add($"{label}: bad colour"); continue; }
            if (!TryParseDay(f[2], out var day)) { errors.Add($"{label}: bad day"); continue; }
            int every = 1;
            if (f.Length > 3 && f[3].Length > 0 && (!int.TryParse(f[3], out every) || every is < 1 or > 4)) { errors.Add($"{label}: weeks must be 1-4"); continue; }
            DateOnly? anchor = null;
            if (f.Length > 4 && f[4].Length > 0)
            {
                if (!DateOnly.TryParseExact(f[4], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var a)) { errors.Add($"{label}: bad anchor date"); continue; }
                anchor = a;
            }
            if (anchor is { } an && an.DayOfWeek != day) { errors.Add($"{label}: anchor is not a {day}"); continue; }
            if (every > 1 && anchor is null) { errors.Add($"{label}: needs an anchor date"); continue; }

            var skips = new List<DateOnly>();
            bool badSkip = false;
            if (f.Length > 5)
                foreach (var s in f[5].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sd)) skips.Add(sd);
                    else badSkip = true;
                }
            if (badSkip) { errors.Add($"{label}: bad skip date"); continue; }
            rules.Add(new BinRule(f[0], colour, day, every, anchor, skips));
        }
        return (rules, errors);
    }

    public static (List<Reminder> Reminders, List<string> Errors) ParseReminders(string? text)
    {
        var list = new List<Reminder>();
        var errors = new List<string>();
        foreach (var f in Entries(text))
        {
            string label = f[0].Length == 0 ? "?" : f[0];
            if (f.Length < 3 || f[0].Length == 0) { errors.Add($"{label}: needs Text|start|end"); continue; }
            if (!TimeOnly.TryParseExact(f[1], "H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
                !TimeOnly.TryParseExact(f[2], "H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)) { errors.Add($"{label}: bad time"); continue; }
            if (start == end) { errors.Add($"{label}: empty window"); continue; }
            byte mask = 0x7F;
            if (f.Length > 3 && f[3].Length > 0 && !f[3].Equals("daily", StringComparison.OrdinalIgnoreCase))
            {
                mask = 0;
                bool bad = false;
                foreach (var d in f[3].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (TryParseDay(d, out var day)) mask |= (byte)(1 << (int)day); else bad = true;
                if (bad || mask == 0) { errors.Add($"{label}: bad days"); continue; }
            }
            list.Add(new Reminder(f[0], start, end, mask));
        }
        return (list, errors);
    }
}
