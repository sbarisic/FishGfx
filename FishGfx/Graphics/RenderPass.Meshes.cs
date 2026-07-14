using FishGfx.Graphics.Drawables;

namespace FishGfx.Graphics;

public sealed partial class RenderPass
{
	public void DrawMesh(
		Mesh2D mesh,
		Texture texture = null,
		ShaderProgram shader = null
	)
	{
		EnsureActive();
		context.Renderer.DrawMesh(this, mesh, texture, shader);
	}

	public void DrawMesh(
		Mesh3D mesh,
		Texture texture = null,
		ShaderProgram shader = null
	)
	{
		EnsureActive();
		context.Renderer.DrawMesh(this, mesh, texture, shader);
	}
}
