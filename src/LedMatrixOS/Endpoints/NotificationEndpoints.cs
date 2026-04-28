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
                var font = Fonts.Big.Scale(3);
                var render = new InterruptMessageRender(message.Message, (int)Math.Round(256d / font.BoundingBox.X, MidpointRounding.ToZero), font, message.Color ?? new Pixel(150, 0, 255));
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

    private record MessageRequest(string Message, Pixel? Color = null);
}
