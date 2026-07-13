using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FishGfx.Graphics;
using StbTrueTypeSharp;
using static StbTrueTypeSharp.StbTrueType;

namespace FishGfx.Formats
{
	public sealed class TTFFontOptions
	{
		public int BasePixelHeight { get; set; } = 48;
		public int InitialAtlasSize { get; set; } = 512;
		public int MaximumAtlasSize { get; set; } = 4096;
		public int SdfPadding { get; set; } = 8;
		public byte SdfOnEdgeValue { get; set; } = 128;
		public float SdfPixelDistanceScale { get; set; } = 64;
		public bool PreloadPrintableAscii { get; set; } = true;
	}

	public sealed unsafe class TTFFont : GfxFont, IGfxAtlasFont, IDisposable
	{
		private sealed class Glyph
		{
			internal char Codepoint;
			internal int Width;
			internal int Height;
			internal int X;
			internal int Y;
			internal int XOffset;
			internal int YOffset;
			internal int Advance;
			internal byte[] Bitmap;
		}

		private readonly byte[] fontData;
		private readonly GCHandle fontDataHandle;
		private readonly stbtt_fontinfo fontInfo;
		private readonly TTFFontOptions options;
		private readonly Dictionary<char, Glyph> glyphs = new Dictionary<char, Glyph>();
		private readonly char fallback;
		private readonly float scale;
		private byte[] atlasPixels;
		private Texture atlasTexture;
		private bool atlasDirty = true;
		private bool disposed;
		private int atlasSize;
		private int lineHeight;
		private int tabSize;
		private int ascentPixels;

		public TTFFont(string fileName, TTFFontOptions options = null)
			: this(
				File.ReadAllBytes(fileName ?? throw new ArgumentNullException(nameof(fileName))),
				Path.GetFileNameWithoutExtension(fileName),
				options
			)
		{ }

		public TTFFont(byte[] data, string fontName = "TrueType", TTFFontOptions options = null)
		{
			if (data == null)
				throw new ArgumentNullException(nameof(data));

			if (data.Length == 0)
				throw new ArgumentException("Font data is empty.", nameof(data));

			TTFFontOptions source = options ?? new TTFFontOptions();
			ValidateOptions(source);

			this.options = new TTFFontOptions
			{
				BasePixelHeight = source.BasePixelHeight,
				InitialAtlasSize = source.InitialAtlasSize,
				MaximumAtlasSize = source.MaximumAtlasSize,
				SdfPadding = source.SdfPadding,
				SdfOnEdgeValue = source.SdfOnEdgeValue,
				SdfPixelDistanceScale = source.SdfPixelDistanceScale,
				PreloadPrintableAscii = source.PreloadPrintableAscii,
			};

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

			FontNameValue = string.IsNullOrWhiteSpace(fontName) ? "TrueType" : fontName;
			scale = stbtt_ScaleForPixelHeight(fontInfo, this.options.BasePixelHeight);

			int ascent;
			int descent;
			int gap;

			stbtt_GetFontVMetrics(fontInfo, &ascent, &descent, &gap);

			ascentPixels = (int)MathF.Round(ascent * scale);
			lineHeight = Math.Max(1, (int)MathF.Ceiling((ascent - descent + gap) * scale));
			atlasSize = this.options.InitialAtlasSize;
			atlasPixels = new byte[atlasSize * atlasSize];
			fallback = stbtt_FindGlyphIndex(fontInfo, 0xFFFD) != 0 ? '\uFFFD' : '?';
			AddGlyph(fallback);

			if (this.options.PreloadPrintableAscii)
			{
				for (char c = ' '; c <= '~'; c++)
				{
					AddGlyph(c);
				}
			}

			Glyph space = GetGlyph(' ');
			tabSize = Math.Max(1, space.Advance * 4);
			ScaledFontSize = FontSize;
		}

