using System.Numerics;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Core.Data;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Graphics.UI;

/// <summary>
/// A list of nodes built from bound items, with keyed diffing so changes animate instead of jumping:
/// <list type="bullet">
/// <item>an item whose key is still present keeps its node and slides to its new slot;</item>
/// <item>a new key gets a fresh node that expands, fades and slides in;</item>
/// <item>a key that disappeared fades out and collapses before its node is dropped.</item>
/// </list>
/// The key defaults to the item itself (use a <c>key</c> selector for an id). Items with a duplicate key are skipped.
/// The first non-empty list is shown without animation. The source is re-read every frame, but the diff only runs when the
/// list reference changes and its contents differ. When a retained key's item changes (Equals), <see cref="ItemChanged"/> is called with its node.
/// </summary>
public sealed class ListView<T> : Container
{
    private sealed class Entry(object key, T item, Node node)
    {
        public object Key = key;
        public T Item = item;
        public readonly Node Node = node;
        public readonly Tween<float> Scale = new(1f);
        public bool Removing;
        public bool Gone;
    }

    private readonly Func<IReadOnlyList<T>?> _source;
    private readonly Func<T, Node> _template;
    private readonly Func<T, object> _key;
    private readonly List<Entry> _entries = new();
    private readonly Dictionary<object, Entry> _lookup = new();
    private readonly Dictionary<Entry, float> _oldVisual = new();
    private IReadOnlyList<T>? _lastList;
    private bool _populated;
    private bool _wasScaling;
    private Orientation _orientation = Orientation.Vertical;
    private int _gap;
    private Align _crossAlign = Align.Stretch;

    public ListView(Func<IReadOnlyList<T>?> source, Func<T, Node> template, Func<T, object>? key = null)
    {
        _source = source;
        _template = template;
        _key = key ?? (static item => item!);
    }

    public ListView(ILiveData<IReadOnlyList<T>> data, Func<T, Node> template, Func<T, object>? key = null)
        : this(() => data.Value, template, key)
    {
    }

    public Orientation Orientation { get => _orientation; set => SetLayout(ref _orientation, value); }
    public int Gap { get => _gap; set => SetLayout(ref _gap, value); }
    public Align CrossAlign { get => _crossAlign; set => SetLayout(ref _crossAlign, value); }

    public TimeSpan EnterDuration { get; set; } = TimeSpan.FromMilliseconds(300);
    public TimeSpan MoveDuration { get; set; } = TimeSpan.FromMilliseconds(350);
    public TimeSpan ExitDuration { get; set; } = TimeSpan.FromMilliseconds(250);
    public Func<float, float> Easing { get; set; } = Core.Animation.Easing.OutCubic;

    /// <summary>How far (px) new items slide in from, across the list's direction.</summary>
    public int EnterOffset { get; set; } = 8;

    /// <summary>Called when a retained key's item changed, so the node can pick up the new values.</summary>
    public Action<Node, T>? ItemChanged { get; set; }

    /// <summary>Number of live items (not counting ones still animating out).</summary>
    public int Count
    {
        get
        {
            int n = 0;
            foreach (var e in _entries) if (!e.Removing) n++;
            return n;
        }
    }

    /// <summary>The node for a key, if the item is present and not leaving.</summary>
    public Node? NodeFor(object key) =>
        _lookup.TryGetValue(key, out var entry) && !entry.Removing ? entry.Node : null;

    public override void Update(FrameContext ctx)
    {
        var list = _source();
        if (list is not null && !ReferenceEquals(list, _lastList))
        {
            _lastList = list;
            if (Changed(list)) Diff(list);
        }

        base.Update(ctx);

        bool anyScaling = false;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            if (entry.Gone)
            {
                _entries.RemoveAt(i);
                if (_lookup.TryGetValue(entry.Key, out var mapped) && ReferenceEquals(mapped, entry)) _lookup.Remove(entry.Key);
                RemoveChild(entry.Node);
                continue;
            }
            if (entry.Scale.IsRunning) anyScaling = true;
        }

