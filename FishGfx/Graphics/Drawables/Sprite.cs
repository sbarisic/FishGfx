using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

namespace FishGfx.Graphics.Drawables {
	public class Sprite : IDrawable {
		Mesh3D Mesh;

		public ShaderProgram Shader;
		public Texture Texture;
		public Vector2 Center;
		public Vector2 Position;
		public Vector2 Scale;

		public bool FlipX;

		public Sprite() {
			Mesh = new Mesh3D(BufferUsage.DynamicDraw);
			Mesh.PrimitiveType = PrimitiveType.Triangles;

			Mesh.SetVertices(new Vertex3[] {
				new Vertex3(new Vector3(0, 0, 0), new Vector2(0, 0)),
				new Vertex3(new Vector3(0, 1, 0), new Vector2(0, 1)),
				new Vertex3(new Vector3(1, 1, 0), new Vector2(1, 1)),
				new Vertex3(new Vector3(1, 1, 0), new Vector2(1, 1)),
				new Vertex3(new Vector3(1, 0, 0), new Vector2(1, 0)),
				new Vertex3(new Vector3(0, 0, 0), new Vector2(0, 0))
			});
			Center = new Vector2(0, 0);
			Position = new Vector2(0, 0);
			Scale = new Vector2(1, 1);
		}

		public Sprite(Texture Tex) : this() {
			Texture = Tex;
		}

		public void SetUVs(Vector2 A, Vector2 B, Vector2 C, Vector2 D) {
			Mesh.SetUVs(new Vector2[] { A, B, C, C, D, A });
		}

		public void SetUVs(Vector2 Min, Vector2 Max) {
			SetUVs(Min, new Vector2(Min.X, Max.Y), Max, new Vector2(Max.X, Min.Y));
		}

		public void Draw() {
			Vector2 FlipScale = new Vector2(FlipX ? -1 : 1, 1);

			Matrix4x4 OldModel = ShaderUniforms.Current.Model;
			Matrix4x4 Translation = Matrix4x4.CreateTranslation(Position.X - Center.X * FlipScale.X, Position.Y - Center.Y * FlipScale.Y, 0);
			ShaderUniforms.Current.Model = Matrix4x4.CreateScale(Scale.X * FlipScale.X, Scale.Y * FlipScale.Y, 1) * Translation;

			Shader?.Bind(ShaderUniforms.Current);
			Texture?.BindTextureUnit();
			Mesh.Draw();
			Texture?.UnbindTextureUnit();
			Shader?.Unbind();

			ShaderUniforms.Current.Model = OldModel;
		}
	}
}

