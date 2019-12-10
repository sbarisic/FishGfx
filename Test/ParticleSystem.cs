using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FishGfx.Graphics.Drawables;
using FishGfx.Graphics;
using System.Numerics;
using FishGfx;

namespace Test {
	public struct Particle {
		public float StartOfLife;
		public float EndOfLife;
		public Texture Tex;

		public Vector2 Position;
		public Vector2 Velocity;

		public Particle(float GameTime, float Len, Texture Tex) {
			StartOfLife = GameTime;
			EndOfLife = GameTime + Len;

			this.Tex = Tex;
			Position = Vector2.Zero;
			Velocity = Vector2.Zero;
		}

		public static Particle CreateExplosionParticle(float GameTime, Vector2 Position, Texture Tex, float Velocity = 96) {
			Particle Part = new Particle(GameTime, 2, Tex);

			Part.Position = Position;
			Part.Velocity = GfxUtils.RandomDir2() * GfxUtils.RandomFloat() * Velocity;

			return Part;
		}

		public static Particle[] CreateExplosion(float GameTime, Vector2 Position, Texture[] Tex) {
			Particle[] Parts = new Particle[Tex.Length];

			for (int i = 0; i < Parts.Length; i++) {
				Particle Part = CreateExplosionParticle(GameTime, Position, Tex[i]);
				Part.EndOfLife += GfxUtils.RandomFloat() - 0.5f;
				Parts[i] = Part;
			}

			return Parts;
		}
	}

	class ParticleSystem {
		Sprite Sprite;
		Particle?[] Particles;

		public ParticleSystem(TestGame Game) {
			// TODO: Move max particles constant somewhere else
			Particles = new Particle?[1024];

			Sprite = new Sprite();
			Sprite.Shader = Game.DefaultShader;
		}

		public bool SpawnParticle(Particle Part) {
			for (int i = 0; i < Particles.Length; i++)
				if (Particles[i] == null) {
					Particles[i] = Part;
					return true;
				}

			return false;
		}

		public void SpawnParticles(Particle[] Parts) {
			foreach (var P in Parts)
				SpawnParticle(P);
		}

		public void Update(float Dt, float GameTime) {
			for (int i = 0; i < Particles.Length; i++) {
				if (Particles[i] == null)
					continue;
				Particle Part = Particles[i].Value;

				if (Part.EndOfLife <= GameTime) {
					Particles[i] = null;
					continue;
				}

				Part.Position = Part.Position + Part.Velocity * Dt;
				Particles[i] = Part;
			}
		}

		public void Draw() {
			for (int i = 0; i < Particles.Length; i++) {
				if (Particles[i] == null)
					continue;
				Particle Part = Particles[i].Value;

				Sprite.Texture = Part.Tex;
				Sprite.Scale = Sprite.Texture.Size;
				Sprite.Center = Sprite.Scale * 0.5f;
				Sprite.Position = Part.Position;
				Sprite.Draw();
			}
		}
	}
}
