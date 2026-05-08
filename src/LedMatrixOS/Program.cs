using System.Diagnostics;
using System.Text.Json;
using LedMatrixOS;
using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using LedMatrixOS.Endpoints;
using LedMatrixOS.Engine;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Hardware.RpiLedMatrix;
using LedMatrixOS.Hardware.Simulator;
using LedMatrixOS.Scenes;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddJsonFile("appsettings.local.json", optional: true)
    .AddEnvironmentVariables();

// Settings
var config = builder.Configuration.Get<AppConfig>();
int width = 256;
int height = 64;
bool useSimulator         = builder.Configuration.GetValue("Matrix:UseSimulator", false);
bool useMonoGameEngine    = builder.Configuration.GetValue("Matrix:UseMonoGameEngine", false);
int  simulatorScale       = builder.Configuration.GetValue("Matrix:SimulatorScale", 4);

// ── Headless display setup (must happen before SDL initialises) ───────────────
// On a Pi running without a display server (e.g. as a systemd service) MonoGame
// needs an OpenGL context.  We try two strategies in order:
//
//  1. Xvfb  – virtual X11 framebuffer.  SDL's X11 + Mesa GLX path is the most
//             reliable headless OpenGL stack.  Requires: apt install xvfb
//
//  2. Offscreen fallback – SDL offscreen driver + Mesa software renderer.
//             Works when Xvfb is not installed but llvmpipe/softpipe is present.
//             (apt install libgl1-mesa-dri)
if (useMonoGameEngine && !useSimulator)
{
    SetupHeadlessDisplay();
}

Fonts.Load();
if (useMonoGameEngine) MatrixFonts.Load();

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
builder.Services.AddSingleton<InterruptService>();
if (useMonoGameEngine)
{
    // ── MonoGame engine path ──────────────────────────────────────────────────
    // IMatrixOutput: null in simulator mode (MonoGame window IS the display),
    // real RPi output on hardware.
    builder.Services.AddSingleton<IMatrixOutput>(sp =>
    {
        if (useSimulator)
            return new NullMatrixOutput(width, height);

        var factory = new RgbMatrixFactory(config!.Matrix);
        var ledMatrix = new LedMatrix(factory);
        return new RpiMatrixOutput(ledMatrix);
    });

    builder.Services.AddSingleton<SceneRegistry>(sp =>
    {
        var appMgr = sp.GetRequiredService<AppManager>();
        var registry = new SceneRegistry();

        // Helper: registers the scene in both SceneRegistry (used by MatrixGame to
        // instantiate) and AppManager (used by ActivateAsync to fire the event).
        void Add<TScene>() where TScene : IMatrixScene, new()
        {
            registry.Register<TScene>();
            appMgr.RegisterScene(typeof(TScene));
        }

        void AddFactory<TScene>(Func<IMatrixScene> factory) where TScene : IMatrixScene, new()
        {
            var id = new TScene().Id;
            registry.Register(id, factory);
            appMgr.RegisterScene(id, typeof(TScene));
        }

        // ── Registered MonoGame scenes ───────────────────────────────────────
        Add<SolidColorScene>();
        Add<RainbowSpiralScene>();
        Add<BouncingBallsScene>();
        Add<FireScene>();
        Add<DvdLogoScene>();
        Add<GeometricPatternsScene>();
        Add<ScrollingTextScene>();
        Add<ClockScene>();
        Add<AnimatedClockScene>();
        Add<HomePageScene>();

        // Equalizer needs AudioDataService injected
        var audioSvc = sp.GetRequiredService<AudioDataService>();
        AddFactory<EqualizerScene>(() => new EqualizerScene(audioSvc));

        return registry;
    });

    builder.Services.AddSingleton<MatrixGame>(sp =>
    {
        var appMgr          = sp.GetRequiredService<AppManager>();
        var output          = sp.GetRequiredService<IMatrixOutput>();
        var registry        = sp.GetRequiredService<SceneRegistry>();
        var interruptSvc    = sp.GetRequiredService<InterruptService>();
        var apps            = appMgr;

        // Register all legacy apps so they still show up in /api/apps
        foreach (var appType in BuiltInApps.GetAll()) apps.Register(appType);

        return new MatrixGame(appMgr, output, registry, interruptSvc, width, height, simulatorScale);
    });

}
else
{
    // ── Legacy render engine path ─────────────────────────────────────────────
    builder.Services.AddSingleton<IMatrixDevice>(sp =>
    {
        if (useSimulator)
            return new SimulatedMatrixDevice(width, height);

        var factory = new RgbMatrixFactory(config!.Matrix);
        var ledMatrix = new LedMatrix(factory);
        return new RpiLedMatrixDevice(ledMatrix);
    });

    builder.Services.AddSingleton<RenderEngine>(sp =>
    {
        var device          = sp.GetRequiredService<IMatrixDevice>();
        var apps            = sp.GetRequiredService<AppManager>();
        var interruptService = sp.GetRequiredService<InterruptService>();
        foreach (var appType in BuiltInApps.GetAll()) apps.Register(appType);
        return new RenderEngine(device, apps, interruptService);
    });
}

