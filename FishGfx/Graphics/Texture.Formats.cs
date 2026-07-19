using System;
using Silk.NET.OpenGL;
using GLPixelFormat = Silk.NET.OpenGL.PixelFormat;

namespace FishGfx.Graphics;

public unsafe sealed partial class Texture
{
	private static TextureTarget ToTarget(TextureDimension dimension)
	{
		return dimension switch
		{
			TextureDimension.Texture2D => TextureTarget.Texture2D,
			TextureDimension.Texture3D => TextureTarget.Texture3D,
			TextureDimension.Cube => TextureTarget.TextureCubeMap,
			TextureDimension.Texture2DMultisample => TextureTarget.Texture2DMultisample,
			_ => throw new ArgumentOutOfRangeException(nameof(dimension)),
		};
	}

	private static TextureTarget ToFaceTarget(CubeFace face)
	{
		return (TextureTarget)((int)TextureTarget.TextureCubeMapPositiveX + (int)face);
	}

	private static GetPName BindingQuery(TextureTarget textureTarget)
	{
		return textureTarget switch
		{
			TextureTarget.Texture2D => GetPName.TextureBinding2D,
			TextureTarget.Texture3D => GetPName.TextureBinding3D,
			TextureTarget.TextureCubeMap => GetPName.TextureBindingCubeMap,
			TextureTarget.Texture2DMultisample => (GetPName)0x9104,
			_ => throw new ArgumentOutOfRangeException(nameof(textureTarget)),
		};
	}

	private static GLEnum ToGlFilter(TextureFilter filter)
	{
		return filter switch
		{
			TextureFilter.Nearest => GLEnum.Nearest,
			TextureFilter.Linear => GLEnum.Linear,
			TextureFilter.NearestMipmapNearest => GLEnum.NearestMipmapNearest,
			TextureFilter.LinearMipmapNearest => GLEnum.LinearMipmapNearest,
			TextureFilter.NearestMipmapLinear => GLEnum.NearestMipmapLinear,
			TextureFilter.LinearMipmapLinear => GLEnum.LinearMipmapLinear,
			_ => throw new ArgumentOutOfRangeException(nameof(filter)),
		};
	}

	private static GLEnum ToGlWrap(TextureWrap wrap)
	{
		return wrap switch
		{
			TextureWrap.ClampToEdge => GLEnum.ClampToEdge,
			TextureWrap.Repeat => GLEnum.Repeat,
			TextureWrap.MirroredRepeat => GLEnum.MirroredRepeat,
			TextureWrap.ClampToBorder => GLEnum.ClampToBorder,
			_ => throw new ArgumentOutOfRangeException(nameof(wrap)),
		};
	}

	private static InternalFormat ToInternalFormat(TextureFormat format)
	{
		return format switch
		{
			TextureFormat.R8Unorm => InternalFormat.R8,
			TextureFormat.RG8Unorm => InternalFormat.RG8,
			TextureFormat.RGB8Unorm => InternalFormat.Rgb8,
			TextureFormat.RGBA8Unorm => InternalFormat.Rgba8,
			TextureFormat.SRGB8 => InternalFormat.Srgb8,
			TextureFormat.SRGB8Alpha8 => InternalFormat.Srgb8Alpha8,
			TextureFormat.R16Unorm => InternalFormat.R16,
			TextureFormat.R16Float => InternalFormat.R16f,
			TextureFormat.RG16Float => InternalFormat.RG16f,
			TextureFormat.RGB16Float => InternalFormat.Rgb16f,
			TextureFormat.RGBA16Float => InternalFormat.Rgba16f,
			TextureFormat.R32Float => InternalFormat.R32f,
			TextureFormat.RG32Float => InternalFormat.RG32f,
			TextureFormat.RGB32Float => InternalFormat.Rgb32f,
			TextureFormat.RGBA32Float => InternalFormat.Rgba32f,
			TextureFormat.R11G11B10Float => InternalFormat.R11fG11fB10f,
			TextureFormat.Depth16Unorm => InternalFormat.DepthComponent16,
			TextureFormat.Depth24Unorm => InternalFormat.DepthComponent24,
			TextureFormat.Depth32Float => InternalFormat.DepthComponent32f,
			TextureFormat.Depth24Stencil8 => InternalFormat.Depth24Stencil8,
			TextureFormat.Depth32FloatStencil8 => InternalFormat.Depth32fStencil8,
			_ => throw new ArgumentOutOfRangeException(nameof(format)),
		};
	}

