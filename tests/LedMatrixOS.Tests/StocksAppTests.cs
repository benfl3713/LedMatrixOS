using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Stocks;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class StocksAppTests(ITestOutputHelper output)
{
    private const string YahooJson = """
        {"chart":{"result":[{"meta":{"symbol":"AAPL","regularMarketPrice":190.5,"chartPreviousClose":187.0},
          "indicators":{"quote":[{"close":[188.0,null,189.5,190.5]}]}}],"error":null}}
        """;

    private static StockQuote Q(string symbol, double price, double change)
    {
        var history = Enumerable.Range(0, 30).Select(i => (float)(price + Math.Sin(i / 3.0) * price * 0.01 + (change > 0 ? i : -i) * price * 0.002)).ToArray();
        return new StockQuote(symbol, price, change, history);
    }

    private static StockBoard EightTiles() => new(
    [
        Q("AAPL", 190.52, 1.84), Q("MSFT", 411.20, -0.62), Q("NVDA", 1204.8, 3.41), Q("TSLA", 177.05, -2.9),
        Q("BTC-USD", 67250.0, 0.04), Q("ETH-USD", 3120.55, 12.4), Q("^GSPC", 5123.4, 0.0), Q("GBPUSD=X", 1.2712, -0.31),
    ]);

    private static (StocksApp App, AppStage Stage) Screen(MutableLive<StockBoard>? live, int warmFrames = 30)
    {
        Fonts.Load();
        var app = new StocksApp(new FakeStockSource()) { Time = new FakeTime() };
        if (live is not null) app.UseData(live);
        var stage = new AppStage(app);
        stage.Step(33, warmFrames);
        return (app, stage);
    }

    private static void Golden(AppStage stage, string name)
    {
        var frame = stage.Snapshot();
        Preview(frame, name);
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    // ---- model -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Parse_ReadsPriceChangeAndSkipsNullCloses()
    {
        var q = YahooStockSource.Parse(YahooJson, "AAPL");

        Assert.Equal("AAPL", q.Symbol);
        Assert.Equal(190.5, q.Price);
        Assert.Equal((190.5 - 187.0) / 187.0 * 100, q.ChangePercent, 6);
        Assert.Equal([188.0f, 189.5f, 190.5f], q.History);
    }

    [Fact]
    public void Parse_NoResult_Throws() =>
        Assert.ThrowsAny<Exception>(() => YahooStockSource.Parse("""{"chart":{"result":null,"error":{"code":"Not Found"}}}""", "ZZZZ"));

    [Fact]
    public void Url_EscapesSymbols() => Assert.Contains("%5EGSPC", YahooStockSource.Url("^GSPC"));

    [Theory]
    [InlineData(190.5, "190.50")]
    [InlineData(1204.84, "1,204.8")]
    [InlineData(67250.2, "67,250")]
    [InlineData(0.5, "0.5000")]
    public void Price_Formats(double price, string expected) => Assert.Equal(expected, StockFormat.Price(price));

    [Theory]
    [InlineData(1.84, "+1.8%")]
    [InlineData(-0.62, "-0.6%")]
    [InlineData(0.0, "+0.0%")]
    [InlineData(123.4, "+123%")]
    public void Change_Formats(double percent, string expected) => Assert.Equal(expected, StockFormat.Change(percent));

    [Theory]
    [InlineData(0.04, 0)]
    [InlineData(0.06, 1)]
    [InlineData(-0.06, -1)]
    public void Direction_TreatsRoundedZeroAsFlat(double percent, int expected) => Assert.Equal(expected, StockFormat.Direction(percent));

    [Theory]
    [InlineData("BTC-USD", "BTC")]
    [InlineData("vod.l", "VOD")]
    [InlineData("^GSPC", "^GSPC")]
    [InlineData("GOOGLEX", "GOOGL")]
    public void Short_TidiesSymbols(string symbol, string expected) => Assert.Equal(expected, StockFormat.Short(symbol));

    [Fact]
    public void Fake_IsDeterministicAndSkipsMissing()
    {
        var source = new FakeStockSource("NOPE");
        var a = source.GetAsync(["AAPL", "NOPE", "MSFT"], default).Result;
        var b = source.GetAsync(["AAPL", "NOPE", "MSFT"], default).Result;

        Assert.Equal(["AAPL", "MSFT"], a.Quotes.Select(q => q.Symbol));
        Assert.Equal(a.Quotes[0].Price, b.Quotes[0].Price);
        Assert.Equal(a.Quotes[0].History, b.Quotes[0].History);
        Assert.ThrowsAny<Exception>(() => source.GetAsync(["NOPE"], default).GetAwaiter().GetResult());
    }

    // ---- app ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Identity_AndSettings()
    {
        var app = new StocksApp(new FakeStockSource());
        Assert.Equal("stocks", app.Id);
        Assert.Equal(new[] { "symbols", "pageSeconds" }, app.GetSettings().Select(s => s.Key).ToArray());
    }

    [Fact]
    public void SymbolList_IsDistinctAndCapped()
    {
        var app = new StocksApp(new FakeStockSource()) { Symbols = "aapl, AAPL;msft\nBTC-USD" };
        Assert.Equal(["aapl", "msft", "BTC-USD"], app.SymbolList);
    }

    [Fact]
    public async Task Polls_TheSource()
    {
        Fonts.Load();
        var app = new StocksApp(new FakeStockSource("MSFT")) { Time = new FakeTime(), Symbols = "AAPL,MSFT,TSLA" };
        await app.OnActivatedAsync((64, 256), new ConfigurationBuilder().Build(), CancellationToken.None);
        try
        {
            for (int i = 0; i < 100 && app.Current is null; i++) await Task.Delay(20);
            Assert.Equal(["AAPL", "TSLA"], app.Current!.Quotes.Select(q => q.Symbol));
        }
        finally { await app.OnDeactivatedAsync(CancellationToken.None); }
    }

    [Fact]
    public void Golden_EightTiles() => Golden(Screen(new MutableLive<StockBoard> { Value = EightTiles() }).Stage, "stocks_eight");

    [Fact]
    public void Golden_FewTiles()
    {
        var board = new StockBoard([Q("AAPL", 190.52, 1.84), Q("TSLA", 177.05, -2.9), Q("^GSPC", 5123.4, 0.0)]);
        Golden(Screen(new MutableLive<StockBoard> { Value = board }).Stage, "stocks_three");
    }

    [Fact]
    public void Golden_SecondPage()
    {
        var quotes = EightTiles().Quotes.Concat([Q("AMZN", 180.1, 0.9), Q("GOOG", 150.3, -1.1)]).ToArray();
        var (_, stage) = Screen(new MutableLive<StockBoard> { Value = new StockBoard(quotes) });
        stage.Step(33, 30 * 10);
        Golden(stage, "stocks_page2");
    }

    [Fact]
    public void Golden_Loading() => Golden(Screen(new MutableLive<StockBoard>()).Stage, "stocks_loading");

    [Fact]
    public void Golden_Offline() => Golden(Screen(new MutableLive<StockBoard> { Error = new HttpRequestException("x") }).Stage, "stocks_offline");

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (_, stage) = Screen(new MutableLive<StockBoard> { Value = EightTiles() });
        var run = stage.MeasureSteadyAllocation(windows: 6);
        output.WriteLine($"stocks: {run.MsPerFrame:F3} ms/frame");
        Assert.True(run.Least < 256, $"least allocation in a steady window: {run.Least} bytes");
    }

    [Fact]
    public void IsRegistered() => Assert.Contains(typeof(StocksApp), BuiltInApps.GetAll());
}
