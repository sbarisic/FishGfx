using System;
using System.Numerics;
using System.Threading;
using Glfw3;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics;

public unsafe sealed partial class RenderWindow : IDisposable
{
	private readonly Thread ownerThread;
	private Glfw.Window nativeWindow;
	private Color[] pixelData = Array.Empty<Color>();
	private Glfw.Monitor selectedMonitor;
	private MonitorVideoMode? exclusiveVideoMode;
	private Vector2 contentScale = Vector2.One;
	private Vector2 windowedPosition;
	private Vector2 windowedSize;
	private IntPtr decoratedStyle;
	private IntPtr decoratedExtendedStyle;
	private WindowMode mode;
	private int framebufferWidth;
	private int framebufferHeight;
	private bool captureCursor;
	private bool showCursor = true;
	private bool vSyncEnabled;
	private bool hasWindowedBounds;
	private bool decorationsRemoved;
	private bool borderlessUsesAttachedMonitor;
	private bool disposed;

	public RenderWindow(
		int width,
		int height,
		string title,
		bool resizable = false,
		bool centerWindow = true
	)
		: this(new RenderWindowOptions
		{
			Width = width,
			Height = height,
			Title = title,
			Resizable = resizable,
			CenterWindow = centerWindow,
		})
	{
	}

	public RenderWindow(RenderWindowOptions options)
	{
		ownerThread = Thread.CurrentThread;
		ValidateOptions(options);
		Internal_OpenGL.EnsureGlfwThread();
		GlfwNativeExtensions.EnablePerMonitorDpiAwareness();
		Internal_OpenGL.InitGLFW();
		Glfw.WindowHint(Glfw.Hint.Resizable, options.Resizable);
		selectedMonitor = ResolveInitialMonitor(options.MonitorIndex);
		exclusiveVideoMode = options.ExclusiveVideoMode;

		if (exclusiveVideoMode.HasValue
			&& !GetMonitorInfo(selectedMonitor).Supports(exclusiveVideoMode.Value))
		{
			throw new ArgumentException(
				"The selected monitor does not support the requested exclusive video mode.",
				nameof(options)
			);
		}

		OpenGlVersion selectedVersion = options.PreferredVersion;

		while (selectedVersion >= options.MinimumVersion && !nativeWindow)
		{
			nativeWindow = TryCreateWindow(
				selectedVersion,
				options.Width,
				options.Height,
				options.Title ?? string.Empty
			);

			if (nativeWindow || options.RequireExactVersion)
			{
				break;
			}

			selectedVersion = PreviousVersion(selectedVersion);
		}

		if (!nativeWindow)
		{
			throw new InvalidOperationException("Could not create a supported OpenGL context.");
		}

		try
		{
			MakeNativeCurrent();
			OpenGlVersion actualVersion = new(
				Internal_OpenGL.MajorVersion,
				Internal_OpenGL.MinorVersion
			);
			ValidateCreatedContextVersion(
				selectedVersion,
				actualVersion,
				options.RequireExactVersion
			);

			RefreshWindowMetrics(false);
			RegisterCallbacks();

			if (options.CenterWindow)
			{
				Center();
			}

			CaptureWindowedBounds();
			CaptureCursor = false;
			Graphics = new GraphicsContext(this);
			VSyncEnabled = options.VSync;

			if (options.Mode != WindowMode.Windowed)
			{
				SetWindowMode(
					options.Mode,
					null,
					options.ExclusiveVideoMode
				);
			}
		}
		catch
		{
			CleanupAfterFailedInitialization();

			throw;
		}
	}

	public event EventHandler<MouseMoveEventArgs> MouseMoved;

	public event EventHandler<MouseMoveEventArgs> MouseDelta;

	public event EventHandler<KeyEventArgs> KeyChanged;

	public event EventHandler<MouseButtonEventArgs> MouseButtonChanged;

	public event EventHandler<TextInputEventArgs> TextInput;

	public event EventHandler<ScrollEventArgs> Scrolled;

	public event EventHandler<WindowResizeEventArgs> Resized;

	public event EventHandler<WindowResizeEventArgs> FramebufferResized;

	public event EventHandler<ContentScaleChangedEventArgs> ContentScaleChanged;

	public event EventHandler<WindowMoveEventArgs> Moved;

	public event EventHandler<WindowModeChangedEventArgs> ModeChanged;

	public int Width { get; private set; }

	public int Height { get; private set; }

	public Vector2 Size => new(Width, Height);

	public int FramebufferWidth => framebufferWidth;

	public int FramebufferHeight => framebufferHeight;

	public Vector2 FramebufferSize => new(framebufferWidth, framebufferHeight);

	public Vector2 ContentScale => contentScale;

	public Vector2 MousePosition { get; private set; }

	public GraphicsContext Graphics { get; private set; }

	public ReadOnlyMemory<Color> PixelData => pixelData;

