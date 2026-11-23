using System.Reflection;
using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using Xunit;

namespace LedMatrixOS.Tests;

public sealed class AppMetaTests
{
    [Fact]
    public void EveryRegisteredAppHasCategoryAndDescription()
    {
        foreach (var type in BuiltInApps.GetAll())
        {
            var meta = type.GetCustomAttribute<AppMetaAttribute>(false);
            Assert.True(meta != null, $"{type.Name} is missing [AppMeta]");
            Assert.NotEqual(AppCategory.Other, meta!.Category);
            Assert.False(string.IsNullOrWhiteSpace(meta.Description), $"{type.Name} has no description");
            Assert.True(meta.Description.Length <= 50, $"{type.Name} description is longer than 50 chars");
        }
    }

    [Fact]
    public void KeywordsReachTheCatalogueAndSerialise()
    {
        var meta = typeof(RailDeparturesApp).GetCustomAttribute<AppMetaAttribute>(false)!;
        Assert.Contains("train", meta.Keywords);

        var info = new AppInfo("rail-departures", "Rail Departures", true, meta.Category, meta.Description, meta.Keywords);
        var json = System.Text.Json.JsonSerializer.Serialize(new { info.Id, keywords = info.Keywords ?? [] });
        Assert.Contains("\"keywords\":[", json);
        Assert.Contains("\"train\"", json);
    }
}
