using System.Linq;
using System.Threading;
using LedMatrixOS;
using LedMatrixOS.Core;
using LedMatrixOS.Apps;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Hardware.Simulator;
using LedMatrixOS.Hardware.RpiLedMatrix;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddJsonFile("appsettings.local.json", optional: true)
    .AddEnvironmentVariables();

// Settings
var config = builder.Configuration.Get<AppConfig>();
int width = 254;
int height = 64;
bool useSimulator = builder.Configuration.GetValue("Matrix:UseSimulator", false);

Fonts.Load();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.AddSingleton<AppSettingsStorage>(_ => 
    new AppSettingsStorage(Path.Combine(AppContext.BaseDirectory, "app-settings.json")));
builder.Services.AddSingleton<AppManager>(sp => 
{
    var settingsStorage = sp.GetRequiredService<AppSettingsStorage>();
    return new AppManager(builder.Configuration, height, width, settingsStorage);
});
builder.Services.AddSingleton<AudioDataService>();
builder.Services.AddSingleton<IMatrixDevice>(sp =>
{
    if (useSimulator)
    {
        return new SimulatedMatrixDevice(width, height);
    }
    else
    {
        var factory = new RgbMatrixFactory(config.Matrix);
        var ledMatrix = new LedMatrix(factory);
        
        return new RpiLedMatrixDevice(ledMatrix);
    }
});
builder.Services.AddSingleton<RenderEngine>(sp =>
{
    var device = sp.GetRequiredService<IMatrixDevice>();
    var apps = sp.GetRequiredService<AppManager>();
    foreach (var app in BuiltInApps.GetAll()) apps.Register(app);
    return new RenderEngine(device, apps);
});

var app = builder.Build();

// Static files for local testing preview
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();

// Start render loop
var engine = app.Services.GetRequiredService<RenderEngine>();
var appManager = app.Services.GetRequiredService<AppManager>();
var audioService = app.Services.GetRequiredService<AudioDataService>();

// Set up audio service for equalizer app when it's activated
appManager.AppActivated += (sender, appInstance) =>
{
    if (appInstance is EqualizerApp equalizerApp)
    {
        equalizerApp.SetAudioService(audioService);
    }
};

await appManager.ActivateAsync("animated-clock", CancellationToken.None);
engine.Start();
app.Lifetime.ApplicationStopping.Register(engine.Stop);

// API endpoints
app.MapGet("/api/apps", (AppManager appManager) => 
{
    var apps = appManager.Apps.Select(appType =>
    {
        var instance = (IMatrixApp?)Activator.CreateInstance(appType);
        var hasSettings = instance is IConfigurableApp;
        return new { Id = instance?.Id, Name = instance?.Name, HasSettings = hasSettings };
    }).ToList();
    return Results.Ok(new { apps, activeApp = appManager.ActiveApp?.Id });
});

app.MapPost("/api/apps/{id}", async (string id, AppManager appManager, CancellationToken ct) =>
{
    var ok = await appManager.ActivateAsync(id, ct);
    return ok ? Results.Ok(new { activeApp = id }) : Results.NotFound();
});

app.MapGet("/api/apps/{id}/settings", (string id, AppManager appManager) =>
{
    var activeApp = appManager.ActiveApp;
    if (activeApp?.Id != id)
    {
        return Results.BadRequest("App is not currently active");
    }
    
    if (activeApp is IConfigurableApp configurableApp)
    {
        var settings = configurableApp.GetSettings();
        return Results.Ok(new { appId = id, settings });
    }
    
    return Results.Ok(new { appId = id, settings = Array.Empty<object>() });
});

app.MapPost("/api/apps/{id}/settings", async (string id, Dictionary<string, object> settingsUpdate, AppManager appManager) =>
{
    var activeApp = appManager.ActiveApp;
    if (activeApp?.Id != id)
    {
        return Results.BadRequest("App is not currently active");
    }
    
    if (activeApp is IConfigurableApp)
    {
        foreach (var setting in settingsUpdate)
        {
            appManager.UpdateCurrentAppSetting(setting.Key, setting.Value);
        }
        return Results.Ok(new { message = "Settings updated successfully" });
    }
    
    return Results.BadRequest("App does not support configuration");
});

app.MapGet("/api/settings", (IMatrixDevice device, RenderEngine eng) => 
    Results.Ok(new { 
        device.Width, 
        device.Height, 
        device.Brightness, 
        fps = eng.TargetFps,
        isRunning = eng.IsRunning,
        isEnabled = device.IsEnabled
    }));

app.MapPost("/api/settings/brightness/{value}", (byte value, IMatrixDevice device) =>
{
    device.Brightness = value;
    return Results.Ok(new { brightness = device.Brightness });
});

app.MapPost("/api/settings/power/{enabled}", (bool enabled, IMatrixDevice device) =>
{
    device.IsEnabled = enabled;
    return Results.Ok(new { isEnabled = device.IsEnabled });
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

// Audio streaming endpoint for equalizer visualization
app.MapPost("/api/audio/stream", async (HttpRequest request, AudioDataService audioService) =>
{
    try
    {
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();
        
        // Log the incoming data for debugging
        Console.WriteLine($"Received audio data: {json.Substring(0, Math.Min(200, json.Length))}...");
        
        var audioData = System.Text.Json.JsonSerializer.Deserialize<AudioStreamData>(json, new System.Text.Json.JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });
        
        if (audioData?.Samples != null && audioData.Samples.Length > 0)
        {
            Console.WriteLine($"Processing {audioData.Samples.Length} samples. First few: {string.Join(", ", audioData.Samples.Take(5))}");
            audioService.AddAudioSamples(audioData.Samples);
            return Results.Ok(new { message = "Audio data received", sampleCount = audioData.Samples.Length });
        }
        
        Console.WriteLine("Invalid audio data - samples null or empty");
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

app.Run();

public record AudioStreamData(float[] Samples, int SampleRate = 44100);
