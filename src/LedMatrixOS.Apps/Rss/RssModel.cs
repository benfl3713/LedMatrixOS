using System.Net;
using System.Xml;
using System.Xml.Linq;
using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.Rss;

/// <summary>One parsed feed: its title and its newest item titles, newest first.</summary>
public sealed record RssFeed(string Title, IReadOnlyList<string> Items);

public sealed record RssHeadline(int FeedIndex, string Tag, string Title);

/// <summary>The headlines of every feed, interleaved so each feed gets a turn. A class, so the app can tell a new board by reference.</summary>
public sealed class RssBoard(IReadOnlyList<RssHeadline> headlines, IReadOnlyList<string> feedTags)
{
    public IReadOnlyList<RssHeadline> Headlines { get; } = headlines;

    /// <summary>Short tag per feed, indexed by <see cref="RssHeadline.FeedIndex"/>.</summary>
    public IReadOnlyList<string> FeedTags { get; } = feedTags;

    /// <summary>Round-robin merge of the feeds, taking at most <paramref name="perFeed"/> items from each. A null feed (one that failed) is skipped.</summary>
    public static RssBoard Merge(IReadOnlyList<RssFeed?> feeds, int perFeed)
    {
        var tags = new string[feeds.Count];
        for (int i = 0; i < feeds.Count; i++) tags[i] = feeds[i] is { } f ? RssParser.TagOf(f.Title) : "";

        var list = new List<RssHeadline>();
        for (int round = 0; round < perFeed; round++)
            for (int i = 0; i < feeds.Count; i++)
                if (feeds[i] is { } feed && round < feed.Items.Count)
                    list.Add(new RssHeadline(i, tags[i], feed.Items[round]));

        return new RssBoard(list, tags);
    }
}

public interface IRssSource
{
    /// <summary>Fetches and parses one feed.</summary>
    Task<RssFeed> GetAsync(string url, CancellationToken cancellationToken);
}

public static class RssParser
{
    private const int MaxItems = 20;

    /// <summary>Reads RSS 2.0 (<c>item</c>) or Atom (<c>entry</c>) and returns the item titles in document order.</summary>
    public static RssFeed Parse(string xml)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        var doc = XDocument.Load(reader);

        var channel = doc.Descendants().FirstOrDefault(e => e.Name.LocalName is "channel" or "feed");
        if (channel is null) throw new FormatException("Not an RSS or Atom feed");

        string title = Clean(channel.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value);
        var items = doc.Descendants()
            .Where(e => e.Name.LocalName is "item" or "entry")
            .Select(e => Clean(e.Elements().FirstOrDefault(c => c.Name.LocalName == "title")?.Value))
            .Where(t => t.Length > 0)
            .Take(MaxItems)
            .ToArray();
        return new RssFeed(title, items);
    }

    /// <summary>Decodes leftover HTML entities and collapses whitespace.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var decoded = WebUtility.HtmlDecode(text);
        return string.Join(' ', decoded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>A 3 letter upper-case tag for a feed title ("BBC News - Home" gives "BBC").</summary>
    public static string TagOf(string title)
    {
        var letters = new string(title.Where(char.IsLetterOrDigit).Take(3).ToArray());
        return letters.Length == 0 ? "RSS" : letters.ToUpperInvariant();
    }
}

/// <summary>Real feeds over HTTP. A feed that cannot be fetched or parsed throws, and the app skips it.</summary>
public sealed class HttpRssSource(HttpClient http) : IRssSource
{
    public async Task<RssFeed> GetAsync(string url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var xml = await http.GetStringAsync(url, timeout.Token);
        return RssParser.Parse(xml);
    }
}

/// <summary>Deterministic stand-in with canned headlines per url. Used by tests and for offline demos (config Rss:Source = Fake).</summary>
public sealed class FakeRssSource(IReadOnlyDictionary<string, RssFeed>? feeds = null) : IRssSource
{
    public static readonly IReadOnlyDictionary<string, RssFeed> Sample = new Dictionary<string, RssFeed>
    {
        ["https://example.test/world.xml"] = new("World Wire",
        [
            "Talks resume as delegates return to the table after a week of silence",
            "Storm system weakens before reaching the coast",
            "Rail strike called off after late-night deal",
        ]),
        ["https://example.test/tech.xml"] = new("Tech Daily",
        [
            "New open source LED panel driver doubles refresh rate",
            "Raspberry Pi shortage easing, makers report",
            "Why every kitchen clock should run Linux",
        ]),
        ["https://example.test/sport.xml"] = new("Sport Desk",
        [
            "Late goal settles the derby",
            "Marathon record falls in cool conditions",
        ]),
    };

    public Task<RssFeed> GetAsync(string url, CancellationToken ct)
    {
        var source = feeds ?? Sample;
        return source.TryGetValue(url, out var feed)
            ? Task.FromResult(feed)
            : Task.FromException<RssFeed>(new HttpRequestException("no such feed"));
    }
}
