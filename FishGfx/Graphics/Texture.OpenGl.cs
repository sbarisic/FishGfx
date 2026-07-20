using System;
using Silk.NET.OpenGL;
using GLPixelFormat = Silk.NET.OpenGL.PixelFormat;

namespace FishGfx.Graphics;

public unsafe sealed partial class Texture
{
	private void Allocate()
	{
		if (Multisampled)
		{
			AllocateMultisampleStorage();

			return;
		}

		if (Is3D || Is2DArray)
		{
			AllocateLayeredStorage();

			return;
		}

		if (Internal_OpenGL.Is45OrAbove)
		{
			Internal_OpenGL.GL.TextureStorage2D(
				Handle,
				MipLevels,
				internalFormat,
				Width,
				Height
			);
		}
		else if (Internal_OpenGL.Is42OrAbove)
		{
			WithBound(() => Internal_OpenGL.GL.TexStorage2D(
				target,
				MipLevels,
				internalFormat,
				Width,
				Height
			));
		}
		else
		{
			WithBound(AllocateMutableStorage);
		}
	}

	private void AllocateLayeredStorage()
	{
		int layers = Is2DArray ? ArrayLayers : Depth;

		if (Internal_OpenGL.Is45OrAbove)
		{
			Internal_OpenGL.GL.TextureStorage3D(
				Handle,
				MipLevels,
				internalFormat,
				Width,
				Height,
				layers
			);
		}
		else if (Internal_OpenGL.Is42OrAbove)
		{
			WithBound(() => Internal_OpenGL.GL.TexStorage3D(
				target,
				MipLevels,
				internalFormat,
				Width,
				Height,
				layers
			));
		}
		else
		{
			WithBound(AllocateMutableLayeredStorage);
		}
	}

	private void AllocateMutableLayeredStorage()
	{
		(GLPixelFormat format, PixelType type) = AllocationPixelFormat(Format);

		for (int level = 0; level < MipLevels; level++)
		{
			(int width, int height, int depth) = GetLayeredMipSize(level);
			Internal_OpenGL.GL.TexImage3D(
				target,
				level,
				internalFormat,
				(uint)width,
				(uint)height,
				(uint)depth,
				0,
				format,
				type,
				null
			);
		}
	}

	private void AllocateMultisampleStorage()
	{
		if (Internal_OpenGL.Is45OrAbove)
		{
			Internal_OpenGL.GL.TextureStorage2DMultisample(
				Handle,
				Descriptor.Samples,
				internalFormat,
				Width,
				Height,
				Descriptor.FixedSampleLocations
			);
		}
		else if (Internal_OpenGL.Is42OrAbove)
		{
			WithBound(() => Internal_OpenGL.GL.TexStorage2DMultisample(
				target,
				Descriptor.Samples,
				internalFormat,
				Width,
				Height,
				Descriptor.FixedSampleLocations
			));
		}
		else
		{
			WithBound(() => Internal_OpenGL.GL.TexImage2DMultisample(
				target,
				(uint)Descriptor.Samples,
				(GLEnum)internalFormat,
				(uint)Width,
				(uint)Height,
				Descriptor.FixedSampleLocations
			));
		}
	}

	private void AllocateMutableStorage()
	{
		(GLPixelFormat format, PixelType type) = AllocationPixelFormat(Format);

		for (int level = 0; level < MipLevels; level++)
		{
			(int width, int height) = GetMipSize(level);

			if (IsCubeMap)
			{
				for (int face = 0; face < 6; face++)
				{
					Internal_OpenGL.GL.TexImage2D(
						ToFaceTarget((CubeFace)face),
						level,
						internalFormat,
						(uint)width,
						(uint)height,
						0,
						format,
						type,
						null
					);
				}
			}
			else
			{
				Internal_OpenGL.GL.TexImage2D(
					target,
					level,
					internalFormat,
					(uint)width,
					(uint)height,
					0,
					format,
					type,
					null
				);
			}
		}
	}

