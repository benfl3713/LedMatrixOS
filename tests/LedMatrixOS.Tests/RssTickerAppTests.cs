using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Rss;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;
using static LedMatrixOS.Tests.TubeFixtures;

namespace LedMatrixOS.Tests;

public class RssTickerAppTests(ITestOutputHelper output)
{
    private const string RssXml = """
        <?xml version="1.0"?>
        <rss version="2.0"><channel><title>BBC News - Home</title>
          <item><title>First &amp;amp; foremost</title></item>
          <item><title>  Second
              headline </title></item>
          <item><title></title></item>
        </channel></rss>
        """;

    private const string AtomXml = """
        <feed xmlns="http://www.w3.org/2005/Atom"><title>Blog</title>
          <entry><title>Hello</title></entry><entry><title>World</title></entry></feed>
        """;

    private static RssBoard Board(params string[] urls)
    {
        var source = new FakeRssSource();
        var feeds = urls.Select(u => (RssFeed?)source.GetAsync(u, default).Result).ToArray();
        return RssBoard.Merge(feeds, 5);
    }

    private static (RssTickerApp App, AppStage Stage) Screen(MutableLive<RssBoard>? live, string feeds = "x", int warmFrames = 30, double startSeconds = 0)
    {
        Fonts.Load();
        var app = new RssTickerApp(new FakeRssSource()) { Time = new FakeTime(), Feeds = feeds };
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

    private static readonly string[] ThreeFeeds = ["https://example.test/world.xml", "https://example.test/tech.xml", "https://example.test/sport.xml"];

    // ---- parsing -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Parse_Rss_DecodesAndCollapsesWhitespaceAndSkipsEmpty()
    {
        var feed = RssParser.Parse(RssXml);
        Assert.Equal("BBC News - Home", feed.Title);
        Assert.Equal(["First & foremost", "Second headline"], feed.Items);
    }

    [Fact]
    public void Parse_Atom() => Assert.Equal(["Hello", "World"], RssParser.Parse(AtomXml).Items);

    [Fact]
    public void Parse_RejectsNonFeeds() => Assert.Throws<FormatException>(() => RssParser.Parse("<html><body/></html>"));

    [Fact]
    public void Parse_RefusesDtds() => Assert.ThrowsAny<Exception>(() => RssParser.Parse("<!DOCTYPE x [<!ENTITY a 'b'>]><rss><channel><title>t</title></channel></rss>"));

    [Theory]
    [InlineData("BBC News - Home", "BBC")]
    [InlineData("  ", "RSS")]
    [InlineData("a", "A")]
    public void Tag_IsThreeUpperCaseLetters(string title, string tag) => Assert.Equal(tag, RssParser.TagOf(title));

    [Fact]
    public void Merge_InterleavesFeedsAndSkipsFailedOnes()
    {
        var source = new FakeRssSource();
        var world = source.GetAsync(ThreeFeeds[0], default).Result;
        var sport = source.GetAsync(ThreeFeeds[2], default).Result;

        var board = RssBoard.Merge([world, null, sport], perFeed: 2);

        Assert.Equal(new[] { 0, 2, 0, 2 }, board.Headlines.Select(h => h.FeedIndex).ToArray());
        Assert.Equal("WOR", board.Headlines[0].Tag);
        Assert.Equal(world.Items[1], board.Headlines[2].Title);
    }

    // ---- app ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Identity_AndSettings()
    {
        var app = new RssTickerApp(new FakeRssSource());
        Assert.Equal("rss-ticker", app.Id);
        Assert.Equal(new[] { "feeds", "perFeed", "pageSeconds", "scrollSpeed" }, app.GetSettings().Select(s => s.Key).ToArray());
    }

    [Fact]
    public void FeedUrls_MergeSettingAndConfigAndCap()
    {
        var app = new RssTickerApp(new FakeRssSource()) { Feeds = "https://a.test/x, https://b.test/y\nhttps://a.test/x" };
        Assert.Equal(["https://a.test/x", "https://b.test/y"], app.FeedUrls);
    }

    [Fact]
    public async Task Polls_TheFakeSourceForEveryConfiguredFeed()
    {
        Fonts.Load();
        var app = new RssTickerApp(new FakeRssSource()) { Time = new FakeTime(), Feeds = ThreeFeeds[0] };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Rss:Feeds"] = ThreeFeeds[1] }).Build();
        await app.OnActivatedAsync((64, 256), config, CancellationToken.None);
        try
        {
            for (int i = 0; i < 100 && app.Current is null; i++) await Task.Delay(20);
            Assert.NotNull(app.Current);
            Assert.Equal(new[] { 0, 1, 0, 1, 0, 1 }, app.Current!.Headlines.Select(h => h.FeedIndex).ToArray());
        }
        finally { await app.OnDeactivatedAsync(CancellationToken.None); }
    }

    [Fact]
    public void Golden_ThreeFeeds_FirstPage() => Golden(Screen(new MutableLive<RssBoard> { Value = Board(ThreeFeeds) }).Stage, "rss_ticker_page1");

    [Fact]
    public void Golden_ThreeFeeds_Scrolling()
    {
        var (_, stage) = Screen(new MutableLive<RssBoard> { Value = Board(ThreeFeeds) }, warmFrames: 30 * 4);
        Golden(stage, "rss_ticker_scrolling");
    }

    [Fact]
    public void Golden_SecondPage()
    {
        var (_, stage) = Screen(new MutableLive<RssBoard> { Value = Board(ThreeFeeds) }, warmFrames: 30);
        stage.Step(33, 30 * 12);
        Golden(stage, "rss_ticker_page2");
    }

    [Fact]
    public void Golden_NoFeeds() => Golden(Screen(null, feeds: "").Stage, "rss_ticker_no_feeds");

    [Fact]
    public void Golden_Loading() => Golden(Screen(new MutableLive<RssBoard>()).Stage, "rss_ticker_loading");

    [Fact]
    public void Golden_Offline() => Golden(Screen(new MutableLive<RssBoard> { Error = new HttpRequestException("offline") }).Stage, "rss_ticker_offline");

    [Fact]
    public void SteadyState_DoesNotAllocate()
    {
        var (_, stage) = Screen(new MutableLive<RssBoard> { Value = Board(ThreeFeeds) });
        // The page (and so the strings) changes every 12 s; measure windows that stay on one page.
        var run = stage.MeasureSteadyAllocation(windows: 12, beginWindow: () =>
        {
            int page = (int)(stage.Time.TotalSeconds / 12);
            return () => page == (int)(stage.Time.TotalSeconds / 12);
        });

        output.WriteLine($"rss-ticker: {run.MsPerFrame:F3} ms/frame, {run.Measured} steady windows");
        Assert.True(run.Measured >= 3);
        Assert.True(run.Least < 256, $"least allocation in a steady window: {run.Least} bytes");
    }

    [Fact]
    public void IsRegistered() => Assert.Contains(typeof(RssTickerApp), BuiltInApps.GetAll());
}
