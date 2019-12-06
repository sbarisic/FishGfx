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

		public virtual bool Collides(Vector2 Position) {
			return Collides(Position, out Vector2 TestPoint, out int TileID);
		}

		public virtual bool Collides(Vector2 Position, out Vector2 TestPoint, out int TileID) {
			return Collides(Position, ((TestGame)Game).Lvl, out TestPoint, out TileID);
		}

		public virtual bool Collides(Vector2 Position, GameLevel Level, out Vector2 TestPoint, out int TileID) {
			Position = Position - Sprite.Center;
			TileID = -1;

			if (Level.GetSolid(TestPoint = Position, out TileID))
				return true;

			if (Level.GetSolid(TestPoint = Position + new Vector2(Size.X, 0), out TileID))
				return true;

			if (Level.GetSolid(TestPoint = Position + new Vector2(0, Size.Y), out TileID))
				return true;

			if (Level.GetSolid(TestPoint = Position + Size, out TileID))
				return true;

			TestPoint = Vector2.Zero;
			return false;
		}

		bool CollidesIterate(int Count, Vector2 NewPos, out Vector2 CorNewPos) {
			Vector2 Dist = NewPos - Position;
			CorNewPos = Position;

			for (int i = Count; i >= 1; i--) {
				if (Collides(Position + Dist * (1.0f / i)))
					return true;
				else {
					CorNewPos = Position + Dist * (1.0f / i);
					continue;
				}
			}

			return false;
		}

		protected virtual void Animate(Vector2 MoveDir) {
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
		}

		public virtual void Move(float Dt, Vector2 MoveDir) {
			Vector2 MoveAccel = MoveDir * new Vector2(Grounded ? MoveAccelX : AirMoveAccelX, MoveAccelY);
			Velocity += MoveAccel + new Vector2(0, -32);

			Velocity.X = GfxUtils.Clamp(Velocity.X * (Grounded ? DeccelX : AirDeccelX), -MaxVelocityX, MaxVelocityX);
			Velocity.Y = GfxUtils.Clamp(Velocity.Y * DeccelY, -MaxVelocityY, MaxVelocityY);
		}

		public override void Update(float Dt, float GameTime) {
			Animator.Update(GameTime, Sprite);
			Vector2 NewPos = Position + Velocity * Dt;
			Vector2 CorNewPos;

			const int CollisionIterations = 20;

			if (CollidesIterate(CollisionIterations, new Vector2(Position.X, NewPos.Y), out CorNewPos)) {
				NewPos.Y = (int)CorNewPos.Y;
				Velocity.Y = 0;
			}

			if (CollidesIterate(CollisionIterations, new Vector2(NewPos.X, Position.Y), out CorNewPos)) {
				NewPos.X = (int)CorNewPos.X;
				Velocity.X = 0;
			}

			Position = NewPos;
			Grounded = Collides(Position + new Vector2(0, -1), out Vector2 TestPoint, out GroundTileID);

			Animate(Velocity);
		}

		public override void Draw() {
			Sprite.Position = Position;
			Sprite.Draw();
		}
	}
}