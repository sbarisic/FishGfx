using System;
using FishGfx.Graphics.Drawables;

namespace FishGfx.Graphics;

/// <summary>
/// Draws a retained 3D mesh using optional caller-owned texture and shader resources.
/// </summary>
public sealed class DrawMeshCommand : RenderCommand
{
	public DrawMeshCommand(Mesh3D mesh, Texture texture = null, ShaderProgram shader = null)
	{
		Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
		Texture = texture;
		Shader = shader;
	}

	public Mesh3D Mesh { get; }

	public Texture Texture { get; }

	public ShaderProgram Shader { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.DrawMesh(Mesh, Texture, Shader);
	}
}

/// <summary>
/// Draws a retained model and its material textures using an optional caller-owned shader.
/// </summary>
public sealed class DrawModelCommand : RenderCommand
{
	public DrawModelCommand(RenderModel model, ShaderProgram shader = null)
	{
		Model = model ?? throw new ArgumentNullException(nameof(model));
		Shader = shader;
	}

	public RenderModel Model { get; }

	public ShaderProgram Shader { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.DrawModel(Model, Shader);
	}
}
