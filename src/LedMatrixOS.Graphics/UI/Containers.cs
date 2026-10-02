using System.Collections;
using LedMatrixOS.Core;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Graphics.UI;

/// <summary>
/// The children of a <see cref="Panel"/> or <see cref="Stack"/>. Exists so children can be added inside an object initializer
/// next to other properties: <c>new Stack { Gap = 2, Children = { a, b } }</c>.
/// </summary>
public sealed class NodeCollection : IReadOnlyList<Node>
{
    private readonly Container _owner;
    private readonly List<Node> _list;

    internal NodeCollection(Container owner, List<Node> list)
    {
        _owner = owner;
        _list = list;
    }

    public int Count => _list.Count;
    public Node this[int index] => _list[index];

    public void Add(Node child) => _owner.AddFromCollection(child);

    public IEnumerator<Node> GetEnumerator() => _list.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// A node that owns children. By default it overlays them all on its content area (each one aligned by its own HAlign/VAlign).
/// </summary>
public abstract class Container : Node
{
    private readonly List<Node> _children = new();

    public IReadOnlyList<Node> Children => _children;

    protected List<Node> ChildList => _children;

    internal void AddFromCollection(Node child) => AddChild(child);

    protected void AddChild(Node child)
    {
        if (child.Parent is not null) throw new InvalidOperationException("The node already has a parent");
        child.Parent = this;
        _children.Add(child);
        child.AttachHost(Host);
        InvalidateLayout();
    }

    protected bool RemoveChild(Node child)
    {
        if (!_children.Remove(child)) return false;
        child.Parent = null;
        child.AttachHost(null);
        InvalidateLayout();
        return true;
    }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        // Children may add or remove nodes while updating, so re-check the bounds each step.
        for (int i = 0; i < _children.Count; i++) _children[i].Update(ctx);
    }

    protected override Size MeasureCore(int availW, int availH)
    {
        int w = 0, h = 0;
        int cw = Math.Max(0, availW - Padding.Horizontal), ch = Math.Max(0, availH - Padding.Vertical);
        foreach (var child in _children)
        {
            child.Measure(cw, ch);
            w = Math.Max(w, child.DesiredSize.Width);
            h = Math.Max(h, child.DesiredSize.Height);
        }
        return new Size(w + Padding.Horizontal, h + Padding.Vertical);
    }

    protected override void ArrangeCore()
    {
        var c = Content;
        foreach (var child in _children)
            if (child.Visible) child.Arrange(c, Align.Start, Align.Start);
    }

    protected override void PaintChildren(FrameBuffer frame)
    {
        for (int i = 0; i < _children.Count; i++)
            _children[i].Paint(frame, ScreenBounds.X, ScreenBounds.Y);
    }

    internal override void AttachHost(UiHost? host)
    {
        base.AttachHost(host);
        foreach (var child in _children) child.AttachHost(host);
    }
}

/// <summary>
/// Overlays its children on one content area, which makes it the single-child anchor container:
/// <c>new Panel { new Label("x") { HAlign = Align.End, VAlign = Align.Center } }</c>. Children default to their own size at the top left.
/// </summary>
public class Panel : Container, IEnumerable<Node>
{
    private NodeCollection? _nodes;

    public new NodeCollection Children => _nodes ??= new NodeCollection(this, ChildList);

    public void Add(Node child) => AddChild(child);
    public bool Remove(Node child) => RemoveChild(child);

    public IEnumerator<Node> GetEnumerator() => ChildList.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Lays children out in a row or column. Children with <see cref="Node.Grow"/> above zero share what the others leave over
/// (like CSS <c>flex: n</c>, their own size does not count); the rest keep their desired size.
/// </summary>
public class Stack : Container, IEnumerable<Node>
{
    private Orientation _orientation;
    private int _gap;
    private Align _crossAlign = Align.Start;
    private NodeCollection? _nodes;

    public Stack(Orientation orientation = Orientation.Vertical, int gap = 0)
    {
        _orientation = orientation;
        _gap = gap;
    }

