using BdfFontParser;
using BdfFontParser.Models;
using LedMatrixOS.Core;
using System.Reflection;

namespace LedMatrixOS.Graphics.Text;

public static class TextExtensions
{
    public static void DrawText(this FrameBuffer frame, BdfFont font, int x, int y, Pixel color, string text, int startLine = 0, int? endLine = null, bool shadow = true)
    {
        var map = font.GetMapOfString(text);
        DrawText(frame, font, x, y, color, map, startLine, endLine, shadow);
    }

    /// <summary>
    /// Draws text with a style. <paramref name="x"/> is the left edge, centre or right edge depending on <paramref name="align"/>.
    /// </summary>
    public static void DrawText(this FrameBuffer frame, TextStyle style, int x, int y, string text, TextAlign align = TextAlign.Left)
    {
        if (string.IsNullOrEmpty(text)) return;
        int width = style.Font.MeasureText(text);
        x = align switch
        {
            TextAlign.Center => x - width / 2,
            TextAlign.Right => x - width,
            _ => x,
        };

        var map = style.Font.GetMapOfString(text);
        DrawText(frame, style.Font, x, y, style.Color, map, 0, null, style.Shadow, style.ShadowColor);
    }

    /// <summary>
    /// Width in pixels of the text when drawn with this font.
    /// </summary>
    public static int MeasureText(this BdfFont font, string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return font.GetMapOfString(text).GetLength(0);
    }

    /// <summary>
    /// Shortens the text with a trailing ellipsis until it fits in <paramref name="maxWidth"/> pixels.
    /// </summary>
    public static string TruncateWithEllipsis(this BdfFont font, string text, int maxWidth, string ellipsis = "...")
    {
        if (font.MeasureText(text) <= maxWidth) return text;
        if (font.MeasureText(ellipsis) > maxWidth) return string.Empty;

        for (int length = text.Length - 1; length > 0; length--)
        {
            var candidate = text[..length].TrimEnd() + ellipsis;
            if (font.MeasureText(candidate) <= maxWidth) return candidate;
        }

        return ellipsis;
    }

    private static void DrawText(FrameBuffer frame, BdfFont font, int x, int y, Pixel color, bool[,] map, int startLine = 0, int? endLine = null, bool withShadow = true, Pixel? shadowColor = null)
    {
        var shadow = shadowColor ?? Pixel.Black;
        var width = map.GetLength(0);
        var height = map.GetLength(1);

        if (height > endLine)
            height = endLine.Value;

        for (int line = startLine; line < height; line++)
        {
            // iterate through every bit
            for (int bit = 0; bit < width; bit++)
            {
                if (!map[bit, line]) continue;

                var charX = bit + x;
                var charY = line + (y - font.BoundingBox.Y - font.BoundingBox.OffsetY);

                // Glyph pixels off the frame are skipped along with their shadow; SetPixel also honours the clip stack
                if (charX < 0 || charY < 0 || charX >= frame.Width || charY >= frame.Height) continue;
                frame.SetPixel(charX, charY, color);
                if (withShadow) frame.SetPixel(charX + 1, charY + 1, shadow);
            }
        }
    }

    /// <summary>
    /// Creates a scaled version of the BdfFont by the specified factor
    /// </summary>
    /// <param name="font">The original font to scale</param>
    /// <param name="scaleFactor">The scaling factor (e.g., 2 for double size, 3 for triple size)</param>
    /// <returns>A new BdfFont instance scaled by the specified factor</returns>
    public static BdfFont Scale(this BdfFont font, int scaleFactor)
    {
        if (scaleFactor <= 0)
            throw new ArgumentException("Scale factor must be positive", nameof(scaleFactor));
        
        if (scaleFactor == 1)
            return font;

        // Create a new BdfFont using reflection to access private members
        var scaledFont = new BdfFont(new[] { "STARTFONT 2.1", "ENDFONT" });
        
        // Scale the main bounding box
        var scaledBoundingBox = new BoundingBox
        {
            X = font.BoundingBox.X * scaleFactor,
            Y = font.BoundingBox.Y * scaleFactor,
            OffsetX = font.BoundingBox.OffsetX * scaleFactor,
            OffsetY = font.BoundingBox.OffsetY * scaleFactor
        };
        scaledFont.BoundingBox = scaledBoundingBox;

        // Get the private _charMap field using reflection
        var originalCharMapField = typeof(BdfFont).GetField("_charMap", BindingFlags.NonPublic | BindingFlags.Instance);
        var scaledCharMapField = typeof(BdfFont).GetField("_charMap", BindingFlags.NonPublic | BindingFlags.Instance);
        
        if (originalCharMapField == null || scaledCharMapField == null)
            throw new InvalidOperationException("Unable to access BdfFont internal structure");

        var originalCharMap = originalCharMapField.GetValue(font) as Dictionary<char, CharData>;
        var scaledCharMap = scaledCharMapField.GetValue(scaledFont) as Dictionary<char, CharData>;
        
        if (originalCharMap == null || scaledCharMap == null)
            throw new InvalidOperationException("Unable to access BdfFont character maps");
        
        // Clear the scaled font's character map and populate with scaled characters
        scaledCharMap.Clear();
        
        foreach (var kvp in originalCharMap)
        {
            var originalChar = kvp.Value;
            var scaledChar = ScaleCharData(originalChar, scaleFactor, scaledBoundingBox.Y);
            scaledCharMap[kvp.Key] = scaledChar;
        }

        return scaledFont;
    }

