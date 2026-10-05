using System.Globalization;
using System.Net;
using System.Text.Json;
using LedMatrixOS.Apps.Commute;
using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.Journey;

internal enum JourneyStatus { Ok, Ambiguous, NoRoute, Offline }

/// <summary>One leg of a journey. <see cref="Label"/> is the short pill text ("WALK 4", "VIC", "73"), built once at parse time.</summary>
internal sealed record JourneyLeg(string Mode, string LineId, string Label, string From, int Minutes, DateTime Departure, bool Walking, bool Disrupted);

/// <summary>One suggested journey. Times are London local time, as TfL returns them.</summary>
internal sealed record JourneyOption(DateTime Start, DateTime Arrival, int Minutes, JourneyLeg[] Legs)
{
    /// <summary>The first leg that is not walking, i.e. the vehicle you have to be at the stop for; null for a walk-only journey.</summary>
    public JourneyLeg? FirstTransit => Array.Find(Legs, l => !l.Walking);

    /// <summary>When the first vehicle leaves (or the journey starts, if it is all walking).</summary>
    public DateTime BoardAt => FirstTransit?.Departure ?? Start;

    public bool Disrupted => Array.Exists(Legs, l => l.Disrupted);

    /// <summary>Identifies the content, so a re-poll that changes nothing does not rebuild the pages.</summary>
    public string Key => $"{Start:HHmm}|{Arrival:HHmm}|{Minutes}|{string.Join(',', Legs.Select(l => l.Label + (l.Disrupted ? "!" : "")))}";
}

internal sealed record JourneyResult(JourneyStatus Status, JourneyOption[] Journeys)
{
    public static JourneyResult Of(JourneyStatus status) => new(status, []);
}

/// <param name="Preference">TfL <c>journeyPreference</c>: LeastTime, LeastInterchange or LeastWalking.</param>
internal sealed record JourneyQuery(string From, string To, string Modes, DateTime When, string Preference = "LeastTime");

internal interface IJourneySource
{
    /// <summary>Plans journeys. Never throws for network or HTTP problems; those come back as <see cref="JourneyStatus.Offline"/>.</summary>
    Task<JourneyResult> GetAsync(JourneyQuery query, CancellationToken ct);
}

/// <summary>Turns a TfL Journey Planner response into <see cref="JourneyResult"/>. Pure, so it is tested with captured bodies.</summary>
internal static class JourneyParser
{
    public const int MaxJourneys = 3;

