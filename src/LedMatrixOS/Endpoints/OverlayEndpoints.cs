using LedMatrixOS.Core;
using LedMatrixOS.Core.Overlays;
using LedMatrixOS.Core.Scheduling;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Endpoints;

public sealed record ToastRequest(string Message, double? Seconds, string? Color, string? Background);

public sealed record BadgeRequest(string Id, int? X, int? Y, int? Size, string? Color, bool? Pulsing);

public static class OverlayEndpoints
{
    public static void MapOverlayEndpoints(this IEndpointRouteBuilder endpoints, string schedulePath)
    {
        endpoints.MapPost("/api/overlays/toast", ([FromServices] RenderEngine engine, [FromBody] ToastRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.Message)) return Results.BadRequest("Message is required");

            var text = new TextRun();
            text.Set(Fonts.Small, req.Message);
            var fg = ParseColor(req.Color, Pixel.Black);
            var bg = ParseColor(req.Background, Pixel.White);
            int height = Math.Max(8, text.Height + 2);
            int width = engine.Overlays.Width;

            var toast = new ToastOverlay(
                TimeSpan.FromSeconds(Math.Clamp(req.Seconds ?? 4, 1, 60)),
                (frame, _) => text.Draw(frame, Math.Max(0, (width - text.Width) / 2), 1, fg),
                bg,
                new Rectangle(0, 0, width, height));
            engine.Overlays.Add(toast);
            return Results.Ok(new { id = toast.Id });
        });

        endpoints.MapPost("/api/overlays/badge", ([FromServices] RenderEngine engine, [FromBody] BadgeRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id)) return Results.BadRequest("Id is required");
            int size = Math.Clamp(req.Size ?? 4, 1, 32);
            engine.Overlays.Dismiss(req.Id);
            engine.Overlays.Add(new BadgeOverlay(
                req.Id,
                new Rectangle(req.X ?? engine.Overlays.Width - size - 1, req.Y ?? 1, size, size),
                ParseColor(req.Color, new Pixel(255, 60, 60)),
                req.Pulsing ?? true));
            return Results.Ok(new { id = req.Id });
        });

        endpoints.MapDelete("/api/overlays/{id}", ([FromServices] RenderEngine engine, string id) =>
            engine.Overlays.Dismiss(id) ? Results.Ok() : Results.NotFound());

        endpoints.MapDelete("/api/overlays", ([FromServices] RenderEngine engine) =>
        {
            engine.Overlays.Clear();
            return Results.Ok();
        });

        endpoints.MapPost("/api/schedule/reload", ([FromServices] ScheduleService schedule) =>
        {
            lock (schedule.Gate)
            {
                schedule.Clear();
                if (!schedule.TryLoadFromJson(schedulePath))
                    return Results.NotFound($"Could not load {Path.GetFileName(schedulePath)}");
            }
            return Results.Ok(new { loaded = Path.GetFileName(schedulePath) });
        });

        endpoints.MapGet("/api/schedule", ([FromServices] ScheduleService schedule) =>
        {
            lock (schedule.Gate)
                return Results.Ok(new { appId = schedule.GetActiveAppId(), brightness = schedule.GetActiveBrightnessOverride() });
        });
    }

    private static Pixel ParseColor(string? hex, Pixel fallback) =>
        hex != null && Pixel.TryParseHex(hex, out var p) ? p : fallback;
}
