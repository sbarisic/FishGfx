using System;

namespace FishGfx.Graphics;

public enum TextureDimension
{
	Texture2D,
	Cube,
	Texture2DMultisample,
}

[Flags]
public enum TextureUsageFlags
{
	None = 0,
	Sampled = 1 << 0,
	ColorAttachment = 1 << 1,
	DepthStencilAttachment = 1 << 2,
	TransferSource = 1 << 3,
	TransferDestination = 1 << 4,
}

public enum TextureFormat
{
	R8Unorm,
	RG8Unorm,
	RGB8Unorm,
	RGBA8Unorm,
	SRGB8,
	SRGB8Alpha8,
	R16Unorm,
	R16Float,
	RG16Float,
	RGB16Float,
	RGBA16Float,
	R32Float,
	RG32Float,
	RGB32Float,
	RGBA32Float,
	R11G11B10Float,
	Depth16Unorm,
	Depth24Unorm,
	Depth32Float,
	Depth24Stencil8,
	Depth32FloatStencil8,
}

public enum TextureDataFormat
{
	R8Unorm,
	RG8Unorm,
	RGB8Unorm,
	RGBA8Unorm,
	BGR8Unorm,
	BGRA8Unorm,
	R16Unorm,
	R16Float,
	R32Float,
	RG32Float,
	RGB32Float,
	RGBA32Float,
	Depth32Float,
	Depth24Stencil8,
}

public enum TextureWrap
{
	ClampToEdge,
	Repeat,
	MirroredRepeat,
	ClampToBorder,
}

public enum TextureFilter
{
	Nearest,
	Linear,
	NearestMipmapNearest,
	LinearMipmapNearest,
	NearestMipmapLinear,
	LinearMipmapLinear,
}

public enum CubeFace
{
	PositiveX,
	NegativeX,
	PositiveY,
	NegativeY,
	PositiveZ,
	NegativeZ,
}

public readonly record struct TextureSamplingState
{
	public TextureSamplingState(
		TextureFilter minFilter = TextureFilter.Nearest,
		TextureFilter magFilter = TextureFilter.Nearest,
		TextureWrap wrapU = TextureWrap.ClampToEdge,
		TextureWrap wrapV = TextureWrap.ClampToEdge,
		TextureWrap wrapW = TextureWrap.ClampToEdge,
		float anisotropy = 1
	)
	{
		if (!Enum.IsDefined(minFilter))
		{
			throw new ArgumentOutOfRangeException(nameof(minFilter));
		}

		if (magFilter != TextureFilter.Nearest && magFilter != TextureFilter.Linear)
		{
			throw new ArgumentOutOfRangeException(
				nameof(magFilter),
				"Magnification only supports nearest or linear filtering."
			);
		}

		ValidateWrap(wrapU, nameof(wrapU));
		ValidateWrap(wrapV, nameof(wrapV));
		ValidateWrap(wrapW, nameof(wrapW));

		if (!float.IsFinite(anisotropy) || anisotropy < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(anisotropy));
		}

		MinFilter = minFilter;
		MagFilter = magFilter;
		WrapU = wrapU;
		WrapV = wrapV;
		WrapW = wrapW;
		Anisotropy = anisotropy;
	}

	public TextureFilter MinFilter { get; }

	public TextureFilter MagFilter { get; }

	public TextureWrap WrapU { get; }

	public TextureWrap WrapV { get; }

	public TextureWrap WrapW { get; }

	public float Anisotropy { get; }

	public static TextureSamplingState Default => new(TextureFilter.Nearest);

	private static void ValidateWrap(TextureWrap wrap, string parameterName)
	{
		if (!Enum.IsDefined(wrap))
		{
			throw new ArgumentOutOfRangeException(parameterName);
		}
	}
}

