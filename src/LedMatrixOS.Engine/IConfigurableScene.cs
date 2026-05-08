using LedMatrixOS.Core;

namespace LedMatrixOS.Engine;

/// <summary>
/// Optional interface for <see cref="IMatrixScene"/> implementations that expose
/// user-configurable settings via the REST API.
/// Mirrors <see cref="IConfigurableApp"/> from the legacy system.
/// </summary>
public interface IConfigurableScene : IMatrixScene
{
    IEnumerable<AppSetting> GetSettings();
    void UpdateSetting(string key, object value);
}
