using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

namespace FishGfx.Graphics.Drawables {
	public class Tilemap : IDrawable {
		List<Vertex3> VertList = new List<Vertex3>();

		Mesh3D Mesh;

		public ShaderProgram Shader;
		public Texture TileAtlas;
		public Vector2 Position;
		public Vector2 Size;

		bool Dirty;

		public Tilemap(int TileSize, int Width, int Height) {
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

			Position = new Vector2(0, 0);
			Size = new Vector2(1, 1);

			Dirty = true;
		}

		void Update() {
			VertList.Clear();

			Mesh.SetVertices(VertList.ToArray());
		}

		public void Draw() {
			if (Dirty) {
				Dirty = false;
				Update();
			}

			ShaderUniforms.Current.Model = Matrix4x4.CreateScale(Size.X, Size.Y, 1) * Matrix4x4.CreateTranslation(Position.X, Position.Y, 0);

			Shader?.Bind(ShaderUniforms.Current);
			TileAtlas?.BindTextureUnit();
			Mesh.Draw();
			TileAtlas?.UnbindTextureUnit();
			Shader?.Unbind();
		}
	}
}

