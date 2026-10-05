using LedMatrixOS.Apps.Stocks;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LedMatrixOS.Apps;

/// <summary>
/// Stock, index, FX and crypto tiles (Yahoo Finance's public chart data, no key). Each tile shows the symbol, the change since the previous
/// close, the price and an intraday sparkline, in green when up and red when down. Eight tiles fit on screen; more symbols page.
/// </summary>
public sealed class StocksApp : WidgetApp
{
    private enum State { Loading, Ready, Offline }

    public const int Columns = 4, TileRows = 2, TilesPerPage = Columns * TileRows;
    public const int MaxSymbols = 24;

    public static readonly Pixel Up = new(60, 225, 110), Down = new(255, 80, 80), Flat = new(170, 170, 180);

    /// <summary>One tile of the grid. It is restyled when its quote changes, never per frame.</summary>
    private sealed class Tile
    {
        private readonly Block _background = new(new Pixel(12, 12, 16));
        private readonly Block _edge = new(Flat, width: 2);
        private readonly Label _symbol, _change, _price;
        private readonly Sparkline _spark;
        private readonly TextStyle[] _changeStyles = new TextStyle[3], _priceStyles = new TextStyle[3];

        public Tile()
        {
            var symbolStyle = new TextStyle(Fonts.QuiteSmall, new Pixel(190, 200, 225), Shadow: false);
            for (int i = 0; i < 3; i++)
            {
                var color = Color(i - 1);
                _changeStyles[i] = new TextStyle(Fonts.QuiteSmall, color, Shadow: false);
                _priceStyles[i] = new TextStyle(Fonts.Small, Pixel.White, Shadow: false);
            }

            _symbol = new Label("") { Style = symbolStyle, HAlign = Align.Start };
            _change = new Label("") { Style = _changeStyles[1], HAlign = Align.End };
            _price = new Label("") { Style = _priceStyles[1], HAlign = Align.Start };
            _spark = new Sparkline { NaturalHeight = 9, FillBrightness = 0.25f, ShowLatest = false, Grow = 1, Line = Flat };

            Root = new Panel
            {
                HAlign = Align.Stretch,
                VAlign = Align.Stretch,
                Grow = 1,
                Children =
                {
                    _background,
                    new Stack(Orientation.Horizontal) { HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { _edge } },
                    new Stack(Orientation.Vertical, gap: 0)
                    {
                        HAlign = Align.Stretch,
                        VAlign = Align.Stretch,
                        Padding = new Thickness(5, 2, 3, 1),
                        Children =
                        {
                            new Panel { HAlign = Align.Stretch, Height = 8, Children = { _symbol, _change } },
                            _price,
                            _spark,
                        },
                    },
                },
            };
        }

        public Node Root { get; }

        public static Pixel Color(int direction) => direction > 0 ? Up : direction < 0 ? Down : Flat;

        public void Set(StockQuote? quote)
        {
            Root.Visible = quote is not null;
            if (quote is null) return;

            int dir = StockFormat.Direction(quote.ChangePercent);
            var color = Color(dir);
            _symbol.Text = StockFormat.Short(quote.Symbol);
            _change.Text = StockFormat.Change(quote.ChangePercent);
            _change.Style = _changeStyles[dir + 1];
            _price.Text = StockFormat.Price(quote.Price);
            _price.Style = _priceStyles[dir + 1];
            _edge.Color = color.WithBrightness(0.8f);
            _background.Color = dir == 0 ? new Pixel(12, 12, 16) : color.WithBrightness(0.07f);
            _spark.Values = quote.History;
            _spark.Line = color;
        }
    }

    private IStockSource _source;
    private volatile ILiveData<StockBoard>? _data;
    private CancellationTokenSource? _pollCts;
    private bool _active;

    private StockBoard? _boardFor;
    private int _pageApplied = -1;
    private State _state = State.Loading;

    private Tile[] _tiles = null!;
    private Node _content = null!;
    private Label _message = null!;

    public override string Id => "stocks";
    public override string Name => "Stocks";
    public override int FrameRate => 30;

    [Setting("Symbols", Description = "Tickers separated by commas: shares (AAPL), indices (^GSPC), FX (GBPUSD=X) or crypto (BTC-USD).")]
    public string Symbols { get; set; } = "AAPL,MSFT,NVDA,TSLA,BTC-USD,ETH-USD,^GSPC,GBPUSD=X";

