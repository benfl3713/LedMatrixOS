using BdfFontParser;

namespace LedMatrixOS.Engine;

/// <summary>
/// Static registry of BDF bitmap fonts for use in MonoGame scenes.
/// Call <see cref="Load"/> once at startup (before any scene renders text).
/// The BDF files are sourced from <c>Text/Fonts/</c> relative to the application base directory,
/// which is where they are copied by the <c>LedMatrixOS.Graphics</c> project reference.
/// </summary>
public static class MatrixFonts
{
    public static MatrixFont Big        { get; private set; } = null!;
    public static MatrixFont Small      { get; private set; } = null!;
    public static MatrixFont QuiteSmall { get; private set; } = null!;
    public static MatrixFont ExtraSmall { get; private set; } = null!;
    public static MatrixFont TubeFont   { get; private set; } = null!;

    /// <summary>Load all fonts from the <c>Text/Fonts/</c> subdirectory of the app base directory.</summary>
    public static void Load()
    {
        static string P(string file) => Path.Combine(AppContext.BaseDirectory, "Text", "Fonts", file);

        Big        = new MatrixFont(new BdfFont(P("9x18.bdf")));
        Small      = new MatrixFont(new BdfFont(P("6x12.bdf")));
        QuiteSmall = new MatrixFont(new BdfFont(P("5x7.bdf")));
        ExtraSmall = new MatrixFont(new BdfFont(P("4x6.bdf")));
        TubeFont   = new MatrixFont(new BdfFont(P("TubeFont.bdf")));
    }
}
