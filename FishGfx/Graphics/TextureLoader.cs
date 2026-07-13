using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace FishGfx.Graphics
{
	public sealed class TextureLoadOptions
	{
		public bool FlipY { get; set; } = true;
		public TextureFormat Format { get; set; } = TextureFormat.RGBA8Unorm;
		public int MipLevels { get; set; } = 1;
		public TextureSamplingState Sampling { get; set; } = TextureSamplingState.Default;
	}

	public readonly struct CubemapPaths
	{
		public CubemapPaths(string left, string front, string right, string back, string bottom, string top)
		{
			Left = Required(left, nameof(left)); Front = Required(front, nameof(front)); Right = Required(right, nameof(right));
			Back = Required(back, nameof(back)); Bottom = Required(bottom, nameof(bottom)); Top = Required(top, nameof(top));
		}
		public string Left { get; } public string Front { get; } public string Right { get; }
		public string Back { get; } public string Bottom { get; } public string Top { get; }
		private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A cubemap path is required.", name) : value;
	}

	public static class TextureLoader
	{
		public static Texture Load2D(GraphicsContext context, string path, TextureLoadOptions options = null)
		{
			if (context == null) throw new ArgumentNullException(nameof(context));
			if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A texture path is required.", nameof(path));
			context.EnsureCurrent();
			using Image image = Image.FromFile(path);
			return FromImage(context, image, options);
		}

		public static Texture FromImage(GraphicsContext context, Image image, TextureLoadOptions options = null)
		{
			if (context == null) throw new ArgumentNullException(nameof(context));
			if (image == null) throw new ArgumentNullException(nameof(image));
			context.EnsureCurrent();
			options ??= new TextureLoadOptions();
			ValidateColorFormat(options.Format);
			Texture texture = context.CreateTexture(new TextureDescriptor(image.Width, image.Height, options.Format,
				TextureUsageFlags.Sampled | TextureUsageFlags.TransferDestination, mipLevels: options.MipLevels, sampling: options.Sampling));
			try
			{
				Update2D(texture, image, options.FlipY);
				if (options.MipLevels > 1) texture.GenerateMipmaps();
				return texture;
			}
			catch { texture.Dispose(); throw; }
		}

		public static void Update2D(Texture texture, Image image, bool flipY = false, int x = 0, int y = 0)
		{
			if (texture == null) throw new ArgumentNullException(nameof(texture));
			if (image == null) throw new ArgumentNullException(nameof(image));
			if (texture.IsCubeMap || texture.Multisampled) throw new ArgumentException("Update2D requires a non-multisampled 2D texture.", nameof(texture));
			byte[] pixels = ExtractBgra(image, flipY);
			texture.Write<byte>(pixels, TextureDataFormat.BGRA8Unorm, new TextureRegion(x, y, image.Width, image.Height));
		}

		public static Texture[] LoadAtlas(GraphicsContext context, string path, int tileWidth, int tileHeight, TextureLoadOptions options = null)
		{
			if (context == null) throw new ArgumentNullException(nameof(context));
			if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An atlas path is required.", nameof(path));
			if (tileWidth <= 0 || tileHeight <= 0) throw new ArgumentOutOfRangeException(nameof(tileWidth));
			context.EnsureCurrent();
			using Image source = Image.FromFile(path);
			if (source.Width % tileWidth != 0 || source.Height % tileHeight != 0)
				throw new ArgumentException("Atlas dimensions must be exactly divisible by the tile dimensions.", nameof(path));
			using Bitmap sourceBitmap = new Bitmap(source);
			List<Texture> textures = new List<Texture>();
			try
			{
				for (int y = 0; y < source.Height; y += tileHeight)
				for (int x = 0; x < source.Width; x += tileWidth)
				{
					using Bitmap tile = sourceBitmap.Clone(new Rectangle(x, y, tileWidth, tileHeight), DrawingPixelFormat.Format32bppArgb);
					textures.Add(FromImage(context, tile, options));
				}
				return textures.ToArray();
			}
			catch { foreach (Texture texture in textures) texture.Dispose(); throw; }
		}

		public static Texture LoadCubemap(GraphicsContext context, CubemapPaths paths, TextureLoadOptions options = null)
		{
			if (context == null) throw new ArgumentNullException(nameof(context));
			context.EnsureCurrent();
			using Image left = Image.FromFile(paths.Left);
			using Image front = Image.FromFile(paths.Front);
			using Image right = Image.FromFile(paths.Right);
			using Image back = Image.FromFile(paths.Back);
			using Image bottom = Image.FromFile(paths.Bottom);
			using Image top = Image.FromFile(paths.Top);
			return LoadCubemap(context, left, front, right, back, bottom, top, options);
		}

		public static Texture LoadCubemap(GraphicsContext context, Image left, Image front, Image right, Image back, Image bottom, Image top, TextureLoadOptions options = null)
		{
			if (context == null) throw new ArgumentNullException(nameof(context));
			context.EnsureCurrent();
			Image[] images = { right, left, top, bottom, front, back };
			for (int i = 0; i < images.Length; i++) if (images[i] == null) throw new ArgumentNullException(nameof(images));
			int size = images[0].Width;
			if (size != images[0].Height) throw new ArgumentException("Cubemap images must be square.");
			for (int i = 1; i < images.Length; i++) if (images[i].Width != size || images[i].Height != size)
				throw new ArgumentException("All cubemap images must be square and identically sized.");
			options ??= new TextureLoadOptions { Sampling = new TextureSamplingState(TextureFilter.Linear, TextureFilter.Linear) };
			ValidateColorFormat(options.Format);
			Texture texture = context.CreateTexture(new TextureDescriptor(size, size, options.Format,
				TextureUsageFlags.Sampled | TextureUsageFlags.TransferDestination, TextureDimension.Cube, options.MipLevels, sampling: options.Sampling));
			try
			{
				for (int face = 0; face < 6; face++)
				{
					byte[] pixels = ExtractBgra(images[face], options.FlipY);
					texture.Write<byte>(pixels, TextureDataFormat.BGRA8Unorm, new TextureSubresource(0, (CubeFace)face));
				}
				if (options.MipLevels > 1) texture.GenerateMipmaps();
				return texture;
			}
			catch { texture.Dispose(); throw; }
		}

		private static byte[] ExtractBgra(Image image, bool flipY)
		{
			using Bitmap source = new Bitmap(image);
			using Bitmap bitmap = source.Clone(new Rectangle(0, 0, source.Width, source.Height), DrawingPixelFormat.Format32bppArgb);
			// File images are top-down while OpenGL texture coordinates conventionally start at the bottom-left.
			if (flipY) bitmap.RotateFlip(RotateFlipType.RotateNoneFlipY);
			BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppArgb);
			try
			{
				int rowBytes = checked(bitmap.Width * 4);
				byte[] pixels = new byte[checked(rowBytes * bitmap.Height)];
				for (int y = 0; y < bitmap.Height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * rowBytes, rowBytes);
				return pixels;
			}
			finally { bitmap.UnlockBits(data); }
		}

		private static void ValidateColorFormat(TextureFormat format)
		{
			if (Texture.IsDepthFormat(format)) throw new ArgumentException("Image loading requires a color texture format.", nameof(format));
		}
	}
}
