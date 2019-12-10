using FishGfx.Game;
using FishGfx.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using FishGfx;

namespace Test {
	class Player : Pawn {
		public override void OnSpawn() {
			List<Texture> WalkLeftFrames = new List<Texture>();
			for (int i = 2; i < 10; i++)
				WalkLeftFrames.Add(Texture.FromFile(string.Format("data/textures/rick/{0}.png", i)));

			Animator.AddAnimation(ANIM_MOVE_LEFT, new SpriteAnimation(75.0f / 1000, true), WalkLeftFrames.ToArray());
			Animator.CloneAnimation(ANIM_MOVE_LEFT, ANIM_MOVE_RIGHT).FlipX = true;

			Animator.AddAnimation(ANIM_STAND_LEFT, new SpriteAnimation(), new[] { Texture.FromFile("data/textures/rick/1.png") });
			Animator.CloneAnimation(ANIM_STAND_LEFT, ANIM_STAND_RIGHT).FlipX = true;

			Sprite.Texture = WalkLeftFrames[0];
			CenterResizeSprite();
			CreatePhysicsBox();

			base.OnSpawn();
		}


		public override void Kill(PhysicsTags Reason) {
			if (Dead)
				return;

			base.Kill(Reason);

			Game.Particles.SpawnParticles(Particle.CreateExplosion(Game.GameTime, Position, ParticleSystem.RickDeath));

			Console.WriteLine("You died by " + Reason);
			//Teleport(Game.Lvl.GetEntitiesByName("spawn_player").First().Position);
		}

		void CalcCameraPos() {
			Camera Cam = ShaderUniforms.Current.Camera;

			Vector2 HalfViewport = Cam.ViewportSize / 2;
			Vector2 CameraPos = new Vector2(Cam.Position.X, Cam.Position.Y) + HalfViewport;

			// TODO: Horizontal smoothing
			Vector2 FollowPos = Position - Sprite.Center;
			Vector2 CurPos = CameraPos;

			float FollowDistX = Cam.ViewportSize.X / 6;
			float FollowDistY = Cam.ViewportSize.Y / 6;

			if (Utils.DistanceX(FollowPos, CurPos, out float DistX) > FollowDistX)
				CurPos = CurPos + new Vector2(DistX + (FollowDistX * (DistX > 0 ? -1 : 1)), 0);

			if (Utils.DistanceY(FollowPos, CurPos, out float DistY) > FollowDistY)
				CurPos = CurPos + new Vector2(0, DistY + (FollowDistY * (DistY > 0 ? -1 : 1)));

			Cam.Position = new Vector3(Utils.Round(CurPos - HalfViewport), 0);
		}

		public override void Update(float Dt, float GameTime) {
			Key KeyLeft = Key.A;
			Key KeyRight = Key.D;
			Key KeyJump = Key.Space;
			Vector2 MoveDir = Vector2.Zero;

			if (Dead && Game.Input.GetKeyPressed(KeyJump)) {
				Game.Con.OnCommand("respawn");
				return;
			}

			if (Game.Input.GetKeyDown(KeyLeft))
				MoveDir.X = -1;

			if (Game.Input.GetKeyDown(KeyRight))
				MoveDir.X = 1;

			if (Grounded && Game.Input.GetKeyDown(KeyJump))
				MoveDir.Y = 1;

			// Debug movement
			if (Game.Input.GetKeyDown(Key.W)) {
				MoveDir.Y = 1;

				if (!Dead)
					Game.Particles.SpawnParticle(Particle.CreateExplosionParticle(Game.GameTime, Position + Sprite.Center, ParticleSystem.Fire.Random(), 64));
			}

			if (Game.Input.GetKeyDown(Key.S))
				MoveDir.Y = -1;

			Move(Dt, MoveDir);
			base.Update(Dt, GameTime);

			/*if (Position.Y < -100)
				Position.Y = 600;

			if (Position.X < -32)
				Position.X = 800;
			if (Position.X > 832)
				Position.X = 0;*/

			CalcCameraPos();
		}
	}
}
