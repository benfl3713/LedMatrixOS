namespace LedMatrixOS.Core.Input;

public enum InputButton { Up, Down, Left, Right, A, B, Start, Select }

public enum InputState { Down, Up }

/// <summary>One button transition from one player (0-3).</summary>
public readonly record struct InputEvent(int Player, InputButton Button, InputState State)
{
    public const int MaxPlayers = 4;
    public const int ButtonCount = 8;
}

/// <summary>Validation of the wire format shared by POST /api/input and /ws/input.</summary>
public static class InputParser
{
    /// <summary>
    /// Parses a request. <paramref name="release"/> is true for state "press", meaning the event is a Down that must be followed
    /// immediately by an Up. Returns false with a message when the player, button or state is invalid.
    /// </summary>
    public static bool TryParse(int? player, string? button, string? state, out InputEvent first, out bool release, out string error)
    {
        first = default;
        release = false;
        int p = player ?? 0;
        if (p < 0 || p >= InputEvent.MaxPlayers) { error = $"player must be 0-{InputEvent.MaxPlayers - 1}"; return false; }

        var name = button?.Trim() ?? "";
        if (name.Length == 0 || char.IsDigit(name[0]) || !Enum.TryParse<InputButton>(name, ignoreCase: true, out var b) || !Enum.IsDefined(b))
        { error = "button must be one of: up, down, left, right, a, b, start, select"; return false; }

        InputState s;
        switch ((state ?? "").Trim().ToLowerInvariant())
        {
            case "down": s = InputState.Down; break;
            case "up": s = InputState.Up; break;
            case "press": s = InputState.Down; release = true; break;
            default: error = "state must be one of: down, up, press"; return false;
        }

        first = new InputEvent(p, b, s);
        error = "";
        return true;
    }
}
