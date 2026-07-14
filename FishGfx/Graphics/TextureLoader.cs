using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace FishGfx.Graphics;

internal static class TextureLoader
{
	internal static Texture Load(
		GraphicsContext context,
		string path,
		TextureLoadOptions options = null
	)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		context.EnsureCurrent();

		using Image image = Image.FromFile(path);

		return CreateFromImage(context, image, options);
	}

	internal static Texture CreateFromImage(
		GraphicsContext context,
		Image image,
		TextureLoadOptions options = null
	)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(image);
		context.EnsureCurrent();

		options ??= new TextureLoadOptions();
		ValidateColorFormat(options.Format);

		TextureDescriptor descriptor = new(
			image.Width,
			image.Height,
			options.Format,
			TextureUsageFlags.Sampled | TextureUsageFlags.TransferDestination,
			mipLevels: options.MipLevels,
			sampling: options.Sampling
		);
		Texture texture = context.CreateTexture(descriptor);

		try
		{
			Update(texture, image, options.FlipY);

			if (options.MipLevels > 1)
			{
				texture.GenerateMipmaps();
			}

			return texture;
		}
		catch
		{
			texture.Dispose();

			throw;
		}
	}

	internal static void Update(
		Texture texture,
		Image image,
		bool flipY = false,
		int x = 0,
		int y = 0
	)
	{
		ArgumentNullException.ThrowIfNull(texture);
		ArgumentNullException.ThrowIfNull(image);

		if (texture.IsCubeMap || texture.Multisampled)
		{
			throw new ArgumentException(
				"Updating an image requires a non-multisampled 2D texture.",
				nameof(texture)
			);
		}

		byte[] pixels = ExtractBgra(image, flipY);
		TextureRegion region = new(x, y, image.Width, image.Height);
		texture.Write<byte>(pixels, TextureDataFormat.BGRA8Unorm, region);
	}

	internal static Texture[] LoadAtlas(
		GraphicsContext context,
		string path,
		int tileWidth,
		int tileHeight,
		TextureLoadOptions options = null
	)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		if (tileWidth <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(tileWidth));
		}

		if (tileHeight <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(tileHeight));
		}

		context.EnsureCurrent();

		using Image source = Image.FromFile(path);

		if (source.Width % tileWidth != 0 || source.Height % tileHeight != 0)
		{
			throw new ArgumentException(
				"Atlas dimensions must be exactly divisible by the tile dimensions.",
				nameof(path)
			);
		}

		using Bitmap sourceBitmap = new(source);
		List<Texture> textures = new();

		try
		{
			for (int y = 0; y < source.Height; y += tileHeight)
			{
				for (int x = 0; x < source.Width; x += tileWidth)
				{
					Rectangle area = new(x, y, tileWidth, tileHeight);

					using Bitmap tile = sourceBitmap.Clone(
						area,
						DrawingPixelFormat.Format32bppArgb
					);

					textures.Add(CreateFromImage(context, tile, options));
				}
			}

			return textures.ToArray();
		}
		catch
		{
			foreach (Texture texture in textures)
			{
				texture.Dispose();
			}

			throw;
		}
	}

	internal static Texture LoadCubemap(
		GraphicsContext context,
		CubemapPaths paths,
		TextureLoadOptions options = null
	)
	{
		ArgumentNullException.ThrowIfNull(context);
		context.EnsureCurrent();

		using Image left = Image.FromFile(paths.Left);
		using Image front = Image.FromFile(paths.Front);
		using Image right = Image.FromFile(paths.Right);
		using Image back = Image.FromFile(paths.Back);
		using Image bottom = Image.FromFile(paths.Bottom);
		using Image top = Image.FromFile(paths.Top);

		return LoadCubemap(
			context,
			left,
			front,
			right,
			back,
			bottom,
			top,
			options
		);
	}

	internal static Texture LoadCubemap(
		GraphicsContext context,
		Image left,
		Image front,
		Image right,
		Image back,
		Image bottom,
		Image top,
		TextureLoadOptions options = null
	)
	{
		ArgumentNullException.ThrowIfNull(context);
		context.EnsureCurrent();

		Image[] images = { right, left, top, bottom, front, back };
		ValidateCubemapImages(images);
		int size = images[0].Width;
		options ??= new TextureLoadOptions
		{
			Sampling = new TextureSamplingState(
				TextureFilter.Linear,
				TextureFilter.Linear
			),
		};
		ValidateColorFormat(options.Format);

		TextureDescriptor descriptor = new(
			size,
			size,
			options.Format,
			TextureUsageFlags.Sampled | TextureUsageFlags.TransferDestination,
			TextureDimension.Cube,
			options.MipLevels,
			sampling: options.Sampling
		);
		Texture texture = context.CreateTexture(descriptor);

		try
		{
			for (int face = 0; face < images.Length; face++)
			{
				byte[] pixels = ExtractBgra(images[face], options.FlipY);
				TextureSubresource subresource = new(0, (CubeFace)face);
				texture.Write<byte>(
					pixels,
					TextureDataFormat.BGRA8Unorm,
					subresource
				);
			}

			if (options.MipLevels > 1)
			{
				texture.GenerateMipmaps();
			}

			return texture;
		}
		catch
		{
			texture.Dispose();

			throw;
		}
	}

	private static byte[] ExtractBgra(Image image, bool flipY)
	{
		using Bitmap source = new(image);
		Rectangle area = new(0, 0, source.Width, source.Height);

		using Bitmap bitmap = source.Clone(
			area,
			DrawingPixelFormat.Format32bppArgb
		);

		if (flipY)
		{
			bitmap.RotateFlip(RotateFlipType.RotateNoneFlipY);
		}

		BitmapData data = bitmap.LockBits(
			area,
			ImageLockMode.ReadOnly,
			DrawingPixelFormat.Format32bppArgb
		);

		try
		{
			int rowBytes = checked(bitmap.Width * 4);
			byte[] pixels = new byte[checked(rowBytes * bitmap.Height)];

			for (int y = 0; y < bitmap.Height; y++)
			{
				Marshal.Copy(
					data.Scan0 + y * data.Stride,
					pixels,
					y * rowBytes,
					rowBytes
				);
			}

			return pixels;
		}
		finally
		{
			bitmap.UnlockBits(data);
		}
	}

	private static void ValidateCubemapImages(Image[] images)
	{
		for (int index = 0; index < images.Length; index++)
		{
			if (images[index] == null)
			{
				throw new ArgumentNullException(
					nameof(images),
					$"Cubemap image {index} is null."
				);
			}
		}

		int size = images[0].Width;

		if (size != images[0].Height)
		{
			throw new ArgumentException("Cubemap images must be square.");
		}

		for (int index = 1; index < images.Length; index++)
		{
			if (images[index].Width != size || images[index].Height != size)
			{
				throw new ArgumentException(
					"All cubemap images must be square and identically sized."
				);
			}
		}
	}

	private static void ValidateColorFormat(TextureFormat format)
	{
		if (Texture.IsDepthFormat(format))
		{
			throw new ArgumentException(
				"Image loading requires a color texture format.",
				nameof(format)
			);
		}
	}
}
