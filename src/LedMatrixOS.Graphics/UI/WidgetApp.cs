using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Core.Settings;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Graphics.UI;

/// <summary>
/// Base class for apps written as a widget tree. Override <see cref="Build"/> once to return the root node; the app lays it out to the
/// matrix size, advances animations and renders it every frame. Subclasses use <see cref="Animator"/>, <see cref="Time"/> and
/// <see cref="Frame"/> instead of reading the clock, and bind widgets to <c>Poll(...)</c> data. Lives in Graphics (not Core) because the tree needs the canvas API.
/// <para>
/// The tree is built lazily on the first frame, after <c>OnActivatedAsync</c>, so an override can start its data polls before <see cref="Build"/> runs.
/// It is discarded on deactivation and rebuilt on the next activation.
/// </para>
/// </summary>
public abstract class WidgetApp : SettingsAppBase
{
    private UiHost? _host;
    private int _width = 256, _height = 64;

    /// <summary>Wall-clock source for widgets such as <see cref="Clock"/>. Replace it (before the first frame) for deterministic tests.</summary>
    public TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>Drives tweens and timelines for this app's widgets. Safe to use from <see cref="Build"/>.</summary>
    protected Animator Animator => Host.Animator;

    /// <summary>Timing of the frame being processed (Time since the engine started, Delta since the last frame).</summary>
    protected FrameContext Frame => _host?.Frame ?? default;

    /// <summary>The host running the tree (created on first use).</summary>
    protected UiHost Host => _host ??= CreateHost();

    /// <summary>The root of the built tree.</summary>
    public Node? Root => _host?.Root;

    /// <summary>Returns the root node. Called once per activation.</summary>
    protected abstract Node Build();

    public override async Task OnActivatedAsync((int height, int width) valueTuple, IConfiguration configuration, CancellationToken cancellationToken)
    {
        await base.OnActivatedAsync(valueTuple, configuration, cancellationToken);
        _height = valueTuple.height;
        _width = valueTuple.width;
        _host = null;
    }

    public override async Task OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        await base.OnDeactivatedAsync(cancellationToken);
        _host?.Animator.Clear();
        _host = null;
    }

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        Host.Update(context);
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        Host.Render(frame);
    }

    private UiHost CreateHost()
    {
        var host = new UiHost(_width, _height, Time);
        _host = host;
        // Assigned before Build runs so Build can use Animator/Time.
        host.Root = Build();
        return host;
    }
}
