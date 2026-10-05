using System.Text.Json;
using LedMatrixOS.Core.Screens;
using Microsoft.AspNetCore.Mvc;

namespace LedMatrixOS.Endpoints;

public static class ScreenEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapScreenEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/screens/schema", () => Results.Ok(ScreenSchemaInfo.Build()));

        endpoints.MapGet("/api/screens", ([FromServices] ScreenCatalog screens) =>
            Results.Ok(new { screens = screens.All.Select(s => new { s.Id, s.Name }) }));

        endpoints.MapGet("/api/screens/{id}", ([FromServices] ScreenCatalog screens, string id) =>
            screens.TryGet(id) is { } s ? Results.Ok(s) : Results.NotFound());

        endpoints.MapPut("/api/screens/{id}", async (HttpRequest request, [FromServices] ScreenCatalog screens, [FromServices] ILoggerFactory loggers, string id) =>
        {
            try
            {
                using var reader = new StreamReader(request.Body);
                ScreenDefinition? def;
                try { def = JsonSerializer.Deserialize<ScreenDefinition>(await reader.ReadToEndAsync(), Json); }
                catch (JsonException ex) { return Results.BadRequest(new { errors = new[] { new { path = "", message = $"Invalid JSON: {ex.Message}" } } }); }
                if (def == null) return Results.BadRequest(new { errors = new[] { new { path = "", message = "Body must be a JSON object" } } });

                if (string.IsNullOrEmpty(def.Id)) def.Id = id;
                else if (def.Id != id)
                    return Results.BadRequest(new { errors = new[] { new { path = "id", message = $"id '{def.Id}' does not match the route id '{id}'" } } });

                var errors = screens.Put(def);
                if (errors.Count > 0)
                    return Results.BadRequest(new { errors = errors.Select(e => new { path = StripPrefix(e.Path), message = e.Message }) });
                return Results.Ok(def);
            }
            catch (Exception ex)
            {
                loggers.CreateLogger("Screens").LogError(ex, "Saving screen {Id} failed", id);
                return Results.Json(new { errors = new[] { new { path = "", message = $"Could not save the screen ({ex.GetType().Name}): {ex.Message}" } } }, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        endpoints.MapDelete("/api/screens/{id}", async ([FromServices] ScreenCatalog screens, string id, CancellationToken ct) =>
            await screens.DeleteAsync(id, ct) ? Results.Ok() : Results.NotFound());
    }

    // Validation paths are rooted at "screens[0]" (a one-screen document); the client edits a single screen, so drop that.
    private static string StripPrefix(string path) =>
        path.StartsWith("screens[0].", StringComparison.Ordinal) ? path["screens[0].".Length..] : path == "screens[0]" ? "" : path;
}
