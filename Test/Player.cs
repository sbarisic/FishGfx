using FishGfx.Game;
using FishGfx.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Test {
	class Player : Pawn {
		public Player(FishGfxGame Game, ShaderProgram Shader) : base(Game, Shader) {
			List<Texture> WalkLeftFrames = new List<Texture>();
			for (int i = 2; i < 10; i++)
				WalkLeftFrames.Add(Texture.FromFile(string.Format("data/textures/rick/{0}.png", i)));

			Animator.AddAnimation(ANIM_MOVE_LEFT, new SpriteAnimation(75.0f / 1000, true), WalkLeftFrames.ToArray());
			Animator.CloneAnimation(ANIM_MOVE_LEFT, ANIM_MOVE_RIGHT).FlipX = true;

			Animator.AddAnimation(ANIM_STAND_LEFT, new SpriteAnimation(), new[] { Texture.FromFile("data/textures/rick/1.png") });
			Animator.CloneAnimation(ANIM_STAND_LEFT, ANIM_STAND_RIGHT).FlipX = true;

			Sprite.Texture = WalkLeftFrames[0];
			CenterResizeSprite();
		}

		public override void Update(float Dt, float GameTime) {
			Key KeyLeft = Key.A;
			Key KeyRight = Key.D;
			Key KeyJump = Key.Space;
			Vector2 MoveDir = Vector2.Zero;

			if (Game.Input.GetKeyDown(KeyLeft))
				MoveDir.X = -1;

			if (Game.Input.GetKeyDown(KeyRight))
				MoveDir.X = 1;

			if (Grounded && Game.Input.GetKeyDown(KeyJump))
				MoveDir.Y = 1;

			Move(Dt, MoveDir);
			base.Update(Dt, GameTime);
		}
	}
}
