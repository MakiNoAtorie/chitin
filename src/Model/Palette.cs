using Avalonia.Media;

namespace Chitin;

public sealed record Palette(
    string Name,
    Color[] VpColors,
    Color Grid,
    Color Ink,
    Color Paper,
    Color Border,
    Color Void,
    Color Horizon,
    Color Prompt)
{
    public static readonly Palette Atelier = new(
        "atelier",
        new[]
        {
            Hex("#8C6A46"), Hex("#A85A62"), Hex("#6F8158"), Hex("#B07A2E"), Hex("#7C5C74"),
            Hex("#9A6B4F"), Hex("#6B7A6A"), Hex("#A8563C"), Hex("#7A6A52")
        },
        Hex("#8C8272"),
        Hex("#23201D"),
        Hex("#F5F0E6"),
        Hex("#1A1715"),
        Hex("#2B2724"),
        Hex("#8C8272"),
        Hex("#A8563C"));

    public static readonly Palette InkPalette = new(
        "ink",
        new[]
        {
            // Grey ramp kept at 3:1 or more against the paper, so every guide line stays visible.
            Hex("#3A3A3A"), Hex("#454545"), Hex("#505050"), Hex("#5B5B5B"), Hex("#666666"),
            Hex("#717171"), Hex("#7C7C7C"), Hex("#878787"), Hex("#B07A2E")
        },
        Hex("#8C8C8C"),
        Hex("#111111"),
        Hex("#F7F7F4"),
        Hex("#1A1A1A"),
        Hex("#262626"),
        Hex("#6E6E6E"),
        Hex("#B07A2E"));

    public static readonly Palette Charcoal = new(
        "charcoal",
        new[]
        {
            Hex("#D8D0C0"), Hex("#C8B89A"), Hex("#E0CFA8"), Hex("#B89C78"), Hex("#D6B98C"),
            Hex("#A89070"), Hex("#C4B49C"), Hex("#E2D6BE"), Hex("#C08A5A")
        },
        Hex("#6E655A"),
        Hex("#EDE6D8"),
        Hex("#1E1C1A"),
        Hex("#0C0B0A"),
        Hex("#121110"),
        Hex("#8A7D6C"),
        Hex("#D98E5F"));

    public static readonly IReadOnlyList<Palette> BuiltIns = new[] { Atelier, InkPalette, Charcoal };

    // The palette of a new document.
    public static readonly Palette Default = InkPalette;

    // For an unknown name, and for a project file that names none: older files keep their look.
    public static readonly Palette Fallback = Atelier;

    public static Palette ByName(string? name)
    {
        foreach (Palette palette in BuiltIns)
        {
            if (string.Equals(palette.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return palette;
            }
        }

        return Fallback;
    }

    private static Color Hex(string value)
    {
        return Color.Parse(value);
    }
}
