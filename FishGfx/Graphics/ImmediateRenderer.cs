using System;
using System.Collections.Generic;
using System.IO;
using FishGfx.Graphics.Drawables;

namespace FishGfx.Graphics;

internal sealed partial class ImmediateRenderer : IDisposable
{
	private readonly GraphicsContext context;
	private readonly string shaderDirectory;
	private Texture whiteTexture;
	private ShaderProgram default2D;
	private ShaderProgram line2D;
	private ShaderProgram point2D;
	private ShaderProgram sdfText2D;
	private ShaderProgram default3D;
	private Mesh2D scratchMesh2D;
	private Mesh3D scratchMesh3D;
	private bool disposed;

	internal ImmediateRenderer(GraphicsContext context)
	{
		this.context = context ?? throw new ArgumentNullException(nameof(context));
		shaderDirectory = Path.Combine(AppContext.BaseDirectory, "data", "shaders");
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		scratchMesh2D?.Dispose();
		scratchMesh3D?.Dispose();
		default2D?.Dispose();
		line2D?.Dispose();
		point2D?.Dispose();
		sdfText2D?.Dispose();
		default3D?.Dispose();
		whiteTexture?.Dispose();
	}

	private void Ensure2DResources(PrimitiveType primitiveType)
	{
		EnsureUsable();
		EnsureWhiteTexture();

		default2D ??= LoadProgram(
			(ShaderStageType.Vertex, "default2d.vert"),
			(ShaderStageType.Fragment, "default_tex_clr.frag")
		);
		line2D ??= LoadProgram(
			(ShaderStageType.Vertex, "line2d.vert"),
			(ShaderStageType.Geometry, "line.geom"),
			(ShaderStageType.Fragment, "line.frag")
		);
		point2D ??= LoadProgram(
			(ShaderStageType.Vertex, "point2d.vert"),
			(ShaderStageType.Geometry, "point.geom"),
			(ShaderStageType.Fragment, "point.frag")
		);
		sdfText2D ??= LoadProgram(
			(ShaderStageType.Vertex, "default2d.vert"),
			(ShaderStageType.Fragment, "sdf_text.frag")
		);
		scratchMesh2D ??= context.CreateMesh2D(BufferUsage.Stream);
		scratchMesh2D.PrimitiveType = primitiveType;
	}

	private void Ensure3DResources(PrimitiveType primitiveType)
	{
		EnsureUsable();
		EnsureWhiteTexture();

		default3D ??= LoadProgram(
			(ShaderStageType.Vertex, "default3d.vert"),
			(ShaderStageType.Fragment, "default.frag")
		);
		scratchMesh3D ??= context.CreateMesh3D(BufferUsage.Dynamic);
		scratchMesh3D.PrimitiveType = primitiveType;
	}

	private void EnsureWhiteTexture()
	{
		if (whiteTexture != null)
		{
			return;
		}

		whiteTexture = context.CreateTexture(new TextureDescriptor(1, 1));

		try
		{
			whiteTexture.Write(new[] { Color.White }, TextureDataFormat.RGBA8Unorm);
		}
		catch
		{
			whiteTexture.Dispose();
			whiteTexture = null;

			throw;
		}
	}

	private ShaderProgram LoadProgram(
		params (ShaderStageType Type, string FileName)[] stageDescriptions
	)
	{
		List<ShaderStage> stages = new List<ShaderStage>(stageDescriptions.Length);

		try
		{
			foreach ((ShaderStageType type, string fileName) in stageDescriptions)
			{
				stages.Add(context.LoadShaderStage(type, Path.Combine(shaderDirectory, fileName)));
			}

			ShaderProgram program = context.CreateShaderProgram(stages.ToArray());
			program.SetUniform("uTexture", 0);

			return program;
		}
		finally
		{
			foreach (ShaderStage stage in stages)
			{
				stage.Dispose();
			}
		}
	}

	private void EnsureUsable()
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(ImmediateRenderer));
		}

		context.EnsureCurrent();
	}
}
