using LedMatrixOS.Core;
using LedMatrixOS.Hardware.Simulator;

namespace LedMatrixOS.Endpoints;

/// <summary>The live WebSocket preview and the simulator's PNG snapshot.</summary>
public static class PreviewEndpoints
{
    public static void MapPreviewEndpoints(this IEndpointRouteBuilder app)
    {
        // Live preview: binary frames [width u16][height u16][RGB...] at up to 30 fps, only when the picture changed
        app.MapGet("/ws/preview", async (HttpContext context, RenderEngine eng) =>
        {
            if (!context.WebSockets.IsWebSocketRequest) return Results.BadRequest("WebSocket request expected");

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            using var subscription = eng.Broadcaster.Subscribe();
            var ct = context.RequestAborted;
            long last = 0;
            var message = new byte[0];

            // A viewer that closes shows up as a completed receive
            var closed = Task.Run(async () =>
            {
                var buffer = new byte[64];
                try { while (socket.State == System.Net.WebSockets.WebSocketState.Open && (await socket.ReceiveAsync(buffer, ct)).MessageType != System.Net.WebSockets.WebSocketMessageType.Close) { } }
                catch (Exception) { }
            }, ct);

            try
            {
                while (socket.State == System.Net.WebSockets.WebSocketState.Open && !closed.IsCompleted)
                {
                    int length = eng.Broadcaster.TryRead(ref last, ref message);
                    if (length > 0) await socket.SendAsync(new ArraySegment<byte>(message, 0, length), System.Net.WebSockets.WebSocketMessageType.Binary, true, ct);
                    await Task.Delay(33, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (System.Net.WebSockets.WebSocketException) { }
            return Results.Empty;
        });

        // Simulator preview
        app.MapGet("/preview", (IMatrixDevice device) =>
        {
            if (device is SimulatedMatrixDevice sim)
            {
                var bytes = sim.GetPngBytes();
                return Results.File(bytes, "image/png");
            }
            return Results.BadRequest("Preview only available in simulator mode");
        });
    }
}