var app = builder.Build();

// Static files for local testing preview
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();

var appManager   = app.Services.GetRequiredService<AppManager>();
var audioService = app.Services.GetRequiredService<AudioDataService>();

// Set up audio service for equalizer app when it's activated
appManager.AppActivated += (sender, appInstance) =>
{
    if (appInstance is EqualizerApp equalizerApp)
    {
        equalizerApp.SetAudioService(audioService);
    }
};

if (useMonoGameEngine)
{
    // ── MonoGame cohosting ────────────────────────────────────────────────────
    // Start ASP.NET Core without blocking (StartAsync), then hand the main
    // thread to MonoGame (Game.Run() must be called from the main thread on
    // macOS/Linux due to SDL2 restrictions).
    var matrixGame = app.Services.GetRequiredService<MatrixGame>();

    await appManager.ActivateAsync("solid_color", CancellationToken.None);

    await app.StartAsync();
    app.Lifetime.ApplicationStopping.Register(() => matrixGame.Exit());

    using (matrixGame)
        matrixGame.Run();

    await app.StopAsync();
}
else
{
    // ── Legacy RenderEngine startup ───────────────────────────────────────────
    var engine = app.Services.GetRequiredService<RenderEngine>();

    await appManager.ActivateAsync("tube-line", CancellationToken.None);
    engine.Start();
    app.Lifetime.ApplicationStopping.Register(engine.Stop);

    app.Run();
}

// API endpoints
app.MapGet("/api/apps", (AppManager appManager) => 
{
    var apps = appManager.Apps.Select(appType =>
    {
        var instance = Activator.CreateInstance(appType);
        // Works for both IMatrixApp and IMatrixScene — both expose Id and Name via reflection.
        var id   = (instance as IMatrixApp)?.Id   ?? appType.GetProperty("Id")?.GetValue(instance)   as string;
        var name = (instance as IMatrixApp)?.Name ?? appType.GetProperty("Name")?.GetValue(instance) as string;
        var hasSettings = instance is IConfigurableApp || instance is IConfigurableScene;
        return new { Id = id, Name = name, HasSettings = hasSettings };
    }).ToList();
    var activeId = appManager.ActiveApp?.Id ?? appManager.ActiveSceneId;
    return Results.Ok(new { apps, activeApp = activeId });
});

app.MapPost("/api/apps/{id}", async (string id, AppManager appManager, CancellationToken ct) =>
{
    var ok = await appManager.ActivateAsync(id, ct);
    return ok ? Results.Ok(new { activeApp = id }) : Results.NotFound();
});

app.MapGet("/api/apps/{id}/settings", (string id, AppManager appManager, IServiceProvider sp) =>
{
    // Scene path
    if (useMonoGameEngine && appManager.ActiveSceneId == id)
    {
        var game = sp.GetRequiredService<MatrixGame>();
        if (game.ActiveScene is IConfigurableScene configScene)
            return Results.Ok(new { appId = id, settings = configScene.GetSettings() });
        return Results.Ok(new { appId = id, settings = Array.Empty<object>() });
    }

    // Legacy app path
    var activeApp = appManager.ActiveApp;
    if (activeApp?.Id != id)
        return Results.BadRequest("App is not currently active");

    if (activeApp is IConfigurableApp configurableApp)
        return Results.Ok(new { appId = id, settings = configurableApp.GetSettings() });

    return Results.Ok(new { appId = id, settings = Array.Empty<object>() });
});

app.MapPost("/api/apps/{id}/settings", async (string id, Dictionary<string, object> settingsUpdate, AppManager appManager, IServiceProvider sp) =>
{
    // Scene path
    if (useMonoGameEngine && appManager.ActiveSceneId == id)
    {
        var game = sp.GetRequiredService<MatrixGame>();
        if (game.ActiveScene is IConfigurableScene configScene)
        {
            foreach (var setting in settingsUpdate)
                configScene.UpdateSetting(setting.Key, setting.Value);
            return Results.Ok(new { message = "Settings updated successfully" });
        }
        return Results.BadRequest("Scene does not support configuration");
    }

    // Legacy app path
    var activeApp = appManager.ActiveApp;
    if (activeApp?.Id != id)
        return Results.BadRequest("App is not currently active");

    if (activeApp is IConfigurableApp)
    {
        foreach (var setting in settingsUpdate)
            appManager.UpdateCurrentAppSetting(setting.Key, setting.Value);
        return Results.Ok(new { message = "Settings updated successfully" });
    }

    return Results.BadRequest("App does not support configuration");
});

