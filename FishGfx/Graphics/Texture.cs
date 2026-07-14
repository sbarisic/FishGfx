using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.OpenGL;
using GLPixelFormat = Silk.NET.OpenGL.PixelFormat;

namespace FishGfx.Graphics;

public unsafe sealed partial class Texture : GraphicsResource
{
	private readonly TextureTarget target;
	private readonly InternalFormat internalFormat;
	private readonly Dictionary<uint, Stack<uint>> previousBindings = new();

	internal Texture(GraphicsContext owner, TextureDescriptor descriptor)
		: base(owner)
	{
		ValidateDescriptor(descriptor);
		ValidateCapabilities(descriptor, owner.Capabilities);
		Descriptor = descriptor;
		target = ToTarget(descriptor.Dimension);
		internalFormat = ToInternalFormat(descriptor.Format);
		Handle = Internal_OpenGL.Is45OrAbove
			? Internal_OpenGL.GL.CreateTexture(target)
			: Internal_OpenGL.GL.GenTexture();

		try
		{
			Allocate();

			if (!Multisampled)
			{
				SetSampling(descriptor.Sampling);
			}

			RegisterResource();
		}
		catch
		{
			Internal_OpenGL.GL.DeleteTexture(Handle);
			Handle = 0;

			throw;
		}
	}

	public TextureDescriptor Descriptor { get; }

	public int Width => Descriptor.Width;

	public int Height => Descriptor.Height;

	public int MipLevels => Descriptor.MipLevels;

	public int Multisamples => Multisampled ? Descriptor.Samples : 0;

	public bool Multisampled =>
		Descriptor.Dimension == TextureDimension.Texture2DMultisample;

	public bool IsCubeMap => Descriptor.Dimension == TextureDimension.Cube;

	public TextureFormat Format => Descriptor.Format;

	public TextureUsageFlags Usage => Descriptor.Usage;

	public Vector2 Size => new(Width, Height);

	public TextureSamplingState Sampling { get; private set; }

	public void SetSampling(TextureSamplingState sampling)
	{
		EnsureCurrentOwner();

		if (Multisampled)
		{
			throw new InvalidOperationException(
				"Multisample textures do not have sampling state."
			);
		}

		sampling = NormalizeSampling(sampling);
		GraphicsCapabilities capabilities = Owner.Capabilities;

		if (sampling.Anisotropy > 1 && !capabilities.SupportsAnisotropy)
		{
			throw new NotSupportedException(
				"Texture anisotropy is not supported by this context."
			);
		}

		if (sampling.Anisotropy > capabilities.MaximumAnisotropy)
		{
			throw new ArgumentOutOfRangeException(
				nameof(sampling),
				$"Anisotropy exceeds the context limit of " +
				$"{capabilities.MaximumAnisotropy}."
			);
		}

		SetParameter(
			TextureParameterName.TextureMinFilter,
			(int)ToGlFilter(sampling.MinFilter)
		);
		SetParameter(
			TextureParameterName.TextureMagFilter,
			(int)ToGlFilter(sampling.MagFilter)
		);
		SetParameter(
			TextureParameterName.TextureWrapS,
			(int)ToGlWrap(sampling.WrapU)
		);
		SetParameter(
			TextureParameterName.TextureWrapT,
			(int)ToGlWrap(sampling.WrapV)
		);
		SetParameter(
			TextureParameterName.TextureWrapR,
			(int)ToGlWrap(sampling.WrapW)
		);

		if (capabilities.SupportsAnisotropy)
		{
			SetParameter((TextureParameterName)0x84FE, sampling.Anisotropy);
		}

		Sampling = sampling;
	}

	public void Write<T>(
		ReadOnlySpan<T> data,
		TextureDataFormat dataFormat,
		TextureSubresource subresource = default
	)
		where T : unmanaged
	{
		ValidateSubresource(subresource);
		(int width, int height) = GetMipSize(subresource.MipLevel);
		Write(
			data,
			dataFormat,
			new TextureRegion(0, 0, width, height),
			subresource
		);
	}