    public new NodeCollection Children => _nodes ??= new NodeCollection(this, ChildList);

    public Orientation Orientation { get => _orientation; set => SetLayout(ref _orientation, value); }
    public int Gap { get => _gap; set => SetLayout(ref _gap, value); }

    /// <summary>Default placement across the stacking direction; a child's own HAlign/VAlign wins.</summary>
    public Align CrossAlign { get => _crossAlign; set => SetLayout(ref _crossAlign, value); }

    public void Add(Node child) => AddChild(child);
    public bool Remove(Node child) => RemoveChild(child);

    public IEnumerator<Node> GetEnumerator() => ChildList.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    protected override Size MeasureCore(int availW, int availH)
    {
        bool horizontal = _orientation == Orientation.Horizontal;
        int cw = Math.Max(0, availW - Padding.Horizontal), ch = Math.Max(0, availH - Padding.Vertical);
        int main = 0, cross = 0, count = 0;
        foreach (var child in ChildList)
        {
            if (!child.Visible) continue;
            child.Measure(cw, ch);
            count++;
            int childMain = horizontal ? child.DesiredSize.Width : child.DesiredSize.Height;
            int childCross = horizontal ? child.DesiredSize.Height : child.DesiredSize.Width;
            if (child.Grow <= 0f) main += childMain;
            cross = Math.Max(cross, childCross);
        }

        main += Math.Max(0, count - 1) * _gap;
        return horizontal
            ? new Size(main + Padding.Horizontal, cross + Padding.Vertical)
            : new Size(cross + Padding.Horizontal, main + Padding.Vertical);
    }

    protected override void ArrangeCore()
    {
        bool horizontal = _orientation == Orientation.Horizontal;
        var c = Content;
        int mainAvail = horizontal ? c.Width : c.Height;

        int fixedTotal = 0, count = 0;
        float growTotal = 0f;
        foreach (var child in ChildList)
        {
            if (!child.Visible) continue;
            count++;
            if (child.Grow > 0f) growTotal += child.Grow;
            else fixedTotal += horizontal ? child.DesiredSize.Width : child.DesiredSize.Height;
        }

        int leftover = Math.Max(0, mainAvail - fixedTotal - Math.Max(0, count - 1) * _gap);
        int pos = horizontal ? c.X : c.Y;
        float cumulative = 0f;
        int given = 0;

        foreach (var child in ChildList)
        {
            if (!child.Visible) continue;
            int extent;
            if (child.Grow > 0f)
            {
                // Cumulative rounding hands out exactly `leftover` pixels in total.
                cumulative += child.Grow;
                int target = (int)MathF.Round(leftover * cumulative / growTotal);
                extent = target - given;
                given = target;
            }
            else extent = horizontal ? child.DesiredSize.Width : child.DesiredSize.Height;

            if (horizontal)
            {
                child.Arrange(new Rectangle(pos, c.Y, extent, c.Height), Align.Stretch, _crossAlign);
            }
            else
            {
                child.Arrange(new Rectangle(c.X, pos, c.Width, extent), _crossAlign, Align.Stretch);
            }

            pos += extent + _gap;
        }
    }
}

/// <summary>
/// Rows and columns of fixed (px), star (<c>*</c>, <c>2*</c>) or <c>auto</c> size. Place children with
/// <c>Add(node, row, column)</c> (optionally with spans); star tracks share the space fixed and auto tracks leave.
/// </summary>
public class Grid : Container, IEnumerable<Node>
{
    private readonly GridLength[] _rows;
    private readonly GridLength[] _columns;
    private readonly int[] _rowSizes;
    private readonly int[] _columnSizes;
    private int _rowGap, _columnGap;

    public Grid(string rows, string columns, int rowGap = 0, int columnGap = 0)
        : this(GridLength.ParseList(rows), GridLength.ParseList(columns), rowGap, columnGap)
    {
    }

