using System.Reflection;
using BdfFontParser;

namespace LedMatrixOS.Graphics.Text;

public static class Fonts
{
    public static BdfFont Big { get; private set; } = null!;
    public static BdfFont Small { get; private set; } = null!;
    public static BdfFont QuiteSmall { get; private set; } = null!;
    public static BdfFont ExtraSmall { get; private set; } = null!;

    public static void Load()
    {
        Big = new BdfFont($"{Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}/Text/Fonts/9x18.bdf");
        Small = new BdfFont($"{Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}/Text/Fonts/6x12.bdf");
        QuiteSmall = new BdfFont($"{Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}/Text/Fonts/5x7.bdf");
        ExtraSmall = new BdfFont($"{Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}/Text/Fonts/4x6.bdf");
    }
}
