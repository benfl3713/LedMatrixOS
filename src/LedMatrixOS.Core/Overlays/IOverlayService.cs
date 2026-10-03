namespace LedMatrixOS.Core.Overlays;

/// <summary>
/// The slice of the overlay manager an app may use to raise overlays, without referencing the engine.
/// <see cref="OverlayManager"/> implements it; tests can supply a fake.
/// </summary>
public interface IOverlayService
{
    int Width { get; }
    int Height { get; }

    /// <summary>Adds an overlay; it renders from the next frame.</summary>
    void Add(IOverlay overlay);

    /// <summary>Drops every overlay with this id immediately. Returns true if any was removed.</summary>
    bool Remove(string id);
}
