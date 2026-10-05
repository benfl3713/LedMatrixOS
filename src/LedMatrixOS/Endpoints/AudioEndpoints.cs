using System.Text.Json;
using LedMatrixOS.Core;

namespace LedMatrixOS.Endpoints;

/// <summary>Receives audio samples for the visualiser apps.</summary>
public static class AudioEndpoints
{
    public static void MapAudioEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/audio/stream", async (HttpRequest request, AudioDataService audioService) =>
        {
            try
            {
                using var reader = new StreamReader(request.Body);
                var json = await reader.ReadToEndAsync();

                var audioData = JsonSerializer.Deserialize<AudioStreamData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (audioData?.Samples != null && audioData.Samples.Length > 0)
                {
                    audioService.AddAudioSamples(audioData.Samples);
                    return Results.Ok(new { message = "Audio data received", sampleCount = audioData.Samples.Length });
                }

                return Results.BadRequest("Invalid audio data");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing audio: {ex.Message}\n{ex.StackTrace}");
                return Results.BadRequest($"Error processing audio: {ex.Message}");
            }
        });

        app.MapGet("/api/audio/status", (AudioDataService audioService) =>
        {
            return Results.Ok(new
            {
                hasRecentData = audioService.HasRecentData(),
                bandCount = AudioDataService.FrequencyBandCount
            });
        });
    }

    private record AudioStreamData(float[] Samples, int SampleRate = 44100);
}
