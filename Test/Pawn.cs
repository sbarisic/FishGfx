using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using System.Numerics;
using FishGfx.Game;
using FishGfx;
using Humper;
using Humper.Responses;

namespace Test {
	class Pawn : Entity {
		protected const string ANIM_MOVE_LEFT = "move_left";
		protected const string ANIM_MOVE_RIGHT = "move_right";
		protected const string ANIM_STAND_LEFT = "stand_left";
		protected const string ANIM_STAND_RIGHT = "stand_right";
		protected const string ANIM_JUMP = "jump";
		protected const string ANIM_FALL = "fall";

		protected Sprite Sprite;
		protected SpriteAnimator Animator;

		public Vector2 Position;
		public Vector2 Velocity;

		protected bool Grounded;
		protected int GroundTileID;

		// Movement stuff for pawns
		protected float MoveAccelX = 128;
		protected float MoveAccelY = 512;

		protected float MaxVelocityX = 192;
		protected float MaxVelocityY = 512;

		protected float DeccelX = 0.6f;
		protected float DeccelY = 0.99f;

		protected float AirMoveAccelX = 16;
		protected float AirDeccelX = 0.99f;

		// Other stuff
		bool LookingLeft;

		IBox PhysBox;

		public Vector2 Size {
			get {
				return Sprite.Scale;
			}
		}

		public Pawn(TestGame Game, ShaderProgram Shader) : base(Game) {
			Sprite = new Sprite();
			Sprite.Shader = Shader;

			Animator = new SpriteAnimator();
		}

		protected void CenterResizeSprite() {
			Sprite.Scale = Sprite.Texture.Size;
			Sprite.Center = Sprite.Scale * new Vector2(0.5f, 0.0f);
		}

		protected virtual void CreatePhysicsBox() {
			Vector2 Pos = Sprite.Position - Sprite.Center;
			PhysBox = Game.PhysWorld.Create(Pos.X, Pos.Y, Size.X, Size.Y);
			PhysBox.AddTags(PhysicsTags.Pawn);
		}

		protected virtual void Animate(float GameTime, Vector2 MoveDir) {
			float AbsX = Math.Abs(MoveDir.X);
			float AbsY = Math.Abs(MoveDir.Y);

			if (AbsY > AbsX) {
				if (MoveDir.Y > 0)
					Animator.Play(ANIM_JUMP, false);
				else if (MoveDir.Y < 0)
					Animator.Play(ANIM_FALL, false);
			} else {
				if (MoveDir.X > 0) {
					Animator.Play(ANIM_MOVE_RIGHT, false);
					LookingLeft = false;
				} else if (MoveDir.X < 0) {
					Animator.Play(ANIM_MOVE_LEFT, false);
					LookingLeft = true;
				}
			}

			if (MoveDir.Length() < 5)
				Animator.Play(LookingLeft ? ANIM_STAND_LEFT : ANIM_STAND_RIGHT);

			Animator.Update(GameTime, Sprite);
		}

		public virtual void Move(float Dt, Vector2 MoveDir) {
			Vector2 Gravity = new Vector2(0, -30);
			//Gravity = Vector2.Zero;

			Vector2 MoveAccel = MoveDir * new Vector2(Grounded ? MoveAccelX : AirMoveAccelX, MoveAccelY);
			Velocity += MoveAccel + Gravity;

			Velocity.X = GfxUtils.Clamp(Velocity.X * (Grounded ? DeccelX : AirDeccelX), -MaxVelocityX, MaxVelocityX);
			Velocity.Y = GfxUtils.Clamp(Velocity.Y * DeccelY, -MaxVelocityY, MaxVelocityY);
		}

		public override void Update(float Dt, float GameTime) {
			Vector2 Pos = Position - Sprite.Center;
			Vector2 NewPos = Utils.Round(Pos + Velocity * Dt);

			IMovement PawnMove = PhysBox.Move(NewPos.X, NewPos.Y, (Col) => {
				return CollisionResponses.Slide;
			});

			Pos.X = PhysBox.X;
			Pos.Y = PhysBox.Y;

			Grounded = PawnMove.Hits.Any((Hit) => Hit.Box.HasTag(PhysicsTags.Solid) && Hit.Normal.Y > 0);
			if (Grounded && Velocity.Y <= 0)
				Velocity.Y = 0;

			/*if (CCounter++ > 0) {
				CCounter = 0;
				Console.WriteLine("Velocity: {0:0.00} .. {1:0.00}", Velocity.X, Velocity.Y);
			}*/

			Position = Pos + Sprite.Center;
			Animate(GameTime, Velocity);
		}

		//int CCounter;

		public override void Draw() {
			Sprite.Position = Position;
			Sprite.Draw();
		}
	}
}