	private void WritePixels(
		void* pixels,
		GLPixelFormat pixelFormat,
		PixelType pixelType,
		TextureRegion region,
		TextureSubresource subresource
	)
	{
		if (Internal_OpenGL.Is45OrAbove)
		{
			if (IsCubeMap)
			{
				Internal_OpenGL.GL.TextureSubImage3D(
					Handle,
					subresource.MipLevel,
					region.X,
					region.Y,
					(int)subresource.Face.Value,
					region.Width,
					region.Height,
					1,
					pixelFormat,
					pixelType,
					(nint)pixels
				);
			}
			else
			{
				Internal_OpenGL.GL.TextureSubImage2D(
					Handle,
					subresource.MipLevel,
					region.X,
					region.Y,
					region.Width,
					region.Height,
					pixelFormat,
					pixelType,
					(nint)pixels
				);
			}

			return;
		}

		Internal_OpenGL.GL.GetInteger(BindingQuery(target), out int previous);
		Internal_OpenGL.GL.BindTexture(target, Handle);

		try
		{
			TextureTarget uploadTarget = IsCubeMap
				? ToFaceTarget(subresource.Face.Value)
				: target;
			Internal_OpenGL.GL.TexSubImage2D(
				uploadTarget,
				subresource.MipLevel,
				region.X,
				region.Y,
				region.Width,
				region.Height,
				pixelFormat,
				pixelType,
				(nint)pixels
			);
		}
		finally
		{
			Internal_OpenGL.GL.BindTexture(target, (uint)previous);
		}
	}

	private void WritePixels3D(
		void* pixels,
		GLPixelFormat pixelFormat,
		PixelType pixelType,
		TextureRegion3D region,
		int mipLevel
	)
	{
		if (Internal_OpenGL.Is45OrAbove)
		{
			Internal_OpenGL.GL.TextureSubImage3D(
				Handle,
				mipLevel,
				region.X,
				region.Y,
				region.Z,
				region.Width,
				region.Height,
				region.Depth,
				pixelFormat,
				pixelType,
				(nint)pixels
			);

			return;
		}

		WithBound(() => Internal_OpenGL.GL.TexSubImage3D(
			target,
			mipLevel,
			region.X,
			region.Y,
			region.Z,
			region.Width,
			region.Height,
			region.Depth,
			pixelFormat,
			pixelType,
			(nint)pixels
		));
	}

	private void CopyImage(
		Texture destination,
		TextureCopyRegion source,
		TextureSubresource destinationSubresource,
		int destinationX,
		int destinationY
	)
	{
		Internal_OpenGL.GL.CopyImageSubData(
			Handle,
			(GLEnum)target,
			source.Subresource.MipLevel,
			source.Region.X,
			source.Region.Y,
			Layer(source.Subresource),
			destination.Handle,
			(GLEnum)destination.target,
			destinationSubresource.MipLevel,
			destinationX,
			destinationY,
			destination.Layer(destinationSubresource),
			(uint)source.Region.Width,
			(uint)source.Region.Height,
			1
		);
	}

	private void CopyWithFramebuffers(
		Texture destination,
		TextureCopyRegion source,
		TextureSubresource destinationSubresource,
		int destinationX,
		int destinationY
	)
	{
		Internal_OpenGL.GL.GetInteger((GetPName)0x8CAA, out int previousRead);
		Internal_OpenGL.GL.GetInteger((GetPName)0x8CA6, out int previousDraw);
		uint readFramebuffer = Internal_OpenGL.GL.GenFramebuffer();
		uint drawFramebuffer = Internal_OpenGL.GL.GenFramebuffer();

		try
		{
			FramebufferAttachment attachment = GetFramebufferAttachment(Format);
			Internal_OpenGL.GL.BindFramebuffer(
				FramebufferTarget.ReadFramebuffer,
				readFramebuffer
			);
			AttachToFramebuffer(
				FramebufferTarget.ReadFramebuffer,
				attachment,
				this,
				source.Subresource
			);
			Internal_OpenGL.GL.BindFramebuffer(
				FramebufferTarget.DrawFramebuffer,
				drawFramebuffer
			);
			AttachToFramebuffer(
				FramebufferTarget.DrawFramebuffer,
				attachment,
				destination,
				destinationSubresource
			);

			if (IsDepthFormat(Format))
			{
				Internal_OpenGL.GL.ReadBuffer(ReadBufferMode.None);
				Internal_OpenGL.GL.DrawBuffer(DrawBufferMode.None);
			}

			ClearBufferMask mask = GetCopyMask(Format);
			Internal_OpenGL.GL.BlitFramebuffer(
				source.Region.X,
				source.Region.Y,
				source.Region.X + source.Region.Width,
				source.Region.Y + source.Region.Height,
				destinationX,
				destinationY,
				destinationX + source.Region.Width,
				destinationY + source.Region.Height,
				mask,
				BlitFramebufferFilter.Nearest
			);
		}
		finally
		{
			Internal_OpenGL.GL.BindFramebuffer(
				FramebufferTarget.ReadFramebuffer,
				(uint)previousRead
			);
			Internal_OpenGL.GL.BindFramebuffer(
				FramebufferTarget.DrawFramebuffer,
				(uint)previousDraw
			);
			Internal_OpenGL.GL.DeleteFramebuffer(readFramebuffer);
			Internal_OpenGL.GL.DeleteFramebuffer(drawFramebuffer);
		}
	}

