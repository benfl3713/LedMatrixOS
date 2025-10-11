using System.Linq;
using System.Threading;
using LedMatrixOS;
using LedMatrixOS.Core;
using LedMatrixOS.Apps;
using LedMatrixOS.Hardware.Simulator;
using LedMatrixOS.Hardware.RpiLedMatrix;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

// Settings
var config = builder.Configuration.Get<AppConfig>();
int width = 254;
int height = 64;
bool useSimulator = builder.Configuration.GetValue("Matrix:UseSimulator", true);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.AddSingleton<AppManager>(_ => new AppManager(height, width));
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
    
    if (activeApp is IConfigurableApp configurableApp)
    {
        foreach (var setting in settingsUpdate)
        {
            configurableApp.UpdateSetting(setting.Key, setting.Value);
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
        isRunning = eng.IsRunning 
    }));

app.MapPost("/api/settings/brightness/{value}", (byte value, IMatrixDevice device) =>
{
    device.Brightness = value;
    return Results.Ok(new { brightness = device.Brightness });
});

app.MapPost("/api/settings/fps/{value}", (int value, RenderEngine eng) =>
{
    eng.TargetFps = value;
    return Results.Ok(new { fps = eng.TargetFps });
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

app.Run();


