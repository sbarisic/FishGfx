using System;
using System.Text;
using System.Linq;
using System.Runtime.InteropServices;
using static StbTrueTypeSharp.StbTrueType;

namespace FishGfx.Formats;

public sealed unsafe partial class TrueTypeFont
{
	internal byte GetGlyphBorderMaximum(char character) => GetGlyphBorderMaximum(new Rune(character));

	internal byte GetGlyphBorderMaximum(Rune character)
	{
		Glyph glyph = ResolveGlyph(character);

		if (glyph.Width == 0 || glyph.Height == 0 || glyph.Bitmap.Length == 0)
		{
			return 0;
		}

		byte maximum = 0;

		for (int x = 0; x < glyph.Width; x++)
		{
			maximum = Math.Max(maximum, glyph.Bitmap[x]);
			maximum = Math.Max(
				maximum,
				glyph.Bitmap[(glyph.Height - 1) * glyph.Width + x]
			);
		}

		for (int y = 0; y < glyph.Height; y++)
		{
			maximum = Math.Max(maximum, glyph.Bitmap[y * glyph.Width]);
			maximum = Math.Max(
				maximum,
				glyph.Bitmap[y * glyph.Width + glyph.Width - 1]
			);
		}

		return maximum;
	}

	private Glyph ResolveGlyph(Rune character)
	{
		if (!Rune.IsValid(character.Value))
		{
			character = fallback;
		}

		if (!glyphs.TryGetValue(character, out Glyph glyph))
		{
			AddGlyph(character);
			glyph = glyphs[character];
		}

		return glyph;
	}

	private void AddAlias(Rune character, Rune target)
	{
		if (!glyphs.ContainsKey(character))
		{
			glyphs.Add(character, ResolveGlyph(target));
		}
	}

	private Rune Normalize(Rune character)
	{
		return !Rune.IsValid(character.Value)
			|| stbtt_FindGlyphIndex(fontInfo, character.Value) == 0
			? fallback
			: character;
	}

	private void AddGlyph(Rune requested)
	{
		if (glyphs.ContainsKey(requested))
		{
			return;
		}

		Rune character = Normalize(requested);

		if (character != requested)
		{
			AddAlias(requested, character);

			return;
		}

		Glyph glyph = RasterizeGlyph(character);
		glyphs.Add(character, glyph);

		if (Repack())
		{
			return;
		}

		glyphs.Remove(character);

		if (character != fallback)
		{
			AddAlias(requested, fallback);
		}
	}

	private Glyph RasterizeGlyph(Rune character)
	{
		int advance;
		int bearing;
		stbtt_GetCodepointHMetrics(fontInfo, character.Value, &advance, &bearing);

		int width = 0;
		int height = 0;
		int xOffset = 0;
		int yOffset = 0;
		byte* bitmap = stbtt_GetCodepointSDF(
			fontInfo,
			scale,
			character.Value,
			options.SdfPadding,
			options.SdfOnEdgeValue,
			options.SdfPixelDistanceScale,
			&width,
			&height,
			&xOffset,
			&yOffset
		);
		byte[] pixels = width > 0 && height > 0
			? new byte[width * height]
			: Array.Empty<byte>();

		if (bitmap != null)
		{
			Marshal.Copy((IntPtr)bitmap, pixels, 0, pixels.Length);
			stbtt_FreeSDF(bitmap, null);
		}

		return new Glyph
		{
			Character = character,
			Width = width,
			Height = height,
			XOffset = xOffset,
			YOffset = ascentPixels + yOffset,
			Advance = (int)MathF.Round(advance * scale),
			Bitmap = pixels,
		};
	}

    private bool Repack()
    {
        int candidateSize = atlasSize;
        while (true)
        {
            if (TryPack(candidateSize, out byte[] pixels, out var positions))
            {
                foreach (var entry in positions) { entry.Key.X = entry.Value.X; entry.Key.Y = entry.Value.Y; }
                atlasPixels = pixels;
                atlasSize = candidateSize;
                atlasVersion++;
                return true;
            }
            if (candidateSize >= options.MaximumAtlasSize) return false;
            candidateSize = Math.Min(candidateSize * 2, options.MaximumAtlasSize);
        }
    }

    private bool TryPack(int size, out byte[] pixels, out System.Collections.Generic.Dictionary<Glyph, (int X, int Y)> positions)
    {
        pixels = null;
        positions = new();
        int x = 1, y = 1, rowHeight = 0;
        foreach (Glyph glyph in glyphs.Values.Distinct().OrderBy(value => value.Character.Value))
        {
            if (glyph.Width == 0 || glyph.Height == 0) { positions.Add(glyph, (0, 0)); continue; }
            if (glyph.Width + 2 > size) return false;
            if (x + glyph.Width + 1 > size) { x = 1; y += rowHeight + 1; rowHeight = 0; }
            if (y + glyph.Height + 1 > size) return false;
            positions.Add(glyph, (x, y));
            x += glyph.Width + 1;
            rowHeight = Math.Max(rowHeight, glyph.Height);
        }
        pixels = new byte[checked(size * size)];
        foreach (var entry in positions)
            for (int row = 0; row < entry.Key.Height; row++)
                Buffer.BlockCopy(entry.Key.Bitmap, row * entry.Key.Width, pixels,
                    (entry.Value.Y + row) * size + entry.Value.X, entry.Key.Width);
        return true;
    }

	private sealed class Glyph
	{
		internal Rune Character { get; init; }

		internal int Width { get; init; }

		internal int Height { get; init; }

		internal int X { get; set; }

		internal int Y { get; set; }

		internal int XOffset { get; init; }

		internal int YOffset { get; init; }

		internal int Advance { get; init; }

		internal byte[] Bitmap { get; init; }
	}
}
