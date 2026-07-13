using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using FishGfx.Formats;
using FishGfx.Graphics.Drawables;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics
{
	public readonly struct OpenGLVersion : IEquatable<OpenGLVersion>, IComparable<OpenGLVersion>
	{
		public OpenGLVersion(int major, int minor)
		{
			if (major < 1)
				throw new ArgumentOutOfRangeException(nameof(major));
			if (minor < 0)
				throw new ArgumentOutOfRangeException(nameof(minor));
			Major = major;
			Minor = minor;
		}

		public int Major { get; }
		public int Minor { get; }
		public int CompareTo(OpenGLVersion other) => Major != other.Major ? Major.CompareTo(other.Major) : Minor.CompareTo(other.Minor);
		public bool Equals(OpenGLVersion other) => Major == other.Major && Minor == other.Minor;
		public override bool Equals(object obj) => obj is OpenGLVersion other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(Major, Minor);
		public override string ToString() => $"{Major}.{Minor}";
		public static bool operator >=(OpenGLVersion left, OpenGLVersion right) => left.CompareTo(right) >= 0;
		public static bool operator <=(OpenGLVersion left, OpenGLVersion right) => left.CompareTo(right) <= 0;
		public static bool operator >(OpenGLVersion left, OpenGLVersion right) => left.CompareTo(right) > 0;
		public static bool operator <(OpenGLVersion left, OpenGLVersion right) => left.CompareTo(right) < 0;
	}

	public sealed class GraphicsCapabilities
	{
		internal GraphicsCapabilities(
			OpenGLVersion version,
			string renderer,
			IReadOnlyList<string> extensions,
			int maximumTexture2DSize,
			int maximumCubeTextureSize,
			int maximumSamples,
			float maximumAnisotropy
		)
		{
			Version = version;
			Renderer = renderer ?? string.Empty;
			Extensions = Array.AsReadOnly(new List<string>(extensions ?? Array.Empty<string>()).ToArray());
			MaximumTexture2DSize = maximumTexture2DSize;
			MaximumCubeTextureSize = maximumCubeTextureSize;
			MaximumSamples = maximumSamples;
			MaximumAnisotropy = maximumAnisotropy;
		}

		public OpenGLVersion Version { get; }
		public string Renderer { get; }
		public IReadOnlyList<string> Extensions { get; }
		public bool SupportsDirectStateAccess => Version >= new OpenGLVersion(4, 5);
		public bool SupportsProgramUniforms => Version >= new OpenGLVersion(4, 1);
		public bool SupportsCopyImage => Version >= new OpenGLVersion(4, 3) || ContainsExtension("GL_ARB_copy_image");
		public bool SupportsAnisotropy => ContainsExtension("GL_EXT_texture_filter_anisotropic") || ContainsExtension("GL_ARB_texture_filter_anisotropic");
		public int MaximumTexture2DSize { get; }
		public int MaximumCubeTextureSize { get; }
		public int MaximumSamples { get; }
		public float MaximumAnisotropy { get; }

		private bool ContainsExtension(string extension)
		{
			for (int i = 0; i < Extensions.Count; i++)
				if (string.Equals(Extensions[i], extension, StringComparison.Ordinal))
					return true;
			return false;
		}
	}

	public readonly struct RenderView
	{
		public RenderView(
			Matrix4x4 view,
			Matrix4x4 projection,
			Vector3 position,
			Vector2 viewportSize,
			float near,
			float far
		)
		{
			if (viewportSize.X < 0 || viewportSize.Y < 0)
				throw new ArgumentOutOfRangeException(nameof(viewportSize));
			View = view;
			Projection = projection;
			Position = position;
			ViewportSize = viewportSize;
			Near = near;
			Far = far;
		}

		public RenderView(Camera camera)
			: this(
				(camera ?? throw new ArgumentNullException(nameof(camera))).View,
				camera.Projection,
				camera.Position,
				camera.ViewportSize,
				camera.Near,
				camera.Far
			) { }

		public Matrix4x4 View { get; }
		public Matrix4x4 Projection { get; }
		public Vector3 Position { get; }
		public Vector2 ViewportSize { get; }
		public float Near { get; }
		public float Far { get; }
	}

	public enum RenderLoadAction
	{
		Load,
		Clear,
		DontCare,
	}

	public sealed class RenderPassDescriptor
	{
		public RenderView View { get; set; }
		public RenderState State { get; set; } = Gfx.CreateDefaultRenderState();
		public RenderLoadAction ColorLoadAction { get; set; } = RenderLoadAction.Load;
		public RenderLoadAction DepthLoadAction { get; set; } = RenderLoadAction.Load;
		public RenderLoadAction StencilLoadAction { get; set; } = RenderLoadAction.Load;
		public Color ClearColor { get; set; } = Color.Black;
		public float ClearDepth { get; set; } = 1;
		public int ClearStencil { get; set; }
		public Vector2 TextureSize { get; set; }
		public float AlphaTest { get; set; }
		public int MultisampleCount { get; set; }
	}

	public sealed class RenderTargetDescriptor
	{
		public int Width { get; set; }
		public int Height { get; set; }
		public int Samples { get; set; }
		public bool CreateColor { get; set; } = true;
		public bool CreateDepthStencil { get; set; } = true;
		public bool IsGBuffer { get; set; }
	}

	public sealed class RenderTarget : IDisposable
	{
		private readonly bool ownsTexture;

		internal RenderTarget(GraphicsContext context, RenderTexture texture, bool isBackbuffer, int width, int height)
		{
			Context = context ?? throw new ArgumentNullException(nameof(context));
			Texture = texture;
			IsBackbuffer = isBackbuffer;
			Width = width;
			Height = height;
			ownsTexture = texture != null;
		}

		public GraphicsContext Context { get; }
		public RenderTexture Texture { get; }
		public bool IsBackbuffer { get; }
		public int Width { get; internal set; }
		public int Height { get; internal set; }
		public bool IsDisposed { get; private set; }

		public void Dispose()
		{
			if (IsDisposed)
				return;
			if (IsBackbuffer)
				throw new InvalidOperationException("The context-owned backbuffer cannot be disposed.");
			if (ReferenceEquals(Context.ActivePass?.Target, this))
				throw new InvalidOperationException("An active render target cannot be disposed.");
			IsDisposed = true;
			if (ownsTexture)
				Texture.Dispose();
		}
	}

	public sealed class GraphicsContext : IDisposable
	{
		[ThreadStatic]
		private static GraphicsContext current;

		private readonly Queue<GraphicsObject> deletionQueue = new Queue<GraphicsObject>();
		private readonly HashSet<GraphicsObject> resources = new HashSet<GraphicsObject>();
		private readonly int ownerThreadId;
		private GraphicsFrame activeFrame;
		private bool disposed;

		internal GraphicsContext(RenderWindow window)
		{
			Window = window ?? throw new ArgumentNullException(nameof(window));
			ownerThreadId = Environment.CurrentManagedThreadId;
			MakeCurrent();
			Internal_OpenGL.GL.GetInteger(GetPName.MaxTextureSize, out int maximumTextureSize);
			Internal_OpenGL.GL.GetInteger(GetPName.MaxCubeMapTextureSize, out int maximumCubeTextureSize);
			Internal_OpenGL.GL.GetInteger((GetPName)0x8D57, out int maximumSamples);
			bool supportsAnisotropy = Array.IndexOf(Internal_OpenGL.Extensions, "GL_EXT_texture_filter_anisotropic") >= 0 ||
				Array.IndexOf(Internal_OpenGL.Extensions, "GL_ARB_texture_filter_anisotropic") >= 0;
			float maximumAnisotropy = 1;
			if (supportsAnisotropy)
				Internal_OpenGL.GL.GetFloat((GLEnum)0x84FF, out maximumAnisotropy);
			Capabilities = new GraphicsCapabilities(
				new OpenGLVersion(Internal_OpenGL.MajorVersion, Internal_OpenGL.MinorVersion),
				RenderAPI.Renderer,
				Internal_OpenGL.Extensions,
				maximumTextureSize,
				maximumCubeTextureSize,
				maximumSamples,
				maximumAnisotropy
			);
			Backbuffer = new RenderTarget(this, null, true, window.WindowWidth, window.WindowHeight);
			RenderStates.Push(Gfx.CreateDefaultRenderState());
			Uniforms.Push(ShaderUniforms.CreateDefault());
		}

		public static GraphicsContext Current => current ?? throw new InvalidOperationException("No FishGfx graphics context is current on this thread.");
		internal static GraphicsContext CurrentOrNull => current;

		public RenderWindow Window { get; }
		public GraphicsCapabilities Capabilities { get; }
		public RenderTarget Backbuffer { get; }
		public bool IsDisposed => disposed;
		public bool IsFrameActive => activeFrame != null;
		internal RenderPass ActivePass => activeFrame?.ActivePass;
		internal Stack<RenderState> RenderStates { get; } = new Stack<RenderState>();
		internal RenderState? AppliedRenderState { get; set; }
		internal uint BoundProgram { get; set; }
		internal Stack<ShaderUniforms> Uniforms { get; } = new Stack<ShaderUniforms>();
		internal Stack<RenderTexture> RenderTargets { get; } = new Stack<RenderTexture>();
		internal OcclusionQuery ActiveQuery { get; set; }
		internal ImmediateGraphicsResources ImmediateResources { get; } = new ImmediateGraphicsResources();

		public GraphicsFrame BeginFrame()
		{
			EnsureCurrent();
			if (activeFrame != null)
				throw new InvalidOperationException("A graphics frame is already active for this context.");
			CollectGarbage();
			activeFrame = new GraphicsFrame(this);
			return activeFrame;
		}

		internal void EndFrame(GraphicsFrame frame)
		{
			if (!ReferenceEquals(activeFrame, frame))
				throw new InvalidOperationException("The graphics frame is not active on this context.");
			activeFrame = null;
		}

		public void MakeCurrent()
		{
			EnsureOwnerThread();
			Window.MakeNativeCurrent();
			current = this;
		}

		public void InvalidateStateCache() => Gfx.InvalidateStateCache(this);

		internal void ResizeBackbuffer(int width, int height)
		{
			Backbuffer.Width = width;
			Backbuffer.Height = height;
		}

		internal void EnsureCurrent()
		{
			if (disposed)
				throw new ObjectDisposedException(nameof(GraphicsContext));
			EnsureOwnerThread();
			if (!ReferenceEquals(current, this))
				throw new InvalidOperationException("This graphics context is not current on its owning thread.");
		}

		private void EnsureOwnerThread()
		{
			if (Environment.CurrentManagedThreadId != ownerThreadId)
				throw new InvalidOperationException("Graphics contexts may only be used from their owning thread.");
		}

		internal void Register(GraphicsObject resource)
		{
			lock (resources)
				resources.Add(resource);
		}

		internal void EnqueueDeletion(GraphicsObject resource)
		{
			lock (deletionQueue)
				deletionQueue.Enqueue(resource);
		}

		public void CollectGarbage()
		{
			EnsureCurrent();
			while (true)
			{
				GraphicsObject resource;
				lock (deletionQueue)
				{
					if (deletionQueue.Count == 0)
						break;
					resource = deletionQueue.Dequeue();
				}
				resource.DeleteOnOwnerContext();
				lock (resources)
					resources.Remove(resource);
			}
		}

		public Texture CreateTexture(TextureDescriptor descriptor) { EnsureCurrent(); return new Texture(descriptor); }
		public Texture LoadTexture2D(string path, TextureLoadOptions options = null) { EnsureCurrent(); return TextureLoader.Load2D(this, path, options); }
		public Texture LoadTextureCubemap(CubemapPaths paths, TextureLoadOptions options = null) { EnsureCurrent(); return TextureLoader.LoadCubemap(this, paths, options); }
		public GraphicsBuffer CreateBuffer(GraphicsBufferDescriptor descriptor) { EnsureCurrent(); return new GraphicsBuffer(descriptor); }
		public GraphicsBuffer CreateBuffer<T>(ReadOnlySpan<T> data, BufferBindFlags bindFlags, BufferUsage usage = BufferUsage.Static) where T : unmanaged
		{
			EnsureCurrent();
			int size = checked(data.Length * System.Runtime.CompilerServices.Unsafe.SizeOf<T>());
			if (size == 0)
				throw new ArgumentException("Initial buffer data cannot be empty.", nameof(data));
			GraphicsBuffer buffer = new GraphicsBuffer(new GraphicsBufferDescriptor(size, bindFlags, usage));
			buffer.Write(data);
			return buffer;
		}
		public VertexArray CreateVertexArray() { EnsureCurrent(); return new VertexArray(); }
		public Framebuffer CreateFramebuffer() { EnsureCurrent(); return new Framebuffer(); }
		public Renderbuffer CreateRenderbuffer() { EnsureCurrent(); return new Renderbuffer(); }
		public ShaderStage CreateShaderStage(ShaderType type, string sourceFile) { EnsureCurrent(); return new ShaderStage(type, sourceFile); }
		public ShaderProgram CreateShaderProgram(params ShaderStage[] stages) { EnsureCurrent(); return new ShaderProgram(stages); }
		public OcclusionQuery CreateQuery(QueryTgt target) { EnsureCurrent(); return new OcclusionQuery(target); }
		public TTFFont CreateTrueTypeFont(string path, TTFFontOptions options = null) { EnsureCurrent(); return new TTFFont(path, options); }
		public BMFont CreateBitmapFont(string path = null, float fontSize = -1, bool loadTextures = true) { EnsureCurrent(); return new BMFont(path, fontSize, loadTextures); }

		public RenderTarget CreateRenderTarget(RenderTargetDescriptor descriptor)
		{
			if (descriptor == null)
				throw new ArgumentNullException(nameof(descriptor));
			EnsureCurrent();
			if (descriptor.Width <= 0 || descriptor.Height <= 0)
				throw new ArgumentOutOfRangeException(nameof(descriptor), "Render-target dimensions must be positive.");
			if (descriptor.Samples < 0 || descriptor.Samples == 1)
				throw new ArgumentOutOfRangeException(nameof(descriptor), "Use zero samples to disable MSAA or at least two samples to enable it.");
			if (!descriptor.CreateColor && !descriptor.CreateDepthStencil && !descriptor.IsGBuffer)
				throw new ArgumentException("A render target must create at least one attachment.", nameof(descriptor));
			RenderTexture texture = new RenderTexture(
				descriptor.Width,
				descriptor.Height,
				descriptor.Samples,
				descriptor.IsGBuffer,
				descriptor.CreateColor,
				descriptor.CreateDepthStencil
			);
			return new RenderTarget(this, texture, false, descriptor.Width, descriptor.Height);
		}

		public void Dispose()
		{
			if (disposed)
				return;
			EnsureOwnerThread();
			MakeCurrent();
			activeFrame?.Dispose();
			GraphicsObject[] outstanding;
			lock (resources)
				outstanding = new List<GraphicsObject>(resources).ToArray();
			foreach (GraphicsObject resource in outstanding)
				resource.Dispose();
			CollectGarbage();
			disposed = true;
			if (ReferenceEquals(current, this))
				current = null;
		}
	}

	public sealed class GraphicsFrame : IDisposable
	{
		private readonly GraphicsContext context;
		private bool disposed;
		private bool presented;

		internal GraphicsFrame(GraphicsContext context) => this.context = context;
		internal RenderPass ActivePass { get; private set; }
		public bool IsPresented => presented;

		public RenderPass BeginPass(RenderTarget target, RenderPassDescriptor descriptor)
		{
			if (disposed)
				throw new ObjectDisposedException(nameof(GraphicsFrame));
			context.EnsureCurrent();
			if (presented)
				throw new InvalidOperationException("A presented frame cannot begin another pass.");
			if (ActivePass != null)
				throw new InvalidOperationException("A render pass is already active for this frame.");
			if (target == null)
				throw new ArgumentNullException(nameof(target));
			if (descriptor == null)
				throw new ArgumentNullException(nameof(descriptor));
			if (!ReferenceEquals(target.Context, context))
				throw new InvalidOperationException("The render target belongs to another graphics context.");
			if (target.IsDisposed)
				throw new ObjectDisposedException(nameof(target));
			ActivePass = new RenderPass(this, context, target, descriptor);
			return ActivePass;
		}

		internal void EndPass(RenderPass pass)
		{
			if (!ReferenceEquals(ActivePass, pass))
				throw new InvalidOperationException("The render pass is not active for this frame.");
			ActivePass = null;
		}

		public void Present()
		{
			if (disposed)
				throw new ObjectDisposedException(nameof(GraphicsFrame));
			context.EnsureCurrent();
			if (ActivePass != null)
				throw new InvalidOperationException("Close the active render pass before presenting the frame.");
			if (presented)
				throw new InvalidOperationException("The frame has already been presented.");
			context.CollectGarbage();
			context.Window.SwapNativeBuffers();
			presented = true;
		}

		public void Dispose()
		{
			if (disposed)
				return;
			ActivePass?.Dispose();
			disposed = true;
			context.EndFrame(this);
		}
	}

	public sealed class RenderPass : IDisposable
	{
		private readonly GraphicsFrame frame;
		private readonly GraphicsContext context;
		private readonly RenderTarget target;
		private readonly ShaderUniforms uniforms;
		private int scopeDepth;
		private bool disposed;

		internal RenderPass(GraphicsFrame frame, GraphicsContext context, RenderTarget target, RenderPassDescriptor descriptor)
		{
			this.frame = frame;
			this.context = context;
			this.target = target;
			uniforms = ShaderUniforms.CreateDefault();
			uniforms.SetRenderView(descriptor.View);
			uniforms.TextureSize = descriptor.TextureSize;
			uniforms.AlphaTest = descriptor.AlphaTest;
			uniforms.MultisampleCount = descriptor.MultisampleCount;

			bool targetBound = false;
			bool uniformsPushed = false;
			bool statePushed = false;
			try
			{
				if (target.IsBackbuffer)
					Internal_OpenGL.GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
				else
					target.Texture.BindForPass();
				targetBound = true;

				ShaderUniforms.Push(uniforms);
				uniformsPushed = true;
				Gfx.PushRenderState(descriptor.State);
				statePushed = true;

				Gfx.Clear(
					descriptor.ClearColor,
					descriptor.ColorLoadAction == RenderLoadAction.Clear,
					descriptor.DepthLoadAction == RenderLoadAction.Clear,
					descriptor.StencilLoadAction == RenderLoadAction.Clear,
					descriptor.ClearDepth,
					descriptor.ClearStencil
				);
			}
			catch
			{
				try { if (statePushed) Gfx.PopRenderState(); }
				finally
				{
					try { if (uniformsPushed) ShaderUniforms.Pop(); }
					finally { if (targetBound && !target.IsBackbuffer) target.Texture.UnbindForPass(); }
				}
				throw;
			}
		}

		public GraphicsContext Context => context;
		public RenderTarget Target => target;
		public RenderView View => uniforms.RenderView;

		private void EnsureActive()
		{
			context.EnsureCurrent();
			if (disposed || !ReferenceEquals(context.ActivePass, this))
				throw new InvalidOperationException("The render pass is not active.");
		}

		public IDisposable PushState(RenderState state)
		{
			EnsureActive();
			Gfx.PushRenderState(state);
			return new PassScope(this, () => Gfx.PopRenderState());
		}

		public IDisposable PushModel(Matrix4x4 model)
		{
			EnsureActive();
			Matrix4x4 previous = uniforms.Model;
			uniforms.Model = model;
			return new PassScope(this, () => uniforms.Model = previous);
		}

		public IDisposable PushView(RenderView view)
		{
			EnsureActive();
			RenderView previous = uniforms.RenderView;
			uniforms.SetRenderView(view);
			return new PassScope(this, () => uniforms.SetRenderView(previous));
		}

		public IDisposable BeginQuery(OcclusionQuery query)
		{
			EnsureActive();
			if (query == null)
				throw new ArgumentNullException(nameof(query));
			query.EnsureOwner(context);
			query.Bind();
			return new PassScope(this, query.Unbind);
		}

		internal int OpenScope()
		{
			EnsureActive();
			return ++scopeDepth;
		}

		internal void CloseScope(int depth, Action restore)
		{
			EnsureActive();
			if (depth != scopeDepth)
				throw new InvalidOperationException("Render-pass scopes must be disposed in reverse order.");
			try { restore(); }
			finally { scopeDepth--; }
		}

		public void Clear(Color color, bool clearColor = true, bool clearDepth = true, bool clearStencil = true) { EnsureActive(); Gfx.Clear(color, clearColor, clearDepth, clearStencil); }
		public void Point(Vertex2 point, float thickness = 1) { EnsureActive(); Gfx.Point(point, thickness); }
		public void Point(Vertex2[] points, float thickness = 1) { EnsureActive(); Gfx.Point(points, thickness); }
		public void Point(Vertex3 point, float thickness = 1) { EnsureActive(); Gfx.Point(point, thickness); }
		public void Point(Vertex3[] points, float thickness = 1) { EnsureActive(); Gfx.Point(points, thickness); }
		public void Line(Vertex2 start, Vertex2 end, float thickness = 1) { EnsureActive(); Gfx.Line(start, end, thickness); }
		public void Line(Vertex3 start, Vertex3 end, float thickness = 1) { EnsureActive(); Gfx.Line(start, end, thickness); }
		public void LineStrip(Vertex2[] points, float thickness = 1) { EnsureActive(); Gfx.LineStrip(points, thickness); }
		public void Rectangle(float X, float Y, float W, float H, float Thickness = 1, Color? Color = null) { EnsureActive(); Gfx.Rectangle(X, Y, W, H, Thickness, Color); }
		public void FilledRectangle(float X, float Y, float W, float H, Color? Clr = null) { EnsureActive(); Gfx.FilledRectangle(X, Y, W, H, Clr); }
		public void TexturedRectangle(float X, float Y, float W, float H, float U0 = 0, float V0 = 0, float U1 = 1, float V1 = 1, Color? Color = null, Texture Texture = null, ShaderProgram Shader = null) { EnsureActive(); Gfx.TexturedRectangle(X, Y, W, H, U0, V0, U1, V1, Color, Texture, Shader); }
		public void NinePatch(float X, float Y, float W, float H, Texture Texture, NinePatchInsets Insets, Color? Color = null, ShaderProgram Shader = null) { EnsureActive(); Gfx.NinePatch(X, Y, W, H, Texture, Insets, Color, Shader); }
		public void NinePatch(Vector2 Position, Vector2 Size, Texture Texture, NinePatchInsets Insets, Color? Color = null, ShaderProgram Shader = null) { EnsureActive(); Gfx.NinePatch(Position, Size, Texture, Insets, Color, Shader); }
		public void RoundedRectangle(float X, float Y, float W, float H, CornerRadii Radii, float Thickness = 1, Color? Color = null, int CornerSegments = 0) { EnsureActive(); Gfx.RoundedRectangle(X, Y, W, H, Radii, Thickness, Color, CornerSegments); }
		public void RoundedRectangle(Vector2 Position, Vector2 Size, CornerRadii Radii, float Thickness = 1, Color? Color = null, int CornerSegments = 0) { EnsureActive(); Gfx.RoundedRectangle(Position, Size, Radii, Thickness, Color, CornerSegments); }
		public void FilledRoundedRectangle(float X, float Y, float W, float H, CornerRadii Radii, Color? Color = null, int CornerSegments = 0) { EnsureActive(); Gfx.FilledRoundedRectangle(X, Y, W, H, Radii, Color, CornerSegments); }
		public void FilledRoundedRectangle(Vector2 Position, Vector2 Size, CornerRadii Radii, Color? Color = null, int CornerSegments = 0) { EnsureActive(); Gfx.FilledRoundedRectangle(Position, Size, Radii, Color, CornerSegments); }
		public void TexturedRoundedRectangle(float X, float Y, float W, float H, CornerRadii Radii, Texture Texture, float U0 = 0, float V0 = 0, float U1 = 1, float V1 = 1, Color? Color = null, ShaderProgram Shader = null, int CornerSegments = 0) { EnsureActive(); Gfx.TexturedRoundedRectangle(X, Y, W, H, Radii, Texture, U0, V0, U1, V1, Color, Shader, CornerSegments); }
		public void TexturedRoundedRectangle(Vector2 Position, Vector2 Size, CornerRadii Radii, Texture Texture, Vector2 UVMin, Vector2 UVMax, Color? Color = null, ShaderProgram Shader = null, int CornerSegments = 0) { EnsureActive(); Gfx.TexturedRoundedRectangle(Position, Size, Radii, Texture, UVMin, UVMax, Color, Shader, CornerSegments); }
		public void Circle(Vector2 Center, float Radius, float Thickness = 1, Color? Color = null, int Segments = 0) { EnsureActive(); Gfx.Circle(Center, Radius, Thickness, Color, Segments); }
		public void FilledCircle(Vector2 Center, float Radius, Color? Color = null, int Segments = 0) { EnsureActive(); Gfx.FilledCircle(Center, Radius, Color, Segments); }
		public void Ellipse(Vector2 Center, Vector2 Radii, float Thickness = 1, Color? Color = null, int Segments = 0) { EnsureActive(); Gfx.Ellipse(Center, Radii, Thickness, Color, Segments); }
		public void FilledEllipse(Vector2 Center, Vector2 Radii, Color? Color = null, int Segments = 0) { EnsureActive(); Gfx.FilledEllipse(Center, Radii, Color, Segments); }
		public void TexturedCircle(Vector2 Center, float Radius, Texture Texture, float U0 = 0, float V0 = 0, float U1 = 1, float V1 = 1, Color? Color = null, ShaderProgram Shader = null, int Segments = 0) { EnsureActive(); Gfx.TexturedCircle(Center, Radius, Texture, U0, V0, U1, V1, Color, Shader, Segments); }
		public void TexturedEllipse(Vector2 Center, Vector2 Radii, Texture Texture, float U0 = 0, float V0 = 0, float U1 = 1, float V1 = 1, Color? Color = null, ShaderProgram Shader = null, int Segments = 0) { EnsureActive(); Gfx.TexturedEllipse(Center, Radii, Texture, U0, V0, U1, V1, Color, Shader, Segments); }
		public void Ring(Vector2 Center, float InnerRadius, float OuterRadius, Color? Color = null, int Segments = 0) { EnsureActive(); Gfx.Ring(Center, InnerRadius, OuterRadius, Color, Segments); }
		public void Ring(Vector2 Center, float InnerRadius, float OuterRadius, float StartAngle, float EndAngle, Color? Color = null, int Segments = 0) { EnsureActive(); Gfx.Ring(Center, InnerRadius, OuterRadius, StartAngle, EndAngle, Color, Segments); }
		public void RingLines(Vector2 Center, float InnerRadius, float OuterRadius, float Thickness = 1, Color? Color = null, int Segments = 0) { EnsureActive(); Gfx.RingLines(Center, InnerRadius, OuterRadius, Thickness, Color, Segments); }
		public void RingLines(Vector2 Center, float InnerRadius, float OuterRadius, float StartAngle, float EndAngle, float Thickness = 1, Color? Color = null, int Segments = 0) { EnsureActive(); Gfx.RingLines(Center, InnerRadius, OuterRadius, StartAngle, EndAngle, Thickness, Color, Segments); }
		public void QuadraticBezier(Vector2 Start, Vector2 Control, Vector2 End, float Thickness = 1, Color? Color = null, int Segments = 0) { EnsureActive(); Gfx.QuadraticBezier(Start, Control, End, Thickness, Color, Segments); }
		public void CubicBezier(Vector2 Start, Vector2 Control1, Vector2 Control2, Vector2 End, float Thickness = 1, Color? Color = null, int Segments = 0) { EnsureActive(); Gfx.CubicBezier(Start, Control1, Control2, End, Thickness, Color, Segments); }
		public Vector2 DrawText(GfxFont font, Vector2 position, string text, Color color, float fontSize = -1, bool debugDraw = false) { EnsureActive(); return Gfx.DrawText(font, position, text, color, fontSize, debugDraw); }
		public void Execute(CommandList commands) { EnsureActive(); if (commands == null) throw new ArgumentNullException(nameof(commands)); commands.Execute(this); }
		public void Execute(GraphicsCommandBatch batch) { EnsureActive(); if (batch == null) throw new ArgumentNullException(nameof(batch)); batch.Execute(this); }

		public void Execute(DeferredRenderQueue queue, RenderBucket bucket, IComparer<RenderSubmission> comparer = null) { EnsureActive(); if (queue == null) throw new ArgumentNullException(nameof(queue)); queue.Execute(this, bucket, comparer); }
		public void Draw(IDrawable drawable) { EnsureActive(); if (drawable == null) throw new ArgumentNullException(nameof(drawable)); drawable.Draw(); }
		public void Dispose()
		{
			if (disposed)
				return;
			if (scopeDepth != 0)
				throw new InvalidOperationException("All render-pass scopes must be closed before the pass is disposed.");
			disposed = true;
			try { Gfx.PopRenderState(); }
			finally
			{
				try { ShaderUniforms.Pop(); }
				finally
				{
					try { if (!target.IsBackbuffer) target.Texture.UnbindForPass(); }
					finally { frame.EndPass(this); }
				}
			}
		}

		private sealed class PassScope : IDisposable
		{
			private readonly RenderPass pass;
			private readonly Action restore;
			private readonly int depth;
			private bool disposed;

			public PassScope(RenderPass pass, Action restore)
			{
				this.pass = pass;
				this.restore = restore;
				depth = pass.OpenScope();
			}

			public void Dispose()
			{
				if (disposed)
					return;
				pass.CloseScope(depth, restore);
				disposed = true;
			}
		}
	}
}
