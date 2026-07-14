using System;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using FishGfx.Formats;
using FishGfx.Graphics.Drawables;

namespace FishGfx.Graphics;

public sealed partial class GraphicsContext
{
	public Texture CreateTexture(TextureDescriptor descriptor)
	{
		EnsureCurrent();

		return new Texture(this, descriptor);
	}

	public Texture LoadTexture(
		string path,
		TextureLoadOptions options = null
	)
	{
		EnsureCurrent();

		return TextureLoader.Load(this, path, options);
	}

	public Texture CreateTextureFromImage(
		Image image,
		TextureLoadOptions options = null
	)
	{
		EnsureCurrent();

		return TextureLoader.CreateFromImage(this, image, options);
	}

	public Texture LoadCubemap(
		CubemapPaths paths,
		TextureLoadOptions options = null
	)
	{
		EnsureCurrent();

		return TextureLoader.LoadCubemap(this, paths, options);
	}

	public Texture[] LoadTextureAtlas(
		string path,
		int tileWidth,
		int tileHeight,
		TextureLoadOptions options = null
	)
	{
		EnsureCurrent();

		return TextureLoader.LoadAtlas(
			this,
			path,
			tileWidth,
			tileHeight,
			options
		);
	}

	public GraphicsBuffer CreateBuffer(GraphicsBufferDescriptor descriptor)
	{
		EnsureCurrent();

		return new GraphicsBuffer(this, descriptor);
	}

	public GraphicsBuffer CreateBuffer<T>(
		ReadOnlySpan<T> data,
		BufferBindFlags bindFlags,
		BufferUsage usage = BufferUsage.Static
	)
		where T : unmanaged
	{
		EnsureCurrent();

		int size = checked(data.Length * Unsafe.SizeOf<T>());

		if (size == 0)
		{
			throw new ArgumentException(
				"Initial buffer data cannot be empty.",
				nameof(data)
			);
		}

		GraphicsBuffer buffer = new(
			this,
			new GraphicsBufferDescriptor(size, bindFlags, usage)
		);

		try
		{
			buffer.Write(data);

			return buffer;
		}
		catch
		{
			buffer.Dispose();

			throw;
		}
	}

	public ShaderStage CreateShaderStage(
		ShaderStageType type,
		string source
	)
	{
		EnsureCurrent();

		return new ShaderStage(this, type, source);
	}

	public ShaderStage LoadShaderStage(
		ShaderStageType type,
		string path
	)
	{
		EnsureCurrent();
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		string fullPath = Path.GetFullPath(path);
		string source = File.ReadAllText(fullPath);

		return new ShaderStage(this, type, source, fullPath);
	}

	public ShaderProgram CreateShaderProgram(params ShaderStage[] stages)
	{
		EnsureCurrent();
		ArgumentNullException.ThrowIfNull(stages);

		return new ShaderProgram(this, stages);
	}

	public GraphicsQuery CreateQuery(GraphicsQueryType type)
	{
		EnsureCurrent();

		return new GraphicsQuery(this, type);
	}

	public Mesh2D CreateMesh2D(BufferUsage usage = BufferUsage.Static)
	{
		EnsureCurrent();

		return new Mesh2D(this, usage);
	}

	public Mesh3D CreateMesh3D(BufferUsage usage = BufferUsage.Static)
	{
		EnsureCurrent();

		return new Mesh3D(this, usage);
	}

	public Mesh3D CreateMesh3D(
		Vertex3[] vertices,
		bool hasUvs = true,
		bool hasColors = true
	)
	{
		EnsureCurrent();

		return new Mesh3D(this, vertices, hasUvs, hasColors);
	}

	public Mesh3D CreateMesh3D(
		Vertex2[] vertices,
		bool hasUvs = true,
		bool hasColors = true
	)
	{
		EnsureCurrent();

		return new Mesh3D(this, vertices, hasUvs, hasColors);
	}

	public Mesh3D CreateMesh3D(
		GenericMesh mesh,
		bool hasUvs = true,
		bool hasColors = true
	)
	{
		EnsureCurrent();

		return new Mesh3D(this, mesh, hasUvs, hasColors);
	}

	public RenderTarget CreateRenderTarget(RenderTargetDescriptor descriptor)
	{
		EnsureCurrent();

		return new RenderTarget(this, descriptor);
	}

	internal VertexArray CreateVertexArray()
	{
		EnsureCurrent();

		return new VertexArray(this);
	}
}
