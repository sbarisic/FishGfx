using System;
using FishGfx.Graphics.Drawables;

namespace FishGfx.Graphics;

public sealed partial class RenderPass
{
	public void DrawModel(RenderModel model, ShaderProgram shader = null)
	{
		EnsureActive();
		ArgumentNullException.ThrowIfNull(model);
		model.Render(this, shader ?? model.Shader);
	}

	public void Render(IRenderable renderable)
	{
		EnsureActive();
		ArgumentNullException.ThrowIfNull(renderable);
		renderable.Render(this);
	}
}