		public override string FontName => FontNameValue;
		private string FontNameValue { get; }
		public override int LineHeight => lineHeight;
		public override int FontSize => options.BasePixelHeight;
		public override int TabSize => tabSize;
		public Texture AtlasTexture => atlasTexture;
		public GfxFontRenderMode RenderMode => GfxFontRenderMode.SignedDistanceField;
		public float SdfPixelRange =>
			Math.Min(options.SdfOnEdgeValue, 255 - options.SdfOnEdgeValue) / options.SdfPixelDistanceScale;
		internal int AtlasSize => atlasSize;
		internal int GlyphCount => glyphs.Values.Distinct().Count();

		internal byte GetGlyphBorderMaximum(char c)
		{
			Glyph glyph = GetGlyph(c);

			if (glyph.Width <= 0 || glyph.Height <= 0 || glyph.Bitmap == null || glyph.Bitmap.Length == 0)
				return 0;

			byte maximum = 0;

			for (int x = 0; x < glyph.Width; x++)
			{
				maximum = Math.Max(maximum, glyph.Bitmap[x]);
				maximum = Math.Max(maximum, glyph.Bitmap[(glyph.Height - 1) * glyph.Width + x]);
			}

			for (int y = 0; y < glyph.Height; y++)
			{
				maximum = Math.Max(maximum, glyph.Bitmap[y * glyph.Width]);
				maximum = Math.Max(maximum, glyph.Bitmap[y * glyph.Width + glyph.Width - 1]);
			}

			return maximum;
		}

		public void PrepareText(string text)
		{
			ThrowIfDisposed();

			if (text != null)
			{
				foreach (char c in text)
				{
					if (!char.IsSurrogate(c) && c != '\r' && c != '\n' && c != '\t')
					{
						AddGlyph(c);
					}
					else if (char.IsSurrogate(c))
					{
						AddAlias(c, fallback);
					}
				}
			}

			if (!atlasDirty && atlasTexture != null)
			{
				return;
			}

			Texture replacement = GraphicsContext.Current.CreateTexture(new TextureDescriptor(
				atlasSize,
				atlasSize,
				TextureFormat.R8Unorm,
				TextureUsageFlags.Sampled | TextureUsageFlags.TransferDestination,
				sampling: new TextureSamplingState(TextureFilter.Linear, TextureFilter.Linear)
			));
			byte[] flipped = new byte[atlasPixels.Length];

			for (int y = 0; y < atlasSize; y++)
			{
				Buffer.BlockCopy(atlasPixels, y * atlasSize, flipped, (atlasSize - y - 1) * atlasSize, atlasSize);
			}

			replacement.Write<byte>(flipped, TextureDataFormat.R8Unorm);

			atlasTexture?.Dispose();
			atlasTexture = replacement;
			atlasDirty = false;
		}

		public override CharOrigin? GetCharInfo(char c)
		{
			ThrowIfDisposed();

			if (char.IsSurrogate(c))
				c = fallback;
			Glyph glyph = GetGlyph(c);
			return new CharOrigin
			{
				Char = c,
				Owner = this,
				X = glyph.X,
				Y = glyph.Y,
				W = glyph.Width,
				H = glyph.Height,
				XOffset = glyph.XOffset,
				YOffset = glyph.YOffset,
				XAdvance = glyph.Advance,
			};
		}

		public override int GetKerning(char first, char second)
		{
			ThrowIfDisposed();
			first = Normalize(first);
			second = Normalize(second);
			return (int)MathF.Round(stbtt_GetCodepointKernAdvance(fontInfo, first, second) * scale);
		}

		private Glyph GetGlyph(char c)
		{
			if (!glyphs.TryGetValue(c, out Glyph glyph))
			{
				AddGlyph(c);
				glyph = glyphs[c];
			}

			return glyph;
		}

		private void AddAlias(char c, char target)
		{
			if (!glyphs.ContainsKey(c))
			{
				glyphs[c] = GetGlyph(target);
			}
		}

		private char Normalize(char c) => char.IsSurrogate(c) || stbtt_FindGlyphIndex(fontInfo, c) == 0 ? fallback : c;