public readonly record struct TextureDescriptor
{
	private const TextureUsageFlags AllUsage =
		TextureUsageFlags.Sampled |
		TextureUsageFlags.ColorAttachment |
		TextureUsageFlags.DepthStencilAttachment |
		TextureUsageFlags.TransferSource |
		TextureUsageFlags.TransferDestination;

	public TextureDescriptor(
		int width,
		int height,
		TextureFormat format = TextureFormat.RGBA8Unorm,
		TextureUsageFlags usage = TextureUsageFlags.Sampled |
			TextureUsageFlags.TransferDestination,
		TextureDimension dimension = TextureDimension.Texture2D,
		int mipLevels = 1,
		int samples = 1,
		bool fixedSampleLocations = true,
		TextureSamplingState sampling = default
	)
	{
		if (width <= 0 || height <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(width),
				"Texture dimensions must be positive."
			);
		}

		if (!Enum.IsDefined(format))
		{
			throw new ArgumentOutOfRangeException(nameof(format));
		}

		if (!Enum.IsDefined(dimension))
		{
			throw new ArgumentOutOfRangeException(nameof(dimension));
		}

		if (usage == TextureUsageFlags.None || (usage & ~AllUsage) != 0)
		{
			throw new ArgumentOutOfRangeException(nameof(usage));
		}

		if (dimension == TextureDimension.Cube && width != height)
		{
			throw new ArgumentException("Cube textures must be square.");
		}

		int maximumMips = 1 + (int)Math.Floor(Math.Log2(Math.Max(width, height)));

		if (mipLevels <= 0 || mipLevels > maximumMips)
		{
			throw new ArgumentOutOfRangeException(nameof(mipLevels));
		}

		ValidateSamples(dimension, mipLevels, samples, usage);
		ValidateAttachmentUsage(format, usage);

		if (sampling == default)
		{
			sampling = TextureSamplingState.Default;
		}

		Width = width;
		Height = height;
		Format = format;
		Usage = usage;
		Dimension = dimension;
		MipLevels = mipLevels;
		Samples = samples;
		FixedSampleLocations = fixedSampleLocations;
		Sampling = sampling;
	}

	public int Width { get; }

	public int Height { get; }

	public TextureFormat Format { get; }

	public TextureUsageFlags Usage { get; }

	public TextureDimension Dimension { get; }

	public int MipLevels { get; }

	public int Samples { get; }

	public bool FixedSampleLocations { get; }

	public TextureSamplingState Sampling { get; }

	private static void ValidateSamples(
		TextureDimension dimension,
		int mipLevels,
		int samples,
		TextureUsageFlags usage
	)
	{
		if (dimension != TextureDimension.Texture2DMultisample)
		{
			if (samples != 1)
			{
				throw new ArgumentOutOfRangeException(
					nameof(samples),
					"Non-multisampled textures use one sample."
				);
			}

			return;
		}

		if (samples < 2)
		{
			throw new ArgumentOutOfRangeException(
				nameof(samples),
				"Multisample textures require at least two samples."
			);
		}

		if (mipLevels != 1)
		{
			throw new ArgumentException(
				"Multisample textures have exactly one mip level.",
				nameof(mipLevels)
			);
		}

		TextureUsageFlags transferUsage =
			TextureUsageFlags.TransferSource |
			TextureUsageFlags.TransferDestination;

		if ((usage & transferUsage) != 0)
		{
			throw new ArgumentException(
				"Multisample texture transfers use framebuffer blits.",
				nameof(usage)
			);
		}
	}

	private static void ValidateAttachmentUsage(
		TextureFormat format,
		TextureUsageFlags usage
	)
	{
		bool isDepth = Texture.IsDepthFormat(format);

		if (isDepth && (usage & TextureUsageFlags.ColorAttachment) != 0)
		{
			throw new ArgumentException(
				"Depth formats cannot be color attachments.",
				nameof(usage)
			);
		}

		if (!isDepth && (usage & TextureUsageFlags.DepthStencilAttachment) != 0)
		{
			throw new ArgumentException(
				"Color formats cannot be depth attachments.",
				nameof(usage)
			);
		}
	}
}

public readonly record struct TextureSubresource
{
	public TextureSubresource(int mipLevel = 0, CubeFace? face = null)
	{
		if (mipLevel < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(mipLevel));
		}

		if (face.HasValue && !Enum.IsDefined(face.Value))
		{
			throw new ArgumentOutOfRangeException(nameof(face));
		}

		MipLevel = mipLevel;
		Face = face;
	}

	public int MipLevel { get; }

	public CubeFace? Face { get; }
}

public readonly record struct TextureRegion
{
	public TextureRegion(int x, int y, int width, int height)
	{
		if (x < 0 || y < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(x));
		}

		if (width <= 0 || height <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width));
		}

		X = x;
		Y = y;
		Width = width;
		Height = height;
	}

	public int X { get; }

	public int Y { get; }

	public int Width { get; }

	public int Height { get; }
}

public readonly record struct TextureCopyRegion(
	TextureRegion Region,
	TextureSubresource Subresource = default
);
