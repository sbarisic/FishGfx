using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx.Graphics.Drawables
{
	public class Sprite : IDrawable, IDisposable
	{
		Mesh3D Mesh;

		public ShaderProgram Shader;
		public Texture Texture;
		public Vector2 Center;
		public Vector2 Position;
		public Vector2 Scale;

		public bool FlipX;

		public Sprite()
		{
			Mesh = new Mesh3D(BufferUsage.Dynamic);
			Mesh.PrimitiveType = PrimitiveType.Triangles;

			Mesh.SetVertices(
				new Vertex3[]
				{
					new Vertex3(new Vector3(0, 0, 0), new Vector2(0, 0)),
					new Vertex3(new Vector3(0, 1, 0), new Vector2(0, 1)),
					new Vertex3(new Vector3(1, 1, 0), new Vector2(1, 1)),
					new Vertex3(new Vector3(1, 1, 0), new Vector2(1, 1)),
					new Vertex3(new Vector3(1, 0, 0), new Vector2(1, 0)),
					new Vertex3(new Vector3(0, 0, 0), new Vector2(0, 0)),
				}
			);
			Center = new Vector2(0, 0);
			Position = new Vector2(0, 0);
			Scale = new Vector2(1, 1);
		}

		public Sprite(Texture Tex)
			: this()
		{
			Texture = Tex;
		}

		public void SetUVs(Vector2 A, Vector2 B, Vector2 C, Vector2 D)
		{
			Mesh.SetUVs(new Vector2[] { A, B, C, C, D, A });
		}

		public void SetUVs(Vector2 Min, Vector2 Max)
		{
			SetUVs(Min, new Vector2(Min.X, Max.Y), Max, new Vector2(Max.X, Min.Y));
		}

		public void Draw()
		{
			Vector2 flipScale = new Vector2(FlipX ? -1 : 1, 1);
			ShaderUniforms uniforms = ShaderUniforms.Current;
			Matrix4x4 oldModel = uniforms.Model;
			bool shaderBound = false;
			bool textureBound = false;
			try
			{
				Matrix4x4 translation = Matrix4x4.CreateTranslation(Position.X - Center.X * flipScale.X, Position.Y - Center.Y * flipScale.Y, 0);
				uniforms.Model = Matrix4x4.CreateScale(Scale.X * flipScale.X, Scale.Y * flipScale.Y, 1) * translation;
				if (Shader != null) { Shader.Bind(uniforms); shaderBound = true; }
				if (Texture != null) { Texture.BindTextureUnit(); textureBound = true; }
				Mesh.Draw();
			}
			finally
			{
				if (textureBound) Texture.UnbindTextureUnit();
				if (shaderBound) Shader.Unbind();
				uniforms.Model = oldModel;
			}
		}

		public void Dispose() => Mesh.Dispose();
	}
}
