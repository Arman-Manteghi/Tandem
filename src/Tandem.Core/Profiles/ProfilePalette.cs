using System.Globalization;

namespace Tandem.Core.Profiles;

public sealed record ProfileColor(string Name, string Hex);

public static class ProfilePalette
{
    public static IReadOnlyList<ProfileColor> Colors { get; } =
    [
        new("Coral", "#E07A5F"),
        new("Amber", "#D99A2B"),
        new("Olive", "#7D9A3A"),
        new("Teal", "#2A9D8F"),
        new("Sky", "#3B82C4"),
        new("Indigo", "#5B5FD6"),
        new("Plum", "#9B59B6"),
        new("Rose", "#D9487A"),
        new("Slate", "#64748B"),
    ];

    public static string DefaultColor => Colors[0].Hex;

    /// <summary>Returns "#RRGGBB" in upper case, or null when the value is not a 6-digit hex color.</summary>
    public static string? Normalize(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var hex = value.Trim();
        if (hex.StartsWith('#'))
        {
            hex = hex[1..];
        }

        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
        {
            return null;
        }

        return "#" + hex.ToUpperInvariant();
    }

    /// <summary>First palette color not used yet; cycles when all are taken.</summary>
    public static string NextUnused(IEnumerable<string> usedColors)
    {
        var used = new HashSet<string>(usedColors.Select(Normalize).OfType<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var color in Colors)
        {
            if (!used.Contains(color.Hex))
            {
                return color.Hex;
            }
        }

        return Colors[used.Count % Colors.Count].Hex;
    }
}
