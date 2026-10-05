using System.Globalization;
using System.Text.Json;

namespace LedMatrixOS.Apps.Stocks;

/// <summary>One instrument: latest price, change since the previous close (percent) and the intraday closes for the sparkline.</summary>
public sealed record StockQuote(string Symbol, double Price, double ChangePercent, IReadOnlyList<float> History);

/// <summary>The quotes for every symbol that could be read, in the order they were asked for. A class so apps can tell a new board by reference.</summary>
public sealed class StockBoard(IReadOnlyList<StockQuote> quotes)
{
    public IReadOnlyList<StockQuote> Quotes { get; } = quotes;
}

public interface IStockSource
{
    /// <summary>Quotes for the symbols. A symbol that cannot be read is left out; throws when none can.</summary>
    Task<StockBoard> GetAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken);
}

/// <summary>How a quote is shown: price and change text, and which way it moved.</summary>
public static class StockFormat
{
    public static string Price(double price) =>
        price >= 10000 ? price.ToString("#,##0", CultureInfo.InvariantCulture)
        : price >= 1000 ? price.ToString("#,##0.0", CultureInfo.InvariantCulture)
        : price >= 1 ? price.ToString("0.00", CultureInfo.InvariantCulture)
        : price.ToString("0.0000", CultureInfo.InvariantCulture);

    public static string Change(double percent) =>
        (percent >= 0 ? "+" : "") + percent.ToString(Math.Abs(percent) >= 100 ? "0" : "0.0", CultureInfo.InvariantCulture) + "%";

    /// <summary>1 up, -1 down, 0 when the change rounds to zero.</summary>
    public static int Direction(double percent) => Math.Round(percent, 1) > 0 ? 1 : Math.Round(percent, 1) < 0 ? -1 : 0;

    /// <summary>Symbol as shown on a tile: no exchange suffix or "-USD", at most 5 characters.</summary>
    public static string Short(string symbol)
    {
        var s = symbol.ToUpperInvariant();
        if (s.EndsWith("-USD", StringComparison.Ordinal)) s = s[..^4];
        int dot = s.IndexOf('.');
        if (dot > 0) s = s[..dot];
        return s.Length > 5 ? s[..5] : s;
    }
}

/// <summary>
/// Real quotes from Yahoo Finance's public chart endpoint (no key): one request per symbol (<c>range=1d</c>, 5 minute candles).
/// Shares, indices (<c>^GSPC</c>), FX (<c>GBPUSD=X</c>) and crypto (<c>BTC-USD</c>) all work.
/// </summary>
public sealed class YahooStockSource(HttpClient http) : IStockSource
{
    public const int MaxHistory = 48;

    public async Task<StockBoard> GetAsync(IReadOnlyList<string> symbols, CancellationToken ct)
    {
        var results = await Task.WhenAll(symbols.Select(async symbol =>
        {
            try { return await FetchAsync(symbol, ct); }
            catch (Exception) when (!ct.IsCancellationRequested) { return null; }
        }));

        var quotes = results.OfType<StockQuote>().ToArray();
        if (quotes.Length == 0) throw new HttpRequestException("No quote could be read");
        return new StockBoard(quotes);
    }

    private async Task<StockQuote> FetchAsync(string symbol, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, Url(symbol));
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (LedMatrixOS)");
        using var response = await http.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(timeout.Token), symbol);
    }

    public static string Url(string symbol) =>
        $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=1d&interval=5m";

    public static StockQuote Parse(string json, string symbol)
    {
        using var doc = JsonDocument.Parse(json);
        var results = doc.RootElement.GetProperty("chart").GetProperty("result");
        if (results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0) throw new FormatException("No result for " + symbol);
        var result = results[0];
        var meta = result.GetProperty("meta");

        var closes = new List<float>();
        if (result.TryGetProperty("indicators", out var ind) && ind.TryGetProperty("quote", out var quote) && quote.GetArrayLength() > 0
            && quote[0].TryGetProperty("close", out var close))
            foreach (var c in close.EnumerateArray())
                if (c.ValueKind == JsonValueKind.Number) closes.Add((float)c.GetDouble());

        double? price = Num(meta, "regularMarketPrice") ?? (closes.Count > 0 ? closes[^1] : null);
        if (price is null) throw new FormatException("No price for " + symbol);
        double previous = Num(meta, "chartPreviousClose") ?? Num(meta, "previousClose") ?? (closes.Count > 0 ? closes[0] : price.Value);
        double change = previous == 0 ? 0 : (price.Value - previous) / previous * 100;

        return new StockQuote(meta.TryGetProperty("symbol", out var s) && s.GetString() is { Length: > 0 } name ? name : symbol,
            price.Value, change, Downsample(closes, MaxHistory));
    }

    private static float[] Downsample(List<float> values, int max)
    {
        if (values.Count <= max) return values.ToArray();
        var result = new float[max];
        for (int i = 0; i < max; i++) result[i] = values[(int)((long)i * (values.Count - 1) / (max - 1))];
        return result;
    }

    private static double? Num(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}

/// <summary>
/// Deterministic stand-in: the same symbol always gives the same quote (derived from a hash of its name). Used by tests and offline demos
/// (config Stocks:Source = Fake). Symbols listed in <paramref name="missing"/> fail, like an unknown ticker would.
/// </summary>
public sealed class FakeStockSource(params string[] missing) : IStockSource
{
    public Task<StockBoard> GetAsync(IReadOnlyList<string> symbols, CancellationToken ct)
    {
        var quotes = symbols.Where(s => !missing.Contains(s, StringComparer.OrdinalIgnoreCase)).Select(Quote).ToArray();
        return quotes.Length == 0
            ? Task.FromException<StockBoard>(new HttpRequestException("offline"))
            : Task.FromResult(new StockBoard(quotes));
    }

    public static StockQuote Quote(string symbol)
    {
        int h = 17;
        foreach (char c in symbol.ToUpperInvariant()) h = unchecked(h * 31 + c);
        h &= 0x7fffffff;

        double price = 20 + h % 4000 / 4.0 + (h % 7 == 0 ? 20000 : 0);
        double change = (h % 1200) / 100.0 - 5.0;          // -5.00 .. +6.99
        double previous = price / (1 + change / 100);
        var history = new float[YahooStockSource.MaxHistory];
        for (int i = 0; i < history.Length; i++)
        {
            double t = i / (history.Length - 1.0);
            double drift = previous + (price - previous) * t;
            history[i] = (float)(drift + price * 0.01 * Math.Sin(i * 0.7 + h % 13));
        }

        history[^1] = (float)price;
        return new StockQuote(symbol.ToUpperInvariant(), price, change, history);
    }
}