	public void Write<T>(
		ReadOnlySpan<T> data,
		TextureDataFormat dataFormat,
		TextureRegion region,
		TextureSubresource subresource = default
	)
		where T : unmanaged
	{
		EnsureCurrentOwner();

		if (Multisampled)
		{
			throw new InvalidOperationException(
				"Multisample textures cannot receive CPU uploads."
			);
		}

		if ((Usage & TextureUsageFlags.TransferDestination) == 0)
		{
			throw new InvalidOperationException(
				"The texture is missing TransferDestination usage."
			);
		}

		ValidateSubresource(subresource);
		ValidateRegion(region, subresource.MipLevel);
		ValidateUploadCompatibility(Format, dataFormat);

		int pixelCount = checked(region.Width * region.Height);
		int requiredBytes = checked(pixelCount * BytesPerPixel(dataFormat));
		int actualBytes = checked(data.Length * Unsafe.SizeOf<T>());

		if (actualBytes != requiredBytes)
		{
			throw new ArgumentException(
				$"Upload contains {actualBytes} bytes; " +
				$"{requiredBytes} bytes are required.",
				nameof(data)
			);
		}

		if (actualBytes == 0)
		{
			return;
		}

		(GLPixelFormat pixelFormat, PixelType pixelType) = ToPixelFormat(dataFormat);
		Internal_OpenGL.GL.GetInteger(
			GetPName.UnpackAlignment,
			out int previousAlignment
		);
		Internal_OpenGL.GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

		try
		{
			fixed (T* pointer = data)
			{
				WritePixels(
					pointer,
					pixelFormat,
					pixelType,
					region,
					subresource
				);
			}
		}
		finally
		{
			Internal_OpenGL.GL.PixelStore(
				PixelStoreParameter.UnpackAlignment,
				previousAlignment
			);
		}
	}

	public void GenerateMipmaps()
	{
		EnsureCurrentOwner();

		if (Multisampled)
		{
			throw new InvalidOperationException(
				"Multisample textures do not have mipmaps."
			);
		}

		if (MipLevels < 2)
		{
			throw new InvalidOperationException(
				"The texture descriptor only allocated one mip level."
			);
		}

		if (Internal_OpenGL.Is45OrAbove)
		{
			Internal_OpenGL.GL.GenerateTextureMipmap(Handle);
		}
		else
		{
			WithBound(() => Internal_OpenGL.GL.GenerateMipmap(target));
		}
	}

	public void CopyTo(Texture destination)
	{
		ArgumentNullException.ThrowIfNull(destination);

		if (Descriptor.Dimension != destination.Descriptor.Dimension
			|| Width != destination.Width
			|| Height != destination.Height
			|| MipLevels != destination.MipLevels)
		{
			throw new InvalidOperationException(
				"Whole-texture copies require matching dimensions, extents, and mip counts."
			);
		}

		int faceCount = IsCubeMap ? 6 : 1;

		for (int mipLevel = 0; mipLevel < MipLevels; mipLevel++)
		{
			(int width, int height) = GetMipSize(mipLevel);

			for (int face = 0; face < faceCount; face++)
			{
				TextureSubresource subresource = new(
					mipLevel,
					IsCubeMap ? (CubeFace?)face : null
				);
				TextureRegion region = new(0, 0, width, height);
				CopyTo(
					destination,
					new TextureCopyRegion(region, subresource),
					subresource
				);
			}
		}
	}

