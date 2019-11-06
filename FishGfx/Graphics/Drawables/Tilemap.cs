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

		int Width;
		int Height;
		int TileSize;
		int[] Tiles;

		public Tilemap(int TileSize, int Width, int Height) {
			this.TileSize = TileSize;
			this.Width = Width;
			this.Height = Height;
			Tiles = new int[Width * Height];

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

		void IdxToXY(int Idx, out int X, out int Y) {
			X = Idx % Width;
			Y = (Idx - X) / Width;
		}

		public void SetTile(int Tile, int X, int Y) {
			if (X < 0 || X >= Width)
				throw new Exception("X out of bounds");

			if (Y < 0 || Y >= Height)
				throw new Exception("Y out of bounds");

			Tiles[Y * Width + X] = Tile;
			Dirty = true;
		}

		public int GetTile(int X, int Y) {
			if (X < 0 || X >= Width)
				throw new Exception("X out of bounds");

			if (Y < 0 || Y >= Height)
				throw new Exception("Y out of bounds");

			return Tiles[Y * Width + X];
		}

		void Update() {
			VertList.Clear();

			for (int i = 0; i < Tiles.Length; i++) {
				int Tile = Tiles[i];
				if (Tile == 0)
					continue;

				IdxToXY(i, out int X, out int Y);

				// TODO: UV size and position bad

				Vector3 Pos = new Vector3(X, Y, 0) * TileSize;
				Vector2 UVPos = new Vector2(X, Y) * TileSize;

				VertList.Add(new Vertex3(Pos + new Vector3(0, 0, 0), UVPos));
				VertList.Add(new Vertex3(Pos + new Vector3(0, TileSize, 0), UVPos + new Vector2(0, TileSize)));
				VertList.Add(new Vertex3(Pos + new Vector3(TileSize, TileSize, 0), UVPos + new Vector2(TileSize, TileSize)));
				VertList.Add(new Vertex3(Pos + new Vector3(TileSize, TileSize, 0), UVPos + new Vector2(TileSize, TileSize)));
				VertList.Add(new Vertex3(Pos + new Vector3(TileSize, 0, 0), UVPos + new Vector2(TileSize, 0)));
				VertList.Add(new Vertex3(Pos + new Vector3(0, 0, 0), UVPos));
			}

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

