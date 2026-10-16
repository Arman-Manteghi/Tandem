using System.Buffers.Binary;
using Tandem.Core.Profiles;

namespace Tandem.Core.Shell;

/// <summary>
/// Draws Tandem's own mark (two linked rings on a disc) in any color and packs it into an .ico.
/// Pure code, no image libraries, so the result is identical on every machine.
/// </summary>
public static class IconFactory
{
    public const string AppBackground = "#33302B";
    public const string Cream = "#F4EFE6";

    private static readonly int[] DefaultSizes = [16, 20, 24, 32, 40, 48, 64, 256];

    public static byte[] CreateIco(string backgroundHex, string glyphHex, IReadOnlyList<int>? sizes = null, bool rim = false)
    {
        sizes ??= DefaultSizes;
        var images = sizes.Select(size => (size, data: EncodeBmpEntry(size, RenderBgra(size, backgroundHex, glyphHex, rim)))).ToList();

        using var stream = new MemoryStream();
        Span<byte> header = stackalloc byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(header[2..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], (ushort)images.Count);
        stream.Write(header);

        var offset = 6 + (16 * images.Count);
        Span<byte> entry = stackalloc byte[16];
        foreach (var (size, data) in images)
        {
            entry.Clear();
            entry[0] = (byte)(size >= 256 ? 0 : size);
            entry[1] = (byte)(size >= 256 ? 0 : size);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 32);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)offset);
            stream.Write(entry);
            offset += data.Length;
        }

        foreach (var (_, data) in images)
        {
            stream.Write(data);
        }

        return stream.ToArray();
    }

    /// <summary>Top-down, non-premultiplied BGRA pixels.</summary>
    public static byte[] RenderBgra(int size, string backgroundHex, string glyphHex, bool rim = false)
    {
        var bg = ParseRgb(backgroundHex);
        var fg = ParseRgb(glyphHex);
        var pixels = new byte[size * size * 4];
        const int samples = 4;
        const double sampleWeight = 1.0 / (samples * samples);

        // Geometry in a unit square.
        const double discRadius = 0.5;
        const double ringRadius = 0.2;
        const double ringHalfStroke = 0.052;
        const double ringOffset = 0.115;
        const double rimWidth = 0.035;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                double disc = 0, ring = 0, edge = 0;
                for (var sy = 0; sy < samples; sy++)
                {
                    for (var sx = 0; sx < samples; sx++)
                    {
                        var px = (x + ((sx + 0.5) / samples)) / size;
                        var py = (y + ((sy + 0.5) / samples)) / size;
                        var dx = px - 0.5;
                        var dy = py - 0.5;
                        if ((dx * dx) + (dy * dy) > discRadius * discRadius)
                        {
                            continue;
                        }

                        disc += sampleWeight;
                        if (rim && Math.Sqrt((dx * dx) + (dy * dy)) > discRadius - rimWidth)
                        {
                            edge += sampleWeight;
                        }

                        var left = Math.Abs(Math.Sqrt(((dx + ringOffset) * (dx + ringOffset)) + (dy * dy)) - ringRadius);
                        var right = Math.Abs(Math.Sqrt(((dx - ringOffset) * (dx - ringOffset)) + (dy * dy)) - ringRadius);
                        if (left <= ringHalfStroke || right <= ringHalfStroke)
                        {
                            ring += sampleWeight;
                        }
                    }
                }

                var i = ((y * size) + x) * 4;
                if (disc <= 0)
                {
                    continue;
                }

                var t = (ring + (edge * 0.22)) / disc;
                pixels[i + 0] = Mix(bg.B, fg.B, t);
                pixels[i + 1] = Mix(bg.G, fg.G, t);
                pixels[i + 2] = Mix(bg.R, fg.R, t);
                pixels[i + 3] = (byte)Math.Round(disc * 255);
            }
        }

        return pixels;
    }

    private static byte[] EncodeBmpEntry(int size, byte[] topDownBgra)
    {
        var maskRowBytes = ((size + 31) / 32) * 4;
        var pixelBytes = size * size * 4;
        var maskBytes = maskRowBytes * size;
        var data = new byte[40 + pixelBytes + maskBytes];

        var header = data.AsSpan(0, 40);
        BinaryPrimitives.WriteInt32LittleEndian(header, 40);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], size);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], size * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header[14..], 32);
        BinaryPrimitives.WriteInt32LittleEndian(header[20..], pixelBytes + maskBytes);

        for (var row = 0; row < size; row++)
        {
            var sourceRow = size - 1 - row; // BMP rows are bottom-up
            Buffer.BlockCopy(topDownBgra, sourceRow * size * 4, data, 40 + (row * size * 4), size * 4);
            for (var x = 0; x < size; x++)
            {
                if (topDownBgra[(((sourceRow * size) + x) * 4) + 3] == 0)
                {
                    data[40 + pixelBytes + (row * maskRowBytes) + (x / 8)] |= (byte)(0x80 >> (x % 8));
                }
            }
        }

        return data;
    }

    private static byte Mix(byte a, byte b, double t) => (byte)Math.Round(a + ((b - a) * Math.Clamp(t, 0, 1)));

    private static (byte R, byte G, byte B) ParseRgb(string hex)
    {
        var normalized = ProfilePalette.Normalize(hex) ?? throw new ArgumentException("Expected #RRGGBB.", nameof(hex));
        var value = Convert.ToInt32(normalized[1..], 16);
        return ((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }
}
