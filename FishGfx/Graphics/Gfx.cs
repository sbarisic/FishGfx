using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using OpenGL;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;

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

		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		//////////////////////////////////////////////////// 3D  3D  3D ////////////////////////////////////////////////////////////
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

		public static ShaderProgram Line3D;
		public static ShaderProgram Point3D;
		public static ShaderProgram Default3D;

		static Mesh3D Mesh3D;

		static void Init3D(PrimitiveType Primitive) {
			if (Line3D == null)
				throw new Exception(nameof(Line3D) + " shader not assigned");

			if (Point3D == null)
				throw new Exception(nameof(Point3D) + " shader not assigned");

			if (Default3D == null)
				throw new Exception(nameof(Default3D) + " shader not assigned");

			if (Mesh2D == null)
				Mesh3D = new Mesh3D(BufferUsage.DynamicDraw);

			Mesh3D.PrimitiveType = Primitive;
		}

		public static void Point(Vertex3[] Points, float Thickness) {
			Init3D(PrimitiveType.Points);
			Mesh3D.SetVertices(Points);

			Point3D.Uniform1f("Thickness", Thickness);
			Point3D.Bind();
			Mesh3D.Draw();
			Point3D.Unbind();
		}

		public static void Point(Vertex3 Position, float Thickness) {
			Point(new Vertex3[] { Position }, Thickness);
		}

		public static void Point(Vertex3[] Positions) {
			Init3D(PrimitiveType.Points);
			Mesh3D.SetVertices(Positions);
			
			Default3D.Bind();
			Mesh3D.Draw();
			Default3D.Unbind();
		}

		public static void Point(Vertex3 Position) {
			Point(new Vertex3[] { Position });
		}

		public static void Line(Vertex3 Start, Vertex3 End, float Thickness) {
			Init3D(PrimitiveType.Lines);
			Mesh3D.SetVertices(Start, End);

			Line3D.Uniform1f("Thickness", Thickness);
			Line3D.Bind();
			Mesh3D.Draw();
			Line3D.Unbind();
		}

		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		//////////////////////////////////////////////////// 2D  2D  2D ////////////////////////////////////////////////////////////
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

		public static ShaderProgram Line2D;
		public static ShaderProgram Point2D;
		public static ShaderProgram Default2D;

		static Mesh2D Mesh2D;

		static void Init2D(PrimitiveType Primitive) {
			if (Line2D == null)
				throw new Exception(nameof(Line2D) + " shader not assigned");

			if (Point2D == null)
				throw new Exception(nameof(Point2D) + " shader not assigned");

			if (Default2D == null)
				throw new Exception(nameof(Default2D) + " shader not assigned");

			if (Mesh2D == null)
				Mesh2D = new Mesh2D(BufferUsage.DynamicDraw);

			Mesh2D.PrimitiveType = Primitive;
		}

		public static void Point(Vertex2[] Positions, float Thickness) {
			Init2D(PrimitiveType.Points);
			Mesh2D.SetVertices(Positions);

			Point2D.Uniform1f("Thickness", Thickness);
			Point2D.Bind();
			Mesh2D.Draw();
			Point2D.Unbind();
		}

		public static void Point(Vertex2 Position, float Thickness) {
			Point(new Vertex2[] { Position }, Thickness);
		}

		public static void Point(Vertex2[] Positions) {
			Init2D(PrimitiveType.Points);
			Mesh2D.SetVertices(Positions);

			Default2D.Bind();
			Mesh2D.Draw();
			Default2D.Unbind();
		}

		public static void Point(Vertex2 Position) {
			Point(new Vertex2[] { Position });
		}

		public static void Line(Vertex2 Start, Vertex2 End, float Thickness) {
			Init2D(PrimitiveType.Lines);
			Mesh2D.SetVertices(Start, End);

			Line2D.Uniform1f("Thickness", Thickness);
			Line2D.Bind();
			Mesh2D.Draw();
			Line2D.Unbind();
		}

		public static void LineStrip(Vertex2[] Points) {
			Init2D(PrimitiveType.LineStrip);

			// TODO
		}

		public static void Bezier(Vector2 Start, Vector2 End) {
			Init2D(PrimitiveType.Lines);

			// TODO
		}
	}
}