	private static (GLPixelFormat Format, PixelType Type) AllocationPixelFormat(
		TextureFormat format
	)
	{
		return format switch
		{
			TextureFormat.R8Unorm => (GLPixelFormat.Red, PixelType.UnsignedByte),
			TextureFormat.RG8Unorm => (GLPixelFormat.RG, PixelType.UnsignedByte),
			TextureFormat.RGB8Unorm => (GLPixelFormat.Rgb, PixelType.UnsignedByte),
			TextureFormat.RGBA8Unorm => (GLPixelFormat.Rgba, PixelType.UnsignedByte),
			TextureFormat.SRGB8 => (GLPixelFormat.Rgb, PixelType.UnsignedByte),
			TextureFormat.SRGB8Alpha8 => (GLPixelFormat.Rgba, PixelType.UnsignedByte),
			TextureFormat.R16Unorm => (GLPixelFormat.Red, PixelType.UnsignedShort),
			TextureFormat.R16Float => (GLPixelFormat.Red, PixelType.HalfFloat),
			TextureFormat.RG16Float => (GLPixelFormat.RG, PixelType.HalfFloat),
			TextureFormat.RGB16Float => (GLPixelFormat.Rgb, PixelType.HalfFloat),
			TextureFormat.RGBA16Float => (GLPixelFormat.Rgba, PixelType.HalfFloat),
			TextureFormat.R32Float => (GLPixelFormat.Red, PixelType.Float),
			TextureFormat.RG32Float => (GLPixelFormat.RG, PixelType.Float),
			TextureFormat.RGB32Float => (GLPixelFormat.Rgb, PixelType.Float),
			TextureFormat.RGBA32Float => (GLPixelFormat.Rgba, PixelType.Float),
			TextureFormat.R11G11B10Float => (
				GLPixelFormat.Rgb,
				(PixelType)0x8C3B
			),
			TextureFormat.Depth16Unorm => (
				GLPixelFormat.DepthComponent,
				PixelType.UnsignedShort
			),
			TextureFormat.Depth24Unorm => (
				GLPixelFormat.DepthComponent,
				PixelType.UnsignedInt
			),
			TextureFormat.Depth32Float => (
				GLPixelFormat.DepthComponent,
				PixelType.Float
			),
			TextureFormat.Depth24Stencil8 => (
				GLPixelFormat.DepthStencil,
				PixelType.UnsignedInt248
			),
			TextureFormat.Depth32FloatStencil8 => (
				GLPixelFormat.DepthStencil,
				(PixelType)0x8DAD
			),
			_ => throw new ArgumentOutOfRangeException(nameof(format)),
		};
	}

	private static (GLPixelFormat Format, PixelType Type) ToPixelFormat(
		TextureDataFormat format
	)
	{
		return format switch
		{
			TextureDataFormat.R8Unorm => (GLPixelFormat.Red, PixelType.UnsignedByte),
			TextureDataFormat.RG8Unorm => (GLPixelFormat.RG, PixelType.UnsignedByte),
			TextureDataFormat.RGB8Unorm => (GLPixelFormat.Rgb, PixelType.UnsignedByte),
			TextureDataFormat.RGBA8Unorm => (GLPixelFormat.Rgba, PixelType.UnsignedByte),
			TextureDataFormat.BGR8Unorm => (GLPixelFormat.Bgr, PixelType.UnsignedByte),
			TextureDataFormat.BGRA8Unorm => (GLPixelFormat.Bgra, PixelType.UnsignedByte),
			TextureDataFormat.R16Unorm => (GLPixelFormat.Red, PixelType.UnsignedShort),
			TextureDataFormat.R16Float => (GLPixelFormat.Red, PixelType.HalfFloat),
			TextureDataFormat.R32Float => (GLPixelFormat.Red, PixelType.Float),
			TextureDataFormat.RG32Float => (GLPixelFormat.RG, PixelType.Float),
			TextureDataFormat.RGB32Float => (GLPixelFormat.Rgb, PixelType.Float),
			TextureDataFormat.RGBA32Float => (GLPixelFormat.Rgba, PixelType.Float),
			TextureDataFormat.Depth32Float => (
				GLPixelFormat.DepthComponent,
				PixelType.Float
			),
			TextureDataFormat.Depth24Stencil8 => (
				GLPixelFormat.DepthStencil,
				PixelType.UnsignedInt248
			),
			_ => throw new ArgumentOutOfRangeException(nameof(format)),
		};
	}

	private static int BytesPerPixel(TextureDataFormat format)
	{
		return format switch
		{
			TextureDataFormat.R8Unorm => 1,
			TextureDataFormat.RG8Unorm => 2,
			TextureDataFormat.RGB8Unorm => 3,
			TextureDataFormat.RGBA8Unorm => 4,
			TextureDataFormat.BGR8Unorm => 3,
			TextureDataFormat.BGRA8Unorm => 4,
			TextureDataFormat.R16Unorm => 2,
			TextureDataFormat.R16Float => 2,
			TextureDataFormat.R32Float => 4,
			TextureDataFormat.RG32Float => 8,
			TextureDataFormat.RGB32Float => 12,
			TextureDataFormat.RGBA32Float => 16,
			TextureDataFormat.Depth32Float => 4,
			TextureDataFormat.Depth24Stencil8 => 4,
			_ => throw new ArgumentOutOfRangeException(nameof(format)),
		};
	}
}
