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
	class Parallax {
		Texture[] Layers = new Texture[] { };
		Sprite Sprite;

		public Parallax(TestGame Game) {
			Sprite = new Sprite();
			Sprite.Shader = Game.DefaultShader;
		}

		public void AddLayer(Texture Tex) {
			Layers = Layers.Add(Tex);
		}

		public void AddLayers(params Texture[] Tex) {
			foreach (var T in Tex)
				AddLayer(T);
		}

		void DrawTiled(Sprite S, Vector2 ScrollPos, Vector2 CamPos) {
			float DiffX = (CamPos - ScrollPos).X;
			float ModX = (int)(DiffX / S.Scale.X);

			for (int i = 0; i < 2; i++) {
				Vector2 Scroll = new Vector2(S.Scale.X, 0) * i;

				S.Position = ScrollPos + new Vector2(S.Scale.X, 0) * ModX + Scroll;
				S.Draw();
			}
		}

		public void Draw(Camera Cam) {
			const float LayerScale = 0.8f;

			for (int i = 0; i < Layers.Length; i++) {
				//int i = Layers.Length - 1;

				Sprite.Texture = Layers[i];
				Sprite.Scale = Sprite.Texture.Size * 3;

				float ScaleX = (float)Math.Pow(LayerScale, i + 1);
				float ScaleY = 1;

				Vector2 ScrollPos = Cam.Position.XY() * new Vector2(ScaleX, ScaleY);
				DrawTiled(Sprite, ScrollPos, Cam.Position.XY());
			}
		}
	}
}
