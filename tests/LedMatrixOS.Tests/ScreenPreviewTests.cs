using LedMatrixOS.Apps.Screens;
using LedMatrixOS.Core.Screens;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LedMatrixOS.Tests;

public class ScreenPreviewTests
{
    private const string Json = """
    {"id":"draft","name":"Draft","root":{"type":"stack","direction":"vertical","padding":4,"children":[
      {"type":"label","text":"HELLO","font":"Big","color":"#ffffff"}]}}
    """;

    private static ScreenDefinition Parse(string json)
    {
        var doc = ScreenDocument.TryParse("{\"screens\":[" + json + "]}", out var error);
        Assert.True(doc is not null, error);
        return doc!.Screens[0];
    }

    private static ScreenPreviewRenderer Renderer(int w = 256, int h = 64) =>
        new(new ConfigurationBuilder().Build(), new HttpClient(), w, h);

    [Fact]
    public async Task ValidDraft_RendersPngAtDisplaySize()
    {
        var png = await Renderer().RenderPngAsync(Parse(Json), CancellationToken.None);
        using var image = Image.Load<Rgba32>(png);
        Assert.Equal(256, image.Width);
        Assert.Equal(64, image.Height);
        bool anyLit = false;
        for (int y = 0; y < image.Height && !anyLit; y++)
            for (int x = 0; x < image.Width; x++)
                if (image[x, y].R > 0) { anyLit = true; break; }
        Assert.True(anyLit);
    }

    [Fact]
    public async Task CustomDisplaySize_IsHonoured()
    {
        var png = await Renderer(128, 32).RenderPngAsync(Parse(Json), CancellationToken.None);
        using var image = Image.Load<Rgba32>(png);
        Assert.Equal((128, 32), (image.Width, image.Height));
    }

    [Fact]
    public void InvalidDraft_FailsValidation()
    {
        var def = Parse("""{"id":"bad","name":"Bad","root":{"type":"nonsense"}}""");
        Assert.NotEmpty(new ScreenDocument { Screens = { def } }.Validate());
    }
}
