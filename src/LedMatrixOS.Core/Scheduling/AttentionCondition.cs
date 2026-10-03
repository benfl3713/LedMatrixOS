namespace LedMatrixOS.Core.Scheduling;

/// <summary>A parsed rule condition such as "line_disrupted:victoria" (kind + optional argument).</summary>
public sealed record AttentionCondition(string Kind, string? Argument)
{
    public static readonly string[] KnownKinds = { "spotify_playing", "line_disrupted", "bus_due", "ha_state", "bin_day" };

    public static bool TryParse(string text, out AttentionCondition condition, out string error)
    {
        condition = new AttentionCondition("", null);
        error = "";
        if (string.IsNullOrWhiteSpace(text)) { error = "condition is empty"; return false; }

        var trimmed = text.Trim();
        int colon = trimmed.IndexOf(':');
        var kind = (colon < 0 ? trimmed : trimmed[..colon]).Trim().ToLowerInvariant();
        var arg = colon < 0 ? null : trimmed[(colon + 1)..].Trim();
        if (arg?.Length == 0) arg = null;

        switch (kind)
        {
            case "spotify_playing":
            case "bin_day":
                if (arg != null) { error = $"{kind} takes no argument"; return false; }
                break;
            case "line_disrupted":
            case "bus_due":
                if (arg == null) { error = $"{kind} needs an id, e.g. {kind}:<id>"; return false; }
                break;
            case "ha_state":
                if (arg == null) { error = "ha_state must look like ha_state:<entity>=<value>"; return false; }
                int eq = arg.IndexOf('=');
                if (eq <= 0 || eq == arg.Length - 1) { error = "ha_state must look like ha_state:<entity>=<value>"; return false; }
                break;
            default:
                error = $"unknown condition '{kind}' (known: {string.Join(", ", KnownKinds)})";
                return false;
        }

        condition = new AttentionCondition(kind, arg);
        return true;
    }
}
