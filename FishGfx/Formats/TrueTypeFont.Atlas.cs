using System;
using System.Text;
using FishGfx.Graphics;

namespace FishGfx.Formats;

public sealed unsafe partial class TrueTypeFont
{
	public override FontAtlas PrepareAtlas(GraphicsContext graphics, string text)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(graphics);
		ArgumentNullException.ThrowIfNull(text);
		graphics.EnsureCurrent();
		PrepareGlyphs(text);

		if (atlases.TryGetValue(graphics, out AtlasCache cached)
			&& cached.Version == atlasVersion
			&& !cached.Atlas.IsDisposed)
		{
			return cached.Atlas;
		}

		Texture texture = graphics.CreateTexture(
			new TextureDescriptor(
				atlasSize,
				atlasSize,
				TextureFormat.R8Unorm,
				TextureUsageFlags.Sampled | TextureUsageFlags.TransferDestination,
				sampling: new TextureSamplingState(
					TextureFilter.Linear,
					TextureFilter.Linear
				)
			)
		);

		try
		{
			texture.Write<byte>(FlipAtlasVertically(), TextureDataFormat.R8Unorm);
		}
		catch
		{
			texture.Dispose();

			throw;
		}

		FontAtlas atlas = new(graphics, texture, RenderMode, SdfPixelRange);

		if (cached != null)
		{
			cached.Atlas.Dispose();
		}

		atlases[graphics] = new AtlasCache(atlas, atlasVersion);

		return atlas;
	}

    private void PrepareGlyphs(string text)
    {
        System.Collections.Generic.List<Rune> added = null;
        foreach (Rune character in text.EnumerateRunes())
        {
            if (character.Value is '\r' or '\n' or '\t' || glyphs.ContainsKey(character)) continue;
            Rune normalized = Normalize(character);
            if (normalized != character) { AddAlias(character, fallback); continue; }
            glyphs.Add(character, RasterizeGlyph(character));
            (added ??= new()).Add(character);
        }
        if (added == null || Repack()) return;
        // Retry individually so a large request still admits the glyphs that fit.
        foreach (Rune character in added) glyphs.Remove(character);
        foreach (Rune character in added) AddGlyph(character);
    }

	private byte[] FlipAtlasVertically()
	{
		byte[] flipped = new byte[atlasPixels.Length];

		for (int y = 0; y < atlasSize; y++)
		{
			Buffer.BlockCopy(
				atlasPixels,
				y * atlasSize,
				flipped,
				(atlasSize - y - 1) * atlasSize,
				atlasSize
			);
		}

		return flipped;
	}

	private sealed record AtlasCache(FontAtlas Atlas, int Version);
}
