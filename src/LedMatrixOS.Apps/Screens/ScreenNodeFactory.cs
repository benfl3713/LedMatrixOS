using System.Globalization;
using System.Text.Json;
using BdfFontParser;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Screens;
using LedMatrixOS.Core.Transitions;
using LedMatrixOS.Graphics;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Screens;

/// <summary>A panel that paints a background fill (optionally rounded, optionally with a border) behind its children.</summary>
internal sealed class ScreenPanel : Panel
{
    public Pixel? Background { get; set; }
    public Pixel? Border { get; set; }
    public int Radius { get; set; }

    // Row-by-row fill with inset corners; SimpleGraphics.FillRoundedRect allocates a closure per call.
    private static void FillRounded(FrameBuffer frame, Rectangle rect, int radius, Pixel color)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        float rad = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2f);
        for (int row = 0; row < rect.Height; row++)
        {
            int edge = Math.Min(row, rect.Height - 1 - row);
            int inset = 0;
            if (rad > 0 && edge < rad)
            {
                float dy = rad - edge - 0.5f;
                inset = (int)MathF.Round(rad - MathF.Sqrt(Math.Max(0f, rad * rad - dy * dy)), MidpointRounding.AwayFromZero);
            }
            frame.Fill(new Rectangle(rect.X + inset, rect.Y + row, rect.Width - inset * 2, 1), color);
        }
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        if (Border is { } border)
        {
            FillRounded(frame, bounds, Radius, border);
            if (Background is { } inner)
                FillRounded(frame, new Rectangle(bounds.X + 1, bounds.Y + 1, bounds.Width - 2, bounds.Height - 2), Math.Max(0, Radius - 1), inner);
        }
        else if (Background is { } bg) FillRounded(frame, bounds, Radius, bg);
    }
}

/// <summary>A pill whose text follows a bound source (<see cref="Pill"/> itself only takes fixed text).</summary>
internal sealed class BoundPill : Panel
{
    private readonly Pill _pill;
    private readonly Func<string> _text;

    public BoundPill(Pill pill, Func<string> text)
    {
        _pill = pill;
        _text = text;
        Add(pill);
    }

    public override void Update(FrameContext ctx)
    {
        _pill.Text = _text();
        base.Update(ctx);
    }
}

/// <summary>
/// Builds the real widget tree for a screen definition. Runs once per activation; everything dynamic is a pre-compiled delegate from
/// <see cref="BindingResolver"/>. Bad input degrades: an invalid node, prop or binding is skipped rather than throwing.
/// </summary>
internal sealed class ScreenNodeFactory(BindingResolver resolver, TimeProvider time)
{
    private int _nodes;

    private sealed record Ctx(int Depth, Orientation Parent, ItemScope? Item);

    public Node? Build(ScreenNode root) => Create(root, new Ctx(1, Orientation.Vertical, null));

    private Node? Create(ScreenNode? n, Ctx ctx)
    {
        if (n is null || string.IsNullOrEmpty(n.Type) || ctx.Depth > ScreenSchema.MaxDepth || ++_nodes > ScreenSchema.MaxNodes) return null;
        try
        {
            var p = new PropBag(n.Props);
            Node? node = n.Type switch
            {
                "stack" => MakeStack(n, p, ctx),
                "grid" => MakeGrid(n, p, ctx),
                "dock" => MakeDock(n, ctx),
                "panel" => MakePanel(n, p, ctx),
                "label" => MakeLabel(p, ctx, marquee: false),
                "marquee" => MakeLabel(p, ctx, marquee: true),
                "clock" => MakeClock(p),
                "progress" => MakeProgress(p, ctx),
                "pill" => MakePill(p, ctx),
                "divider" => MakeDivider(p, ctx),
                "sparkline" => MakeSparkline(p, ctx),
                "bar_chart" => MakeBarChart(p, ctx),
                "rolling_number" => MakeRolling(p, ctx),
                "pager" => MakePager(n, p, ctx),
                "list" => MakeList(n, p, ctx),
                _ => null,
            };
            if (node is not null) ApplyCommon(node, p);
            return node;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private List<Node> Children(ScreenNode n, Ctx ctx, Orientation parent)
    {
        var list = new List<Node>();
        if (n.Children is null) return list;
        var child = new Ctx(ctx.Depth + 1, parent, ctx.Item);
        foreach (var c in n.Children)
            if (Create(c, child) is { } node) list.Add(node);
        return list;
    }

    // ---- containers ------------------------------------------------------------------------------------------------------------

    private Node MakeStack(ScreenNode n, PropBag p, Ctx ctx)
    {
        var orientation = p.Enum("direction", "vertical") == "horizontal" ? Orientation.Horizontal : Orientation.Vertical;
        var stack = new Stack(orientation, Math.Max(0, p.Int("gap") ?? 0))
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            CrossAlign = Align.Stretch,
        };
        foreach (var c in Children(n, ctx, orientation)) stack.Add(c);
        return stack;
    }

    private Node MakeGrid(ScreenNode n, PropBag p, Ctx ctx)
    {
        var kids = Children(n, ctx, Orientation.Vertical);
        int columns = Math.Clamp(p.Int("columns") ?? 2, 1, 16);
        int rows = Math.Max(1, (kids.Count + columns - 1) / columns);
        int gap = Math.Max(0, p.Int("gap") ?? 0);
        var grid = new Grid(Enumerable.Repeat(GridLength.Star(), rows).ToArray(), Enumerable.Repeat(GridLength.Star(), columns).ToArray(), gap, gap)
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
        };
        for (int i = 0; i < kids.Count; i++) grid.Add(kids[i], i / columns, i % columns);
        return grid;
    }

