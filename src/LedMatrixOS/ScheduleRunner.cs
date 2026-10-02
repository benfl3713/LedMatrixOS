using LedMatrixOS.Core;
using LedMatrixOS.Core.Scheduling;

namespace LedMatrixOS;

/// <summary>
/// Applies the schedule to the running device. Changes are edge-triggered: an app is only activated when the
/// schedule's choice changes, so a manual switch over the API sticks until the playlist next rotates.
/// </summary>
public sealed class ScheduleRunner(
    ScheduleService schedule,
    AppManager apps,
    IMatrixDevice device,
    ILogger<ScheduleRunner> logger) : BackgroundService
{
    private string? _lastAppId;
    private byte? _savedBrightness;
    private bool _brightnessOverridden;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                try { await TickAsync(stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Schedule tick failed");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        string? appId;
        PlaylistEntry? entry;
        byte? brightness;
        lock (schedule.Gate)
        {
            appId = schedule.GetActiveAppId();
            entry = schedule.GetActiveEntry();
            brightness = schedule.GetActiveBrightnessOverride();
        }

        if (appId != null && appId != _lastAppId)
        {
            _lastAppId = appId;
            if (await apps.ActivateAsync(appId, ct) && entry?.SettingsOverride is { } overrides)
            {
                foreach (var (key, value) in overrides)
                    apps.UpdateCurrentAppSetting(key, value);
            }
        }
        else if (appId == null)
        {
            _lastAppId = null;
        }

        if (brightness is { } target)
        {
            if (!_brightnessOverridden)
            {
                _savedBrightness = device.Brightness;
                _brightnessOverridden = true;
            }
            if (device.Brightness != target) device.Brightness = target;
        }
        else if (_brightnessOverridden)
        {
            if (_savedBrightness is { } saved) device.Brightness = saved;
            _brightnessOverridden = false;
        }
    }
}
