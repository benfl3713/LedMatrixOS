using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps.Tube;

/// <summary>
/// The middle of the bottom strip: the station name, which gives way every few seconds to each current disruption ("CIRCLE: SEVERE DELAYS")
/// in amber, so a problem on the line announces itself without anyone asking.
/// </summary>
internal sealed class Ticker : Panel
{
    private static readonly TimeSpan Dwell = TimeSpan.FromSeconds(6);

    private readonly MarqueeLabel _name, _alert;
    private readonly Func<string?> _nameSource;
    private readonly Func<IReadOnlyList<LineStatus>?> _statuses;
    private string? _rawName, _nameText = "";
    private IReadOnlyList<LineStatus>? _lastStatuses;
    private string[] _alerts = [];
    private string _alertText = "";

    public Ticker(TextStyle nameStyle, TextStyle alertStyle, Func<string?> name, Func<IReadOnlyList<LineStatus>?> statuses)
    {
        _nameSource = name;
        _statuses = statuses;
        Grow = 1;
        _name = new MarqueeLabel(() => _nameText) { Style = nameStyle, HAlign = Align.Stretch, VAlign = Align.Center };
        _alert = new MarqueeLabel(() => _alertText) { Style = alertStyle, HAlign = Align.Stretch, VAlign = Align.Center, Visible = false };
        Add(_name);
        Add(_alert);
    }

    /// <summary>The text currently on show.</summary>
    public string Current => _alert.Visible ? _alertText : _nameText ?? "";

    public void Tick(TimeSpan time)
    {
        var raw = _nameSource();
        if (!ReferenceEquals(raw, _rawName))
        {
            _rawName = raw;
            _nameText = (raw ?? "").ToUpperInvariant();
        }

        var statuses = _statuses();
        if (!ReferenceEquals(statuses, _lastStatuses))
        {
            _lastStatuses = statuses;
            _alerts = (statuses ?? [])
                .Where(s => s.Health.NeedsAttention())
                .Select(s => $"{s.Name}: {s.Description}".ToUpperInvariant())
                .ToArray();
        }

        bool hasName = _nameText!.Length > 0;
        int count = (hasName ? 1 : 0) + _alerts.Length;
        int slot = count == 0 ? 0 : (int)(time.Ticks / Dwell.Ticks % count);
        bool showAlert = _alerts.Length > 0 && (!hasName || slot > 0);
        if (showAlert) _alertText = _alerts[hasName ? slot - 1 : slot];

        _name.Visible = !showAlert;
        _alert.Visible = showAlert;
    }
}