app.MapGet("/api/settings", (IServiceProvider sp) =>
{
    // Works with either the legacy IMatrixDevice or the new IMatrixOutput.
    if (useMonoGameEngine)
    {
        var output = sp.GetRequiredService<IMatrixOutput>();
        var game   = sp.GetRequiredService<MatrixGame>();
        return Results.Ok(new
        {
            output.Width,
            output.Height,
            output.Brightness,
            fps       = 60,
            isRunning = true,
            output.IsEnabled
        });
    }
    else
    {
        var device = sp.GetRequiredService<IMatrixDevice>();
        var eng    = sp.GetRequiredService<RenderEngine>();
        return Results.Ok(new
        {
            device.Width,
            device.Height,
            device.Brightness,
            fps       = eng.TargetFps,
            isRunning = eng.IsRunning,
            device.IsEnabled
        });
    }
});

app.MapPost("/api/settings/brightness/{value}", (byte value, IServiceProvider sp) =>
{
    if (useMonoGameEngine)
    {
        var output = sp.GetRequiredService<IMatrixOutput>();
        output.Brightness = value;
        return Results.Ok(new { brightness = output.Brightness });
    }
    else
    {
        var device = sp.GetRequiredService<IMatrixDevice>();
        device.Brightness = value;
        return Results.Ok(new { brightness = device.Brightness });
    }
});

app.MapPost("/api/settings/power/{enabled}", (bool enabled, IServiceProvider sp) =>
{
    if (useMonoGameEngine)
    {
        var output = sp.GetRequiredService<IMatrixOutput>();
        output.IsEnabled = enabled;
        return Results.Ok(new { isEnabled = output.IsEnabled });
    }
    else
    {
        var device = sp.GetRequiredService<IMatrixDevice>();
        device.IsEnabled = enabled;
        return Results.Ok(new { isEnabled = device.IsEnabled });
    }
});

// /preview: only available in legacy simulator mode.
// In MonoGame mode the simulator window itself is the visual output.
if (!useMonoGameEngine)
{
    app.MapGet("/preview", (IMatrixDevice device) =>
    {
        if (device is SimulatedMatrixDevice sim)
        {
            var bytes = sim.GetPngBytes();
            return Results.File(bytes, "image/png");
        }
        return Results.BadRequest("Preview only available in simulator mode");
    });
}

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

app.Run();

// ── Headless display setup ────────────────────────────────────────────────────
static void SetupHeadlessDisplay()
{
    // Strategy 1: start Xvfb and use SDL's X11 driver.
    // X11 + Mesa GLX is the most reliable headless OpenGL path on Pi.
    // Install: sudo apt install xvfb
    const string vDisplay = ":99";
    var xvfbPath = new[] { "/usr/bin/Xvfb", "/usr/local/bin/Xvfb" }
        .FirstOrDefault(File.Exists);

    if (xvfbPath is not null)
    {
        try
        {
            // Kill any stale Xvfb on :99 from a previous run
            try { Process.Start(new ProcessStartInfo("pkill", $"-f \"Xvfb {vDisplay}\"") { UseShellExecute = false })?.WaitForExit(1000); } catch { }

            var xvfb = Process.Start(new ProcessStartInfo(xvfbPath,
                $"{vDisplay} -screen 0 256x64x24 +extension GLX -nolisten tcp -nolisten unix")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            });

            if (xvfb is not null && !xvfb.HasExited)
            {
                // Give Xvfb a moment to bind the socket
                System.Threading.Thread.Sleep(500);
                Environment.SetEnvironmentVariable("DISPLAY", vDisplay);
                Console.WriteLine($"[LedMatrixOS] Xvfb started on {vDisplay} (pid {xvfb.Id})");
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LedMatrixOS] Xvfb failed to start: {ex.Message}");
        }
    }

    // Strategy 2: SDL offscreen driver + Mesa software renderer.
    // Requires: sudo apt install libgl1-mesa-dri
    Console.WriteLine("[LedMatrixOS] Xvfb not found – falling back to SDL offscreen + Mesa software renderer");
    Environment.SetEnvironmentVariable("SDL_VIDEODRIVER",     "offscreen");
    Environment.SetEnvironmentVariable("LIBGL_ALWAYS_SOFTWARE", "1");
    Environment.SetEnvironmentVariable("GALLIUM_DRIVER",      "softpipe");  // lighter than llvmpipe
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")))
        Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", "/tmp");
}

public record AudioStreamData(float[] Samples, int SampleRate = 44100);
