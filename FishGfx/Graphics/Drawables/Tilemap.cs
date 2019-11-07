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
		public Vector2 Position;
		public Vector2 Scale;

		bool Dirty;
		int DrawableTileCount;

		Texture TileAtlas;
		int TileAtlasWidth;
		int TileAtlasHeight;
		int TextureWidth;
		int TextureHeight;

		public int Width {
			get; private set;
		}

		public int Height {
			get; private set;
		}

		int TileSize;
		int[] Tiles;

		Vector2 TileUVSize;

		public Tilemap(int TileSize, int Width, int Height, Texture TileAtlasTexture) {
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
			Scale = new Vector2(1, 1);

			SetTileAtlas(TileAtlasTexture);
			ClearTiles();
		}

		public void ClearTiles(int Tile = -1) {
			for (int i = 0; i < Tiles.Length; i++)
				Tiles[i] = Tile;
			Dirty = true;
		}

		public void SetTileAtlas(Texture Tex) {
			TileAtlas = Tex;

			TextureWidth = Tex.Width;
			TextureHeight = Tex.Height;

			TileAtlasWidth = TextureWidth / TileSize;
			TileAtlasHeight = TextureHeight / TileSize;

			TileUVSize = new Vector2(TileSize, TileSize) / new Vector2(TextureWidth, TextureHeight);

			Dirty = true;
		}

		void IdxToXY(int Idx, out int X, out int Y) {
			X = Idx % Width;
			Y = (Idx - X) / Width;
		}

		void TileToTileXY(int Tile, out int X, out int Y) {
			X = Tile % TileAtlasWidth;
			Y = (Tile - X) / TileAtlasWidth;
		}

		public void SetTile(int X, int Y, int Tile) {
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

		public bool TryWorldPosToTile(Vector2 WorldPos, out int X, out int Y) {
			X = Y = 0;

			// TODO: Clean that shit up, what the fuck
			Vector2 LocalPos = WorldPos - Position;

			if (LocalPos.X < 0 || LocalPos.Y < 0)
				return false;

			LocalPos = (LocalPos / Scale) / TileSize;
			X = (int)LocalPos.X;
			Y = (int)LocalPos.Y;

			if (X >= Width || Y >= Height)
				return false;

			return true;
		}

		void Update() {
			VertList.Clear();
			DrawableTileCount = 0;

			for (int i = 0; i < Tiles.Length; i++) {
				int Tile = Tiles[i];
				if (Tile < 0)
					continue;

				IdxToXY(i, out int X, out int Y);
				TileToTileXY(Tile, out int TX, out int TY);

				Vector2 UVPos = new Vector2(TX, TY) * TileUVSize;
				UVPos.Y = 1.0f - UVPos.Y - TileUVSize.Y;

				Vector3 Pos = new Vector3(X, Y, 0) * TileSize;
				VertList.Add(new Vertex3(Pos + new Vector3(0, 0, 0), UVPos));
				VertList.Add(new Vertex3(Pos + new Vector3(0, TileSize, 0), UVPos + new Vector2(0, TileUVSize.Y)));
				VertList.Add(new Vertex3(Pos + new Vector3(TileSize, TileSize, 0), UVPos + new Vector2(TileUVSize.X, TileUVSize.Y)));
				VertList.Add(new Vertex3(Pos + new Vector3(TileSize, TileSize, 0), UVPos + new Vector2(TileUVSize.X, TileUVSize.Y)));
				VertList.Add(new Vertex3(Pos + new Vector3(TileSize, 0, 0), UVPos + new Vector2(TileUVSize.X, 0)));
				VertList.Add(new Vertex3(Pos + new Vector3(0, 0, 0), UVPos));

				DrawableTileCount++;
			}

			Mesh.SetVertices(VertList.ToArray());
		}

		public void Draw() {
			if (Dirty) {
				Dirty = false;
				Update();
			}

			if (DrawableTileCount <= 0)
				return;

			ShaderUniforms.Current.Model = Matrix4x4.CreateScale(Scale.X, Scale.Y, 1) * Matrix4x4.CreateTranslation(Position.X, Position.Y, 0);

			Shader?.Bind(ShaderUniforms.Current);
			TileAtlas?.BindTextureUnit();
			Mesh.Draw();
			TileAtlas?.UnbindTextureUnit();
			Shader?.Unbind();
		}
	}
}