    private Node MakeDock(ScreenNode n, Ctx ctx)
    {
        var child = new Ctx(ctx.Depth + 1, Orientation.Vertical, ctx.Item);
        var dock = new Dock { HAlign = Align.Stretch, VAlign = Align.Stretch };
        dock.Top = Create(n.Top, child);
        dock.Bottom = Create(n.Bottom, child);
        dock.Left = Create(n.Left, child);
        dock.Right = Create(n.Right, child);
        dock.Fill = Create(n.Fill, child);
        return dock;
    }

    private Node MakePanel(ScreenNode n, PropBag p, Ctx ctx)
    {
        var panel = new ScreenPanel
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            Background = p.Color("background"),
            Border = p.Color("border"),
            Radius = Math.Max(0, p.Int("radius") ?? 0),
        };
        foreach (var c in Children(n, ctx, Orientation.Vertical)) panel.Add(c);
        return panel;
    }

    private Node MakePager(ScreenNode n, PropBag p, Ctx ctx)
    {
        var pager = new Pager(1, TimeSpan.FromMilliseconds(Math.Clamp(p.Int("interval_ms") ?? 4000, 500, 600_000)),
            new SlideTransition(MoveDirection.Left) { Duration = TimeSpan.FromMilliseconds(500) }, Easing.InOutCubic);
        foreach (var c in Children(n, ctx, Orientation.Vertical)) pager.Add(c);
        return pager;
    }

    private Node? MakeList(ScreenNode n, PropBag p, Ctx ctx)
    {
        if (n.Item is null || !p.TryGet("source", out var sourceProp) || sourceProp.ValueKind != JsonValueKind.String) return null;
        var text = resolver.CompileText(sourceProp.GetString()!, ctx.Item);
        if (text is null) return null;

        int max = Math.Clamp(p.Int("max_items") ?? 8, 1, 32);
        int gap = Math.Max(0, p.Int("gap") ?? 0);
        var child = new Ctx(ctx.Depth + 1, Orientation.Vertical, null);

        Node? Row(string row)
        {
            ParseRow(row, out var label, out var value);
            var scope = new ItemScope(label, value, resolver.SourceForRow(value));
            return Create(n.Item, child with { Item = scope });
        }

        if (text.IsConstant)
        {
            // A fixed list is expanded once; rows that are binding keys ("Victoria|tube.victoria") stay live.
            var stack = new Stack(Orientation.Vertical, gap) { HAlign = Align.Stretch, VAlign = Align.Stretch, CrossAlign = Align.Stretch };
            int count = 0;
            foreach (var row in SplitRows(text.Constant!))
            {
                if (count >= max) break;
                if (Row(row) is { } node) { stack.Add(node); count++; }
            }
            return stack;
        }

        // A bound list rebuilds its rows (as plain text) when the source text changes.
        var get = text.Get;
        string? last = null;
        IReadOnlyList<string> rows = [];
        return new ListView<string>(() =>
            {
                var s = get();
                if (!ReferenceEquals(s, last)) { last = s; rows = SplitRows(s).Take(max).ToArray(); }
                return rows;
            }, row => Row(row) ?? new Panel(), row => row)
        { Orientation = Orientation.Vertical, Gap = gap, CrossAlign = Align.Stretch, HAlign = Align.Stretch, VAlign = Align.Stretch };
    }

    private static IEnumerable<string> SplitRows(string source) =>
        source.Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void ParseRow(string row, out string label, out string value)
    {
        int bar = row.IndexOf('|');
        if (bar < 0) { label = row; value = row; return; }
        label = row[..bar].Trim();
        value = row[(bar + 1)..].Trim();
    }

    // ---- leaves ----------------------------------------------------------------------------------------------------------------

    private Node? MakeLabel(PropBag p, Ctx ctx, bool marquee)
    {
        var text = CompileProp(p, "text", ctx);
        if (text is null) return null;
        var style = Style(p, Fonts.Small, Pixel.White);
        Node node;
        if (marquee)
        {
            var m = new MarqueeLabel { Style = style };
            if (p.Int("speed") is { } speed && speed > 0) m.Speed = speed;
            Apply(m, text);
            node = m;
        }
        else
        {
            var l = new Label { Style = style };
            Apply(l, text);
            node = l;
        }
        return node;

        static void Apply(TextNode t, CompiledText text)
        {
            if (text.IsConstant) t.Text = text.Constant!;
            else t.TextSource = text.Get;
        }
    }

    private Node MakeClock(PropBag p)
    {
        var format = p.Str("format") ?? "HH:mm";
        try { time.GetLocalNow().ToString(format, CultureInfo.InvariantCulture); }
        catch (FormatException) { format = "HH:mm"; }
        // Re-formats once a minute (once a second only if the format shows seconds), so a clock costs no allocations between ticks.
        // The stock Clock widget formats every second.
        bool seconds = format.Contains('s') || format.Contains('f') || format.Contains('F');
        long unit = seconds ? TimeSpan.TicksPerSecond : TimeSpan.TicksPerMinute;
        long last = long.MinValue;
        string cached = "";
        return new Label
        {
            Style = Style(p, Fonts.Small, Pixel.White),
            TextSource = () =>
            {
                var now = time.GetLocalNow();
                long tick = now.Ticks / unit;
                if (tick != last) { last = tick; cached = now.ToString(format, CultureInfo.InvariantCulture); }
                return cached;
            },
        };
    }

    private Node? MakeProgress(PropBag p, Ctx ctx)
    {
        if (!p.TryGet("value", out var v)) return null;
        var read = resolver.CompileNumber(v, ctx.Item);
        if (read is null) return null;
        float max = Math.Max(1, p.Int("max") ?? 100);
        var bar = new ProgressBar { ValueSource = () => { float x = read(); return float.IsFinite(x) ? Math.Clamp(x / max, 0f, 1f) : 0f; } };
        if (p.Color("color") is { } fill) bar.Fill = fill;
        if (p.Color("background") is { } bg) bar.Background = bg;
        return bar;
    }

    private Node? MakePill(PropBag p, Ctx ctx)
    {
        var text = CompileProp(p, "text", ctx);
        if (text is null) return null;
        var pill = new Pill(text.IsConstant ? text.Constant! : "", p.Color("background") ?? new Pixel(60, 60, 70))
        {
            Style = Style(p, Fonts.QuiteSmall, Pixel.White, shadow: false),
            Padding = new Thickness(3, 1),
        };
        if (text.IsConstant) return pill;
        return new BoundPill(pill, text.Get);
    }

    private Node MakeDivider(PropBag p, Ctx ctx)
    {
        // A divider runs across a vertical stack and down a horizontal one.
        var divider = new Divider(ctx.Parent == Orientation.Horizontal ? Orientation.Vertical : Orientation.Horizontal);
        if (p.Color("color") is { } c) divider.Color = c;
        return divider;
    }

    private Node? MakeSparkline(PropBag p, Ctx ctx)
    {
        if (!p.TryGet("values", out var v) || resolver.CompileSeries(v, ctx.Item) is not { } series) return null;
        var node = new Sparkline { Source = series };
        if (p.Color("color") is { } c) node.Line = c;
        return node;
    }

    private Node? MakeBarChart(PropBag p, Ctx ctx)
    {
        if (!p.TryGet("values", out var v) || resolver.CompileSeries(v, ctx.Item) is not { } series) return null;
        var node = new BarChart { Source = series };
        if (p.Color("color") is { } c) node.Color = c;
        return node;
    }

    private Node? MakeRolling(PropBag p, Ctx ctx)
    {
        if (!p.TryGet("value", out var v)) return null;
        var read = resolver.CompileNumber(v, ctx.Item);
        if (read is null) return null;
        return new RollingNumber(() => { float x = read(); return float.IsFinite(x) ? (int)Math.Clamp(MathF.Round(x), -999_999_999f, 999_999_999f) : 0; })
        {
            Style = Style(p, Fonts.Big, Pixel.White),
        };
    }

    // ---- shared ----------------------------------------------------------------------------------------------------------------

    private CompiledText? CompileProp(PropBag p, string name, Ctx ctx)
    {
        if (!p.TryGet(name, out var v)) return new CompiledText("");
        switch (v.ValueKind)
        {
            case JsonValueKind.String: return resolver.CompileText(v.GetString()!, ctx.Item);
            case JsonValueKind.Number: return new CompiledText(v.GetRawText());
            case JsonValueKind.Object:
                foreach (var prop in v.EnumerateObject())
                    if (prop.Name == "bind" && prop.Value.ValueKind == JsonValueKind.String)
                        return resolver.CompileText("{" + prop.Value.GetString() + "}", ctx.Item);
                return null;
            default: return null;
        }
    }

    private static TextStyle Style(PropBag p, BdfFont defaultFont, Pixel defaultColor, bool shadow = false)
    {
        var font = p.Enum("font", "") switch
        {
            "big" => Fonts.Big,
            "small" => Fonts.Small,
            "quitesmall" => Fonts.QuiteSmall,
            "extrasmall" => Fonts.ExtraSmall,
            _ => defaultFont,
        };
        return new TextStyle(font, p.Color("color") ?? defaultColor, p.Bool("shadow") ?? shadow);
    }

    private static void ApplyCommon(Node node, PropBag p)
    {
        if (p.Int("width") is { } w && w >= 0) node.Width = w;
        if (p.Int("height") is { } h && h >= 0) node.Height = h;
        if (p.Int("margin") is { } m && m >= 0) node.Margin = new Thickness(m);
        if (p.Int("padding") is { } pad && pad >= 0) node.Padding = new Thickness(pad);
        if (p.Int("grow") is { } g && g >= 0) node.Grow = g;
        switch (p.Enum("halign", ""))
        {
            case "left": node.HAlign = Align.Start; break;
            case "center": node.HAlign = Align.Center; break;
            case "right": node.HAlign = Align.End; break;
        }
        switch (p.Enum("valign", ""))
        {
            case "top": node.VAlign = Align.Start; break;
            case "middle": node.VAlign = Align.Center; break;
            case "bottom": node.VAlign = Align.End; break;
        }
        if (p.Bool("visible") is { } visible) node.Visible = visible;
    }

    /// <summary>Typed, forgiving access to a node's props: wrong types and bad values read as "not set".</summary>
    private sealed class PropBag(Dictionary<string, JsonElement>? props)
    {
        public bool TryGet(string name, out JsonElement value)
        {
            if (props is not null && props.TryGetValue(name, out value)) return true;
            value = default;
            return false;
        }

        public int? Int(string name) => TryGet(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
        public bool? Bool(string name) => TryGet(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
        public string? Str(string name) => TryGet(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        /// <summary>Lower-cased enum text, or <paramref name="fallback"/> when unset.</summary>
        public string Enum(string name, string fallback) => Str(name)?.ToLowerInvariant().Replace("_", "") ?? fallback;

        public Pixel? Color(string name) => Str(name) is { } s ? ParseColor(s) : null;
    }

    /// <summary>Parses #RGB, #RRGGBB or #RRGGBBAA (alpha is ignored: the matrix has no alpha channel).</summary>
    internal static Pixel? ParseColor(string s)
    {
        if (s.Length < 4 || s[0] != '#') return null;
        var hex = s[1..];
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => new string(c, 2)));
        if (hex.Length is not (6 or 8)) return null;
        return byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)
            ? new Pixel(r, g, b) : null;
    }
}
