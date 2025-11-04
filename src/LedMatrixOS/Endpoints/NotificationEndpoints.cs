using LedMatrixOS.Apps.Interrupts;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LedMatrixOS.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/api/notifications",
            ([FromServices] InterruptService interruptService) =>
            {
                var now = DateTime.Now;
                interruptService.RequestInterrupt(
                    new InterruptRequest(
                        RenderRed,
                        () => { return DateTime.Now > now.AddSeconds(5); }));
            });

        endpoints.MapPost(
            "/api/notifications/message",
            ([FromServices] InterruptService interruptService, [FromServices] IOptions<AppConfig> appConfig, [FromBody] MessageRequest message) =>
            {
                var render = new InterruptMessageRender(message.Message, 256 / Fonts.Big.BoundingBox.X);
                interruptService.RequestInterrupt(
                    new InterruptRequest(
                        render.Render,
                        render.HasFinished));
            });
    }

    public static void RenderRed(FrameBuffer frame)
    {
        frame.Clear(new Pixel(200, 0, 0));
    }

    private record MessageRequest(string Message);
}