    public Grid(GridLength[] rows, GridLength[] columns, int rowGap = 0, int columnGap = 0)
    {
        _rowGap = rowGap;
        _columnGap = columnGap;
        _rows = rows.Length == 0 ? [GridLength.Star()] : rows;
        _columns = columns.Length == 0 ? [GridLength.Star()] : columns;
        _rowSizes = new int[_rows.Length];
        _columnSizes = new int[_columns.Length];
    }

    public int RowGap { get => _rowGap; set => SetLayout(ref _rowGap, value); }
    public int ColumnGap { get => _columnGap; set => SetLayout(ref _columnGap, value); }

    public void Add(Node child, int row = 0, int column = 0, int rowSpan = 1, int columnSpan = 1)
    {
        child.GridRow = row;
        child.GridColumn = column;
        child.GridRowSpan = rowSpan;
        child.GridColumnSpan = columnSpan;
        AddChild(child);
    }

    public bool Remove(Node child) => RemoveChild(child);

    public IEnumerator<Node> GetEnumerator() => ChildList.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    protected override Size MeasureCore(int availW, int availH)
    {
        int cw = Math.Max(0, availW - Padding.Horizontal), ch = Math.Max(0, availH - Padding.Vertical);
        foreach (var child in ChildList) child.Measure(cw, ch);

        // With no known extent, star tracks collapse to nothing; the parent's slot decides their real size.
        ComputeTracks(_rows, _rowSizes, 0, _rowGap, horizontal: false, includeStars: false);
        ComputeTracks(_columns, _columnSizes, 0, _columnGap, horizontal: true, includeStars: false);
        return new Size(Sum(_columnSizes, _columnGap) + Padding.Horizontal, Sum(_rowSizes, _rowGap) + Padding.Vertical);
    }

    protected override void ArrangeCore()
    {
        var c = Content;
        ComputeTracks(_rows, _rowSizes, c.Height, _rowGap, horizontal: false, includeStars: true);
        ComputeTracks(_columns, _columnSizes, c.Width, _columnGap, horizontal: true, includeStars: true);

        foreach (var child in ChildList)
        {
            if (!child.Visible) continue;
            int r0 = Math.Clamp(child.GridRow, 0, _rows.Length - 1);
            int c0 = Math.Clamp(child.GridColumn, 0, _columns.Length - 1);
            int r1 = Math.Clamp(r0 + child.GridRowSpan - 1, r0, _rows.Length - 1);
            int c1 = Math.Clamp(c0 + child.GridColumnSpan - 1, c0, _columns.Length - 1);

            int x = c.X + Offset(_columnSizes, _columnGap, c0);
            int y = c.Y + Offset(_rowSizes, _rowGap, r0);
            int w = Offset(_columnSizes, _columnGap, c1) + _columnSizes[c1] - Offset(_columnSizes, _columnGap, c0);
            int h = Offset(_rowSizes, _rowGap, r1) + _rowSizes[r1] - Offset(_rowSizes, _rowGap, r0);
            child.Arrange(new Rectangle(x, y, w, h), Align.Stretch, Align.Stretch);
        }
    }

    private void ComputeTracks(GridLength[] defs, int[] sizes, int available, int gap, bool horizontal, bool includeStars)
    {
        int used = Math.Max(0, defs.Length - 1) * gap;
        float starTotal = 0f;
        for (int i = 0; i < defs.Length; i++)
        {
            switch (defs[i].Unit)
            {
                case GridUnit.Pixel:
                    sizes[i] = (int)defs[i].Value;
                    break;
                case GridUnit.Auto:
                    sizes[i] = 0;
                    foreach (var child in ChildList)
                    {
                        if (!child.Visible) continue;
                        int index = horizontal ? child.GridColumn : child.GridRow;
                        int span = horizontal ? child.GridColumnSpan : child.GridRowSpan;
                        if (index == i && span == 1)
                            sizes[i] = Math.Max(sizes[i], horizontal ? child.DesiredSize.Width : child.DesiredSize.Height);
                    }
                    break;
                default:
                    sizes[i] = 0;
                    starTotal += defs[i].Value;
                    break;
            }

            if (defs[i].Unit != GridUnit.Star) used += sizes[i];
        }

        if (!includeStars || starTotal <= 0f) return;

        int leftover = Math.Max(0, available - used);
        float cumulative = 0f;
        int given = 0;
        for (int i = 0; i < defs.Length; i++)
        {
            if (defs[i].Unit != GridUnit.Star) continue;
            cumulative += defs[i].Value;
            int target = (int)MathF.Round(leftover * cumulative / starTotal);
            sizes[i] = target - given;
            given = target;
        }
    }

