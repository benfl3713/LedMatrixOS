using LedMatrixOS.Core.Media;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace LedMatrixOS.Endpoints;

/// <summary>
/// The media library API (pictures, GIFs, videos for the "media" app). The logic lives in <see cref="MediaLibrary"/>; these only
/// translate HTTP: 201 created, 400 unsupported or corrupt, 413 too big, 503 video without ffmpeg, 507 library full.
/// </summary>
public static class MediaEndpoints
{
    private const long MultipartOverhead = 1024 * 1024;

    public static void MapMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/media");

        group.MapPost("", async (HttpRequest request, [FromServices] MediaLibrary library, CancellationToken ct) =>
        {
            long limit = library.Config.MaxBytes;
            if (!request.HasFormContentType) return Results.BadRequest(new { message = "Send multipart/form-data with a 'file' field." });
            if (request.ContentLength > limit + MultipartOverhead) return TooLarge(limit);

            var sizeLimit = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeLimit is { IsReadOnly: false }) sizeLimit.MaxRequestBodySize = limit + MultipartOverhead;

            IFormCollection form;
            try
            {
                form = await request.ReadFormAsync(ct);
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                return TooLarge(limit);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or BadHttpRequestException)
            {
                return Results.BadRequest(new { message = "The upload could not be read." });
            }

            var file = form.Files.GetFile("file");
            if (file is null) return Results.BadRequest(new { message = "The 'file' field is required." });
            if (file.Length > limit) return TooLarge(limit);

            string? name = form["name"].ToString();
            if (string.IsNullOrWhiteSpace(name)) name = file.FileName;   // shown only; never a path

            try
            {
                await using var stream = file.OpenReadStream();
                var item = await library.AddAsync(stream, name, ct);
                return Results.Created($"/api/media/{item.Id}", item);
            }
            catch (MediaException ex)
            {
                return Failure(ex);
            }
        });

        group.MapGet("", ([FromServices] MediaLibrary library) => Results.Ok(library.Items));

        group.MapGet("/capabilities", ([FromServices] MediaLibrary library) => Results.Ok(library.Capabilities));

        group.MapGet("/{id}/thumb", (string id, [FromServices] MediaLibrary library) =>
            library.GetThumb(id) is { } png ? Results.File(png, "image/png") : Results.NotFound());

        group.MapDelete("/{id}", (string id, [FromServices] MediaLibrary library) =>
            library.Delete(id) ? Results.NoContent() : Results.NotFound());
    }

    private static IResult TooLarge(long limit) =>
        Results.Json(new { message = $"The file is larger than {limit / (1024 * 1024)} MB." }, statusCode: StatusCodes.Status413PayloadTooLarge);

    private static IResult Failure(MediaException ex)
    {
        var body = new { message = ex.Message };
        return ex.Error switch
        {
            MediaError.TooLarge => Results.Json(body, statusCode: StatusCodes.Status413PayloadTooLarge),
            MediaError.VideoUnavailable => Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable),
            MediaError.LibraryFull => Results.Json(body, statusCode: StatusCodes.Status507InsufficientStorage),
            MediaError.NotFound => Results.NotFound(body),
            _ => Results.BadRequest(body),
        };
    }
}
