using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using System.Numerics;
using FishGfx.Game;

namespace Test {
	class Pawn : Entity {
		protected Sprite Sprite;
		protected SpriteAnimator Animator;

		public Vector2 Position;

		public Vector2 Size {
			get {
				return Sprite.Scale;
			}
		}

		public Pawn(FishGfxGame Game, ShaderProgram Shader) : base(Game) {
			Sprite = new Sprite();
			Sprite.Shader = Shader;

			Animator = new SpriteAnimator();
		}

		protected void CenterResizeSprite() {
			Sprite.Scale = Sprite.Texture.Size;
			Sprite.Center = Sprite.Scale * new Vector2(0.5f, 0.0f);
		}

		public override void Update(float Dt, float GameTime) {
			Animator.Update(GameTime, Sprite);
		}

		public override void Draw() {
			Sprite.Position = Position;
			Sprite.Draw();
		}
	}
}
