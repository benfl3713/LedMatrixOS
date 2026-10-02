using LedMatrixOS.Core;
using LedMatrixOS.Core.Overlays;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Endpoints;

/// <summary>
/// The original notification endpoints, now drawn as alert overlays on top of the running app (which keeps animating underneath)
/// instead of taking the whole display over. A new notification replaces the one on screen.
/// </summary>
public static class NotificationEndpoints
{
    private const double ScrollPixelsPerSecond = 70;
    private const string AlertId = "notification";

    public static void MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/notifications", ([FromServices] RenderEngine engine) =>
        {
            Show(engine, new AlertOverlay(
                TimeSpan.FromSeconds(5),
                (frame, _) => frame.Fill(new Rectangle(0, 0, frame.Width, frame.Height), new Pixel(200, 0, 0)),
                drawBorder: false,
                bounds: FullScreen(engine),
                id: AlertId));
            return Results.Ok();
        });

        endpoints.MapPost("/api/notifications/message", ([FromServices] RenderEngine engine, [FromBody] MessageRequest message) =>
        {
            if (string.IsNullOrWhiteSpace(message.Message)) return Results.BadRequest("Message is required");
            Show(engine, MessageAlert(engine, message.Message, message.Color ?? new Pixel(150, 0, 255)));
            return Results.Ok();
        });
    }

    private static void Show(RenderEngine engine, AlertOverlay alert)
    {
        engine.Overlays.Remove(AlertId);
        engine.Overlays.Add(alert);
    }

    private static Rectangle FullScreen(RenderEngine engine) => new(0, 0, engine.Overlays.Width, engine.Overlays.Height);

    /// <summary>Big text centred on a dark card when it fits, otherwise scrolled through once from right to left.</summary>
    internal static AlertOverlay MessageAlert(RenderEngine engine, string text, Pixel color)
    {
        var font = Fonts.Big.Scale(2);
        var run = new TextRun();
        run.Set(font, text);

        int width = engine.Overlays.Width, height = engine.Overlays.Height;
        bool fits = run.Width <= width - 8;
        var duration = fits ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds((run.Width + width) / ScrollPixelsPerSecond + 0.5);
        TimeSpan? started = null;

        return new AlertOverlay(
            duration,
            (frame, ctx) =>
            {
                started ??= ctx.Time;
                int x = fits ? (width - run.Width) / 2 : width - (int)((ctx.Time - started.Value).TotalSeconds * ScrollPixelsPerSecond);
                run.Draw(frame, x, (height - run.Height) / 2, color, shadow: true);
            },
            new Pixel(8, 4, 16),
            color,
            bounds: FullScreen(engine),
            id: AlertId);
    }

    private record MessageRequest(string Message, Pixel? Color = null);
}