	public bool IsCloseRequested
	{
		get
		{
			ThrowIfDisposed();

			return Glfw.WindowShouldClose(nativeWindow);
		}
		set
		{
			ThrowIfDisposed();
			Glfw.SetWindowShouldClose(nativeWindow, value);
		}
	}

	public bool CaptureCursor
	{
		get => captureCursor;
		set
		{
			ThrowIfDisposed();
			captureCursor = value;
			ApplyCursorMode();
		}
	}

	public bool ShowCursor
	{
		get => showCursor;
		set
		{
			ThrowIfDisposed();
			showCursor = value;
			ApplyCursorMode();
		}
	}

	public string ClipboardText
	{
		get
		{
			ThrowIfDisposed();

			return Glfw.GetClipboardString(nativeWindow);
		}
		set
		{
			ThrowIfDisposed();
			Glfw.SetClipboardString(nativeWindow, value ?? string.Empty);
		}
	}

	public void PollEvents()
	{
		ThrowIfDisposed();
		Glfw.PollEvents();
		RefreshWindowMetrics(true);
	}

	public void MakeCurrent()
	{
		ThrowIfDisposed();

		if (Graphics == null)
		{
			MakeNativeCurrent();
			return;
		}

		Graphics.MakeCurrent();
	}

	public void Focus()
	{
		ThrowIfDisposed();
		Glfw.FocusWindow(nativeWindow);
	}

	public void Center()
	{
		ThrowIfDisposed();

		Glfw.Monitor targetMonitor = ResolveSelectedMonitor();
		Glfw.GetMonitorPos(targetMonitor, out int monitorX, out int monitorY);
		Glfw.VideoMode videoMode = Glfw.GetVideoMode(targetMonitor);
		GetNativeWindowSize(out int width, out int height);
		int x = monitorX + videoMode.Width / 2 - width / 2;
		int y = monitorY + videoMode.Height / 2 - height / 2;

		Glfw.SetWindowPos(nativeWindow, x, y);
		selectedMonitor = targetMonitor;
	}

	public void SetTitle(string title)
	{
		ThrowIfDisposed();
		Glfw.SetWindowTitle(nativeWindow, title ?? string.Empty);
	}

	public void ReadPixels()
	{
		ThrowIfDisposed();
		Graphics.MakeCurrent();
		GetNativeFramebufferSize(out int width, out int height);

		if (pixelData.Length != width * height)
		{
			pixelData = new Color[width * height];
		}

		fixed (Color* colorPointer = pixelData)
		{
			Internal_OpenGL.GL.ReadPixels(
				0,
				0,
				(uint)width,
				(uint)height,
				PixelFormat.Rgba,
				PixelType.UnsignedByte,
				colorPointer
			);
		}
	}

	public Color GetPixel(int x, int y)
	{
		ThrowIfDisposed();
		GetNativeFramebufferSize(out int width, out int height);

		if (pixelData.Length == 0 || x < 0 || x >= width || y < 0 || y >= height)
		{
			return Color.Black;
		}

		int index = (height - y - 1) * width + x;

		return index < pixelData.Length ? pixelData[index] : Color.Black;
	}

	public void Close()
	{
		if (disposed)
		{
			return;
		}

		EnsureOwnerThread();

		Glfw.SetWindowShouldClose(nativeWindow, true);
		Graphics?.Dispose();
		Glfw.DestroyWindow(nativeWindow);
		disposed = true;
	}

	public void Dispose()
	{
		Close();
		GC.SuppressFinalize(this);
	}

	public static Vector2 GetDesktopResolution()
	{
		Internal_OpenGL.EnsureGlfwThread();
		GlfwNativeExtensions.EnablePerMonitorDpiAwareness();
		Internal_OpenGL.InitGLFW();

		Glfw.VideoMode videoMode = Glfw.GetVideoMode(Glfw.GetPrimaryMonitor());

		return new Vector2(videoMode.Width, videoMode.Height);
	}

	internal void MakeNativeCurrent()
	{
		EnsureOwnerThread();
		Glfw.MakeContextCurrent(nativeWindow);
		Internal_OpenGL.InitOpenGL();
		Internal_OpenGL.SetupOpenGL();
		Internal_OpenGL.GL.Enable(EnableCap.Multisample);
	}

	internal void SwapNativeBuffers()
	{
		EnsureOwnerThread();
		Glfw.SwapBuffers(nativeWindow);
	}

	private Glfw.Window TryCreateWindow(OpenGlVersion version, int width, int height, string title)
	{
		SetOpenGlHints(version);

		return Glfw.CreateWindow(width, height, title);
	}

