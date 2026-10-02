using BdfFontParser;
using LedMatrixOS.Core;

namespace LedMatrixOS.Graphics.Text;

public enum TextAlign { Left, Center, Right }

public record TextStyle(BdfFont Font, Pixel Color, bool Shadow = true, Pixel? ShadowColorOverride = null)
{
    public Pixel ShadowColor => ShadowColorOverride ?? Pixel.Black;
}
