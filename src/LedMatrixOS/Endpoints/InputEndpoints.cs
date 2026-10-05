using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Input;
using Microsoft.AspNetCore.Mvc;

namespace LedMatrixOS.Endpoints;

/// <summary>
/// Controller input for interactive apps. POST /api/input sends one event; /ws/input takes the same JSON per message and
/// releases whatever that socket was holding when it closes.
/// </summary>
public static class InputEndpoints
{
    public sealed record InputRequest(int? Player, string? Button, string? State);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly byte[] Ok = Encoding.UTF8.GetBytes("{\"ok\":true}");

    public static void MapInputEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/input", ([FromServices] RenderEngine engine, [FromBody] InputRequest request) =>
        {
            if (!Apply(engine.Input, null, request, out var error)) return Results.BadRequest(error);
            return Results.Accepted();
        });

        endpoints.MapGet("/ws/input", async (HttpContext context, RenderEngine engine) =>
        {
            if (!context.WebSockets.IsWebSocketRequest) return Results.BadRequest("WebSocket request expected");

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            using var client = engine.Input.CreateClient(); // disposed on disconnect: releases held buttons
            var ct = context.RequestAborted;
            var buffer = new byte[1024];

            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    int length = 0;
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), ct);
                        length += result.Count;
                    } while (!result.EndOfMessage && length < buffer.Length);

                    if (result.MessageType == WebSocketMessageType.Close) break;
                    if (!result.EndOfMessage) { await DrainAsync(socket, ct); continue; } // oversized message: ignore it
                    if (result.MessageType != WebSocketMessageType.Text) continue;

                    InputRequest? request = null;
                    string error = "invalid JSON";
                    try { request = JsonSerializer.Deserialize<InputRequest>(buffer.AsSpan(0, length), Json); }
                    catch (JsonException) { }

                    if (request is not null && Apply(engine.Input, client, request, out error))
                        await socket.SendAsync(Ok, WebSocketMessageType.Text, true, ct);
                    else
                        await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { ok = false, error }, Json)), WebSocketMessageType.Text, true, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            return Results.Empty;
        });
    }

    private static async Task DrainAsync(WebSocket socket, CancellationToken ct)
    {
        var scratch = new byte[1024];
        WebSocketReceiveResult r;
        do { r = await socket.ReceiveAsync(scratch, ct); } while (!r.EndOfMessage);
    }

    /// <summary>Validates and queues a request. <paramref name="client"/> (a socket) tracks held buttons for release; null for REST.</summary>
    public static bool Apply(InputHub hub, InputClient? client, InputRequest request, out string error)
    {
        if (!InputParser.TryParse(request.Player, request.Button, request.State, out var e, out bool press, out error)) return false;

        if (press) _ = client?.SendPress(e.Player, e.Button) ?? hub.EnqueuePress(e.Player, e.Button);
        else _ = client?.Send(e) ?? hub.Enqueue(e);
        return true;
    }
}