	private static void SetOpenGlHints(OpenGlVersion version)
	{
		Glfw.WindowHint(Glfw.Hint.ClientApi, Glfw.ClientApi.OpenGL);
		Glfw.WindowHint(Glfw.Hint.ContextCreationApi, Glfw.ContextApi.Native);
		Glfw.WindowHint(Glfw.Hint.OpenglProfile, Glfw.OpenGLProfile.Core);
		Glfw.WindowHint(Glfw.Hint.OpenglForwardCompat, false);

#if DEBUG
		Glfw.WindowHint(Glfw.Hint.OpenglDebugContext, true);
#else
		if (version.Major > 4 || version.Major == 4 && version.Minor >= 6)
		{
			Glfw.WindowHint(Glfw.Hint.ContextNoError, true);
		}
#endif

		Glfw.WindowHint(Glfw.Hint.Doublebuffer, true);
		Glfw.WindowHint(Glfw.Hint.ContextVersionMajor, version.Major);
		Glfw.WindowHint(Glfw.Hint.ContextVersionMinor, version.Minor);
		Glfw.WindowHint(Glfw.Hint.Samples, 0);
	}

	private static OpenGlVersion PreviousVersion(OpenGlVersion version)
	{
		return version.Minor > 0
			? new OpenGlVersion(version.Major, version.Minor - 1)
			: new OpenGlVersion(version.Major - 1, 9);
	}

	internal static void ValidateOptions(RenderWindowOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		if (options.Width <= 0 || options.Height <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(options), "Window dimensions must be positive.");
		}

		if (options.MinimumVersion > options.PreferredVersion)
		{
			throw new ArgumentException(
				"The minimum OpenGL version cannot exceed the preferred version.",
				nameof(options)
			);
		}

		if (options.MinimumVersion < new OpenGlVersion(4, 0))
		{
			throw new ArgumentOutOfRangeException(
				nameof(options),
				"FishGfx requires OpenGL 4.0 or newer."
			);
		}

		if (!Enum.IsDefined(options.Mode))
		{
			throw new ArgumentOutOfRangeException(nameof(options), "The window mode is invalid.");
		}

		if (options.MonitorIndex < -1)
		{
			throw new ArgumentOutOfRangeException(
				nameof(options),
				"The monitor index must be -1 for the primary monitor or a non-negative index."
			);
		}

		if (options.ExclusiveVideoMode is MonitorVideoMode videoMode)
		{
			if (options.Mode != WindowMode.ExclusiveFullscreen)
			{
				throw new ArgumentException(
					"An exclusive video mode requires exclusive fullscreen startup mode.",
					nameof(options)
				);
			}

			ValidateVideoMode(videoMode, nameof(options));
		}
	}

	internal static void ValidateCreatedContextVersion(
		OpenGlVersion requestedVersion,
		OpenGlVersion actualVersion,
		bool requireExactVersion
	)
	{
		if (actualVersion < requestedVersion)
		{
			throw new InvalidOperationException(
				$"OpenGL {requestedVersion} or newer was requested, but "
					+ $"the created context reports OpenGL {actualVersion}."
			);
		}

		if (requireExactVersion && actualVersion != requestedVersion)
		{
			throw new InvalidOperationException(
				$"OpenGL {requestedVersion} was requested exactly, but "
					+ $"the created context reports OpenGL {actualVersion}."
			);
		}
	}

	private void GetNativeWindowSize(out int width, out int height)
	{
		Glfw.GetWindowSize(nativeWindow, out width, out height);
	}

	private void GetNativeFramebufferSize(out int width, out int height)
	{
		Glfw.GetFramebufferSize(nativeWindow, out width, out height);
	}

	private void ApplyCursorMode()
	{
		Glfw.CursorMode mode = captureCursor
			? Glfw.CursorMode.Disabled
			: showCursor
				? Glfw.CursorMode.Normal
				: Glfw.CursorMode.Hidden;

		Glfw.SetInputMode(nativeWindow, Glfw.InputMode.Cursor, mode);
	}

	private void DestroyWindowAfterFailedInitialization()
	{
		if (!nativeWindow)
		{
			return;
		}

		if (Glfw.GetCurrentContext() == nativeWindow)
		{
			Glfw.MakeContextCurrent(Glfw.Window.None);
		}

		Glfw.DestroyWindow(nativeWindow);
		nativeWindow = Glfw.Window.None;
	}

	private void CleanupAfterFailedInitialization()
	{
		try
		{
			Graphics?.Dispose();
		}
		catch
		{
		}
		finally
		{
			Graphics = null;
			DestroyWindowAfterFailedInitialization();
		}
	}

	private void ThrowIfDisposed()
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(RenderWindow));
		}

		EnsureOwnerThread();
	}

	private void EnsureOwnerThread()
	{
		if (!ReferenceEquals(Thread.CurrentThread, ownerThread))
		{
			throw new InvalidOperationException(
				"RenderWindow operations may only run on the window's owning thread."
			);
		}

		Internal_OpenGL.EnsureGlfwThread();
	}
}
