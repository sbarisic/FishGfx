using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using FishGfx;
using FishGfx.Game;
using FishGfx.Graphics.Drawables;
using Humper;
using FishGfx.Graphics;
using System.Drawing;

namespace Test {
	class Entity {
		public TestGame Game;

		public virtual void OnSpawn() {
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

			set {
				X = (int)value.X;
				Y = (int)value.Y;
			}
		}

		SpriteAnimator SpriteAnimator;
		Sprite Sprite;

		public override void OnSpawn() {
			int TileSize = Game.Lvl.LayerMain.TileSize;
			// TODO: Entities in editor start at top-left, in engine they start at bottom-left

			if (Name == "spikes") {
				IBox SpikeBox = Game.PhysWorld.Create(X, Y - TileSize, TileSize, TileSize);
				SpikeBox.AddTags(PhysicsTags.Spike, PhysicsTags.Solid);
				SpikeBox.Data = FishGfx.Color.Red;
			} else if (Name == "torch_fire") {
				Y -= TileSize;

				// TODO: Move fire to separate entity
				Sprite = new Sprite();
				Sprite.Shader = Game.DefaultShader;

				SpriteAnimator = new SpriteAnimator();
				SpriteAnimator.AddAnimation("default", new SpriteAnimation(2.0f / 100, Loop: true), Texture.FromFileAtlas("data/textures/fire_10x26.png", 10, 26));
				SpriteAnimator.Play("default");

				Sprite.Texture = SpriteAnimator.Frames[0];
				Sprite.Scale = Sprite.Texture.Size * 1.6f;
				// TODO: Fix size ^
			}
		}

		public override void Update(float Dt, float GameTime) {
			if (SpriteAnimator != null && Sprite != null)
				SpriteAnimator.Update(GameTime, Sprite);
		}

		public override void Draw() {
			if (Sprite != null) {
				Sprite.Position = Position;
				Sprite.Draw();
			}

			// DEBUG DRAW
			if (false) {
				int TileSize = Game.Lvl.LayerMain.TileSize;
				Gfx.Rectangle(X, Y, TileSize, TileSize);
			}
		}

		public override string ToString() {
			return string.Format("Entity '{0}'", Name);
		}
	}
}