		private void AddGlyph(char requested)
		{
			if (glyphs.ContainsKey(requested))
			{
				return;
			}

			char c = Normalize(requested);

			if (c != requested)
			{
				AddAlias(requested, c);
				return;
			}

			int advance;
			int bearing;
			stbtt_GetCodepointHMetrics(fontInfo, c, &advance, &bearing);
			int width = 0;
			int height = 0;
			int xOffset = 0;
			int yOffset = 0;
			byte* bitmap = stbtt_GetCodepointSDF(
				fontInfo,
				scale,
				c,
				options.SdfPadding,
				options.SdfOnEdgeValue,
				options.SdfPixelDistanceScale,
				&width,
				&height,
				&xOffset,
				&yOffset
			);
			byte[] data = width > 0 && height > 0 ? new byte[width * height] : Array.Empty<byte>();

			if (bitmap != null)
			{
				Marshal.Copy((IntPtr)bitmap, data, 0, data.Length);
				stbtt_FreeSDF(bitmap, null);
			}

			Glyph glyph = new Glyph
			{
				Codepoint = c,
				Width = width,
				Height = height,
				XOffset = xOffset,
				YOffset = ascentPixels + yOffset,
				Advance = (int)MathF.Round(advance * scale),
				Bitmap = data,
			};
			glyphs[c] = glyph;

			if (!Repack())
			{
				glyphs.Remove(c);

				if (c != fallback)
					AddAlias(requested, fallback);
			}
		}

		private bool Repack()
		{
			while (true)
			{
				byte[] pixels = new byte[atlasSize * atlasSize];
				int x = 1;
				int y = 1;
				int rowHeight = 0;
				bool fits = true;

				foreach (Glyph glyph in glyphs.Values.Distinct().OrderBy(g => g.Codepoint))
				{
					if (glyph.Width == 0 || glyph.Height == 0)
					{
						glyph.X = glyph.Y = 0;
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
						fits = false;
						break;
					}

					glyph.X = x;
					glyph.Y = y;

					for (int row = 0; row < glyph.Height; row++)
					{
						Buffer.BlockCopy(
							glyph.Bitmap,
							row * glyph.Width,
							pixels,
							(y + row) * atlasSize + x,
							glyph.Width
						);
					}

					x += glyph.Width + 1;
					rowHeight = Math.Max(rowHeight, glyph.Height);
				}

				if (fits)
				{
					atlasPixels = pixels;
					atlasDirty = true;
					return true;
				}

				if (atlasSize >= options.MaximumAtlasSize)
				{
					return false;
				}

				atlasSize = Math.Min(atlasSize * 2, options.MaximumAtlasSize);
			}
		}

		private static void ValidateOptions(TTFFontOptions value)
		{
			if (value.BasePixelHeight <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(value.BasePixelHeight));
			}

			if (
				value.InitialAtlasSize < 32
				|| value.MaximumAtlasSize < value.InitialAtlasSize
				|| !PowerOfTwo(value.InitialAtlasSize)
				|| !PowerOfTwo(value.MaximumAtlasSize)
			)
			{
				throw new ArgumentOutOfRangeException(
					nameof(value.InitialAtlasSize),
					"Atlas sizes must be powers of two and maximum must be at least initial."
				);
			}

			if (
				value.SdfPadding < 1
				|| value.SdfPixelDistanceScale <= 0
				|| !float.IsFinite(value.SdfPixelDistanceScale)
			)
			{
				throw new ArgumentOutOfRangeException(nameof(value.SdfPadding));
			}
		}

		private static bool PowerOfTwo(int value) => (value & (value - 1)) == 0;

		private void ThrowIfDisposed()
		{
			if (disposed)
			{
				throw new ObjectDisposedException(nameof(TTFFont));
			}
		}

		~TTFFont()
		{
			Dispose(false);
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		private void Dispose(bool disposing)
		{
			if (disposed)
			{
				return;
			}

			disposed = true;
			atlasTexture?.Dispose();

			if (fontDataHandle.IsAllocated)
			{
				fontDataHandle.Free();
			}
		}
	}
}
