using FishGfx;
using FishGfx.Graphics;
using OpenGL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;

namespace FishGfx.Gweny.Renderer {
	public class FishGfxRenderer : Base {
		RenderState RS;
		ShaderProgram Shader;
		ShaderUniforms U;

		public FishGfxRenderer(ShaderProgram Shader, RenderWindow RWind) {
			RS = Gfx.CreateDefaultRenderState();
			RS.EnableDepthTest = false;

			U = ShaderUniforms.CreateDefault();
			U.Camera.SetOrthogonal(0, 0, RWind.WindowWidth, RWind.WindowHeight);
			//U.Camera.SetOrthogonal(0, 0, 100, 100);

			this.Shader = Shader;
		}

		public override void Begin() {
			Gfx.PushRenderState(RS);
			ShaderUniforms.Push(U);
			Shader.Bind(ShaderUniforms.Current);
		}

		public override void End() {
			Shader.Unbind();
			ShaderUniforms.Pop();
			Gfx.PopRenderState();
		}

		public override void DrawLinedRect(Rectangle rect) {
			Gfx.Rectangle(rect.X, rect.Y, rect.Width, rect.Height, Clr: DrawColor);
		}

		public override void DrawFilledRect(Rectangle rect) {
			Gfx.FilledRectangle(rect.X, rect.Y, rect.Width, rect.Height, DrawColor);
		}

		public override void DrawTexturedRect(GwenyTexture t, Rectangle targetRect, float u1 = 0, float v1 = 0, float u2 = 1, float v2 = 1) {
			Gfx.TexturedRectangle(targetRect.X, targetRect.Y, targetRect.Width, targetRect.Height, u1, v1, u2, v2, DrawColor, t.RendererData as Texture);
		}

		public override void DrawLine(int x, int y, int a, int b) {
			Gfx.Line(new Vertex2(new Vector2(x, y), DrawColor), new Vertex2(new Vector2(a, b), DrawColor));
		}

		public override void LoadTexture(GwenyTexture t) {
			string Pth = t.Name.StartsWith("data") ? t.Name : "data/textures/" + t.Name;

			Texture T = Texture.FromFile(Pth);

			t.Width = T.Width;
			t.Height = T.Height;
			t.RendererData = T;
			t.Failed = false;
		}
	}
}
