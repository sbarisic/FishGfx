using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx.Graphics {
	public static class Gfx {
		static List<AABB> Scissors = new List<AABB>();
		static Stack<RenderState> RenderStates = new Stack<RenderState>();

		public static RenderState CreateDefaultRenderState() {
			RenderState State = new RenderState();
			State.CullFace = CullFace.Back;
			State.DepthFunc = DepthFunc.Less;
			State.FrontFace = FrontFace.Clockwise;

			State.BlendFunc_Src = BlendFactor.SrcAlpha;
			State.BlendFunc_Dst = BlendFactor.OneMinusSrcAlpha;

			State.EnableCullFace = true;
			State.EnableDepthTest = true;
			State.EnableScissorTest = false;
			State.EnableBlend = true;

			State.PointSize = 1;

			return State;
		}

		public static int GetRenderStateCount() {
			return RenderStates.Count;
		}

		public static void PushRenderState(RenderState State) {
			RenderStates.Push(State);
			SetRenderState(State);
		}

		public static RenderState PeekRenderState() {
			return RenderStates.Peek();
		}

		public static RenderState PopRenderState() {
			RenderState State = RenderStates.Pop();

			if (GetRenderStateCount() > 0)
				SetRenderState(RenderStates.Peek());

			return State;
		}

		static void SetRenderState(RenderState State) {
			if (GlEnable(EnableCap.CullFace, State.EnableCullFace))
				Gl.CullFace((CullFaceMode)State.CullFace);

			if (GlEnable(EnableCap.DepthTest, State.EnableDepthTest))
				Gl.DepthFunc((DepthFunction)State.DepthFunc);

			Gl.FrontFace((FrontFaceDirection)State.FrontFace);

			GlEnable(EnableCap.ScissorTest, State.EnableScissorTest);

			if (GlEnable(EnableCap.Blend, State.EnableBlend))
				Gl.BlendFunc((BlendingFactor)State.BlendFunc_Src, (BlendingFactor)State.BlendFunc_Dst);

			Gl.PointSize(State.PointSize);
		}

		static bool GlEnable(EnableCap Cap, bool Enable) {
			if (Enable)
				Gl.Enable(Cap);
			else
				Gl.Disable(Cap);

			return Enable;
		}

		// TODO: Convert the scissors to use the new render state
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









		public static void Clear() {
			Gl.ClearColor(69 / 255.0f, 112 / 255.0f, 56 / 255.0f, 1.0f);
			Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
		}

		public static void Clear(Color ClearColor) {
			Gl.ClearColor(ClearColor.R / 255.0f, ClearColor.G / 255.0f, ClearColor.B / 255.0f, ClearColor.A / 255.0f);
			Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
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

			if (Mesh3D == null)
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

		static ShaderProgram Line2D;
		static ShaderProgram Point2D;
		static ShaderProgram Default2D;

		static Mesh2D Mesh2D;

		static void Init2D(PrimitiveType Primitive) {
			if (Line2D == null) {
				Line2D = new ShaderProgram(new ShaderStage(ShaderType.VertexShader, "data/shaders/line2d.vert"),
					new ShaderStage(ShaderType.GeometryShader, "data/shaders/line.geom"), new ShaderStage(ShaderType.FragmentShader, "data/shaders/line.frag"));
			}

			if (Point2D == null) {
				Point2D = new ShaderProgram(new ShaderStage(ShaderType.VertexShader, "data/shaders/point2d.vert"),
					new ShaderStage(ShaderType.GeometryShader, "data/shaders/point.geom"), new ShaderStage(ShaderType.FragmentShader, "data/shaders/point.frag"));
			}

			if (Default2D == null) {
				Default2D = new ShaderProgram(new ShaderStage(ShaderType.VertexShader, "data/shaders/default2d.vert"),
					new ShaderStage(ShaderType.FragmentShader, "data/shaders/default_tex_clr.frag"));
			}

			if (Mesh2D == null)
				Mesh2D = new Mesh2D(BufferUsage.DynamicDraw);

			Mesh2D.PrimitiveType = Primitive;
		}

		static void Start2D() {
			RenderState State = PeekRenderState();
			State.FrontFace = FrontFace.CounterClockwise;
			PushRenderState(State);
		}


		static void End2D() {
			PopRenderState();
		}

		public static void Point(Vertex2[] Positions, float Thickness) {
			Init2D(PrimitiveType.Points);
			Mesh2D.SetVertices(Positions);

			Point2D.Uniform1f("Thickness", Thickness);
			Point2D.Bind(ShaderUniforms.Default);
			Mesh2D.Draw();
			Point2D.Unbind();
		}

		public static void Point(Vertex2 Position, float Thickness) {
			Point(new Vertex2[] { Position }, Thickness);
		}

		public static void Point(Vertex2[] Positions) {
			Init2D(PrimitiveType.Points);
			Mesh2D.SetVertices(Positions);

			Start2D();
			Default2D.Bind(ShaderUniforms.Default);
			Mesh2D.Draw();
			Default2D.Unbind();
			End2D();
		}

		public static void Point(Vertex2 Position) {
			Point(new Vertex2[] { Position });
		}

		public static void Line(Vertex2 Start, Vertex2 End, float Thickness = 1) {
			Init2D(PrimitiveType.Lines);
			Mesh2D.SetVertices(Start, End);

			Start2D();
			Line2D.Uniform1f("Thickness", Thickness);
			Line2D.Bind(ShaderUniforms.Default);
			Mesh2D.Draw();
			Line2D.Unbind();
			End2D();
		}

		public static void LineStrip(Vertex2[] Points, float Thickness = 1) {
			Init2D(PrimitiveType.LineStrip);
			Mesh2D.SetVertices(Points);

			Start2D();
			Line2D.Uniform1f("Thickness", Thickness);
			Line2D.Bind(ShaderUniforms.Default);
			Mesh2D.Draw();
			Line2D.Unbind();
			End2D();
		}

		public static void Rectangle(float X, float Y, float W, float H, float Thickness = 1) {
			LineStrip(new[] { new Vertex2(X, Y), new Vertex2(X + W, Y), new Vertex2(X + W, Y + H), new Vertex2(X, Y + H), new Vertex2(X, Y) }, Thickness);
		}

		public static void Bezier(Vector2 Start, Vector2 End) {
			Init2D(PrimitiveType.Lines);

			// TODO
		}
	}
}