        // The flow size of animating entries changes every frame, including the frame in which the animation lands on its end value.
        if (anyScaling || _wasScaling) InvalidateLayout();
        _wasScaling = anyScaling;
    }

    protected override Size MeasureCore(int availW, int availH)
    {
        bool horizontal = _orientation == Orientation.Horizontal;
        int cw = Math.Max(0, availW - Padding.Horizontal), ch = Math.Max(0, availH - Padding.Vertical);
        int main = 0, cross = 0;
        int lastGap = 0;
        foreach (var entry in _entries)
        {
            entry.Node.Measure(cw, ch);
            int natural = horizontal ? entry.Node.DesiredSize.Width : entry.Node.DesiredSize.Height;
            int across = horizontal ? entry.Node.DesiredSize.Height : entry.Node.DesiredSize.Width;
            main += Flow(natural, entry.Scale.Value) + Flow(_gap, entry.Scale.Value);
            lastGap = Flow(_gap, entry.Scale.Value);
            cross = Math.Max(cross, across);
        }

        main -= lastGap;
        return horizontal
            ? new Size(main + Padding.Horizontal, cross + Padding.Vertical)
            : new Size(cross + Padding.Horizontal, main + Padding.Vertical);
    }

    protected override void ArrangeCore()
    {
        bool horizontal = _orientation == Orientation.Horizontal;
        var c = Content;
        int pos = horizontal ? c.X : c.Y;
        foreach (var entry in _entries)
        {
            var node = entry.Node;
            node.Measure(c.Width, c.Height);
            int natural = horizontal ? node.DesiredSize.Width : node.DesiredSize.Height;
            int extent = Flow(natural, entry.Scale.Value);
            if (horizontal) node.Arrange(new Rectangle(pos, c.Y, extent, c.Height), Align.Stretch, _crossAlign);
            else node.Arrange(new Rectangle(c.X, pos, c.Width, extent), _crossAlign, Align.Stretch);
            pos += extent + Flow(_gap, entry.Scale.Value);
        }
    }

    private static int Flow(int size, float scale) => (int)MathF.Round(size * scale);

    private bool Changed(IReadOnlyList<T> list)
    {
        int live = 0;
        var comparer = EqualityComparer<T>.Default;
        foreach (var entry in _entries)
        {
            if (entry.Removing) continue;
            if (live >= list.Count || !comparer.Equals(entry.Item, list[live])) return true;
            live++;
        }
        return live != list.Count;
    }

    private void Diff(IReadOnlyList<T> list)
    {
        bool horizontal = _orientation == Orientation.Horizontal;
        bool animate = _populated && Host is not null;
        if (list.Count > 0) _populated = true;

        // Where each entry is drawn now, so retained ones can slide from there.
        _oldVisual.Clear();
        foreach (var e in _entries)
            _oldVisual[e] = horizontal ? e.Node.Bounds.X + e.Node.Position.X : e.Node.Bounds.Y + e.Node.Position.Y;

        _lookup.Clear();
        foreach (var e in _entries) _lookup[e.Key] = e;

        // Build the new order: matching entries are reused, unknown keys get new entries.
        var next = new List<Entry>(list.Count);
        var comparer = EqualityComparer<T>.Default;
        var seen = new HashSet<Entry>();
        var created = new List<Entry>();
        foreach (var item in list)
        {
            var key = _key(item);
            if (_lookup.TryGetValue(key, out var entry))
            {
                if (!seen.Add(entry)) continue; // duplicate key in this list
                if (!comparer.Equals(entry.Item, item))
                {
                    entry.Item = item;
                    ItemChanged?.Invoke(entry.Node, item);
                }
                if (entry.Removing) Revive(entry);
            }
            else
            {
                entry = new Entry(key, item, _template(item));
                _lookup[key] = entry;
                seen.Add(entry);
                created.Add(entry);
            }
            next.Add(entry);
        }

        // Entries that left stay in the list (animating out) just after the entry that used to precede them.
        int anchor = -1;
        foreach (var old in _entries)
        {
            if (seen.Contains(old)) anchor = next.IndexOf(old);
            else
            {
                BeginExit(old);
                next.Insert(anchor + 1, old);
                anchor++;
            }
        }

        _entries.Clear();
        _entries.AddRange(next);
        ChildList.Clear();
        foreach (var e in _entries) ChildList.Add(e.Node);
        foreach (var e in created)
        {
            e.Node.Parent = this;
            e.Node.AttachHost(Host);
        }

        // Lay out with the new order right away to learn each slot (new entries take no space yet), then animate the difference.
        InvalidateLayout();
        if (animate) foreach (var e in created) e.Scale.Set(0f);
        ArrangeCore();
        foreach (var e in _entries)
        {
            var node = e.Node;
            if (created.Contains(e))
            {
                if (!animate) continue;
                node.Opacity = 0f;
                node.AnimateOpacity(1f, EnterDuration, Easing);
                Host!.Animator.Animate(e.Scale, 1f, EnterDuration, Easing);
                var start = horizontal ? new Vector2(0, EnterOffset) : new Vector2(EnterOffset, 0);
                node.Position = start;
                node.AnimatePosition(Vector2.Zero, EnterDuration, Easing);
            }
            else if (animate && !e.Removing && _oldVisual.TryGetValue(e, out float was))
            {
                float now = horizontal ? node.Bounds.X : node.Bounds.Y;
                if (MathF.Abs(was - now) < 0.5f) continue;
                float delta = was - now;
                node.Position = horizontal ? new Vector2(delta, node.Position.Y) : new Vector2(node.Position.X, delta);
                node.AnimatePosition(horizontal ? new Vector2(0, node.Position.Y) : new Vector2(node.Position.X, 0), MoveDuration, Easing);
            }
        }
    }

    private void BeginExit(Entry entry)
    {
        if (entry.Removing) return;
        entry.Removing = true;
        if (Host is null)
        {
            entry.Gone = true;
            return;
        }

        entry.Node.AnimateOpacity(0f, ExitDuration, Easing);
        entry.Scale.Set(entry.Scale.Value);
        Host.Animator.Animate(entry.Scale, 0f, ExitDuration, Easing, () => entry.Gone = true);
    }

    private void Revive(Entry entry)
    {
        entry.Removing = false;
        entry.Gone = false;
        entry.Node.AnimateOpacity(1f, EnterDuration, Easing);
        Host?.Animator.Animate(entry.Scale, 1f, EnterDuration, Easing);
    }
}
