using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;

namespace Test {
	class SpriteAnimation {
		public int StartFrame;
		public int FrameCount;
		public int CurrentFrame;

		public bool Loop;
		public float FrameTime;
		public float LastTime;

		public bool FlipX;

		public SpriteAnimation(SpriteAnimation Clone) {
			StartFrame = Clone.StartFrame;
			FrameCount = Clone.FrameCount;
			CurrentFrame = Clone.CurrentFrame;
			Loop = Clone.Loop;
			FrameTime = Clone.FrameTime;
			LastTime = Clone.LastTime;
			FlipX = Clone.FlipX;
		}

		public SpriteAnimation(float FrameTime = 0, bool Loop = false, bool FlipX = false) {
			StartFrame = 0;
			FrameCount = 1;
			CurrentFrame = 0;
			LastTime = 0;

			this.FrameTime = FrameTime;
			this.Loop = Loop;
			this.FlipX = FlipX;
		}

		public void Reset() {
			CurrentFrame = StartFrame - 1;
			LastTime = -1;
		}

		public bool TryStepFrame(float GameTime) {
			float NextTime = LastTime + FrameTime;
			int LastFrame = CurrentFrame;

			if (NextTime <= GameTime) {
				LastTime = GameTime;
				CurrentFrame++;

				if (CurrentFrame >= StartFrame + FrameCount) {
					if (Loop)
						CurrentFrame = 0;
					else
						CurrentFrame = StartFrame + FrameCount - 1;
				}

				if (CurrentFrame == LastFrame)
					return false;

				return true;
			}

			return false;
		}
	}

	class SpriteAnimator {
		Dictionary<string, SpriteAnimation> Anims;

		List<Texture> Frames;
		SpriteAnimation Current;

		public SpriteAnimator() {
			Frames = new List<Texture>();
			Anims = new Dictionary<string, SpriteAnimation>();
		}

		public void AddFrame(Texture Frame) {
			Frames.Add(Frame);
		}

		public void AddAnimation(string Name, SpriteAnimation Anim) {
			Anims.Add(Name, Anim);
		}

		public void AddAnimation(string Name, SpriteAnimation Anim, Texture[] AnimFrames) {
			Anim.StartFrame = Frames.Count;
			Anim.FrameCount = AnimFrames.Length;
			Frames.AddRange(AnimFrames);
			AddAnimation(Name, Anim);
		}

		public SpriteAnimation CloneAnimation(string Name, string NewName) {
			SpriteAnimation Old = Anims[Name];

			SpriteAnimation New = new SpriteAnimation(Old);
			AddAnimation(NewName, New);
			return New;
		}

		public void Play(string Name, bool Reset = true) {
			Current = Anims[Name];

			if (Reset)
				Current.Reset();
		}

		public void Update(float GameTime, Sprite Sprite) {
			if (Current == null)
				return;

			if (Current.TryStepFrame(GameTime)) {
				Sprite.Texture = Frames[Current.CurrentFrame];

				if ((Current.FlipX && Sprite.Scale.X > 0) || (!Current.FlipX && Sprite.Scale.X < 0)) {
					Sprite.Scale.X *= -1;
					Sprite.Center.X *= -1;
				}
			}

		}
	}
}
