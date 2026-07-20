using System;

namespace FishGfx.Graphics;

public unsafe sealed partial class Texture
{
	private const TextureUsageFlags AllUsage =
		TextureUsageFlags.Sampled |
		TextureUsageFlags.ColorAttachment |
		TextureUsageFlags.DepthStencilAttachment |
		TextureUsageFlags.TransferSource |
		TextureUsageFlags.TransferDestination;

	private static void ValidateDescriptor(TextureDescriptor descriptor)
	{
		if (descriptor.Width <= 0
			|| descriptor.Height <= 0
			|| descriptor.Depth <= 0
			|| descriptor.ArrayLayers <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(descriptor),
				"Texture dimensions must be positive."
			);
		}

		if (!Enum.IsDefined(descriptor.Format)
			|| !Enum.IsDefined(descriptor.Dimension))
		{
			throw new ArgumentOutOfRangeException(nameof(descriptor));
		}

		if (descriptor.Usage == TextureUsageFlags.None
			|| (descriptor.Usage & ~AllUsage) != 0)
		{
			throw new ArgumentOutOfRangeException(nameof(descriptor));
		}

		if (descriptor.Dimension == TextureDimension.Cube
			&& descriptor.Width != descriptor.Height)
		{
			throw new ArgumentException(
				"Cube textures must be square.",
				nameof(descriptor)
			);
		}

		if (descriptor.Dimension != TextureDimension.Texture3D
			&& descriptor.Depth != 1)
		{
			throw new ArgumentException(
				"Only three-dimensional textures may have depth greater than one.",
				nameof(descriptor)
			);
		}

		if (descriptor.Dimension != TextureDimension.Texture2DArray
			&& descriptor.ArrayLayers != 1)
		{
			throw new ArgumentException(
				"Only two-dimensional array textures may have multiple layers.",
				nameof(descriptor)
			);
		}

		if (descriptor.Dimension == TextureDimension.Texture3D
			&& (descriptor.Usage & (TextureUsageFlags.ColorAttachment |
				TextureUsageFlags.DepthStencilAttachment)) != 0)
		{
			throw new ArgumentException(
				"Three-dimensional textures cannot be render-target attachments.",
				nameof(descriptor)
			);
		}

		int maximumMipExtent = descriptor.Dimension == TextureDimension.Texture3D
			? Math.Max(Math.Max(descriptor.Width, descriptor.Height), descriptor.Depth)
			: Math.Max(descriptor.Width, descriptor.Height);
		int maximumMips = 1 + (int)Math.Floor(Math.Log2(maximumMipExtent));

		if (descriptor.MipLevels <= 0 || descriptor.MipLevels > maximumMips)
		{
			throw new ArgumentOutOfRangeException(nameof(descriptor));
		}

		ValidateDescriptorSamples(descriptor);
		ValidateDescriptorAttachmentUsage(descriptor);
	}

	private static void ValidateCapabilities(
		TextureDescriptor descriptor,
		GraphicsCapabilities capabilities
	)
	{
		int limit = descriptor.Dimension switch
		{
			TextureDimension.Cube => capabilities.MaximumCubeTextureSize,
			TextureDimension.Texture3D => capabilities.MaximumTexture3DSize,
			_ => capabilities.MaximumTexture2DSize,
		};

		if (descriptor.Width > limit
			|| descriptor.Height > limit
			|| descriptor.Depth > limit)
		{
			throw new ArgumentOutOfRangeException(
				nameof(descriptor),
				$"Texture dimensions exceed the context limit of {limit}."
			);
		}

		if (descriptor.Dimension == TextureDimension.Texture2DArray
			&& descriptor.ArrayLayers > capabilities.MaximumTextureArrayLayers)
		{
			throw new ArgumentOutOfRangeException(
				nameof(descriptor),
				$"Texture array layers exceed the context limit of "
					+ $"{capabilities.MaximumTextureArrayLayers}."
			);
		}

		if (descriptor.Samples > capabilities.MaximumSamples)
		{
			throw new ArgumentOutOfRangeException(
				nameof(descriptor),
				$"Sample count exceeds the context limit of " +
				$"{capabilities.MaximumSamples}."
			);
		}
	}

	private static void ValidateUploadCompatibility(
		TextureFormat storage,
		TextureDataFormat data
	)
	{
		bool storageIsDepth = IsDepthFormat(storage);
		bool dataIsDepth = data is
			TextureDataFormat.Depth32Float or
			TextureDataFormat.Depth24Stencil8;

		if (storageIsDepth != dataIsDepth)
		{
			throw new ArgumentException(
				"Color and depth data formats cannot be mixed.",
				nameof(data)
			);
		}

		bool storageHasStencil = storage is
			TextureFormat.Depth24Stencil8 or
			TextureFormat.Depth32FloatStencil8;
		bool dataHasStencil = data == TextureDataFormat.Depth24Stencil8;

		if (storageHasStencil != dataHasStencil)
		{
			throw new ArgumentException(
				"Depth-stencil storage requires depth-stencil upload data.",
				nameof(data)
			);
		}
	}

	private static void ValidateDescriptorSamples(TextureDescriptor descriptor)
	{
		if (descriptor.Dimension != TextureDimension.Texture2DMultisample)
		{
			if (descriptor.Samples != 1)
			{
				throw new ArgumentOutOfRangeException(nameof(descriptor));
			}

			return;
		}

		if (descriptor.Samples < 2 || descriptor.MipLevels != 1)
		{
			throw new ArgumentOutOfRangeException(nameof(descriptor));
		}

		TextureUsageFlags transfers =
			TextureUsageFlags.TransferSource |
			TextureUsageFlags.TransferDestination;

		if ((descriptor.Usage & transfers) != 0)
		{
			throw new ArgumentException(
				"Multisample transfers use render-frame resolves.",
				nameof(descriptor)
			);
		}
	}

	private static void ValidateDescriptorAttachmentUsage(
		TextureDescriptor descriptor
	)
	{
		bool isDepth = IsDepthFormat(descriptor.Format);

		if (isDepth
			&& (descriptor.Usage & TextureUsageFlags.ColorAttachment) != 0)
		{
			throw new ArgumentException(
				"Depth formats cannot be color attachments.",
				nameof(descriptor)
			);
		}

		if (!isDepth
			&& (descriptor.Usage & TextureUsageFlags.DepthStencilAttachment) != 0)
		{
			throw new ArgumentException(
				"Color formats cannot be depth attachments.",
				nameof(descriptor)
			);
		}
	}
}
