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

		protected Sprite Sprite = new Sprite();
		protected SpriteAnimator Animator = new SpriteAnimator();

		public Vector2 Position;
		public Vector2 Velocity;

		protected bool Grounded;

		// Movement stuff for pawns
		protected float MoveAccelX = 128;
		protected float MaxVelocityX = 192;

		protected float MoveAccelY = 512 + 64; // 512
		protected float MaxVelocityY = 512 + 128;

		protected float DeccelX = 0.6f;
		protected float DeccelY = 0.99f;

		protected float AirMoveAccelX = 16;
		protected float AirDeccelX = 0.99f;

		// Other stuff
		bool LookingLeft;
		IBox PhysBox;

		public bool Dead;

		public Vector2 Size {
			get {
				return Sprite.Scale;
			}
		}

		public override void OnSpawn() {
			Sprite.Shader = Game.DefaultShader;
			Respawn(Position);
		}

		public virtual void Respawn(Vector2 Position) {
			Dead = false;
			Teleport(Position);
		}

		protected void CenterResizeSprite() {
			Sprite.Scale = Sprite.Texture.Size;
			Sprite.Center = Sprite.Scale * new Vector2(0.5f, 0.0f);
		}

		protected virtual void CreatePhysicsBox() {
			PhysBox = Game.PhysWorld.Create(Position.X, Position.Y, Size.X, Size.Y);
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

		public virtual void Teleport(Vector2 NewPosition) {
			PhysBox.Move(NewPosition.X, NewPosition.Y, (Col) => {
				return CollisionResponses.None;
			});

			Position.X = PhysBox.X;
			Position.Y = PhysBox.Y;
		}

		public virtual void Kill(PhysicsTags Reason) {
			Dead = true;
		}

		public virtual void Move(float Dt, Vector2 MoveDir) {
			if (Dead) {
				Velocity = Vector2.Zero;
				return;
			}

			Velocity += MoveDir * new Vector2(Grounded ? MoveAccelX : AirMoveAccelX, MoveAccelY);

			// Gravity
			Velocity += new Vector2(0, -30);

			Velocity.X = GfxUtils.Clamp(Velocity.X, -MaxVelocityX, MaxVelocityX);
			Velocity.Y = GfxUtils.Clamp(Velocity.Y, -MaxVelocityY, MaxVelocityY);

			if (MoveDir.X == 0)
				Velocity.X *= (Grounded ? DeccelX : AirDeccelX);

			if (MoveDir.Y == 0)
				Velocity.Y *= DeccelY;
		}

		public override void Update(float Dt, float GameTime) {
			Vector2 NewPos = Position + Velocity * Dt;

			IMovement PawnMove = PhysBox.Move(NewPos.X, NewPos.Y, (Col) => {
				if (!Col.Other.HasTag(PhysicsTags.Solid))
					return CollisionResponses.None;

				return CollisionResponses.Slide;
			});

			Position.X = PhysBox.X;
			Position.Y = PhysBox.Y;

			if (Position.Y < 0)
				Kill(PhysicsTags.Hazard);

			//Console.WriteLine("Pos: {0:0.00}, {1:0.00}", Position.X, Position.Y);

			bool WasGrounded = Grounded;
			Grounded = false;

			foreach (var Hit in PawnMove.Hits) {
				if (Hit.Box.HasTag(PhysicsTags.Spike))
					Kill(PhysicsTags.Spike);

				if (Hit.Box.HasTag(PhysicsTags.Solid)) {

					if (Hit.Normal.X < 0) {
						if (Velocity.X >= 0)
							Velocity.X = 0;
					} else if (Hit.Normal.X > 0) {
						if (Velocity.X <= 0)
							Velocity.X = 0;
					} else if (Hit.Normal.Y < 0) {
						// Hit with head

						if (Velocity.Y >= 0)
							Velocity.Y = 0;
					} else if (Hit.Normal.Y > 0) {
						// Ground

						if (!WasGrounded)
							Velocity.X = 0;

						Grounded = true;

						if (Velocity.Y <= 0)
							Velocity.Y = 0;
					}

				}
			}

			/*Grounded = PawnMove.Hits.Any((Hit) => Hit.Box.HasTag(PhysicsTags.Solid) && Hit.Normal.Y > 0);
			if (Grounded && Velocity.Y <= 0)
				Velocity.Y = 0;*/

			/*if (CCounter++ > 0) {
				CCounter = 0;
				Console.WriteLine("Velocity: {0:0.00} .. {1:0.00}", Velocity.X, Velocity.Y);
			}*/

			//Position = Pos + Sprite.Center;
			Animate(GameTime, Velocity);
		}

		//int CCounter;

		public override void Draw() {
			if (Dead)
				return;

			Sprite.Position = Position + Sprite.Center;
			Sprite.Draw();
		}
	}
}