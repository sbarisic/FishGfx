using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using FishGfx.Formats;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics
{
	internal sealed class ImmediateGraphicsResources
	{
		internal Texture WhiteTexture;
		internal ShaderProgram Line3D;
		internal ShaderProgram Point3D;
		internal ShaderProgram Default3D;
		internal Mesh3D Mesh3D;
		internal ShaderProgram Line2D;
		internal ShaderProgram Point2D;
		internal ShaderProgram Default2D;
		internal ShaderProgram SdfText2D;
		internal Mesh2D Mesh2D;
	}

	public static class Gfx
	{
		static readonly Stack<RenderState> LegacyRenderStates = new Stack<RenderState>();
		static readonly ImmediateGraphicsResources LegacyImmediateResources = new ImmediateGraphicsResources();
		static ImmediateGraphicsResources ImmediateResources => GraphicsContext.CurrentOrNull?.ImmediateResources ?? LegacyImmediateResources;
		static Stack<RenderState> RenderStates => GraphicsContext.CurrentOrNull?.RenderStates ?? LegacyRenderStates;

		internal static void InvalidateStateCache(GraphicsContext context)
		{
			if (context == null) throw new ArgumentNullException(nameof(context));
			context.AppliedRenderState = null;
		}

		public static RenderState CreateDefaultRenderState()
		{
			RenderState State = new RenderState();
			State.CullFace = CullFace.Back;
			State.DepthFunc = DepthFunc.Less;
			State.FrontFace = FrontFace.Clockwise;

			State.BlendFunc_Src = BlendFactor.SrcAlpha;
			State.BlendFunc_Dst = BlendFactor.OneMinusSrcAlpha;

			State.StencilFunc(StencilFunction.Skip, 0, 0);
			State.StencilOp(StencilOperation.Skip, StencilOperation.Skip, StencilOperation.Skip);
			State.StencilWriteMask(uint.MaxValue);

			State.EnableScissorTest = false;
			State.EnableStencilTest = false;
			State.EnableCullFace = true;
			State.EnableDepthTest = true;
			State.EnableBlend = true;
			State.EnableDepthClamp = true;

			State.EnableDepthMask = true;
			State.EnableColorMaskR = true;
			State.EnableColorMaskG = true;
			State.EnableColorMaskB = true;
			State.EnableColorMaskA = true;

			State.PointSize = 1;
			State.ScissorRegion = new AABB(new Vector2(0, 0));

			return State;
		}

		public static int GetRenderStateCount()
		{
			return RenderStates.Count;
		}

		public static void PushRenderState(RenderState State)
		{
			ValidateRenderState(State);
			SetRenderState(State);
			RenderStates.Push(State);
		}

		public static RenderState PeekRenderState()
		{
			return RenderStates.Peek();
		}

		public static RenderState PopRenderState()
		{
			if (GetRenderStateCount() == 0)
				throw new InvalidOperationException("There is no render state to pop.");
			if (GraphicsContext.CurrentOrNull != null && GetRenderStateCount() == 1)
				throw new InvalidOperationException("The graphics context's base render state cannot be popped.");
			RenderState State = RenderStates.Pop();

			if (GetRenderStateCount() > 0)
				SetRenderState(RenderStates.Peek());

			return State;
		}

		static void SetRenderState(RenderState state)
		{
			GraphicsContext context = GraphicsContext.CurrentOrNull;
			RenderState? previousValue = context?.AppliedRenderState;
			RenderState previous = previousValue.GetValueOrDefault();
			bool first = !previousValue.HasValue;

			if (first || previous.EnableCullFace != state.EnableCullFace)
				GlEnable(EnableCap.CullFace, state.EnableCullFace);
			if (state.EnableCullFace && (first || !previous.EnableCullFace || previous.CullFace != state.CullFace))
				Internal_OpenGL.GL.CullFace((TriangleFace)state.CullFace);

			if (first || previous.EnableDepthMask != state.EnableDepthMask)
				Internal_OpenGL.GL.DepthMask(state.EnableDepthMask);
			if (first || previous.EnableColorMaskR != state.EnableColorMaskR || previous.EnableColorMaskG != state.EnableColorMaskG || previous.EnableColorMaskB != state.EnableColorMaskB || previous.EnableColorMaskA != state.EnableColorMaskA)
				Internal_OpenGL.GL.ColorMask(state.EnableColorMaskR, state.EnableColorMaskG, state.EnableColorMaskB, state.EnableColorMaskA);

			if (first || previous.EnableDepthTest != state.EnableDepthTest)
				GlEnable(EnableCap.DepthTest, state.EnableDepthTest);
			if (state.EnableDepthTest && (first || !previous.EnableDepthTest || previous.DepthFunc != state.DepthFunc))
				Internal_OpenGL.GL.DepthFunc((DepthFunction)state.DepthFunc);
			if (first || previous.FrontFace != state.FrontFace)
				Internal_OpenGL.GL.FrontFace((FrontFaceDirection)state.FrontFace);

			if (first || previous.EnableScissorTest != state.EnableScissorTest)
				GlEnable(EnableCap.ScissorTest, state.EnableScissorTest);
			if (state.EnableScissorTest && (first || !previous.EnableScissorTest || previous.ScissorRegion.Position != state.ScissorRegion.Position || previous.ScissorRegion.Size != state.ScissorRegion.Size))
			{
				AABB region = state.ScissorRegion;
				if (region.Size.X < 0 || region.Size.Y < 0)
					throw new ArgumentOutOfRangeException(nameof(state), "Scissor dimensions cannot be negative.");
				Internal_OpenGL.GL.Scissor((int)region.Position.X, (int)region.Position.Y, (uint)region.Size.X, (uint)region.Size.Y);
			}

			if (first || previous.EnableStencilTest != state.EnableStencilTest)
				GlEnable(EnableCap.StencilTest, state.EnableStencilTest);
			if (state.EnableStencilTest)
			{
				if (first || !previous.EnableStencilTest || previous.StencilFrontWriteMask != state.StencilFrontWriteMask)
					Internal_OpenGL.GL.StencilMaskSeparate(TriangleFace.Front, state.StencilFrontWriteMask);
				if (first || !previous.EnableStencilTest || previous.StencilBackWriteMask != state.StencilBackWriteMask)
					Internal_OpenGL.GL.StencilMaskSeparate(TriangleFace.Back, state.StencilBackWriteMask);
				if (state.StencilFrontFunction != StencilFunction.Skip && (first || previous.StencilFrontFunction != state.StencilFrontFunction || previous.StencilFrontReference != state.StencilFrontReference || previous.StencilFrontMask != state.StencilFrontMask))
					Internal_OpenGL.GL.StencilFuncSeparate(TriangleFace.Front, (GLEnum)state.StencilFrontFunction, state.StencilFrontReference, state.StencilFrontMask);
				if (state.StencilBackFunction != StencilFunction.Skip && (first || previous.StencilBackFunction != state.StencilBackFunction || previous.StencilBackReference != state.StencilBackReference || previous.StencilBackMask != state.StencilBackMask))
					Internal_OpenGL.GL.StencilFuncSeparate(TriangleFace.Back, (GLEnum)state.StencilBackFunction, state.StencilBackReference, state.StencilBackMask);
				if (state.StencilFrontSFail != StencilOperation.Skip && state.StencilFrontDPFail != StencilOperation.Skip && state.StencilFrontDPPass != StencilOperation.Skip && (first || previous.StencilFrontSFail != state.StencilFrontSFail || previous.StencilFrontDPFail != state.StencilFrontDPFail || previous.StencilFrontDPPass != state.StencilFrontDPPass))
					Internal_OpenGL.GL.StencilOpSeparate(TriangleFace.Front, (GLEnum)state.StencilFrontSFail, (GLEnum)state.StencilFrontDPFail, (GLEnum)state.StencilFrontDPPass);
				if (state.StencilBackSFail != StencilOperation.Skip && state.StencilBackDPFail != StencilOperation.Skip && state.StencilBackDPPass != StencilOperation.Skip && (first || previous.StencilBackSFail != state.StencilBackSFail || previous.StencilBackDPFail != state.StencilBackDPFail || previous.StencilBackDPPass != state.StencilBackDPPass))
					Internal_OpenGL.GL.StencilOpSeparate(TriangleFace.Back, (GLEnum)state.StencilBackSFail, (GLEnum)state.StencilBackDPFail, (GLEnum)state.StencilBackDPPass);
			}

			if (first || previous.EnableBlend != state.EnableBlend)
				GlEnable(EnableCap.Blend, state.EnableBlend);
			if (state.EnableBlend && (first || !previous.EnableBlend || previous.BlendFunc_Src != state.BlendFunc_Src || previous.BlendFunc_Dst != state.BlendFunc_Dst))
				Internal_OpenGL.GL.BlendFunc((BlendingFactor)state.BlendFunc_Src, (BlendingFactor)state.BlendFunc_Dst);
			if (first || previous.PointSize != state.PointSize)
				Internal_OpenGL.GL.PointSize(state.PointSize);
			if (first || previous.EnableDepthClamp != state.EnableDepthClamp)
				GlEnable((EnableCap)0x864F, state.EnableDepthClamp);

			if (context != null)
				context.AppliedRenderState = state;
		}

		static void ValidateRenderState(RenderState state)
		{
			if (!float.IsFinite(state.PointSize) || state.PointSize <= 0)
				throw new ArgumentOutOfRangeException(nameof(state), "Point size must be finite and positive.");
			if (!Enum.IsDefined(state.FrontFace) || (state.EnableCullFace && !Enum.IsDefined(state.CullFace)) ||
				(state.EnableDepthTest && !Enum.IsDefined(state.DepthFunc)) ||
				(state.EnableBlend && (!Enum.IsDefined(state.BlendFunc_Src) || !Enum.IsDefined(state.BlendFunc_Dst))))
				throw new ArgumentOutOfRangeException(nameof(state), "The render state contains an unknown enum value.");
			if (state.EnableScissorTest)
			{
				AABB region = state.ScissorRegion;
				if (!float.IsFinite(region.Position.X) || !float.IsFinite(region.Position.Y) ||
					!float.IsFinite(region.Size.X) || !float.IsFinite(region.Size.Y) || region.Size.X < 0 || region.Size.Y < 0)
					throw new ArgumentOutOfRangeException(nameof(state), "Scissor coordinates must be finite and dimensions cannot be negative.");
			}
		}

		static bool GlEnable(EnableCap Cap, bool Enable)
		{
			if (Enable)
				Internal_OpenGL.GL.Enable(Cap);
			else
				Internal_OpenGL.GL.Disable(Cap);

			return Enable;
		}

		static bool GlEnable(int Cap, bool Enable)
		{
			return GlEnable((EnableCap)Cap, Enable);
		}

		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		//////////////////////////////////////////////////// Generic ///////////////////////////////////////////////////////////////
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

		static Texture WhiteTex { get => ImmediateResources.WhiteTexture; set => ImmediateResources.WhiteTexture = value; }

		static void InitGeneric()
		{
			if (WhiteTex == null)
			{
				WhiteTex = GraphicsContext.Current.CreateTexture(new TextureDescriptor(1, 1));
				WhiteTex.Write(new Color[] { Color.White }, TextureDataFormat.RGBA8Unorm);
			}
		}

		public static void Clear(Color ClearColor, bool Color, bool Depth, bool Stencil) =>
			Clear(ClearColor, Color, Depth, Stencil, 1, 0);

		public static void Clear(Color ClearColor, bool Color, bool Depth, bool Stencil, float DepthValue, int StencilValue)
		{
			ClearBufferMask mask = 0;
			if (Color)
			{
				Internal_OpenGL.GL.ClearColor(
					ClearColor.R / 255.0f,
					ClearColor.G / 255.0f,
					ClearColor.B / 255.0f,
					ClearColor.A / 255.0f
				);
				mask |= ClearBufferMask.ColorBufferBit;
			}
			if (Depth)
			{
				Internal_OpenGL.GL.ClearDepth(DepthValue);
				mask |= ClearBufferMask.DepthBufferBit;
			}
			if (Stencil)
			{
				Internal_OpenGL.GL.ClearStencil(StencilValue);
				mask |= ClearBufferMask.StencilBufferBit;
			}
			if (mask != 0)
				Internal_OpenGL.GL.Clear(mask);
		}

		public static void Clear()
		{
			Clear(new Color(69, 112, 56), true, true, true);
		}

		public static void Clear(Color ClearColor)
		{
			Clear(ClearColor, true, true, true);
		}

		public static void ClearDepth(float Value = 1)
		{
			Internal_OpenGL.GL.ClearDepth(Value);
			Internal_OpenGL.GL.Clear(ClearBufferMask.DepthBufferBit);
		}

		public static void ClearStencil(int S = 0)
		{
			Internal_OpenGL.GL.ClearStencil(S);
			Internal_OpenGL.GL.Clear(ClearBufferMask.StencilBufferBit);
		}

		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		//////////////////////////////////////////////////// 3D  3D  3D ////////////////////////////////////////////////////////////
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

		public static ShaderProgram Line3D { get => ImmediateResources.Line3D; set => ImmediateResources.Line3D = value; }
		public static ShaderProgram Point3D { get => ImmediateResources.Point3D; set => ImmediateResources.Point3D = value; }
		public static ShaderProgram Default3D { get => ImmediateResources.Default3D; set => ImmediateResources.Default3D = value; }

		static Mesh3D Mesh3D { get => ImmediateResources.Mesh3D; set => ImmediateResources.Mesh3D = value; }

		static void Init3D(PrimitiveType Primitive)
		{
			InitGeneric();

			if (Line3D == null)
				throw new Exception(nameof(Line3D) + " shader not assigned");

			if (Point3D == null)
				throw new Exception(nameof(Point3D) + " shader not assigned");

			if (Default3D == null)
				throw new Exception(nameof(Default3D) + " shader not assigned");

			if (Mesh3D == null)
				Mesh3D = new Mesh3D(BufferUsage.Dynamic);

			Mesh3D.PrimitiveType = Primitive;
		}

		public static void Point(Vertex3[] Points, float Thickness)
		{
			Init3D(PrimitiveType.Points);
			Mesh3D.SetVertices(Points);

			Point3D.Uniform1f("Thickness", Thickness);
			Point3D.Bind();
			try { Mesh3D.Draw(); }
			finally { Point3D.Unbind(); }
		}

		public static void Point(Vertex3 Position, float Thickness)
		{
			Point(new Vertex3[] { Position }, Thickness);
		}

		public static void Point(Vertex3[] Positions)
		{
			Init3D(PrimitiveType.Points);
			Mesh3D.SetVertices(Positions);

			Default3D.Bind();
			try { Mesh3D.Draw(); }
			finally { Default3D.Unbind(); }
		}

		public static void Point(Vertex3 Position)
		{
			Point(new Vertex3[] { Position });
		}

		public static void Line(Vertex3 Start, Vertex3 End, float Thickness = 1)
		{
			Init3D(PrimitiveType.Lines);
			Mesh3D.SetVertices(Start, End);

			Line3D.Uniform1f("Thickness", Thickness);
			Line3D.Bind();
			try { Mesh3D.Draw(); }
			finally { Line3D.Unbind(); }
		}

		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		//////////////////////////////////////////////////// 2D  2D  2D ////////////////////////////////////////////////////////////
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

		static ShaderProgram Line2D { get => ImmediateResources.Line2D; set => ImmediateResources.Line2D = value; }
		static ShaderProgram Point2D { get => ImmediateResources.Point2D; set => ImmediateResources.Point2D = value; }
		static ShaderProgram Default2D { get => ImmediateResources.Default2D; set => ImmediateResources.Default2D = value; }
		static ShaderProgram SdfText2D { get => ImmediateResources.SdfText2D; set => ImmediateResources.SdfText2D = value; }

		static Mesh2D Mesh2D { get => ImmediateResources.Mesh2D; set => ImmediateResources.Mesh2D = value; }

		public static string ShadersDirectory = Path.Combine(AppContext.BaseDirectory, "data", "shaders");

		static void Init2D(PrimitiveType Primitive)
		{
			InitGeneric();

			if (Line2D == null)
			{
				Line2D = new ShaderProgram(
					new ShaderStage(ShaderType.VertexShader, Path.Combine(ShadersDirectory, "line2d.vert")),
					new ShaderStage(ShaderType.GeometryShader, Path.Combine(ShadersDirectory, "line.geom")),
					new ShaderStage(ShaderType.FragmentShader, Path.Combine(ShadersDirectory, "line.frag"))
				);
			}

			if (Point2D == null)
			{
				Point2D = new ShaderProgram(
					new ShaderStage(ShaderType.VertexShader, Path.Combine(ShadersDirectory, "point2d.vert")),
					new ShaderStage(ShaderType.GeometryShader, Path.Combine(ShadersDirectory, "point.geom")),
					new ShaderStage(ShaderType.FragmentShader, Path.Combine(ShadersDirectory, "point.frag"))
				);
			}

			if (Default2D == null)
			{
				Default2D = new ShaderProgram(
					new ShaderStage(ShaderType.VertexShader, Path.Combine(ShadersDirectory, "default2d.vert")),
					new ShaderStage(ShaderType.FragmentShader, Path.Combine(ShadersDirectory, "default_tex_clr.frag"))
				);
			}

			if (SdfText2D == null)
			{
				SdfText2D = new ShaderProgram(
					new ShaderStage(ShaderType.VertexShader, Path.Combine(ShadersDirectory, "default2d.vert")),
					new ShaderStage(ShaderType.FragmentShader, Path.Combine(ShadersDirectory, "sdf_text.frag"))
				);
			}

			if (Mesh2D == null)
				Mesh2D = new Mesh2D(BufferUsage.Stream);

			Mesh2D.PrimitiveType = Primitive;
		}

		static void Start2D()
		{
			RenderState State = PeekRenderState();
			State.FrontFace = FrontFace.CounterClockwise;
			PushRenderState(State);
		}

		static void End2D()
		{
			PopRenderState();
		}

		public static void Point(Vertex2[] Positions, float Thickness)
		{
			Init2D(PrimitiveType.Points);
			Mesh2D.SetVertices(Positions);

			Point2D.Uniform1f("Thickness", Thickness);
			Point2D.Bind(ShaderUniforms.Current);
			try { Mesh2D.Draw(); }
			finally { Point2D.Unbind(); }
		}

		public static void Point(Vertex2 Position, float Thickness)
		{
			Point(new Vertex2[] { Position }, Thickness);
		}

		public static void Point(Vertex2[] Positions)
		{
			Init2D(PrimitiveType.Points);
			Mesh2D.SetVertices(Positions);

			Start2D();
			try
			{
				Default2D.Bind(ShaderUniforms.Current);
				try { Mesh2D.Draw(); }
				finally { Default2D.Unbind(); }
			}
			finally { End2D(); }
		}

		public static void Point(Vertex2 Position)
		{
			Point(new Vertex2[] { Position });
		}

		public static void Line(Vertex2 Start, Vertex2 End, float Thickness = 1)
		{
			Init2D(PrimitiveType.Lines);
			Mesh2D.SetVertices(Start, End);

			Start2D();
			try
			{
				Line2D.Uniform1f("Thickness", Thickness);
				Line2D.Bind(ShaderUniforms.Current);
				try { Mesh2D.Draw(); }
				finally { Line2D.Unbind(); }
			}
			finally { End2D(); }
		}

		public static void LineStrip(Vertex2[] Points, float Thickness = 1)
		{
			Init2D(PrimitiveType.LineStrip);
			Mesh2D.SetVertices(Points);

			Start2D();
			try
			{
				Line2D.Uniform1f("Thickness", Thickness);
				Line2D.Bind(ShaderUniforms.Current);
				try { Mesh2D.Draw(); }
				finally { Line2D.Unbind(); }
			}
			finally { End2D(); }
		}

		static Vertex2[] EmitRectangleTris(
			Vertex2[] Verts,
			int Offset,
			float X,
			float Y,
			float W,
			float H,
			float U0 = 0,
			float V0 = 0,
			float U1 = 1,
			float V1 = 1,
			Color? Color = null
		)
		{
			Color C = Color ?? FishGfx.Color.White;

			Verts[Offset] = new Vertex2(new Vector2(X, Y), new Vector2(U0, V0), C);
			Verts[Offset + 1] = new Vertex2(new Vector2(X + W, Y + H), new Vector2(U1, V1), C);
			Verts[Offset + 2] = new Vertex2(new Vector2(X, Y + H), new Vector2(U0, V1), C);
			Verts[Offset + 3] = new Vertex2(new Vector2(X, Y), new Vector2(U0, V0), C);
			Verts[Offset + 4] = new Vertex2(new Vector2(X + W, Y), new Vector2(U1, V0), C);
			Verts[Offset + 5] = new Vertex2(new Vector2(X + W, Y + H), new Vector2(U1, V1), C);

			return Verts;
		}

		public static void Rectangle(float X, float Y, float W, float H, float Thickness = 1, Color? Clr = null)
		{
			Color C = Clr ?? Color.White;

			LineStrip(
				new[]
				{
					new Vertex2(new Vector2(X, Y), C),
					new Vertex2(new Vector2(X + W, Y), C),
					new Vertex2(new Vector2(X + W, Y + H), C),
					new Vertex2(new Vector2(X, Y + H), C),
					new Vertex2(new Vector2(X, Y), C),
				},
				Thickness
			);
		}

		public static void TexturedRectangle(
			float X,
			float Y,
			float W,
			float H,
			float U0 = 0,
			float V0 = 0,
			float U1 = 1,
			float V1 = 1,
			Color? Color = null,
			Texture Texture = null,
			ShaderProgram Shader = null
		)
		{
			DrawTexturedTriangles(
				EmitRectangleTris(new Vertex2[6], 0, X, Y, W, H, U0, V0, U1, V1, Color),
				Texture,
				Shader
			);
		}

		private static void DrawTexturedTriangles(Vertex2[] vertices, Texture texture, ShaderProgram shader)
		{
			if (vertices.Length == 0)
				return;
			Init2D(PrimitiveType.Triangles);
			Mesh2D.SetVertices(vertices);
			Start2D();
			try
			{
				texture?.BindTextureUnit();
				try
				{
					ShaderProgram selectedShader = shader ?? Default2D;
					selectedShader.Bind(ShaderUniforms.Current);
					try { Mesh2D.Draw(); }
					finally { selectedShader.Unbind(); }
				}
				finally { texture?.UnbindTextureUnit(); }
			}
			finally { End2D(); }
		}

		public static void NinePatch(
			float X,
			float Y,
			float W,
			float H,
			Texture Texture,
			NinePatchInsets Insets,
			Color? Color = null,
			ShaderProgram Shader = null
		)
		{
			if (Texture == null)
				throw new ArgumentNullException(nameof(Texture));
			Vertex2[] vertices = NinePatchTessellator.Create(
				new Vector2(X, Y),
				new Vector2(W, H),
				Texture.Size,
				Insets,
				Color ?? FishGfx.Color.White
			);
			DrawTexturedTriangles(vertices, Texture, Shader);
		}

		public static void NinePatch(
			Vector2 Position,
			Vector2 Size,
			Texture Texture,
			NinePatchInsets Insets,
			Color? Color = null,
			ShaderProgram Shader = null
		)
		{
			NinePatch(Position.X, Position.Y, Size.X, Size.Y, Texture, Insets, Color, Shader);
		}

		public static void FilledRectangle(float X, float Y, float W, float H, Color? Clr = null)
		{
			TexturedRectangle(X, Y, W, H, 0, 0, 1, 1, Clr, WhiteTex);
		}

		public static void RoundedRectangle(
			float X,
			float Y,
			float W,
			float H,
			CornerRadii Radii,
			float Thickness = 1,
			Color? Color = null,
			int CornerSegments = 0
		)
		{
			PrimitiveTessellator.ValidateThickness(Thickness);
			Vector2[] positions = RoundedRectangleTessellator.Outline(
				new Vector2(X, Y),
				new Vector2(W, H),
				Radii,
				CornerSegments
			);

			if (positions.Length == 0)
				return;
			LineStrip(ColorVertices(positions, Color ?? FishGfx.Color.White), Thickness);
		}

		public static void RoundedRectangle(
			Vector2 Position,
			Vector2 Size,
			CornerRadii Radii,
			float Thickness = 1,
			Color? Color = null,
			int CornerSegments = 0
		)
		{
			RoundedRectangle(Position.X, Position.Y, Size.X, Size.Y, Radii, Thickness, Color, CornerSegments);
		}

		public static void FilledRoundedRectangle(
			float X,
			float Y,
			float W,
			float H,
			CornerRadii Radii,
			Color? Color = null,
			int CornerSegments = 0
		)
		{
			Vector2[] positions = RoundedRectangleTessellator.Filled(
				new Vector2(X, Y),
				new Vector2(W, H),
				Radii,
				CornerSegments
			);
			FilledTriangles(positions, Color ?? FishGfx.Color.White);
		}

		public static void FilledRoundedRectangle(
			Vector2 Position,
			Vector2 Size,
			CornerRadii Radii,
			Color? Color = null,
			int CornerSegments = 0
		)
		{
			FilledRoundedRectangle(Position.X, Position.Y, Size.X, Size.Y, Radii, Color, CornerSegments);
		}

		public static void TexturedRoundedRectangle(
			float X,
			float Y,
			float W,
			float H,
			CornerRadii Radii,
			Texture Texture,
			float U0 = 0,
			float V0 = 0,
			float U1 = 1,
			float V1 = 1,
			Color? Color = null,
			ShaderProgram Shader = null,
			int CornerSegments = 0
		)
		{
			if (Texture == null)
				throw new ArgumentNullException(nameof(Texture));
			Vector2 position = new Vector2(X, Y);
			Vector2 size = new Vector2(W, H);
			Vector2[] positions = RoundedRectangleTessellator.Filled(position, size, Radii, CornerSegments);
			Vertex2[] vertices = PrimitiveTessellator.TextureVertices(
				positions,
				position,
				size,
				new Vector2(U0, V0),
				new Vector2(U1, V1),
				Color ?? FishGfx.Color.White
			);
			DrawTexturedTriangles(vertices, Texture, Shader);
		}

		public static void TexturedRoundedRectangle(
			Vector2 Position,
			Vector2 Size,
			CornerRadii Radii,
			Texture Texture,
			Vector2 UVMin,
			Vector2 UVMax,
			Color? Color = null,
			ShaderProgram Shader = null,
			int CornerSegments = 0
		)
		{
			TexturedRoundedRectangle(
				Position.X,
				Position.Y,
				Size.X,
				Size.Y,
				Radii,
				Texture,
				UVMin.X,
				UVMin.Y,
				UVMax.X,
				UVMax.Y,
				Color,
				Shader,
				CornerSegments
			);
		}

		private static Vertex2[] ColorVertices(Vector2[] positions, Color color)
		{
			Vertex2[] vertices = new Vertex2[positions.Length];

			for (int i = 0; i < positions.Length; i++)
				vertices[i] = new Vertex2(positions[i], color);
			return vertices;
		}

		private static void FilledTriangles(Vector2[] positions, Color color)
		{
			if (positions.Length == 0)
				return;

			Init2D(PrimitiveType.Triangles);
			Mesh2D.SetVertices(ColorVertices(positions, color));
			Start2D();
			try
			{
				WhiteTex.BindTextureUnit();
				try
				{
					Default2D.Bind(ShaderUniforms.Current);
					try { Mesh2D.Draw(); }
					finally { Default2D.Unbind(); }
				}
				finally { WhiteTex.UnbindTextureUnit(); }
			}
			finally { End2D(); }
		}

		public static void Circle(
			Vector2 Center,
			float Radius,
			float Thickness = 1,
			Color? Color = null,
			int Segments = 0
		)
		{
			Ellipse(Center, new Vector2(Radius), Thickness, Color, Segments);
		}

		public static void Ring(
			Vector2 Center,
			float InnerRadius,
			float OuterRadius,
			Color? Color = null,
			int Segments = 0
		)
		{
			Ring(Center, InnerRadius, OuterRadius, 0, MathF.Tau, Color, Segments);
		}

		public static void Ring(
			Vector2 Center,
			float InnerRadius,
			float OuterRadius,
			float StartAngle,
			float EndAngle,
			Color? Color = null,
			int Segments = 0
		)
		{
			Vector2[] positions = RingTessellator.Filled(
				Center,
				InnerRadius,
				OuterRadius,
				StartAngle,
				EndAngle,
				Segments
			);
			FilledTriangles(positions, Color ?? FishGfx.Color.White);
		}

		public static void RingLines(
			Vector2 Center,
			float InnerRadius,
			float OuterRadius,
			float Thickness = 1,
			Color? Color = null,
			int Segments = 0
		)
		{
			RingLines(Center, InnerRadius, OuterRadius, 0, MathF.Tau, Thickness, Color, Segments);
		}

		public static void RingLines(
			Vector2 Center,
			float InnerRadius,
			float OuterRadius,
			float StartAngle,
			float EndAngle,
			float Thickness = 1,
			Color? Color = null,
			int Segments = 0
		)
		{
			PrimitiveTessellator.ValidateThickness(Thickness);
			Vector2[][] paths = RingTessellator.Lines(Center, InnerRadius, OuterRadius, StartAngle, EndAngle, Segments);

			foreach (Vector2[] path in paths)
				LineStrip(ColorVertices(path, Color ?? FishGfx.Color.White), Thickness);
		}

		public static void FilledCircle(Vector2 Center, float Radius, Color? Color = null, int Segments = 0)
		{
			FilledEllipse(Center, new Vector2(Radius), Color, Segments);
		}

		public static void Ellipse(
			Vector2 Center,
			Vector2 Radii,
			float Thickness = 1,
			Color? Color = null,
			int Segments = 0
		)
		{
			PrimitiveTessellator.ValidateThickness(Thickness);
			Vector2[] positions = PrimitiveTessellator.EllipseOutline(Center, Radii, Segments);

			if (positions.Length == 0)
				return;
			LineStrip(ColorVertices(positions, Color ?? FishGfx.Color.White), Thickness);
		}

		public static void FilledEllipse(Vector2 Center, Vector2 Radii, Color? Color = null, int Segments = 0)
		{
			Vector2[] positions = PrimitiveTessellator.FilledEllipse(Center, Radii, Segments);
			FilledTriangles(positions, Color ?? FishGfx.Color.White);
		}

		public static void TexturedCircle(
			Vector2 Center,
			float Radius,
			Texture Texture,
			float U0 = 0,
			float V0 = 0,
			float U1 = 1,
			float V1 = 1,
			Color? Color = null,
			ShaderProgram Shader = null,
			int Segments = 0
		)
		{
			TexturedEllipse(Center, new Vector2(Radius), Texture, U0, V0, U1, V1, Color, Shader, Segments);
		}

		public static void TexturedEllipse(
			Vector2 Center,
			Vector2 Radii,
			Texture Texture,
			float U0 = 0,
			float V0 = 0,
			float U1 = 1,
			float V1 = 1,
			Color? Color = null,
			ShaderProgram Shader = null,
			int Segments = 0
		)
		{
			if (Texture == null)
				throw new ArgumentNullException(nameof(Texture));
			Vector2[] positions = PrimitiveTessellator.FilledEllipse(Center, Radii, Segments);
			Vector2 boundsMin = Center - Radii;
			Vector2 boundsSize = Radii * 2;
			Vertex2[] vertices = PrimitiveTessellator.TextureVertices(
				positions,
				boundsMin,
				boundsSize,
				new Vector2(U0, V0),
				new Vector2(U1, V1),
				Color ?? FishGfx.Color.White
			);
			DrawTexturedTriangles(vertices, Texture, Shader);
		}

		public static void QuadraticBezier(
			Vector2 Start,
			Vector2 Control,
			Vector2 End,
			float Thickness = 1,
			Color? Color = null,
			int Segments = 0
		)
		{
			PrimitiveTessellator.ValidateThickness(Thickness);
			Vector2[] positions = PrimitiveTessellator.QuadraticBezier(Start, Control, End, Segments);
			LineStrip(ColorVertices(positions, Color ?? FishGfx.Color.White), Thickness);
		}

		public static void CubicBezier(
			Vector2 Start,
			Vector2 Control1,
			Vector2 Control2,
			Vector2 End,
			float Thickness = 1,
			Color? Color = null,
			int Segments = 0
		)
		{
			PrimitiveTessellator.ValidateThickness(Thickness);
			Vector2[] positions = PrimitiveTessellator.CubicBezier(Start, Control1, Control2, End, Segments);
			LineStrip(ColorVertices(positions, Color ?? FishGfx.Color.White), Thickness);
		}

		public static Vector2 DrawText(
			GfxFont Font,
			Vector2 Pos,
			string Str,
			Color Clr,
			float FontSize = -1,
			bool DebugDraw = false
		)
		{
			if (string.IsNullOrEmpty(Str))
				return Vector2.Zero;

			Pos.X = (int)(Pos.X - 0.5f);
			Pos.Y = (int)(Pos.Y - 0.5f);

			float OldScale = Font.ScaledFontSize;

			if (FontSize > 0)
				Font.ScaledFontSize = FontSize;

			try
			{
				if (!(Font is IGfxAtlasFont AtlasFont))
					throw new NotSupportedException($"Font type {Font.GetType()} does not provide a texture atlas.");

				AtlasFont.PrepareText(Str);
				Texture AtlasTex =
					AtlasFont.AtlasTexture ?? throw new InvalidOperationException("Font atlas texture was not created.");
				GfxFont.CharDest[] Chars = Font.LayoutString(Str);

				Init2D(PrimitiveType.Triangles);

				ShaderProgram TextShader =
					AtlasFont.RenderMode == GfxFontRenderMode.SignedDistanceField ? SdfText2D : Default2D;

				if (AtlasFont.RenderMode == GfxFontRenderMode.SignedDistanceField)
					TextShader.Uniform1f("SdfPixelRange", AtlasFont.SdfPixelRange);

				Vertex2[] TextVertices = new Vertex2[Chars.Length * 6];

				for (int i = 0; i < Chars.Length; i++)
				{
					ref GfxFont.CharDest C = ref Chars[i];

					float X = C.CharOrigin.X / AtlasTex.Width;
					float Y = C.CharOrigin.Y / AtlasTex.Height;
					float W = C.CharOrigin.W / AtlasTex.Width;
					float H = C.CharOrigin.H / AtlasTex.Height;

					EmitRectangleTris(
						TextVertices,
						i * 6,
						Pos.X + C.X,
						Pos.Y + C.Y,
						C.W,
						C.H,
						X,
						1.0f - Y - H,
						X + W,
						1.0f - Y,
						Clr
					);
				}

				Mesh2D.SetVertices(TextVertices);

				Start2D();
				try
				{
					AtlasTex.BindTextureUnit();
					try
					{
						TextShader.Bind(ShaderUniforms.Current);
						try { Mesh2D.Draw(); }
						finally { TextShader.Unbind(); }
					}
					finally { AtlasTex.UnbindTextureUnit(); }
				}
				finally { End2D(); }

				if (DebugDraw)
				{
					FilledRectangle(Pos.X, Pos.Y, 5, 5, Color.Yellow);

					if (Chars.Length > 0)
					{
						FilledRectangle(Pos.X + Chars[0].X, Pos.Y + Chars[0].Y, 5, 5, Color.Red);

						Vector2 Sz = Font.MeasureString(Chars);
						Rectangle(Pos.X, Pos.Y, Sz.X, Sz.Y, Clr: Color.Red);
					}
				}

				return Font.MeasureString(Chars);
			}
			finally
			{
				Font.ScaledFontSize = OldScale;
			}
		}
	}
}
