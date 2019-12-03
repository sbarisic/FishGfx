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
		const string MOVE_LEFT = "move_left";
		const string MOVE_RIGHT = "move_right";
		const string STAND_LEFT = "stand_left";
		const string STAND_RIGHT = "stand_right";

		bool LookingLeft;

		public Player(FishGfxGame Game, ShaderProgram Shader) : base(Game, Shader) {
			List<Texture> WalkLeftFrames = new List<Texture>();
			for (int i = 2; i < 10; i++)
				WalkLeftFrames.Add(Texture.FromFile(string.Format("data/textures/rick/{0}.png", i)));

			Animator.AddAnimation(MOVE_LEFT, new SpriteAnimation(75.0f / 1000, true), WalkLeftFrames.ToArray());
			Animator.CloneAnimation(MOVE_LEFT, MOVE_RIGHT).FlipX = true;

			Animator.AddAnimation(STAND_LEFT, new SpriteAnimation(), new[] { Texture.FromFile("data/textures/rick/1.png") });
			Animator.CloneAnimation(STAND_LEFT, STAND_RIGHT).FlipX = true;

			Sprite.Texture = WalkLeftFrames[0];
			CenterResizeSprite();
		}

		public override void Update(float Dt, float GameTime) {
			Key KeyLeft = Key.A;
			Key KeyRight = Key.D;
			Key KeyJump = Key.Space;

			Vector2 MoveDir = Vector2.Zero;

			if (Game.Input.GetKeyDown(KeyLeft)) {
				MoveDir.X = -1;
			}

			if (Game.Input.GetKeyDown(KeyRight)) {
				MoveDir.X = 1;
			}

			if (Game.Input.GetKeyDown(KeyJump)) {
				MoveDir.Y = 1;
			}

			if (MoveDir.X > 0) {
				Animator.Play(MOVE_RIGHT, false);
				LookingLeft = false;
			} else if (MoveDir.X < 0) {
				Animator.Play(MOVE_LEFT, false);
				LookingLeft = true;
			}

			if (MoveDir.Length() == 0)
				Animator.Play(LookingLeft ? STAND_LEFT : STAND_RIGHT);

			base.Update(Dt, GameTime);
		}
	}
}
