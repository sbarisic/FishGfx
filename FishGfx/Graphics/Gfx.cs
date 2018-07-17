using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenGL;

using System.Numerics;

namespace FishGfx.Graphics {
	public static class Gfx {
		static List<AABB> Scissors = new List<AABB>();

		public static void Clear() {
			Gl.ClearColor(69 / 255.0f, 112 / 255.0f, 56 / 255.0f, 1.0f);
			Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
		}

		public static void Clear(Color ClearColor) {
			Gl.ClearColor(ClearColor.R / 255.0f, ClearColor.G / 255.0f, ClearColor.B / 255.0f, ClearColor.A / 255.0f);
			Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
		}

		public static void CullFront() {
			Internal_OpenGL.CullFront();
		}

		public static void CullBack() {
			Internal_OpenGL.CullBack();
		}

		public static void FrontFace(bool Clockwise = false) {
			Internal_OpenGL.FrontFace(Clockwise);
		}

		public static void Scissor(int X, int Y, int W, int H, bool Enable) {
			Internal_OpenGL.Scissor(X, Y, W, H, Enable);
		}

		public static void Scissor(AABB Rect, bool Enable) {
			if (float.IsInfinity(Rect.Size.X) || float.IsInfinity(Rect.Size.Y)) {
				Scissor(0, 0, 0, 0, false);
				return;
			}

			Scissor((int)Rect.Position.X, (int)Rect.Position.Y, (int)Rect.Size.X, (int)Rect.Size.Y, Enable);
		}

		public static void PushScissor(AABB Rect) {
			AABB Parent = new AABB(Vector2.Zero, new Vector2(float.PositiveInfinity));

			if (Scissors.Count > 0)
				Parent = Scissors.Last();

			Scissors.Add(Parent.Intersection(Rect));
			Scissor(Scissors.Last(), true);
		}

		public static void PopScissor() {
			Scissors.RemoveAt(Scissors.Count - 1);

			if (Scissors.Count > 0)
				Scissor(Scissors.Last(), true);
			else
				Scissor(0, 0, 0, 0, false);
		}

		public static void EnableCullFace(bool Enable) {
			Internal_OpenGL.EnableCullFace(Enable);
		}

		public static void EnableDepthDest(bool Enable) {
			Internal_OpenGL.EnableDepthTest(Enable);
		}

		public static void Line(Vertex2 Start, Vertex2 End) {
			// TODO
		}

		public static void LineStrip(Vertex2[] Points) {
			// TODO
		}

		public static void Bezier(Vector2 Start, Vector2 End) {
			// TODO
		}
	}
}
