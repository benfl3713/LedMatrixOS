using System.Collections;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Transitions;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Graphics.UI;

/// <summary>
/// Shows one page at a time and moves to the next every <see cref="Interval"/> using an <see cref="ITransition"/>.
/// Pages are either added up front (<c>new Pager { pageA, pageB }</c>) or built from bound data: <c>Bind(items, item => row)</c>
/// splits the items into pages of <see cref="PageSize"/> rows. Replaces the paging state machine in the Tube apps.
/// <para>
/// While a transition runs, both pages are drawn into pooled buffers the size of the pager's content area and the transition
/// writes the blended result, so the transition area is opaque. Idle pages draw straight onto the frame.
/// </para>
/// </summary>
public class Pager : Container, IEnumerable<Node>
{
    private readonly List<Node> _staticPages = new();
    private Func<bool>? _refresh;
    private Func<int>? _boundPageCount;
    private Func<int, Node>? _buildBound;

    private Node? _current, _next;
    private int _pageIndex, _nextIndex;
    private TimeSpan _idle, _transitionTime;
    private FrameBuffer? _from, _to, _blend;

    public Pager(int pageSize = 1, TimeSpan? interval = null, ITransition? transition = null, Func<float, float>? easing = null)
    {
        PageSize = Math.Max(1, pageSize);
        Interval = interval ?? TimeSpan.FromSeconds(8);
        Transition = transition ?? new SlideTransition(MoveDirection.Up);
        Easing = easing ?? Core.Animation.Easing.OutCubic;
        // Pages are built lazily, so a pager has no natural size: it fills whatever slot it is given.
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
    }

    /// <summary>Rows per page for bound data.</summary>
    public int PageSize { get; set; }

    /// <summary>Time each page rests before the next transition starts.</summary>
    public TimeSpan Interval { get; set; }

    public ITransition Transition { get; set; }

    /// <summary>True (the default) wraps from the last page back to the first; false stops on the last page, for one-shot sequences.</summary>
    public bool Loop { get; set; } = true;

    /// <summary>Applied to the transition's progress.</summary>
    public Func<float, float> Easing { get; set; }

    public int PageCount => _boundPageCount?.Invoke() ?? _staticPages.Count;

    /// <summary>Index of the page shown (or the one being left, during a transition).</summary>
    public int PageIndex => _pageIndex;

    public bool IsTransitioning => _next is not null;

    public Node? CurrentPage => _current;

    /// <summary>Adds a fixed page.</summary>
    public void Add(Node page) => _staticPages.Add(page);

    public IEnumerator<Node> GetEnumerator() => _staticPages.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Builds pages from a list that is re-read every frame. The pages are rebuilt (without a transition) when the list's contents change.
    /// </summary>
    public Pager Bind<T>(Func<IReadOnlyList<T>?> source, Func<T, Node> template)
    {
        var snapshot = new List<T>();
        _refresh = () =>
        {
            var list = source();
            if (list is null) return false;
            if (snapshot.Count == list.Count && SequenceEqual(snapshot, list)) return false;
            snapshot.Clear();
            for (int i = 0; i < list.Count; i++) snapshot.Add(list[i]);
            return true;
        };
        _boundPageCount = () => (snapshot.Count + PageSize - 1) / PageSize;
        _buildBound = index =>
        {
            var grid = new Grid(Enumerable.Repeat(GridLength.Star(), PageSize).ToArray(), [GridLength.Star()]);
            for (int row = 0; row < PageSize; row++)
            {
                int item = index * PageSize + row;
                if (item < snapshot.Count) grid.Add(template(snapshot[item]), row);
            }
            return grid;
        };
        return this;
    }

    public Pager Bind<T>(ILiveData<IReadOnlyList<T>> data, Func<T, Node> template) => Bind(() => data.Value, template);

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);

        if (_refresh?.Invoke() == true) Rebuild(ctx);

        if (_current is null)
        {
            if (PageCount > 0) Show(ref _current, _pageIndex = 0, ctx);
            return;
        }

        if (_next is not null)
        {
            _transitionTime += ctx.Delta;
            if (_transitionTime >= Transition.Duration) FinishTransition();
        }
        else if (PageCount > 1 && (Loop || _pageIndex < PageCount - 1))
        {
            _idle += ctx.Delta;
            if (_idle >= Interval)
            {
                _nextIndex = (_pageIndex + 1) % PageCount;
                Show(ref _next, _nextIndex, ctx);
                _transitionTime = TimeSpan.Zero;
            }
        }
    }

    protected override void ArrangeCore()
    {
        var c = Content;
        foreach (var page in ChildList)
            if (page.Visible) page.Arrange(c);
    }

    protected override void PaintChildren(FrameBuffer frame)
    {
        if (_current is null || _next is null)
        {
            base.PaintChildren(frame);
            return;
        }

        var area = _current.Bounds;
        if (area.Width <= 0 || area.Height <= 0) return;

        _from = Reuse(_from, area);
        _to = Reuse(_to, area);
        _blend = Reuse(_blend, area);
        _from.Clear(Pixel.Black);
        _to.Clear(Pixel.Black);

        // Each page paints with its own bounds cancelled out, so it lands at the buffer's origin.
        _current.Paint(_from, -_current.Bounds.X, -_current.Bounds.Y);
        _next.Paint(_to, -_next.Bounds.X, -_next.Bounds.Y);

        float duration = (float)Transition.Duration.TotalSeconds;
        float t = duration <= 0f ? 1f : Math.Clamp((float)(_transitionTime.TotalSeconds / duration), 0f, 1f);
        Transition.Render(_from, _to, _blend, Easing(t));
        frame.CopyFrom(_blend, ScreenBounds.X + area.X, ScreenBounds.Y + area.Y);
    }

    private void Show(ref Node? slot, int index, FrameContext ctx)
    {
        var page = _buildBound is not null ? _buildBound(index) : _staticPages[index];
        slot = page;
        AddChild(page);
        page.Update(ctx);
    }

    private void FinishTransition()
    {
        if (_current is not null) RemoveChild(_current);
        _current = _next;
        _next = null;
        _pageIndex = _nextIndex;
        _idle = TimeSpan.Zero;
    }

    private void Rebuild(FrameContext ctx)
    {
        if (_next is not null) RemoveChild(_next);
        if (_current is not null) RemoveChild(_current);
        _current = _next = null;

        int count = PageCount;
        if (count == 0) return;
        _pageIndex = Math.Min(_pageIndex, count - 1);
        Show(ref _current, _pageIndex, ctx);
    }

    private static FrameBuffer Reuse(FrameBuffer? buffer, Rectangle area) =>
        buffer is not null && buffer.Width == area.Width && buffer.Height == area.Height
            ? buffer
            : new FrameBuffer(area.Width, area.Height);

    private static bool SequenceEqual<T>(List<T> a, IReadOnlyList<T> b)
    {
        var comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < a.Count; i++)
            if (!comparer.Equals(a[i], b[i])) return false;
        return true;
    }
}
