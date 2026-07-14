using System;
using System.Linq;
using System.Runtime.InteropServices;
using static StbTrueTypeSharp.StbTrueType;

namespace FishGfx.Formats;

public sealed unsafe partial class TrueTypeFont
{
	internal byte GetGlyphBorderMaximum(char character)
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

	private Glyph ResolveGlyph(char character)
	{
		if (char.IsSurrogate(character))
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

	private void AddAlias(char character, char target)
	{
		if (!glyphs.ContainsKey(character))
		{
			glyphs.Add(character, ResolveGlyph(target));
		}
	}

	private char Normalize(char character)
	{
		return char.IsSurrogate(character)
			|| stbtt_FindGlyphIndex(fontInfo, character) == 0
			? fallback
			: character;
	}

	private void AddGlyph(char requested)
	{
		if (glyphs.ContainsKey(requested))
		{
			return;
		}

		char character = Normalize(requested);

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

	private Glyph RasterizeGlyph(char character)
	{
		int advance;
		int bearing;
		stbtt_GetCodepointHMetrics(fontInfo, character, &advance, &bearing);

		int width = 0;
		int height = 0;
		int xOffset = 0;
		int yOffset = 0;
		byte* bitmap = stbtt_GetCodepointSDF(
			fontInfo,
			scale,
			character,
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
		while (true)
		{
			if (TryPack(out byte[] pixels))
			{
				atlasPixels = pixels;
				atlasVersion++;

				return true;
			}

			if (atlasSize >= options.MaximumAtlasSize)
			{
				return false;
			}

			atlasSize = Math.Min(atlasSize * 2, options.MaximumAtlasSize);
		}
	}

	private bool TryPack(out byte[] pixels)
	{
		pixels = new byte[atlasSize * atlasSize];
		int x = 1;
		int y = 1;
		int rowHeight = 0;

		foreach (Glyph glyph in glyphs.Values.Distinct().OrderBy(value => value.Character))
		{
			if (glyph.Width == 0 || glyph.Height == 0)
			{
				glyph.X = 0;
				glyph.Y = 0;

				continue;
			}

			if (x + glyph.Width + 1 > atlasSize)
			{
				x = 1;
				y += rowHeight + 1;
				rowHeight = 0;
			}

			if (y + glyph.Height + 1 > atlasSize)
			{
				return false;
			}

			glyph.X = x;
			glyph.Y = y;
			CopyGlyphRows(glyph, pixels);
			x += glyph.Width + 1;
			rowHeight = Math.Max(rowHeight, glyph.Height);
		}

		return true;
	}

	private void CopyGlyphRows(Glyph glyph, byte[] pixels)
	{
		for (int row = 0; row < glyph.Height; row++)
		{
			Buffer.BlockCopy(
				glyph.Bitmap,
				row * glyph.Width,
				pixels,
				(glyph.Y + row) * atlasSize + glyph.X,
				glyph.Width
			);
		}
	}

	private sealed class Glyph
	{
		internal char Character { get; init; }

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
