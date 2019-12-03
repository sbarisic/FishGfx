using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;
using FishGfx.Graphics.Drawables;
using FishGfx.Graphics;
using System.Numerics;
using FishGfx.Game;

namespace Test {
	class LevelTile {
		public int X;
		public int Y;
		public int ID;
	}

	class LevelLayer {
		public string Name;
		// public string Tileset; 

		public LevelEntity[] Entities;
		public LevelTile[] Tiles;

		public override string ToString() {
			return string.Format("Layer '{0}'", Name);
		}
	}

	class GameLevel {
		public int Width;
		public int Height;

		[JsonProperty("time_limit")]
		public int TimeLimit;

		public LevelLayer[] Layers;

		public static GameLevel FromFile(string FileName) {
			const bool InvertY = true;

			string JsonSrc = File.ReadAllText(FileName);
			GameLevel Lvl = JsonConvert.DeserializeObject<GameLevel>(JsonSrc);


			for (int i = 0; i < Lvl.Layers.Length; i++) {
				LevelLayer L = Lvl.Layers[i];

				if (L.Name == "entities")
					for (int j = 0; j < L.Entities.Length; j++) {
						LevelEntity Ent = L.Entities[j];
						Ent.Y = Lvl.Height - Ent.Y - 1;
					}

				/*if (L.DataCoords != null)
					for (int j = 0; j < L.DataCoords.Length; j++) {
						int[] XY = L.DataCoords[j];

						if (XY.Length > 1) {
							XY[1] = Lvl.Height - XY[1] - 1;
							L.DataCoords[j] = XY;
						}
					}*/
			}

			return Lvl;
		}

		Tilemap Foreground;
		Tilemap Main;
		Tilemap Background;

		void FillTilemap(Tilemap Map, LevelTile[] Tiles) {
			Texture TileAtlas = Map.GetTileAtlas();
			//int TileCountX = TileAtlas.Width / Map.TileSize;

			for (int i = 0; i < Tiles.Length; i++) {
				int TileX = Tiles[i].X;
				int TileY = Map.Height - Tiles[i].Y - 1;
				int TileIdx = Tiles[i].ID;

				Map.SetTile(TileX, TileY, TileIdx);
			}
		}

		public IEnumerable<LevelEntity> GetAllEntities() {
			for (int i = 0; i < Layers.Length; i++) {
				if (Layers[i].Name == "entities")
					for (int j = 0; j < Layers[i].Entities.Length; j++) {
						yield return Layers[i].Entities[j];
					}
			}
		}

		public IEnumerable<LevelEntity> GetEntitiesByName(string Name) {
			foreach (var E in GetAllEntities()) {
				if (E.Name == Name)
					yield return E;
			}
		}

		public void Init(ShaderProgram Shader) {
			Texture TileTex = Texture.FromFile("data/textures/tilemap.png");
			int TileSize = 16;

			int TilesX = Width / TileSize;
			int TilesY = Height / TileSize;

			for (int i = 0; i < Layers.Length; i++) {
				LevelLayer Layer = Layers[i];

				switch (Layer.Name) {
					case "foreground":
						Foreground = new Tilemap(TileSize, TilesX, TilesY, TileTex);
						Foreground.Shader = Shader;
						FillTilemap(Foreground, Layer.Tiles);
						break;

					case "main":
						Main = new Tilemap(TileSize, TilesX, TilesY, TileTex);
						Main.Shader = Shader;
						FillTilemap(Main, Layer.Tiles);
						break;

					case "background":
						Background = new Tilemap(TileSize, TilesX, TilesY, TileTex);
						Background.Shader = Shader;
						FillTilemap(Background, Layer.Tiles);
						break;

					case "entities":
						break;

					default:
						throw new NotImplementedException();
				}
			}
		}

		public void DrawBackground() {
			Background?.Draw();
			Main?.Draw();
		}

		public void DrawForeground() {
			Foreground?.Draw();
		}
	}
}
