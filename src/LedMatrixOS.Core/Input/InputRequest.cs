namespace LedMatrixOS.Core.Input;

/// <summary>The JSON body of POST /api/input and of each /ws/input message: {"player":0,"button":"up","state":"down"}.</summary>
public sealed record InputRequest(int? Player, string? Button, string? State)
{
    /// <summary>
    /// Validates and queues the request ("press" queues Down then Up). <paramref name="client"/> is a socket's sender, which
    /// tracks held buttons for release on disconnect; null for REST. Returns false with a message when invalid.
    /// </summary>
    public bool TryApply(InputHub hub, InputClient? client, out string error)
    {
        if (!InputParser.TryParse(Player, Button, State, out var e, out bool press, out error)) return false;

        if (press) _ = client?.SendPress(e.Player, e.Button) ?? hub.EnqueuePress(e.Player, e.Button);
        else _ = client?.Send(e) ?? hub.Enqueue(e);
        return true;
    }
}
