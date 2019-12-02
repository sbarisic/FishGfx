using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;
using FishGfx.Graphics.Drawables;
using FishGfx.Graphics;

namespace Test {
	class LevelValues {
		[JsonProperty("time_limit")]
		public int TimeLimit;
	}

	class LevelEntity {
		public string Name;
		public int ID;

		public int X;
		public int Y;

		public int OriginX;
		public int OriginY;

		public override string ToString() {
			return string.Format("Entity '{0}'", Name);
		}
	}

	class LevelDecal {
	}

	class LevelLayer {
		public string Name;

		public int OffsetX;
		public int OffsetY;
		public string Tileset;

		public int GridCellWidth;
		public int GridCellHeight;

		public int GridCellsX;
		public int GridCellsY;

		public string[] Grid;
		public LevelEntity[] Entities;
		public LevelDecal[] Decals;
		public int[][] DataCoords;

		public override string ToString() {
			return string.Format("Layer '{0}'", Name);
		}
	}

	class GameLevel {
		public int Width;
		public int Height;
		public int OffsetX;
		public int OffsetY;

		public LevelValues Values;
		public LevelLayer[] Layers;

		public static GameLevel FromFile(string FileName) {
			string JsonSrc = File.ReadAllText(FileName);
			GameLevel Lvl = JsonConvert.DeserializeObject<GameLevel>(JsonSrc);
			return Lvl;
		}

		Tilemap Foreground;
		Tilemap Main;
		Tilemap Background;

		void FillTilemap(Tilemap Map, int[][] Coords) {
			Texture TileAtlas = Map.GetTileAtlas();
			int TileCountX = TileAtlas.Width / Map.TileSize;

			for (int i = 0; i < Coords.Length; i++) {
				int[] XY = Coords[i];

				if (XY.Length == 1)
					continue;

				int TileX = XY[0];
				int TileY = XY[1];

				int MapX = i % Map.Width;
				int MapY = (i - MapX) / Map.Width;
				MapY = Map.Height - MapY - 1;

				int TileIdx = TileY * TileCountX + TileX;
				Map.SetTile(MapX, MapY, TileIdx);
			}
		}

		IEnumerable<LevelEntity> GetEntityByName(string Name) {
			for (int i = 0; i < Layers.Length; i++) {
				if (Layers[i].Name == "entities")
					for (int j = 0; j < Layers[i].Entities.Length; j++) {
						LevelEntity Ent = Layers[i].Entities[j];

						if (Ent.Name == Name)
							yield return Ent;
					}
			}
		}

		public void Init(ShaderProgram Shader) {
			Texture TileTex = Texture.FromFile("data/textures/tilemap.png");
			int TileSize = 16;

			for (int i = 0; i < Layers.Length; i++) {
				LevelLayer Layer = Layers[i];

				switch (Layer.Name) {
					case "foreground":
						Foreground = new Tilemap(TileSize, Layer.GridCellsX, Layer.GridCellsY, TileTex);
						Foreground.Shader = Shader;
						FillTilemap(Foreground, Layer.DataCoords);
						break;

					case "main":
						Main = new Tilemap(TileSize, Layer.GridCellsX, Layer.GridCellsY, TileTex);
						Main.Shader = Shader;
						FillTilemap(Main, Layer.DataCoords);
						break;

					case "background":
						Background = new Tilemap(TileSize, Layer.GridCellsX, Layer.GridCellsY, TileTex);
						Background.Shader = Shader;
						FillTilemap(Background, Layer.DataCoords);
						break;

					case "grid":
						break;

					case "decals":
						break;

					case "entities":
						break;

					default:
						throw new NotImplementedException();
				}
			}
		}

		public void Draw() {
			Background?.Draw();
			Main?.Draw();
			//Foreground?.Draw();
		}
	}
}
