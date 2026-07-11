using System;
using FishGfx.Graphics.Drawables;

namespace FishGfx.Graphics
{
	/// <summary>
	/// Draws a retained 3D mesh using optional caller-owned texture and shader resources.
	/// </summary>
	public sealed class DrawMesh3DCommand : GraphicsCommand
	{
		public DrawMesh3DCommand(Mesh3D mesh, Texture texture = null, ShaderProgram shader = null)
		{
			Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
			Texture = texture;
			Shader = shader;
		}

		public Mesh3D Mesh { get; }
		public Texture Texture { get; }
		public ShaderProgram Shader { get; }

		public override void Execute()
		{
			bool shaderBound = false;
			bool textureBound = false;

			try
			{
				if (Shader != null)
				{
					Shader.Bind(ShaderUniforms.Current);
					shaderBound = true;
				}

				if (Texture != null)
				{
					Texture.BindTextureUnit();
					textureBound = true;
				}

				Mesh.Draw();
			}
			finally
			{
				if (textureBound)
					Texture.UnbindTextureUnit();
				if (shaderBound)
					Shader.Unbind();
			}
		}
	}

	/// <summary>
	/// Draws a retained model and its material textures using an optional caller-owned shader.
	/// </summary>
	public sealed class DrawRenderModelCommand : GraphicsCommand
	{
		public DrawRenderModelCommand(RenderModel model, ShaderProgram shader = null)
		{
			Model = model ?? throw new ArgumentNullException(nameof(model));
			Shader = shader;
		}

		public RenderModel Model { get; }
		public ShaderProgram Shader { get; }

		public override void Execute()
		{
			if (Shader == null)
			{
				Model.Draw();
				return;
			}

			Shader.Bind(ShaderUniforms.Current);

			try
			{
				Model.Draw();
			}
			finally
			{
				Shader.Unbind();
			}
		}
	}
}
