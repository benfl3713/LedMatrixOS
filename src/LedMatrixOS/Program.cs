using LedMatrixOS.Core.Settings;
using System.Text.Json;
using LedMatrixOS;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Attention;
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

// Mutable state (settings, screens, schedule) lives in DataDir (default: next to the binary). Set it to a directory the
// service user can write when the matrix library drops root privileges, e.g. DataDir=/var/lib/ledmatrixos.
string dataDir = builder.Configuration["DataDir"] is { Length: > 0 } configuredDataDir
    ? Path.GetFullPath(configuredDataDir)
    : AppContext.BaseDirectory;
Directory.CreateDirectory(dataDir);
string DataFile(string name)
{
    var path = Path.Combine(dataDir, name);
    var legacy = Path.Combine(AppContext.BaseDirectory, name);
    if (!File.Exists(path) && File.Exists(legacy) && !string.Equals(path, legacy, StringComparison.Ordinal))
    {
        try { File.Copy(legacy, path); } catch { /* keep going: the app starts with defaults */ }
    }
    return path;
}

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.AddHttpClient();
builder.Services.AddSingleton<AppSettingsStorage>(_ => 
    new AppSettingsStorage(DataFile("app-settings.json")));
builder.Services.AddSingleton<AppManager>(sp => 
{
    var settingsStorage = sp.GetRequiredService<AppSettingsStorage>();
    return new AppManager(sp, builder.Configuration, height, width, settingsStorage);
});
builder.Services.AddSingleton<SettingOptionsRegistry>(sp =>
{
    var registry = new SettingOptionsRegistry();
    BuiltInSettingOptions.Register(registry, sp.GetRequiredService<IHttpClientFactory>().CreateClient(), builder.Configuration);
    registry.Register("media", "items", new MediaItemOptions(sp.GetRequiredService<LedMatrixOS.Core.Media.MediaLibrary>()));
    return registry;
});
// Media library (pictures, GIFs, videos): <DataDir>/media. Video needs the ffmpeg executable (Media:FfmpegPath, default "ffmpeg" on PATH).
builder.Services.AddSingleton(_ => LedMatrixOS.Core.Media.MediaConfig.From(builder.Configuration, width, height));
builder.Services.AddSingleton<LedMatrixOS.Core.Media.IVideoTranscoder>(sp =>
{
    var media = sp.GetRequiredService<LedMatrixOS.Core.Media.MediaConfig>();
    return new LedMatrixOS.Core.Media.FfmpegVideoTranscoder(media.FfmpegPath, TimeSpan.FromSeconds(media.TranscodeTimeoutSeconds));
});
builder.Services.AddSingleton(sp => new LedMatrixOS.Core.Media.MediaLibrary(
    Path.Combine(dataDir, "media"),
    sp.GetRequiredService<LedMatrixOS.Core.Media.MediaConfig>(),
    sp.GetRequiredService<LedMatrixOS.Core.Media.IVideoTranscoder>()));
builder.Services.AddSingleton<LedMatrixOS.Core.Screens.ScreenStore>();
builder.Services.AddSingleton<LedMatrixOS.Core.Screens.IScreenStore>(sp => sp.GetRequiredService<LedMatrixOS.Core.Screens.ScreenStore>());
builder.Services.AddSingleton(sp => new LedMatrixOS.Core.Screens.ScreenCatalog(
    sp.GetRequiredService<LedMatrixOS.Core.Screens.ScreenStore>(),
    sp.GetRequiredService<AppManager>(),
    DataFile("screens.json")));
