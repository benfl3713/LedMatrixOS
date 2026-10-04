using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Tube;

/// <summary>Colours of the road disruption severities: distinct hues, worst brightest.</summary>
internal static class RoadColors
{
    public static readonly Pixel Severe = new(255, 40, 40);
    public static readonly Pixel Serious = new(255, 120, 0);
    public static readonly Pixel Moderate = new(255, 200, 0);
    public static readonly Pixel Minimal = new(70, 170, 235);

    public static Pixel Of(RoadSeverity severity) => severity switch
    {
        RoadSeverity.Severe => Severe,
        RoadSeverity.Serious => Serious,
        RoadSeverity.Moderate => Moderate,
        _ => Minimal,
    };

    public static string Label(RoadSeverity severity) => severity switch
    {
        RoadSeverity.Severe => "SEVERE",
        RoadSeverity.Serious => "SERIOUS",
        RoadSeverity.Moderate => "MODERATE",
        _ => "MINIMAL",
    };
}

/// <summary>One disruption as a row: severity pill, road name, and where/what as a marquee. All text is built once, when the row is.</summary>
internal sealed class RoadRow : Stack
{
    public const int RowHeight = 13;
    public const int PillWidth = 47, RoadWidth = 74;

    public RoadRow(RoadDisruption d, BoardStyles styles) : base(Orientation.Horizontal, gap: 4)
    {
        CrossAlign = Align.Center;
        Padding = new Thickness(3, 1);
        var color = RoadColors.Of(d.Severity);

        Add(new Pill(RoadColors.Label(d.Severity), color, pulse: d.Severity == RoadSeverity.Severe)
        {
            Style = new TextStyle(Fonts.ExtraSmall, TubeColors.TextOn(color), Shadow: false),
            Width = PillWidth,
            Height = 11,
            Radius = 2,
        });
        Add(new MarqueeLabel(d.Corridor) { Style = styles.Small, Width = RoadWidth });
        Add(new MarqueeLabel(Detail(d)) { Style = styles.Strip, Grow = 1, Speed = 36 });
    }

    /// <summary>"Location: comment", whichever parts exist.</summary>
    internal static string Detail(RoadDisruption d)
    {
        if (d.Location.Length == 0) return d.Comment;
        if (d.Comment.Length == 0) return d.Location;
        return $"{d.Location}: {d.Comment}";
    }
}

/// <summary>A green disc with a white tick, for the all-clear page.</summary>
internal sealed class TickBadge : Node
{
    public TickBadge(int size)
    {
        Width = size;
        Height = size;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        int r = Math.Min(bounds.Width, bounds.Height) / 2;
        int cx = bounds.X + bounds.Width / 2, cy = bounds.Y + bounds.Height / 2;
        for (int y = -r; y <= r; y++)
        {
            for (int x = -r; x <= r; x++)
            {
                float cover = Math.Clamp(r - MathF.Sqrt(x * x + y * y) + 0.5f, 0f, 1f);
                if (cover > 0f) frame.BlendPixel(cx + x, cy + y, LineHealth.GoodColor, cover);
            }
        }
        TubeGfx.DrawTick(frame, cx - 2, cy + r / 3 + 1, r, Pixel.White);
    }
}

/// <summary>
/// Road corridors for the "Corridors" picker: TfL's road list (main roads and routes), filtered by what was typed. A short list, so it is
/// shown straight away. The list is cached for an hour.
/// </summary>
internal sealed class TflRoadOptions(TflApi api, Func<DateTime>? now = null) : ISettingOptionsProvider
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);
    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);
    private (DateTime At, RoadCorridor[] Roads)? _cache;

    public bool Browse => true;

    public async Task<IReadOnlyList<SettingOption>> GetOptionsAsync(string appId, string key, string query, CancellationToken ct)
    {
        var roads = await LoadAsync(ct).ConfigureAwait(false);
        query = query.Trim();
        return roads
            .Where(r => query.Length == 0 || r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || r.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(r => new SettingOption(r.Id, r.Name))
            .ToList();
    }

    public async Task<string?> GetLabelAsync(string appId, string key, string value, CancellationToken ct)
    {
        var roads = await LoadAsync(ct).ConfigureAwait(false);
        return roads.FirstOrDefault(r => r.Id.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))?.Name ?? TflApi.PrettyCorridor(value);
    }

    private async Task<RoadCorridor[]> LoadAsync(CancellationToken ct)
    {
        if (_cache is { } hit && _now() - hit.At < Ttl) return hit.Roads;
        var roads = await api.GetRoadCorridorsAsync(ct).ConfigureAwait(false);
        _cache = (_now(), roads);
        return roads;
    }
}