    [Setting("Page Seconds", Description = "Seconds each page of eight tiles stays up when there are more.", Min = 4, Max = 60)]
    public int PageSeconds { get; set; } = 10;

    [ActivatorUtilitiesConstructor]
    public StocksApp(HttpClient http) : this(new YahooStockSource(http)) { }

    public StocksApp(IStockSource source) => _source = source;

    public IReadOnlyList<string> SymbolList => Symbols
        .Split([',', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(MaxSymbols)
        .ToArray();

    public StockBoard? Current => _data?.Value;

    // ---- lifecycle ---------------------------------------------------------------------------------------------------------------

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken ct)
    {
        await base.OnActivatedAsync(dimensions, configuration, ct);
        if (string.Equals(configuration["Stocks:Source"], "Fake", StringComparison.OrdinalIgnoreCase)) _source = new FakeStockSource();
        _active = true;
        StartPolling();
    }

    public override async Task OnDeactivatedAsync(CancellationToken ct)
    {
        _active = false;
        CancelPoll(ref _pollCts);
        await base.OnDeactivatedAsync(ct);
    }

    protected override void OnSettingChanged(string key)
    {
        if (_active && key == "symbols") StartPolling();
        _pageApplied = -1;
    }

    private void StartPolling()
    {
        var symbols = SymbolList;
        _boardFor = null;
        if (symbols.Count == 0)
        {
            CancelPoll(ref _pollCts);
            _data = null;
            return;
        }

        _data = RestartPoll(ref _pollCts, TimeSpan.FromMinutes(5), ct => _source.GetAsync(symbols, ct));
    }

    /// <summary>Test seam: replaces the polled data with a fixed source (call before the first frame).</summary>
    internal void UseData(ILiveData<StockBoard>? data)
    {
        CancelPoll(ref _pollCts);
        _data = data;
    }

    // ---- view --------------------------------------------------------------------------------------------------------------------

    protected override Node Build()
    {
        _tiles = new Tile[TilesPerPage];
        var grid = new Stack(Orientation.Vertical, gap: 1) { HAlign = Align.Stretch, VAlign = Align.Stretch };
        for (int r = 0; r < TileRows; r++)
        {
            var row = new Stack(Orientation.Horizontal, gap: 1) { HAlign = Align.Stretch, VAlign = Align.Stretch, Grow = 1 };
            for (int c = 0; c < Columns; c++)
            {
                var tile = new Tile();
                _tiles[r * Columns + c] = tile;
                row.Add(tile.Root);
            }

            grid.Add(row);
        }

        _content = grid;
        _message = new Label(() => _state == State.Offline ? "Offline, retrying" : "Loading quotes")
        {
            Style = new TextStyle(Fonts.Small, new Pixel(255, 176, 0), Shadow: false),
            HAlign = Align.Center,
            VAlign = Align.Center,
        };
        _content.Visible = false;
        _state = State.Loading;
        _boardFor = null;
        _pageApplied = -1;
        return new Panel { HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { _content, _message } };
    }

    // ---- per frame ---------------------------------------------------------------------------------------------------------------

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;   // builds the tree on the first frame
        var data = _data;
        var board = data?.Value;
        var state = board is not null ? State.Ready : data?.Error is not null ? State.Offline : State.Loading;

        if (!ReferenceEquals(board, _boardFor))
        {
            _boardFor = board;
            _pageApplied = -1;
        }

        int page = 0;
        if (board is { Quotes.Count: > TilesPerPage })
        {
            int pages = (board.Quotes.Count + TilesPerPage - 1) / TilesPerPage;
            page = (int)(context.Time.TotalSeconds / Math.Max(1, PageSeconds)) % pages;
        }

        if (page != _pageApplied)
        {
            _pageApplied = page;
            for (int i = 0; i < TilesPerPage; i++)
            {
                int index = page * TilesPerPage + i;
                _tiles[i].Set(board is not null && index < board.Quotes.Count ? board.Quotes[index] : null);
            }
        }

        if (state != _state)
        {
            _state = state;
            _content.Visible = state == State.Ready;
            _message.Visible = state != State.Ready;
        }

        base.Update(context, cancellationToken);
    }
}
