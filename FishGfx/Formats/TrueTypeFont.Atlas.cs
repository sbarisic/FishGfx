using System;
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
		foreach (char character in text)
		{
			if (character is '\r' or '\n' or '\t')
			{
				continue;
			}

			if (char.IsSurrogate(character))
			{
				AddAlias(character, fallback);
			}
			else
			{
				AddGlyph(character);
			}
		}
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