    /// <summary>
    /// Creates a scaled version of the BdfFont by the specified factors for width and height
    /// </summary>
    /// <param name="font">The original font to scale</param>
    /// <param name="scaleFactorX">The horizontal scaling factor</param>
    /// <param name="scaleFactorY">The vertical scaling factor</param>
    /// <returns>A new BdfFont instance scaled by the specified factors</returns>
    public static BdfFont Scale(this BdfFont font, int scaleFactorX, int scaleFactorY)
    {
        if (scaleFactorX <= 0 || scaleFactorY <= 0)
            throw new ArgumentException("Scale factors must be positive");
        
        if (scaleFactorX == 1 && scaleFactorY == 1)
            return font;

        // Create a new BdfFont using reflection to access private members
        var scaledFont = new BdfFont(new[] { "STARTFONT 2.1", "ENDFONT" });
        
        // Scale the main bounding box
        var scaledBoundingBox = new BoundingBox
        {
            X = font.BoundingBox.X * scaleFactorX,
            Y = font.BoundingBox.Y * scaleFactorY,
            OffsetX = font.BoundingBox.OffsetX * scaleFactorX,
            OffsetY = font.BoundingBox.OffsetY * scaleFactorY
        };
        scaledFont.BoundingBox = scaledBoundingBox;

        // Get the private _charMap field using reflection
        var originalCharMapField = typeof(BdfFont).GetField("_charMap", BindingFlags.NonPublic | BindingFlags.Instance);
        var scaledCharMapField = typeof(BdfFont).GetField("_charMap", BindingFlags.NonPublic | BindingFlags.Instance);
        
        if (originalCharMapField == null || scaledCharMapField == null)
            throw new InvalidOperationException("Unable to access BdfFont internal structure");

        var originalCharMap = originalCharMapField.GetValue(font) as Dictionary<char, CharData>;
        var scaledCharMap = scaledCharMapField.GetValue(scaledFont) as Dictionary<char, CharData>;
        
        if (originalCharMap == null || scaledCharMap == null)
            throw new InvalidOperationException("Unable to access BdfFont character maps");
        
        // Clear the scaled font's character map and populate with scaled characters
        scaledCharMap.Clear();
        
        foreach (var kvp in originalCharMap)
        {
            var originalChar = kvp.Value;
            var scaledChar = ScaleCharData(originalChar, scaleFactorX, scaleFactorY, scaledBoundingBox.Y);
            scaledCharMap[kvp.Key] = scaledChar;
        }

        return scaledFont;
    }

    private static CharData ScaleCharData(CharData original, int scaleFactor, int fontHeight)
    {
        return ScaleCharData(original, scaleFactor, scaleFactor, fontHeight);
    }

    private static CharData ScaleCharData(CharData original, int scaleFactorX, int scaleFactorY, int fontHeight)
    {
        var scaledChar = new CharData
        {
            Character = original.Character,
            Name = original.Name,
            ScalableWidth = new Width
            {
                X = original.ScalableWidth.X * scaleFactorX,
                Y = original.ScalableWidth.Y * scaleFactorY
            },
            DeviceWidth = new Width
            {
                X = original.DeviceWidth.X * scaleFactorX,
                Y = original.DeviceWidth.Y * scaleFactorY
            },
            BoundingBox = new BoundingBox
            {
                X = original.BoundingBox.X * scaleFactorX,
                Y = original.BoundingBox.Y * scaleFactorY,
                OffsetX = original.BoundingBox.OffsetX * scaleFactorX,
                OffsetY = original.BoundingBox.OffsetY * scaleFactorY
            },
            Bitmap = new byte[fontHeight][]
        };

        // Scale the bitmap data
        for (int y = 0; y < fontHeight; y++)
        {
            scaledChar.Bitmap[y] = new byte[0]; // Initialize empty array
        }

        // Scale each row of the original bitmap
        for (int originalY = 0; originalY < original.Bitmap.Length; originalY++)
        {
            var originalRow = original.Bitmap[originalY];
            if (originalRow == null || originalRow.Length == 0) continue;

            // For each scaled Y position
            for (int scaleY = 0; scaleY < scaleFactorY; scaleY++)
            {
                int scaledY = originalY * scaleFactorY + scaleY;
                if (scaledY >= fontHeight) break;

                // Calculate the required byte array size for the scaled row
                int originalBits = originalRow.Length * 8;
                int scaledBits = originalBits * scaleFactorX;
                int scaledBytes = (scaledBits + 7) / 8; // Round up to nearest byte

                scaledChar.Bitmap[scaledY] = new byte[scaledBytes];

                // Scale each byte in the original row
                for (int originalByteIndex = 0; originalByteIndex < originalRow.Length; originalByteIndex++)
                {
                    byte originalByte = originalRow[originalByteIndex];
                    
                    // Process each bit in the byte
                    for (int bit = 0; bit < 8; bit++)
                    {
                        bool isSet = (originalByte & (0x80 >> bit)) != 0;
                        
                        if (isSet)
                        {
                            // Scale this bit horizontally
                            for (int scaleX = 0; scaleX < scaleFactorX; scaleX++)
                            {
                                int scaledBitPosition = (originalByteIndex * 8 + bit) * scaleFactorX + scaleX;
                                int scaledByteIndex = scaledBitPosition / 8;
                                int scaledBitIndex = scaledBitPosition % 8;
                                
                                if (scaledByteIndex < scaledChar.Bitmap[scaledY].Length)
                                {
                                    scaledChar.Bitmap[scaledY][scaledByteIndex] |= (byte)(0x80 >> scaledBitIndex);
                                }
                            }
                        }
                    }
                }
            }
        }

        return scaledChar;
    }
}
