using LedMatrixOS.Core;
using LedMatrixOS.Graphics.UI;
using Microsoft.AspNetCore.Mvc;

namespace LedMatrixOS.Endpoints;

/// <summary>
/// Notifications are alert overlays (see <see cref="LedMatrixOS.Core.Overlays.AlertOverlay"/>), so they composite over
/// the running app and show up in GET /api/overlays instead of taking over the whole render loop.
/// </summary>
public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/api/notifications",
            ([FromServices] RenderEngine engine) =>
            {
                engine.Overlays.Add(AlertFactory.Flash(engine.Overlays.Width, engine.Overlays.Height, TimeSpan.FromSeconds(5)));
            });

        endpoints.MapPost(
            "/api/notifications/message",
            ([FromServices] RenderEngine engine, [FromBody] MessageRequest message) =>
            {
                engine.Overlays.Add(AlertFactory.Message(
                    message.Message,
                    message.Color ?? new Pixel(150, 0, 255),
                    engine.Overlays.Width,
                    engine.Overlays.Height));
            });
    }

    private record MessageRequest(string Message, Pixel? Color = null);
}