    private static readonly Dictionary<string, string> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bakerloo"] = "BAK", ["central"] = "CEN", ["circle"] = "CIR", ["district"] = "DIS", ["hammersmith-city"] = "H&C",
        ["jubilee"] = "JUB", ["metropolitan"] = "MET", ["northern"] = "NOR", ["piccadilly"] = "PIC", ["victoria"] = "VIC",
        ["waterloo-city"] = "W&C", ["dlr"] = "DLR", ["elizabeth"] = "ELZ", ["overground"] = "OVG", ["tram"] = "TRM",
    };

    public static JourneyResult Parse(HttpStatusCode status, string? body)
    {
        if (status == HttpStatusCode.MultipleChoices) return JourneyResult.Of(JourneyStatus.Ambiguous);
        if ((int)status is < 200 or >= 300 || string.IsNullOrWhiteSpace(body)) return JourneyResult.Of(JourneyStatus.Offline);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return JourneyResult.Of(JourneyStatus.Offline);

            var journeys = new List<JourneyOption>();
            if (root.TryGetProperty("journeys", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var j in list.EnumerateArray())
                {
                    if (ParseJourney(j) is { } option) journeys.Add(option);
                    if (journeys.Count == MaxJourneys) break;
                }

            if (journeys.Count > 0) return new JourneyResult(JourneyStatus.Ok, journeys.ToArray());
            return JourneyResult.Of(IsAmbiguous(root, "fromLocationDisambiguation") || IsAmbiguous(root, "toLocationDisambiguation")
                ? JourneyStatus.Ambiguous : JourneyStatus.NoRoute);
        }
        catch (JsonException)
        {
            return JourneyResult.Of(JourneyStatus.Offline);
        }
    }

    private static bool IsAmbiguous(JsonElement root, string name) =>
        root.TryGetProperty(name, out var d) && d.ValueKind == JsonValueKind.Object
        && d.TryGetProperty("matchStatus", out var m) && string.Equals(m.GetString(), "list", StringComparison.OrdinalIgnoreCase);

    private static JourneyOption? ParseJourney(JsonElement j)
    {
        if (!TryTime(j, "startDateTime", out var start) || !TryTime(j, "arrivalDateTime", out var arrival)) return null;
        int minutes = j.TryGetProperty("duration", out var d) && d.TryGetInt32(out var dm) ? dm : (int)Math.Round((arrival - start).TotalMinutes);

        var legs = new List<JourneyLeg>();
        if (j.TryGetProperty("legs", out var legList) && legList.ValueKind == JsonValueKind.Array)
            foreach (var l in legList.EnumerateArray()) legs.Add(ParseLeg(l, start));
        return legs.Count == 0 ? null : new JourneyOption(start, arrival, minutes, legs.ToArray());
    }

    private static JourneyLeg ParseLeg(JsonElement l, DateTime fallbackTime)
    {
        string mode = Str(l, "mode", "id");
        bool walking = mode.Equals("walking", StringComparison.OrdinalIgnoreCase);
        int minutes = l.TryGetProperty("duration", out var d) && d.TryGetInt32(out var dm) ? dm : 0;
        var departure = TryTime(l, "departureTime", out var dt) ? dt : fallbackTime;

        string lineId = "", routeName = "";
        if (l.TryGetProperty("routeOptions", out var options) && options.ValueKind == JsonValueKind.Array && options.GetArrayLength() > 0)
        {
            var first = options[0];
            routeName = Str(first, "name");
            lineId = Str(first, "lineIdentifier", "id");
            if (routeName.Length == 0) routeName = Str(first, "lineIdentifier", "name");
        }

        bool disrupted = (l.TryGetProperty("isDisrupted", out var dis) && dis.ValueKind == JsonValueKind.True)
            || (l.TryGetProperty("disruptions", out var dl) && dl.ValueKind == JsonValueKind.Array && dl.GetArrayLength() > 0);

        return new JourneyLeg(mode, lineId, walking ? $"WALK {minutes}" : LabelFor(mode, lineId, routeName), Str(l, "departurePoint", "commonName"),
            minutes, departure, walking, disrupted);
    }

    /// <summary>Short pill text: three letters for a rail line, the route number for a bus.</summary>
    public static string LabelFor(string mode, string lineId, string routeName)
    {
        if (mode.Equals("bus", StringComparison.OrdinalIgnoreCase))
        {
            var route = (routeName.Length > 0 ? routeName : lineId).ToUpperInvariant();
            return route.Length > 0 ? route : "BUS";
        }

        if (Codes.TryGetValue(TubeColors.Normalize(lineId), out var code)) return code;
        var source = routeName.Length > 0 ? routeName : lineId.Length > 0 ? lineId : mode;
        source = new string(source.Where(char.IsLetterOrDigit).ToArray());
        return source.Length == 0 ? "?" : source[..Math.Min(3, source.Length)].ToUpperInvariant();
    }

    public static Pixel ColorOf(JourneyLeg leg)
    {
        if (leg.Walking) return new Pixel(48, 50, 62);
        return leg.Mode.ToLowerInvariant() switch
        {
            "bus" => Tube.BusColors.For(leg.Label),
            "national-rail" => new Pixel(30, 80, 170),
            "tram" => new Pixel(80, 175, 40),
            "river-bus" or "river-tour" => new Pixel(0, 120, 190),
            "coach" => new Pixel(120, 70, 160),
            _ => TubeColors.Display(leg.LineId),
        };
    }

    private static bool TryTime(JsonElement e, string name, out DateTime value)
    {
        value = default;
        return e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            && DateTime.TryParse(p.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static string Str(JsonElement e, params string[] path)
    {
        foreach (var step in path)
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(step, out e)) return "";
        return e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
    }
}

/// <summary>What to tell the traveller: which journey they can still make, and how long until they must leave to catch its first vehicle.</summary>
internal readonly record struct JourneyPlan(int Index, int LeaveInSeconds, Urgency Urgency, bool Missed)
{
    public static readonly JourneyPlan None = new(-1, 0, Urgency.Relaxed, false);
}

internal static class JourneyPlanner
{
    /// <summary>The first journey whose first vehicle is at least <paramref name="walkBufferMinutes"/> away from <paramref name="now"/>.</summary>
    public static JourneyPlan Plan(IReadOnlyList<JourneyOption> options, DateTime now, int walkBufferMinutes)
    {
        if (options.Count == 0) return JourneyPlan.None;

        double buffer = Math.Max(0, walkBufferMinutes) * 60.0;
        for (int i = 0; i < options.Count; i++)
        {
            double leaveIn = (options[i].BoardAt - now).TotalSeconds - buffer;
            if (leaveIn < 0) continue;

            int seconds = (int)leaveIn;
            var urgency = seconds <= CommutePlanner.NowThresholdSeconds ? Urgency.Now : seconds <= CommutePlanner.SoonThresholdSeconds ? Urgency.Soon : Urgency.Relaxed;
            return new JourneyPlan(i, seconds, urgency, false);
        }

        return new JourneyPlan(0, 0, Urgency.Relaxed, true);
    }
}
