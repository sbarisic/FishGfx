using FishGfx.Formats;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using OpenGL;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx.Graphics {
	public static class Gfx {
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
			State.EnableDepthMask = true;
			State.EnableScissorTest = false;
			State.EnableBlend = true;

			State.PointSize = 1;
			State.ScissorRegion = new AABB(new Vector2(0, 0));

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

		// TODO: Cache state and only do delta-enable
		static void SetRenderState(RenderState State) {
			if (GlEnable(EnableCap.CullFace, State.EnableCullFace))
				Gl.CullFace((CullFaceMode)State.CullFace);

			Gl.DepthMask(State.EnableDepthMask);

			if (GlEnable(EnableCap.DepthTest, State.EnableDepthTest))
				Gl.DepthFunc((DepthFunction)State.DepthFunc);

			Gl.FrontFace((FrontFaceDirection)State.FrontFace);

			if (GlEnable(EnableCap.ScissorTest, State.EnableScissorTest)) {
				AABB Reg = State.ScissorRegion;
				Gl.Scissor((int)Reg.Position.X, (int)Reg.Position.Y, (int)Reg.Size.X, (int)Reg.Size.Y);
			}

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


		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		//////////////////////////////////////////////////// Generic ///////////////////////////////////////////////////////////////
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

		static Texture WhiteTex;

		static void InitGeneric() {
			if (WhiteTex == null) {
				using (System.Drawing.Bitmap Bmp = new System.Drawing.Bitmap(1, 1)) {
					Bmp.SetPixel(0, 0, Color.White);
					WhiteTex = Texture.FromImage(Bmp);
				}
			}
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
			InitGeneric();

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

		public static string ShadersDirectory = "data/shaders";

		static void Init2D(PrimitiveType Primitive) {
			InitGeneric();

			if (Line2D == null) {
				Line2D = new ShaderProgram(new ShaderStage(ShaderType.VertexShader, Path.Combine(ShadersDirectory, "line2d.vert")),
					new ShaderStage(ShaderType.GeometryShader, Path.Combine(ShadersDirectory, "line.geom")), new ShaderStage(ShaderType.FragmentShader, Path.Combine(ShadersDirectory, "line.frag")));
			}

			if (Point2D == null) {
				Point2D = new ShaderProgram(new ShaderStage(ShaderType.VertexShader, Path.Combine(ShadersDirectory, "point2d.vert")),
					new ShaderStage(ShaderType.GeometryShader, Path.Combine(ShadersDirectory, "point.geom")), new ShaderStage(ShaderType.FragmentShader, Path.Combine(ShadersDirectory, "point.frag")));
			}

			if (Default2D == null) {
				Default2D = new ShaderProgram(new ShaderStage(ShaderType.VertexShader, Path.Combine(ShadersDirectory, "default2d.vert")),
					new ShaderStage(ShaderType.FragmentShader, Path.Combine(ShadersDirectory, "default_tex_clr.frag")));
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
			Point2D.Bind(ShaderUniforms.Current);
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
			Default2D.Bind(ShaderUniforms.Current);
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
			Line2D.Bind(ShaderUniforms.Current);
			Mesh2D.Draw();
			Line2D.Unbind();
			End2D();
		}

		public static void LineStrip(Vertex2[] Points, float Thickness = 1) {
			Init2D(PrimitiveType.LineStrip);
			Mesh2D.SetVertices(Points);

			Start2D();
			Line2D.Uniform1f("Thickness", Thickness);
			Line2D.Bind(ShaderUniforms.Current);
			Mesh2D.Draw();
			Line2D.Unbind();
			End2D();
		}

		static Vertex2[] EmitRectangleTris(Vertex2[] Verts, int Offset, float X, float Y, float W, float H, float U0 = 0, float V0 = 0, float U1 = 1, float V1 = 1, Color? Color = null) {
			Color C = Color ?? FishGfx.Color.White;

			Verts[Offset] = new Vertex2(new Vector2(X, Y), new Vector2(U0, V0), C);
			Verts[Offset + 1] = new Vertex2(new Vector2(X + W, Y + H), new Vector2(U1, V1), C);
			Verts[Offset + 2] = new Vertex2(new Vector2(X, Y + H), new Vector2(U0, V1), C);
			Verts[Offset + 3] = new Vertex2(new Vector2(X, Y), new Vector2(U0, V0), C);
			Verts[Offset + 4] = new Vertex2(new Vector2(X + W, Y), new Vector2(U1, V0), C);
			Verts[Offset + 5] = new Vertex2(new Vector2(X + W, Y + H), new Vector2(U1, V1), C);

			return Verts;
		}

		public static void Rectangle(float X, float Y, float W, float H, float Thickness = 1, Color? Clr = null) {
			Color C = Clr ?? Color.White;

			LineStrip(new[] {
				new Vertex2(new Vector2(X, Y), C),
				new Vertex2(new Vector2(X + W, Y), C),
				new Vertex2(new Vector2(X + W, Y + H), C),
				new Vertex2(new Vector2(X, Y + H), C),
				new Vertex2(new Vector2(X, Y), C)
			}, Thickness);
		}

		public static void TexturedRectangle(float X, float Y, float W, float H, float U0 = 0, float V0 = 0, float U1 = 1, float V1 = 1, Color? Color = null, Texture Texture = null, ShaderProgram Shader = null) {
			Init2D(PrimitiveType.Triangles);
			Color C = Color ?? FishGfx.Color.White;

			Mesh2D.SetVertices(EmitRectangleTris(new Vertex2[6], 0, X, Y, W, H, U0, V0, U1, V1, Color));

			Start2D();
			Texture?.BindTextureUnit();

			if (Shader != null)
				Shader.Bind(ShaderUniforms.Current);
			else
				Default2D.Bind(ShaderUniforms.Current);

			Mesh2D.Draw();

			if (Shader != null)
				Shader.Unbind();
			else
				Default2D.Unbind();
			
			Texture?.UnbindTextureUnit();
			End2D();
		}

		public static void FilledRectangle(float X, float Y, float W, float H, Color? Clr = null) {
			TexturedRectangle(X, Y, W, H, 0, 0, 1, 1, Clr, WhiteTex);
		}

		public static void Bezier(Vector2 Start, Vector2 End) {
			Init2D(PrimitiveType.Lines);

			// TODO
		}

		public static Vector2 DrawText(GfxFont Font, Vector2 Pos, string Str, Color Clr, float FontSize = -1, bool DebugDraw = false) {
			if (string.IsNullOrEmpty(Str))
				return Vector2.Zero;

			Pos.X = (int)(Pos.X - 0.5f);
			Pos.Y = (int)(Pos.Y - 0.5f);

			float OldScale = Font.ScaledFontSize;
			if (FontSize > 0)
				Font.ScaledFontSize = FontSize;

			Texture AtlasTex = null;
			if (Font is BMFont BMFont)
				AtlasTex = BMFont.PageNames.First().Value;
			else
				throw new NotImplementedException("Not implemented for " + Font.GetType().ToString());

			GfxFont.CharDest[] Chars = Font.LayoutString(Str);
			Init2D(PrimitiveType.Triangles);
			Vertex2[] TextVertices = new Vertex2[Chars.Length * 6];

			for (int i = 0; i < Chars.Length; i++) {
				ref GfxFont.CharDest C = ref Chars[i];

				float X = C.CharOrigin.X / AtlasTex.Width;
				float Y = C.CharOrigin.Y / AtlasTex.Height;
				float W = C.CharOrigin.W / AtlasTex.Width;
				float H = C.CharOrigin.H / AtlasTex.Height;

				//TexturedRectangle(Pos.X + C.X, Pos.Y + C.Y, C.W, C.H, X, 1.0f - Y - H, X + W, 1.0f - Y, Texture: AtlasTex);
				EmitRectangleTris(TextVertices, i * 6, Pos.X + C.X, Pos.Y + C.Y, C.W, C.H, X, 1.0f - Y - H, X + W, 1.0f - Y, Clr);
			}

			// Draw
			{
				Mesh2D.SetVertices(TextVertices);

				Start2D();
				AtlasTex.BindTextureUnit();
				Default2D.Bind(ShaderUniforms.Current);
				Mesh2D.Draw();
				Default2D.Unbind();
				AtlasTex.UnbindTextureUnit();
				End2D();
			}

			if (DebugDraw) {
				FilledRectangle(Pos.X + Chars[0].X, Pos.Y + Chars[0].Y, 5, 5, Color.Red);
				FilledRectangle(Pos.X, Pos.Y, 5, 5, Color.Yellow);

				Vector2 Sz = Font.MeasureString(Chars);
				Rectangle(Pos.X, Pos.Y, Sz.X, Sz.Y, Clr: Color.Red);
			}

			Font.ScaledFontSize = OldScale;
			return Font.MeasureString(Chars);
		}
	}
}
