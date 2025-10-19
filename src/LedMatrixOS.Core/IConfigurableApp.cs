namespace LedMatrixOS.Core;

public interface IConfigurableApp : IMatrixApp
{
    IEnumerable<AppSetting> GetSettings();
    void UpdateSetting(string key, object value);
}