	private static void AttachToFramebuffer(
		FramebufferTarget framebufferTarget,
		FramebufferAttachment attachment,
		Texture texture,
		TextureSubresource subresource
	)
	{
		TextureTarget attachmentTarget = texture.IsCubeMap
			? ToFaceTarget(subresource.Face.Value)
			: texture.target;
		Internal_OpenGL.GL.FramebufferTexture2D(
			framebufferTarget,
			attachment,
			attachmentTarget,
			texture.Handle,
			subresource.MipLevel
		);
	}

	private void SetParameter(TextureParameterName name, int value)
	{
		if (Internal_OpenGL.Is45OrAbove)
		{
			Internal_OpenGL.GL.TextureParameter(Handle, name, value);
		}
		else
		{
			WithBound(() => Internal_OpenGL.GL.TexParameter(target, name, value));
		}
	}

	private void SetParameter(TextureParameterName name, float value)
	{
		if (Internal_OpenGL.Is45OrAbove)
		{
			Internal_OpenGL.GL.TextureParameter(Handle, name, value);
		}
		else
		{
			WithBound(() => Internal_OpenGL.GL.TexParameter(target, name, value));
		}
	}

	private void WithBound(Action action)
	{
		Internal_OpenGL.GL.GetInteger(BindingQuery(target), out int previous);
		Internal_OpenGL.GL.BindTexture(target, Handle);

		try
		{
			action();
		}
		finally
		{
			Internal_OpenGL.GL.BindTexture(target, (uint)previous);
		}
	}

	private static void WithActiveTextureUnit(uint unit, Action action)
	{
		Internal_OpenGL.GL.GetInteger(GetPName.ActiveTexture, out int previous);
		TextureUnit textureUnit = (TextureUnit)((uint)TextureUnit.Texture0 + unit);
		Internal_OpenGL.GL.ActiveTexture(textureUnit);

		try
		{
			action();
		}
		finally
		{
			Internal_OpenGL.GL.ActiveTexture((TextureUnit)previous);
		}
	}

	private static FramebufferAttachment GetFramebufferAttachment(
		TextureFormat format
	)
	{
		if (format is TextureFormat.Depth24Stencil8
			or TextureFormat.Depth32FloatStencil8)
		{
			return FramebufferAttachment.DepthStencilAttachment;
		}

		return IsDepthFormat(format)
			? FramebufferAttachment.DepthAttachment
			: FramebufferAttachment.ColorAttachment0;
	}

	private static ClearBufferMask GetCopyMask(TextureFormat format)
	{
		if (!IsDepthFormat(format))
		{
			return ClearBufferMask.ColorBufferBit;
		}

		ClearBufferMask mask = ClearBufferMask.DepthBufferBit;

		if (format is TextureFormat.Depth24Stencil8
			or TextureFormat.Depth32FloatStencil8)
		{
			mask |= ClearBufferMask.StencilBufferBit;
		}

		return mask;
	}
}
