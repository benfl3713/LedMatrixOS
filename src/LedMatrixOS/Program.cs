using System.Text.Json;
using LedMatrixOS;
using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Scheduling;
using LedMatrixOS.Core.Transitions;
using LedMatrixOS.Endpoints;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Hardware.RpiLedMatrix;
using LedMatrixOS.Hardware.Simulator;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddJsonFile("appsettings.local.json", optional: true)
    .AddEnvironmentVariables();

// Settings
var config = builder.Configuration.Get<AppConfig>();
int width = builder.Configuration.GetValue("Display:Width", 256);
int height = builder.Configuration.GetValue("Display:Height", 64);
bool useSimulator = builder.Configuration.GetValue("Matrix:UseSimulator", false);

Fonts.Load();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.AddHttpClient();
builder.Services.AddSingleton<AppSettingsStorage>(_ => 
    new AppSettingsStorage(Path.Combine(AppContext.BaseDirectory, "app-settings.json")));
builder.Services.AddSingleton<AppManager>(sp => 
{
    var settingsStorage = sp.GetRequiredService<AppSettingsStorage>();
    return new AppManager(sp, builder.Configuration, height, width, settingsStorage);
});
builder.Services.AddSingleton<AudioDataService>();
builder.Services.AddSingleton<InterruptService>();
var schedulePath = Path.Combine(AppContext.BaseDirectory, "schedule.json");
// Apps/services that can answer rule conditions register an IAttentionSource; none are registered by default
builder.Services.AddSingleton<AttentionEvaluator>(sp => new AttentionEvaluator(sp.GetServices<IAttentionSource>()));
builder.Services.AddSingleton<ScheduleService>(sp =>
{
    var schedule = new ScheduleService(attention: sp.GetRequiredService<AttentionEvaluator>());
    schedule.TryLoadFromJson(schedulePath);
    return schedule;
});
builder.Services.AddHostedService<ScheduleRunner>();
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
    var interruptService = sp.GetRequiredService<InterruptService>();
    foreach (var app in BuiltInApps.GetAll()) apps.Register(app);
    var renderEngine = new RenderEngine(device, apps, interruptService, logger: sp.GetService<ILogger<RenderEngine>>());
    apps.Overlays = renderEngine.Overlays;
    return renderEngine;
});

var app = builder.Build();

// Static files for local testing preview
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.UseWebSockets();

// Start render loop
var engine = app.Services.GetRequiredService<RenderEngine>();
var crashCard = new LedMatrixOS.Graphics.UI.CrashCard();
engine.CrashRenderer = crashCard.Render;
var appManager = app.Services.GetRequiredService<AppManager>();

await appManager.ActivateAsync("home", CancellationToken.None);
engine.Start();
app.Lifetime.ApplicationStopping.Register(engine.Stop);

// API endpoints
app.MapGet("/api/apps", (AppManager appManager) => 
{
    var apps = appManager.AppInfos.Select(i => new { i.Id, i.Name, i.HasSettings }).ToList();
    return Results.Ok(new { apps, activeApp = appManager.ActiveApp?.Id });
});

app.MapPost("/api/apps/{id}", async (string id, AppManager appManager, CancellationToken ct) =>
{
    var ok = await appManager.ActivateAsync(id, ct);
    return ok ? Results.Ok(new { activeApp = id }) : Results.NotFound();
});

app.MapGet("/api/apps/{id}/settings", (string id, AppManager appManager) =>
{
    var lookup = appManager.GetSettings(id);
    return lookup.Status switch
    {
        SettingsStatus.NotFound => Results.NotFound($"Unknown app '{id}'"),
        _ => Results.Ok(new { appId = id, settings = lookup.Settings }),
    };
});

app.MapPost("/api/apps/{id}/settings", (string id, Dictionary<string, object> settingsUpdate, AppManager appManager) =>
{
    var result = appManager.UpdateSettings(id, settingsUpdate);
    return result.Status switch
    {
        SettingsStatus.NotFound => Results.NotFound($"Unknown app '{id}'"),
        SettingsStatus.NotConfigurable => Results.BadRequest("App does not support configuration"),
        _ when result.RejectedKeys.Count > 0 => Results.BadRequest(new { message = "Unknown setting(s)", rejected = result.RejectedKeys }),
        _ => Results.Ok(new { message = "Settings updated successfully" }),
    };
});

app.MapGet("/api/health", (RenderEngine eng, IMatrixDevice device, AppManager apps) =>
    Results.Ok(new
    {
        status = eng.IsRunning ? "ok" : "stopped",
        activeApp = apps.ActiveApp?.Id,
        device.IsEnabled,
        device.Width,
        device.Height,
        uptimeSeconds = (int)(DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds,
    }));

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

app.MapGet("/api/transitions", (RenderEngine eng) =>
    Results.Ok(new
    {
        current = eng.TransitionName,
        transitions = eng.Transitions.Names.Order().Append(TransitionRegistry.RandomName)
    }));

app.MapPost("/api/settings/transition/{name}", (string name, RenderEngine eng) =>
{
    if (!eng.Transitions.IsValidName(name)) return Results.NotFound($"Unknown transition '{name}'");
    eng.TransitionName = name.ToLowerInvariant();
    return Results.Ok(new { transition = eng.TransitionName });
});

// Live preview: binary frames [width u16][height u16][RGB...] at up to 30 fps, only when the picture changed
app.MapGet("/ws/preview", async (HttpContext context, RenderEngine eng) =>
{
    if (!context.WebSockets.IsWebSocketRequest) return Results.BadRequest("WebSocket request expected");

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    using var subscription = eng.Broadcaster.Subscribe();
    var ct = context.RequestAborted;
    long last = 0;
    var message = new byte[0];

    // A viewer that closes shows up as a completed receive
    var closed = Task.Run(async () =>
    {
        var buffer = new byte[64];
        try { while (socket.State == System.Net.WebSockets.WebSocketState.Open && (await socket.ReceiveAsync(buffer, ct)).MessageType != System.Net.WebSockets.WebSocketMessageType.Close) { } }
        catch (Exception) { }
    }, ct);

    try
    {
        while (socket.State == System.Net.WebSockets.WebSocketState.Open && !closed.IsCompleted)
        {
            int length = eng.Broadcaster.TryRead(ref last, ref message);
            if (length > 0) await socket.SendAsync(new ArraySegment<byte>(message, 0, length), System.Net.WebSockets.WebSocketMessageType.Binary, true, ct);
            await Task.Delay(33, ct);
        }
    }
    catch (OperationCanceledException) { }
    catch (System.Net.WebSockets.WebSocketException) { }
    return Results.Empty;
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
        
        var audioData = JsonSerializer.Deserialize<AudioStreamData>(json, new JsonSerializerOptions
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

app.MapNotificationEndpoints();
app.MapOverlayEndpoints(schedulePath);

app.Run();

public record AudioStreamData(float[] Samples, int SampleRate = 44100);
