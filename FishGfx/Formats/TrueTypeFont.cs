using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using FishGfx.Graphics;
using StbTrueTypeSharp;
using static StbTrueTypeSharp.StbTrueType;

namespace FishGfx.Formats;

public sealed class TrueTypeFontOptions
{
	public int BasePixelHeight { get; init; } = 48;

	public int InitialAtlasSize { get; init; } = 512;

	public int MaximumAtlasSize { get; init; } = 4096;

	public int SdfPadding { get; init; } = 8;

	public byte SdfOnEdgeValue { get; init; } = 128;

	public float SdfPixelDistanceScale { get; init; } = 64;

	public bool PreloadPrintableAscii { get; init; } = true;
}

public sealed unsafe partial class TrueTypeFont : GraphicsFont
{
	private readonly byte[] fontData;
	private readonly GCHandle fontDataHandle;
	private readonly stbtt_fontinfo fontInfo;
	private readonly TrueTypeFontOptions options;
	private readonly Dictionary<char, Glyph> glyphs = new();
	private readonly Dictionary<GraphicsContext, AtlasCache> atlases = new();
	private readonly char fallback;
	private readonly float scale;
	private readonly int ascentPixels;
	private readonly float lineHeight;
	private readonly float tabWidth;
	private byte[] atlasPixels;
	private int atlasSize;
	private int atlasVersion;
	private bool disposed;

	public TrueTypeFont(string path, TrueTypeFontOptions options = null)
		: this(
			File.ReadAllBytes(path ?? throw new ArgumentNullException(nameof(path))),
			Path.GetFileNameWithoutExtension(path),
			options
		)
	{
	}

	public TrueTypeFont(
		byte[] data,
		string name = "TrueType",
		TrueTypeFontOptions options = null
	)
	{
		ArgumentNullException.ThrowIfNull(data);

		if (data.Length == 0)
		{
			throw new ArgumentException("Font data is empty.", nameof(data));
		}

		this.options = CopyAndValidate(options ?? new TrueTypeFontOptions());
		fontData = (byte[])data.Clone();
		fontDataHandle = GCHandle.Alloc(fontData, GCHandleType.Pinned);
		fontInfo = new stbtt_fontinfo();
		byte* pointer = (byte*)fontDataHandle.AddrOfPinnedObject();
		int offset = stbtt_GetFontOffsetForIndex(pointer, 0);

		if (offset < 0 || stbtt_InitFont(fontInfo, pointer, offset) == 0)
		{
			fontDataHandle.Free();

			throw new ArgumentException("Data is not a supported TrueType font.", nameof(data));
		}

		Name = string.IsNullOrWhiteSpace(name) ? "TrueType" : name;
		scale = stbtt_ScaleForPixelHeight(fontInfo, this.options.BasePixelHeight);

		int ascent;
		int descent;
		int lineGap;
		stbtt_GetFontVMetrics(fontInfo, &ascent, &descent, &lineGap);
		ascentPixels = (int)MathF.Round(ascent * scale);
		lineHeight = Math.Max(1, MathF.Ceiling((ascent - descent + lineGap) * scale));
		atlasSize = this.options.InitialAtlasSize;
		atlasPixels = new byte[atlasSize * atlasSize];
		fallback = stbtt_FindGlyphIndex(fontInfo, 0xFFFD) != 0 ? '\uFFFD' : '?';
		AddGlyph(fallback);

		if (this.options.PreloadPrintableAscii)
		{
			for (char character = ' '; character <= '~'; character++)
			{
				AddGlyph(character);
			}
		}

		tabWidth = Math.Max(1, ResolveGlyph(' ').Advance * 4);
	}

	public override string Name { get; }

	public override float BaseSize => options.BasePixelHeight;

	public override float LineHeight => lineHeight;

	public override float TabWidth => tabWidth;

	public override FontRenderMode RenderMode => FontRenderMode.SignedDistanceField;

	public override float SdfPixelRange => Math.Min(
		options.SdfOnEdgeValue,
		byte.MaxValue - options.SdfOnEdgeValue
	) / options.SdfPixelDistanceScale;

	internal int AtlasSize => atlasSize;

	internal int GlyphCount => glyphs.Values.Distinct().Count();

	public override GlyphMetrics? GetGlyph(char character)
	{
		ThrowIfDisposed();
		Glyph glyph = ResolveGlyph(character);

		return new GlyphMetrics(
			character,
			new Vector2(glyph.X, glyph.Y),
			new Vector2(glyph.Width, glyph.Height),
			new Vector2(glyph.XOffset, glyph.YOffset),
			glyph.Advance
		);
	}

	public override float GetKerning(char first, char second)
	{
		ThrowIfDisposed();
		first = Normalize(first);
		second = Normalize(second);

		return MathF.Round(stbtt_GetCodepointKernAdvance(fontInfo, first, second) * scale);
	}

	public override void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	private static TrueTypeFontOptions CopyAndValidate(TrueTypeFontOptions value)
	{
		if (value.BasePixelHeight <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(value.BasePixelHeight));
		}

		if (value.InitialAtlasSize < 32
			|| value.MaximumAtlasSize < value.InitialAtlasSize
			|| !IsPowerOfTwo(value.InitialAtlasSize)
			|| !IsPowerOfTwo(value.MaximumAtlasSize))
		{
			throw new ArgumentOutOfRangeException(
				nameof(value.InitialAtlasSize),
				"Atlas sizes must be powers of two and maximum must be at least initial."
			);
		}

		if (value.SdfPadding < 1
			|| !float.IsFinite(value.SdfPixelDistanceScale)
			|| value.SdfPixelDistanceScale <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(value.SdfPadding));
		}

		return new TrueTypeFontOptions
		{
			BasePixelHeight = value.BasePixelHeight,
			InitialAtlasSize = value.InitialAtlasSize,
			MaximumAtlasSize = value.MaximumAtlasSize,
			SdfPadding = value.SdfPadding,
			SdfOnEdgeValue = value.SdfOnEdgeValue,
			SdfPixelDistanceScale = value.SdfPixelDistanceScale,
			PreloadPrintableAscii = value.PreloadPrintableAscii,
		};
	}

	private static bool IsPowerOfTwo(int value)
	{
		return (value & (value - 1)) == 0;
	}

	private void ThrowIfDisposed()
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(TrueTypeFont));
		}
	}

	private void Dispose(bool disposing)
	{
		if (disposed)
		{
			return;
		}

		disposed = true;

		foreach (AtlasCache cache in atlases.Values)
		{
			cache.Atlas.Dispose();
		}

		atlases.Clear();

		if (fontDataHandle.IsAllocated)
		{
			fontDataHandle.Free();
		}
	}

	~TrueTypeFont()
	{
		Dispose(false);
	}

}
