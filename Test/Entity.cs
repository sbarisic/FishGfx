using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using FishGfx;
using FishGfx.Game;

namespace Test {
	class Entity {
		public FishGfxGame Game;

		public Entity(FishGfxGame Game) {
			this.Game = Game;
		}

		public virtual void Update(float Dt, float GameTime) {
		}

		public virtual void Draw() {
		}

		public override string ToString() {
			return string.Format("Entity '{0}'", GetType().Name);
		}
	}

	class LevelEntity : Entity {
		public string Name;
		public int ID;

		public int X;
		public int Y;

		public Vector2 Position {
			get {
				return new Vector2(X, Y);
			}
		}

		public LevelEntity() : base(null) {
		}

		public override string ToString() {
			return string.Format("Entity '{0}'", Name);
		}
	}
}