	public void CopyTo(
		Texture destination,
		TextureCopyRegion source,
		TextureSubresource destinationSubresource,
		int destinationX = 0,
		int destinationY = 0
	)
	{
		GraphicsContext context = EnsureCurrentOwner();
		ArgumentNullException.ThrowIfNull(destination);
		destination.EnsureOwner(context);

		if (ReferenceEquals(this, destination))
		{
			throw new InvalidOperationException("A texture cannot be copied to itself.");
		}

		if (Multisampled || destination.Multisampled)
		{
			throw new InvalidOperationException(
				"Use a render-frame resolve to copy multisample textures."
			);
		}

		if ((Usage & TextureUsageFlags.TransferSource) == 0)
		{
			throw new InvalidOperationException(
				"The source texture is missing TransferSource usage."
			);
		}

		if ((destination.Usage & TextureUsageFlags.TransferDestination) == 0)
		{
			throw new InvalidOperationException(
				"The destination texture is missing TransferDestination usage."
			);
		}

		if (Format != destination.Format)
		{
			throw new InvalidOperationException(
				"Texture copies require identical formats."
			);
		}

		ValidateSubresource(source.Subresource);
		destination.ValidateSubresource(destinationSubresource);
		ValidateRegion(source.Region, source.Subresource.MipLevel);

		TextureRegion destinationRegion = new(
			destinationX,
			destinationY,
			source.Region.Width,
			source.Region.Height
		);
		destination.ValidateRegion(
			destinationRegion,
			destinationSubresource.MipLevel
		);

		if (context.Capabilities.SupportsCopyImage)
		{
			CopyImage(
				destination,
				source,
				destinationSubresource,
				destinationX,
				destinationY
			);

			return;
		}

		CopyWithFramebuffers(
			destination,
			source,
			destinationSubresource,
			destinationX,
			destinationY
		);
	}

	internal void BindTextureUnit(uint unit = 0)
	{
		EnsureCurrentOwner();
		WithActiveTextureUnit(unit, () =>
		{
			Internal_OpenGL.GL.GetInteger(BindingQuery(target), out int previous);
			GetBindingStack(unit).Push((uint)previous);
			Internal_OpenGL.GL.BindTexture(target, Handle);
		});
	}

	internal void UnbindTextureUnit(uint unit = 0)
	{
		EnsureCurrentOwner();

		if (!previousBindings.TryGetValue(unit, out Stack<uint> bindings)
			|| bindings.Count == 0)
		{
			throw new InvalidOperationException(
				$"Texture unit {unit} is not bound by this texture."
			);
		}

		uint previous = bindings.Pop();
		WithActiveTextureUnit(
			unit,
			() => Internal_OpenGL.GL.BindTexture(target, previous)
		);
	}

	internal override void DeleteResource()
	{
		Internal_OpenGL.GL.DeleteTexture(Handle);
	}

	internal static bool IsDepthFormat(TextureFormat format)
	{
		return format is
			TextureFormat.Depth16Unorm or
			TextureFormat.Depth24Unorm or
			TextureFormat.Depth32Float or
			TextureFormat.Depth24Stencil8 or
			TextureFormat.Depth32FloatStencil8;
	}

	private Stack<uint> GetBindingStack(uint unit)
	{
		if (!previousBindings.TryGetValue(unit, out Stack<uint> bindings))
		{
			bindings = new Stack<uint>();
			previousBindings.Add(unit, bindings);
		}

		return bindings;
	}

	private void ValidateSubresource(TextureSubresource subresource)
	{
		if (subresource.MipLevel < 0 || subresource.MipLevel >= MipLevels)
		{
			throw new ArgumentOutOfRangeException(nameof(subresource));
		}

		if (IsCubeMap != subresource.Face.HasValue)
		{
			string message = IsCubeMap
				? "A cube face is required."
				: "A cube face is only valid for cube textures.";

			throw new ArgumentException(message, nameof(subresource));
		}
	}

	private void ValidateRegion(TextureRegion region, int mipLevel)
	{
		if (region.X < 0
			|| region.Y < 0
			|| region.Width <= 0
			|| region.Height <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(region));
		}

		(int width, int height) = GetMipSize(mipLevel);

		if (region.X > width - region.Width || region.Y > height - region.Height)
		{
			throw new ArgumentOutOfRangeException(
				nameof(region),
				"The region exceeds the mip bounds."
			);
		}
	}

	private (int Width, int Height) GetMipSize(int level)
	{
		return (Math.Max(1, Width >> level), Math.Max(1, Height >> level));
	}

	private int Layer(TextureSubresource subresource)
	{
		return IsCubeMap ? (int)subresource.Face.Value : 0;
	}

	private static TextureSamplingState NormalizeSampling(
		TextureSamplingState sampling
	)
	{
		if (sampling == default)
		{
			return TextureSamplingState.Default;
		}

		return new TextureSamplingState(
			sampling.MinFilter,
			sampling.MagFilter,
			sampling.WrapU,
			sampling.WrapV,
			sampling.WrapW,
			sampling.Anisotropy
		);
	}
}
