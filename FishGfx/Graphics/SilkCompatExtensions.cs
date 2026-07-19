using System;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics;

internal static unsafe class SilkCompatExtensions
{
	public static void TextureStorage2DMultisample(
		this GL gl,
		uint texture,
		int samples,
		InternalFormat format,
		int width,
		int height,
		bool fixedLocations
	) =>
		gl.TextureStorage2DMultisample(
			texture,
			(uint)samples,
			(GLEnum)format,
			(uint)width,
			(uint)height,
			fixedLocations
		);

	public static void TexStorage2DMultisample(
		this GL gl,
		TextureTarget target,
		int samples,
		InternalFormat format,
		int width,
		int height,
		bool fixedLocations
	) =>
		gl.TexStorage2DMultisample(
			(GLEnum)target,
			(uint)samples,
			(GLEnum)format,
			(uint)width,
			(uint)height,
			fixedLocations
		);

	public static void TextureStorage2D(
		this GL gl,
		uint texture,
		int levels,
		InternalFormat format,
		int width,
		int height
	) => gl.TextureStorage2D(texture, (uint)levels, (GLEnum)format, (uint)width, (uint)height);

	public static void TexStorage2D(
		this GL gl,
		TextureTarget target,
		int levels,
		InternalFormat format,
		int width,
		int height
	) => gl.TexStorage2D((GLEnum)target, (uint)levels, (GLEnum)format, (uint)width, (uint)height);

	public static void TextureStorage3D(
		this GL gl,
		uint texture,
		int levels,
		InternalFormat format,
		int width,
		int height,
		int depth
	) => gl.TextureStorage3D(
		texture,
		(uint)levels,
		(GLEnum)format,
		(uint)width,
		(uint)height,
		(uint)depth
	);

	public static void TexStorage3D(
		this GL gl,
		TextureTarget target,
		int levels,
		InternalFormat format,
		int width,
		int height,
		int depth
	) => gl.TexStorage3D(
		(GLEnum)target,
		(uint)levels,
		(GLEnum)format,
		(uint)width,
		(uint)height,
		(uint)depth
	);

	public static void TextureSubImage2D(
		this GL gl,
		uint texture,
		int level,
		int x,
		int y,
		int width,
		int height,
		PixelFormat format,
		PixelType type,
		IntPtr pixels
	) =>
		gl.TextureSubImage2D(
			texture,
			level,
			x,
			y,
			(uint)width,
			(uint)height,
			(GLEnum)format,
			(GLEnum)type,
			(void*)pixels
		);

	public static void TexSubImage2D(
		this GL gl,
		TextureTarget target,
		int level,
		int x,
		int y,
		int width,
		int height,
		PixelFormat format,
		PixelType type,
		IntPtr pixels
	) =>
		gl.TexSubImage2D(
			(GLEnum)target,
			level,
			x,
			y,
			(uint)width,
			(uint)height,
			(GLEnum)format,
			(GLEnum)type,
			(void*)pixels
		);

	public static void TextureSubImage3D(
		this GL gl,
		uint texture,
		int level,
		int x,
		int y,
		int z,
		int width,
		int height,
		int depth,
		PixelFormat format,
		PixelType type,
		IntPtr pixels
	) =>
		gl.TextureSubImage3D(
			texture,
			level,
			x,
			y,
			z,
			(uint)width,
			(uint)height,
			(uint)depth,
			(GLEnum)format,
			(GLEnum)type,
			(void*)pixels
		);

	public static void TexSubImage3D(
		this GL gl,
		TextureTarget target,
		int level,
		int x,
		int y,
		int z,
		int width,
		int height,
		int depth,
		PixelFormat format,
		PixelType type,
		IntPtr pixels
	) =>
		gl.TexSubImage3D(
			(GLEnum)target,
			level,
			x,
			y,
			z,
			(uint)width,
			(uint)height,
			(uint)depth,
			(GLEnum)format,
			(GLEnum)type,
			(void*)pixels
		);

	public static void GetTextureImage(
		this GL gl,
		uint texture,
		int level,
		PixelFormat format,
		PixelType type,
		int size,
		IntPtr pixels
	) => gl.GetTextureImage(texture, level, (GLEnum)format, (GLEnum)type, (uint)size, (void*)pixels);

	public static void GetTexImage(
		this GL gl,
		TextureTarget target,
		int level,
		PixelFormat format,
		PixelType type,
		IntPtr pixels
	) => gl.GetTexImage((GLEnum)target, level, (GLEnum)format, (GLEnum)type, (void*)pixels);

	public static void ReadPixels(
		this GL gl,
		int x,
		int y,
		int width,
		int height,
		PixelFormat format,
		PixelType type,
		IntPtr pixels
	) => gl.ReadPixels(x, y, (uint)width, (uint)height, (GLEnum)format, (GLEnum)type, (void*)pixels);

	public static void NamedRenderbufferStorageMultisample(
		this GL gl,
		uint renderbuffer,
		int samples,
		InternalFormat format,
		int width,
		int height
	) =>
		gl.NamedRenderbufferStorageMultisample(
			renderbuffer,
			(uint)samples,
			(GLEnum)format,
			(uint)width,
			(uint)height
		);

	public static void NamedRenderbufferStorage(
		this GL gl,
		uint renderbuffer,
		InternalFormat format,
		int width,
		int height
	) => gl.NamedRenderbufferStorage(renderbuffer, (GLEnum)format, (uint)width, (uint)height);

	public static void DeleteRenderbuffers(this GL gl, uint id) => gl.DeleteRenderbuffers(new uint[] { id });

	public static void DeleteQueries(this GL gl, uint id) => gl.DeleteQueries(new uint[] { id });
}
