using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;

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
	}
}