builder.Services.AddSingleton<AudioDataService>();
var schedulePath = DataFile("schedule.json");
// Services that can answer rule conditions register an IAttentionSource. They are lazy: AttentionCoordinator only lets them
// poll while a schedule rule references their condition.
builder.Services.AddSingleton<IAttentionSource, SpotifyPlayingSource>();
builder.Services.AddSingleton<IAttentionSource, LineDisruptionSource>();
builder.Services.AddSingleton<IAttentionSource, RoadDisruptionSource>();
builder.Services.AddSingleton<IAttentionSource, BusDueSource>();
builder.Services.AddSingleton<IAttentionSource, HomeAssistantStateSource>();
builder.Services.AddSingleton<IAttentionSource, BinDayDueSource>();
builder.Services.AddSingleton<AttentionEvaluator>(sp => new AttentionEvaluator(sp.GetServices<IAttentionSource>()));
builder.Services.AddSingleton<AttentionCoordinator>(sp =>
    new AttentionCoordinator(sp.GetRequiredService<ScheduleService>(), sp.GetServices<IAttentionSource>()));
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
    foreach (var app in BuiltInApps.GetAll()) apps.Register(app);
    foreach (var (alias, target, preset) in BuiltInApps.Aliases()) apps.RegisterAlias(alias, target, preset);
    var renderEngine = new RenderEngine(device, apps, logger: sp.GetService<ILogger<RenderEngine>>());
    apps.Overlays = renderEngine.Overlays;
    apps.Input = renderEngine.Input;
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
var attention = app.Services.GetRequiredService<AttentionCoordinator>();
attention.Start();
app.Lifetime.ApplicationStopping.Register(attention.Dispose);

foreach (var error in app.Services.GetRequiredService<LedMatrixOS.Core.Screens.ScreenCatalog>().Load())
    app.Logger.LogError("screens.json: {Path} {Message}", error.Path, error.Message);

await appManager.ActivateAsync("home", CancellationToken.None);
engine.Start();
app.Lifetime.ApplicationStopping.Register(engine.Stop);

// API endpoints
app.MapGet("/api/apps", (AppManager appManager) => 
{
    var apps = appManager.AppInfos.Select(i => new { i.Id, i.Name, i.HasSettings, isScreen = false })
        .Concat(appManager.Screens.Select(s => new { s.Id, s.Name, s.HasSettings, isScreen = true })).ToList();
    return Results.Ok(new { apps, activeApp = appManager.ActiveAppId });
});

app.MapPost("/api/apps/{id}", async (string id, AppManager appManager, CancellationToken ct) =>
{
    var ok = await appManager.ActivateAsync(id, ct);
    return ok ? Results.Ok(new { activeApp = id }) : Results.NotFound();
});

app.MapGet("/api/apps/{id}/settings", async (string id, AppManager appManager, SettingOptionsRegistry options, CancellationToken ct) =>
{
    var lookup = appManager.GetSettings(id);
    if (lookup.Status == SettingsStatus.NotFound) return Results.NotFound($"Unknown app '{id}'");

    // Search/MultiSearch settings also report the label(s) of what is picked; a lookup that fails just leaves the id as the label
    var settings = await options.WithLabelsAsync(appManager.ResolveAppId(id) ?? id, lookup.Settings, ct);
    return Results.Ok(new { appId = id, settings });
});

// Live options behind a Search/MultiSearch setting. Stateless: works for inactive apps and aliases, and never touches an app instance.
// Options that depend on another setting (the routes of a station) read it from "ctx.<key>=<value>" query parameters, and fall back to the
// persisted value of any setting the client did not send.
app.MapGet("/api/apps/{id}/settings/{key}/options", async (string id, string key, string? q, HttpRequest request, AppManager appManager, SettingOptionsRegistry options, CancellationToken ct) =>
{
    var appId = appManager.ResolveAppId(id);
    if (appId == null) return Results.NotFound($"Unknown app '{id}'");
    if (!options.Has(appId, key)) return Results.NotFound($"App '{id}' has no searchable setting '{key}'");

    var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var setting in appManager.GetSettings(id).Settings) context[setting.Key] = setting.CurrentValue?.ToString() ?? "";
    foreach (var (name, value) in request.Query)
        if (name.StartsWith("ctx.", StringComparison.OrdinalIgnoreCase) && name.Length > 4) context[name[4..]] = value.ToString();

    var found = await options.SearchAsync(appId, key, q, context, ct);
    return Results.Ok(found.Select(o => new { value = o.Value, label = o.Label, subtitle = o.Subtitle }));
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
        activeApp = apps.ActiveAppId,
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

app.MapPreviewEndpoints();
app.MapAudioEndpoints();
app.MapNotificationEndpoints();
app.MapInputEndpoints();
app.MapOverlayEndpoints(schedulePath);
app.MapScreenEndpoints();
app.MapMediaEndpoints();

app.Run();
