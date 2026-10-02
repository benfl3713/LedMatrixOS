using System.Reflection;
using BdfFontParser;

namespace LedMatrixOS.Graphics.Text;

public static class Fonts
{
    public static BdfFont Big { get; private set; } = null!;
    public static BdfFont Small { get; private set; } = null!;
    public static BdfFont QuiteSmall { get; private set; } = null!;
    public static BdfFont ExtraSmall { get; private set; } = null!;

    private static readonly object LoadGate = new();
    private static bool _loaded;

    /// <summary>Loads the fonts once; later calls (including concurrent ones) return immediately, so the shared instances never change under a running app.</summary>
    public static void Load()
    {
        if (_loaded) return;
        lock (LoadGate)
        {
            if (_loaded) return;
            LoadFonts();
            _loaded = true;
        }
    }

    private static void LoadFonts()
    {
        Big = new BdfFont($"{Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}/Text/Fonts/9x18.bdf");
        Small = new BdfFont($"{Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}/Text/Fonts/6x12.bdf");
        QuiteSmall = new BdfFont($"{Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}/Text/Fonts/5x7.bdf");
        ExtraSmall = new BdfFont($"{Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}/Text/Fonts/4x6.bdf");
    }
}
