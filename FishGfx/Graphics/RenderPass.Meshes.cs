using FishGfx.Graphics.Drawables;

namespace FishGfx.Graphics;

public sealed partial class RenderPass
{
    /// <summary>Draws a shared mesh with gl_InstanceID available to its shader.</summary>
    public void DrawMeshInstances(Mesh3D mesh, int instances, Texture texture = null, ShaderProgram shader = null)
    {
        EnsureActive();
        System.ArgumentOutOfRangeException.ThrowIfNegative(instances);
        if (instances == 0) return;
        context.Renderer.DrawMesh(this, mesh, texture, shader, instances);
    }

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
