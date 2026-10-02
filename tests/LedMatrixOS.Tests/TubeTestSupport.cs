using System.Net;
using System.Text;
using LedMatrixOS.Apps;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LedMatrixOS.Tests;

/// <summary>Stub for api.tfl.gov.uk serving fixed JSON for the endpoints the Tube apps call.</summary>
internal sealed class TflStubHandler : HttpMessageHandler
{
    public System.Collections.Concurrent.ConcurrentQueue<string> Requests { get; } = new();

    public string Arrivals { get; set; } = """
        [
          {"destinationName":"Stanmore Underground Station","towards":"","timeToStation":30,"platformName":"Southbound - Platform 1","lineName":"Jubilee","lineId":"jubilee"},
          {"destinationName":"Aldgate Underground Station","towards":"Aldgate via Baker St","timeToStation":300,"platformName":"Eastbound - Platform 3","lineName":"Circle","lineId":"circle"}
        ]
        """;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        Requests.Enqueue(url);
        var body =
            url.Contains("/Arrivals") ? Arrivals
            : url.Contains("/Line/Mode/") ? """
                [
                  {"id":"central","name":"Central","lineStatuses":[{"statusSeverity":10,"statusSeverityDescription":"Good Service"}]},
                  {"id":"northern","name":"Northern","lineStatuses":[{"statusSeverity":6,"statusSeverityDescription":"Severe Delays","reason":"NORTHERN LINE: Severe delays due to a signal failure at Camden Town."}]}
                ]
                """
            : url.Contains("/Line/") ? """
                [
                  {"id":"jubilee","name":"Jubilee","lineStatuses":[{"statusSeverity":10,"statusSeverityDescription":"Good Service"}]},
                  {"id":"circle","name":"Circle","lineStatuses":[{"statusSeverity":6,"statusSeverityDescription":"Severe Delays"}]}
                ]
                """
            : url.Contains("/Search/") ? """
                {"matches":[{"id":"940GZZLUBST","name":"Baker Street Underground Station","modes":["tube"]},{"id":"bus1","name":"Baker Street Bus","modes":["bus"]}]}
                """
            : """{"commonName":"Baker Street Underground Station"}""";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}

/// <summary>Drives a widget app frame by frame on a fake clock, like the engine would.</summary>
internal sealed class AppStage
{
    private long _frame;

    public AppStage(Core.IMatrixApp app, int width = 256, int height = 64)
    {
        Fonts.Load();
        App = app;
        Frame = new FrameBuffer(width, height);
    }

    public Core.IMatrixApp App { get; }
    public FrameBuffer Frame { get; }
    public TimeSpan Time { get; private set; }

    public void Step(int ms, int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            var delta = TimeSpan.FromMilliseconds(ms);
            Time += delta;
            App.Update(new FrameContext(Time, delta, _frame++), CancellationToken.None);
        }
    }

    public FrameBuffer Render()
    {
        Frame.Clear(Pixel.Black);
        App.Render(Frame, CancellationToken.None);
        return Frame;
    }

    /// <summary>Renders, and copies the result so later frames do not overwrite it.</summary>
    public FrameBuffer Snapshot()
    {
        var copy = new FrameBuffer(Frame.Width, Frame.Height);
        copy.CopyFrom(Render());
        return copy;
    }
}

internal static class TubeFixtures
{
    public static TflArrival Arrival(string vehicle, string line, string destination, int seconds, string platform = "Westbound - Platform 1", string lineName = "") => new()
    {
        Id = vehicle + seconds,
        VehicleId = vehicle,
        LineId = line,
        LineName = lineName.Length > 0 ? lineName : char.ToUpper(line[0]) + line[1..],
        DestinationName = destination + " Underground Station",
        TimeToStation = seconds,
        PlatformName = platform,
    };

    public static LineStatus Status(string line, int severity, string description, string reason = "", string? name = null) =>
        new(line, name ?? (char.ToUpper(line[0]) + line[1..]), severity, description, reason);

    public static TflArrival[] CommuteBoard(int firstSeconds = 190) =>
    [
        Arrival("101", "victoria", "Brixton", firstSeconds, "Southbound - Platform 2"),
        Arrival("202", "victoria", "Walthamstow Central", firstSeconds + 150, "Northbound - Platform 1"),
        Arrival("303", "victoria", "Brixton", firstSeconds + 310, "Southbound - Platform 2"),
        Arrival("404", "victoria", "Walthamstow Central", firstSeconds + 470, "Northbound - Platform 1"),
        Arrival("505", "victoria", "Brixton", firstSeconds + 650, "Southbound - Platform 2"),
    ];

    public static LineStatus[] StationLines() =>
    [
        Status("circle", 10, "Good Service"),
        Status("hammersmith-city", 10, "Good Service"),
        Status("metropolitan", 9, "Minor Delays"),
        Status("victoria", 10, "Good Service"),
        Status("jubilee", 6, "Severe Delays"),
    ];

    /// <summary>Writes a 4x enlarged PNG that mimics the LED panel (dark gaps between pixels) into the directory named by LED_PREVIEW_DIR, if set.</summary>
    public static void Preview(FrameBuffer frame, string name)
    {
        var dir = Environment.GetEnvironmentVariable("LED_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        const int scale = 5;
        using var image = new Image<Rgb24>(frame.Width * scale, frame.Height * scale);
        for (int y = 0; y < frame.Height; y++)
            for (int x = 0; x < frame.Width; x++)
            {
                var p = frame.GetPixel(x, y);
                for (int dy = 0; dy < scale; dy++)
                    for (int dx = 0; dx < scale; dx++)
                    {
                        bool gap = dx == scale - 1 || dy == scale - 1;
                        image[x * scale + dx, y * scale + dy] = gap ? new Rgb24(6, 6, 6) : new Rgb24(p.R, p.G, p.B);
                    }
            }
        image.SaveAsPng(Path.Combine(dir, name + ".png"));
    }
}

/// <summary>Live data that can report a failed fetch or be swapped between frames.</summary>
internal sealed class MutableLive<T> : Core.Data.ILiveData<T>
{
    public T? Value { get; set; }
    public bool IsLoading => false;
    public Exception? Error { get; set; }
    public DateTimeOffset? LastUpdated => null;
    public event EventHandler? Changed { add { } remove { } }
}
