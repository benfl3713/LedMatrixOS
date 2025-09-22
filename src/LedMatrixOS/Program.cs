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

// Settings
var config = builder.Configuration.Get<AppConfig>();
int width = 254;
int height = 64;
bool useSimulator = builder.Configuration.GetValue("Matrix:UseSimulator", false);

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
await appManager.ActivateAsync("clock", CancellationToken.None);
engine.Start();
app.Lifetime.ApplicationStopping.Register(engine.Stop);

// API endpoints
app.MapGet("/api/apps", () => appManager.Apps.Select(a => new { a.Id, a.Name }));
app.MapPost("/api/apps/{id}", async (string id, CancellationToken ct) =>
{
    var ok = await appManager.ActivateAsync(id, ct);
    return ok ? Results.Ok() : Results.NotFound();
});

app.MapGet("/api/settings", (IMatrixDevice device) => new { device.Width, device.Height, device.Brightness });
app.MapPost("/api/settings/brightness/{value}", (byte value, IMatrixDevice device) =>
{
    device.Brightness = value;
    return Results.Ok(new { device.Brightness });
});

app.MapPost("/api/settings/fps/{value}", (int value, RenderEngine eng) =>
{
    eng.TargetFps = value;
    return Results.Ok(new { eng.TargetFps });
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