    private static int Sum(int[] sizes, int gap)
    {
        int sum = Math.Max(0, sizes.Length - 1) * gap;
        foreach (var s in sizes) sum += s;
        return sum;
    }

    private static int Offset(int[] sizes, int gap, int index)
    {
        int offset = 0;
        for (int i = 0; i < index; i++) offset += sizes[i] + gap;
        return offset;
    }
}

/// <summary>
/// Docks nodes to the edges of its content area in the order Top, Bottom, Left, Right, then gives what is left to Fill.
/// Top/Bottom take their desired height and the full width; Left/Right take their desired width.
/// </summary>
public class Dock : Container
{
    private Node? _top, _bottom, _left, _right, _fill;

    public Node? Top { get => _top; set => Replace(ref _top, value); }
    public Node? Bottom { get => _bottom; set => Replace(ref _bottom, value); }
    public Node? Left { get => _left; set => Replace(ref _left, value); }
    public Node? Right { get => _right; set => Replace(ref _right, value); }
    public Node? Fill { get => _fill; set => Replace(ref _fill, value); }

    private void Replace(ref Node? slot, Node? value)
    {
        if (ReferenceEquals(slot, value)) return;
        if (slot is not null) RemoveChild(slot);
        slot = value;
        if (value is not null) AddChild(value);
    }

    protected override Size MeasureCore(int availW, int availH)
    {
        int cw = Math.Max(0, availW - Padding.Horizontal), ch = Math.Max(0, availH - Padding.Vertical);
        foreach (var child in ChildList) child.Measure(cw, ch);

        var t = Desired(_top); var b = Desired(_bottom); var l = Desired(_left); var r = Desired(_right); var f = Desired(_fill);
        int w = Math.Max(Math.Max(t.Width, b.Width), l.Width + f.Width + r.Width);
        int h = t.Height + b.Height + Math.Max(Math.Max(l.Height, r.Height), f.Height);
        return new Size(w + Padding.Horizontal, h + Padding.Vertical);
    }

    protected override void ArrangeCore()
    {
        var rest = Content;

        if (_top is { Visible: true } top)
        {
            int h = Math.Min(top.DesiredSize.Height, rest.Height);
            top.Arrange(new Rectangle(rest.X, rest.Y, rest.Width, h));
            rest = new Rectangle(rest.X, rest.Y + h, rest.Width, rest.Height - h);
        }

        if (_bottom is { Visible: true } bottom)
        {
            int h = Math.Min(bottom.DesiredSize.Height, rest.Height);
            bottom.Arrange(new Rectangle(rest.X, rest.Bottom - h, rest.Width, h));
            rest = new Rectangle(rest.X, rest.Y, rest.Width, rest.Height - h);
        }

        if (_left is { Visible: true } left)
        {
            int w = Math.Min(left.DesiredSize.Width, rest.Width);
            left.Arrange(new Rectangle(rest.X, rest.Y, w, rest.Height));
            rest = new Rectangle(rest.X + w, rest.Y, rest.Width - w, rest.Height);
        }

        if (_right is { Visible: true } right)
        {
            int w = Math.Min(right.DesiredSize.Width, rest.Width);
            right.Arrange(new Rectangle(rest.Right - w, rest.Y, w, rest.Height));
            rest = new Rectangle(rest.X, rest.Y, rest.Width - w, rest.Height);
        }

        if (_fill is { Visible: true } fill) fill.Arrange(rest);
    }

    private static Size Desired(Node? node) => node is { Visible: true } ? node.DesiredSize : default;
}
