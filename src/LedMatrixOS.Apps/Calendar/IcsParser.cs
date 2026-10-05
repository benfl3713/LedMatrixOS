using System.Globalization;

namespace LedMatrixOS.Apps.Calendar;

internal sealed record CalEvent(string Title, DateTimeOffset Start, DateTimeOffset End, bool AllDay, string? Location, bool Declined = false);

/// <summary>
/// A small iCalendar reader: VEVENTs with UTC, TZID and floating times, all-day dates, escaping and folded lines, and simple recurrence
/// (DAILY/WEEKLY with BYDAY/MONTHLY/YEARLY, INTERVAL, COUNT, UNTIL, EXDATE). An instance with a RECURRENCE-ID (moved, edited or cancelled)
/// replaces the generated occurrence it names; the override then stands on its own, or vanishes when it is cancelled.
/// </summary>
internal static class IcsParser
{
    private sealed record Prop(string Name, Dictionary<string, string> Parameters, string Value);

    public static List<CalEvent> Parse(string ics, DateTimeOffset from, DateTimeOffset to, TimeZoneInfo localZone, string? selfEmail = null)
    {
        var vevents = new List<List<Prop>>();
        List<Prop>? current = null;
        foreach (var prop in ReadProps(ics))
        {
            if (prop.Name == "BEGIN" && prop.Value == "VEVENT") current = new List<Prop>();
            else if (prop.Name == "END" && prop.Value == "VEVENT" && current != null)
            {
                vevents.Add(current);
                current = null;
            }
            else current?.Add(prop);
        }

        // Pass 1: the occurrences that RECURRENCE-ID instances replace, per UID (cancelled ones included: that is how a cancellation is expressed)
        var replaced = new Dictionary<string, HashSet<long>>();
        foreach (var props in vevents)
        {
            var recurrenceId = props.FirstOrDefault(p => p.Name == "RECURRENCE-ID");
            if (recurrenceId is null) continue;
            try
            {
                var (wall, zone, _) = ParseTime(recurrenceId, localZone);
                string uid = props.FirstOrDefault(p => p.Name == "UID")?.Value.Trim() ?? "";
                if (!replaced.TryGetValue(uid, out var set)) replaced[uid] = set = new HashSet<long>();
                set.Add(InstantKey(wall, zone));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or TimeZoneNotFoundException) { /* ignore a malformed id */ }
        }

        var events = new List<CalEvent>();
        foreach (var props in vevents)
        {
            string uid = props.FirstOrDefault(p => p.Name == "UID")?.Value.Trim() ?? "";
            bool isOverride = props.Any(p => p.Name == "RECURRENCE-ID");
            try { Expand(props, from, to, localZone, events, !isOverride && replaced.TryGetValue(uid, out var skip) ? skip : null, selfEmail); }
            catch (Exception ex) when (ex is FormatException or ArgumentException or TimeZoneNotFoundException) { /* skip a malformed event */ }
        }

        events.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));
        return events;
    }

    /// <summary>The instant a wall-clock time in a zone denotes, as a comparable number (all-day dates use the local zone's midnight).</summary>
    private static long InstantKey(DateTime wall, TimeZoneInfo zone) => new DateTimeOffset(wall, zone.GetUtcOffset(wall)).UtcTicks;

    private static IEnumerable<Prop> ReadProps(string ics)
    {
        var lines = new List<string>();
        foreach (var raw in ics.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t') && lines.Count > 0) lines[^1] += raw[1..];
            else lines.Add(raw);
        }

        foreach (var line in lines)
        {
            int colon = IndexOfValueColon(line);
            if (colon <= 0) continue;
            var head = line[..colon].Split(';');
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in head.Skip(1))
            {
                int eq = p.IndexOf('=');
                if (eq > 0) parameters[p[..eq]] = p[(eq + 1)..].Trim('"');
            }
            yield return new Prop(head[0].ToUpperInvariant(), parameters, line[(colon + 1)..]);
        }
    }

    // The first colon outside double quotes ends the name and parameters
    private static int IndexOfValueColon(string line)
    {
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == ':' && !quoted) return i;
        }
        return -1;
    }

    private static string Unescape(string s) => s.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");

    /// <summary>
    /// True when the user declined the event. With <paramref name="selfEmail"/> only that attendee's PARTSTAT counts; without it an event
    /// counts as declined only when every attendee declined (a single decline among several is somebody else's).
    /// </summary>
    private static bool IsDeclined(List<Prop> props, string? selfEmail)
    {
        var attendees = props.Where(p => p.Name == "ATTENDEE").ToList();
        if (attendees.Count == 0) return false;
        static bool Declined(Prop a) => string.Equals(a.Parameters.GetValueOrDefault("PARTSTAT"), "DECLINED", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(selfEmail))
        {
            var me = attendees.FirstOrDefault(a => a.Value.Contains(selfEmail.Trim(), StringComparison.OrdinalIgnoreCase));
            return me is not null && Declined(me);
        }
        return attendees.All(Declined);
    }

    private static void Expand(List<Prop> props, DateTimeOffset from, DateTimeOffset to, TimeZoneInfo localZone, List<CalEvent> output, HashSet<long>? overridden, string? selfEmail)
    {
        bool declined = IsDeclined(props, selfEmail);
        var start = props.FirstOrDefault(p => p.Name == "DTSTART");
        if (start is null) return;

        string title = Unescape(props.FirstOrDefault(p => p.Name == "SUMMARY")?.Value ?? "(no title)");
        string? location = props.FirstOrDefault(p => p.Name == "LOCATION")?.Value is { Length: > 0 } loc ? Unescape(loc) : null;
        if (props.Any(p => p.Name == "STATUS" && p.Value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase))) return;

        var (startWall, zone, allDay) = ParseTime(start, localZone);
        var endProp = props.FirstOrDefault(p => p.Name == "DTEND");
        DateTime endWall = endProp != null ? ParseTime(endProp, localZone).Wall : allDay ? startWall.AddDays(1) : startWall;
        var length = endWall - startWall;

        var rrule = props.FirstOrDefault(p => p.Name == "RRULE")?.Value;
        if (rrule is null)
        {
            Add(startWall, zone, length, allDay, title, location, declined, from, to, localZone, output);
            return;
        }

        var rule = rrule.Split(';').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].ToUpperInvariant(), p => p[1]);
        // EXDATE values and RECURRENCE-ID overrides both remove generated occurrences, compared as instants so zones may differ
        var excluded = new HashSet<long>(overridden ?? []);
        var excludedDays = new HashSet<DateTime>();   // a date-only EXDATE on a timed series removes that whole day's occurrence
        foreach (var exdate in props.Where(p => p.Name == "EXDATE"))
            foreach (var value in exdate.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var (exWall, exZone, exAllDay) = ParseTime(new Prop("EXDATE", exdate.Parameters, value), localZone);
                if (exAllDay && !allDay) excludedDays.Add(exWall.Date);
                else excluded.Add(InstantKey(exWall, exZone));
            }
        string freq = rule.GetValueOrDefault("FREQ", "");
        int interval = rule.TryGetValue("INTERVAL", out var iv) && int.TryParse(iv, out var n) && n > 0 ? n : 1;
        int? count = rule.TryGetValue("COUNT", out var cv) && int.TryParse(cv, out var c) ? c : null;
        DateTime? until = rule.TryGetValue("UNTIL", out var uv) ? ParseTime(new Prop("UNTIL", new(), uv), localZone).Wall : null;
        if (until != null && rule["UNTIL"].Trim().Length == 8) until = until.Value.AddDays(1).AddTicks(-1);
        var byDay = rule.TryGetValue("BYDAY", out var bd)
            ? bd.Split(',').Select(d => DayOf(d.Length >= 2 ? d[^2..] : d)).Where(d => d != null).Select(d => d!.Value).ToHashSet()
            : null;

        // Walk day by day from the first occurrence; the window is weeks, so this stays cheap
        var lastDay = to.UtcDateTime.AddDays(2).Date;
        int produced = 0;
        for (var day = startWall.Date; day <= lastDay; day = day.AddDays(1))
        {
            if (!Matches(freq, day, startWall.Date, interval, byDay)) continue;
            var wall = day + startWall.TimeOfDay;
            if (until != null && wall > until) break;
            if (count != null && ++produced > count) break;
            if (excluded.Contains(InstantKey(wall, zone)) || excludedDays.Contains(day)) continue;
            Add(wall, zone, length, allDay, title, location, declined, from, to, localZone, output);
        }
    }

    private static bool Matches(string freq, DateTime day, DateTime first, int interval, HashSet<DayOfWeek>? byDay)
    {
        int days = (day - first).Days;
        switch (freq)
        {
            case "DAILY": return days % interval == 0;
            case "WEEKLY":
                int weeks = (StartOfWeek(day) - StartOfWeek(first)).Days / 7;
                return weeks % interval == 0 && (byDay != null ? byDay.Contains(day.DayOfWeek) : day.DayOfWeek == first.DayOfWeek);
            case "MONTHLY":
                return day.Day == first.Day && ((day.Year - first.Year) * 12 + day.Month - first.Month) % interval == 0;
            case "YEARLY":
                return day.Month == first.Month && day.Day == first.Day && (day.Year - first.Year) % interval == 0;
            default: return false;
        }
    }

    private static DateTime StartOfWeek(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    private static DayOfWeek? DayOf(string code) => code.ToUpperInvariant() switch
    {
        "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday, "TH" => DayOfWeek.Thursday,
        "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday, "SU" => DayOfWeek.Sunday, _ => null,
    };

    private static void Add(DateTime wall, TimeZoneInfo zone, TimeSpan length, bool allDay, string title, string? location, bool declined,
        DateTimeOffset from, DateTimeOffset to, TimeZoneInfo localZone, List<CalEvent> output)
    {
        var start = TimeZoneInfo.ConvertTime(new DateTimeOffset(wall, zone.GetUtcOffset(wall)), localZone);
        var end = start + length;
        if (allDay)
        {
            start = new DateTimeOffset(wall.Date, localZone.GetUtcOffset(wall.Date));
            end = new DateTimeOffset((wall.Date + length), localZone.GetUtcOffset(wall.Date + length));
        }
        if (end > from && start < to) output.Add(new CalEvent(title, start, end, allDay, location, declined));
    }

    private static (DateTime Wall, TimeZoneInfo Zone, bool AllDay) ParseTime(Prop prop, TimeZoneInfo localZone)
    {
        var v = prop.Value.Trim();
        if (v.Length == 8 || prop.Parameters.GetValueOrDefault("VALUE") == "DATE")
            return (DateTime.ParseExact(v[..8], "yyyyMMdd", CultureInfo.InvariantCulture), localZone, true);

        bool utc = v.EndsWith('Z');
        var wall = DateTime.ParseExact(v.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
        if (utc) return (wall, TimeZoneInfo.Utc, false);
        if (prop.Parameters.TryGetValue("TZID", out var tzid))
        {
            try { return (wall, TimeZoneInfo.FindSystemTimeZoneById(tzid), false); }
            catch (TimeZoneNotFoundException) { /* unknown zone name: treat as local */ }
        }
        return (wall, localZone, false);
    }
}
