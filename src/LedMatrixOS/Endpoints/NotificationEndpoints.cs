using LedMatrixOS.Core;
using LedMatrixOS.Graphics.UI;
using Microsoft.AspNetCore.Mvc;

namespace LedMatrixOS.Endpoints;

/// <summary>
/// The original notification endpoints, drawn as alert overlays on top of the running app (which keeps animating underneath)
/// instead of taking the whole display over. A new notification replaces the one on screen, and each shows up in
/// GET /api/overlays so it can be dismissed.
/// </summary>
public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        string? current = null;
        var gate = new object();

        void Show(RenderEngine engine, Core.Overlays.AlertOverlay alert)
        {
            lock (gate)
            {
                if (current is not null) engine.Overlays.Remove(current);
                current = alert.Id;
                engine.Overlays.Add(alert);
            }
        }

        endpoints.MapPost("/api/notifications", ([FromServices] RenderEngine engine) =>
        {
            Show(engine, AlertFactory.Flash(engine.Overlays.Width, engine.Overlays.Height, TimeSpan.FromSeconds(5)));
            return Results.Ok();
        });

        endpoints.MapPost("/api/notifications/message", ([FromServices] RenderEngine engine, [FromBody] MessageRequest message) =>
        {
            if (string.IsNullOrWhiteSpace(message.Message)) return Results.BadRequest("Message is required");
            Show(engine, AlertFactory.Message(message.Message, message.Color ?? new Pixel(150, 0, 255), engine.Overlays.Width, engine.Overlays.Height));
            return Results.Ok();
        });
    }

    private record MessageRequest(string Message, Pixel? Color = null);
}
