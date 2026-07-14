using System;
using System.Numerics;
using Glfw3;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics;

public unsafe sealed partial class RenderWindow : IDisposable
{
	private Glfw.Window nativeWindow;
	private Color[] pixelData = Array.Empty<Color>();
	private bool captureCursor;
	private bool showCursor = true;
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
		ValidateOptions(options);
		Internal_OpenGL.InitGLFW();
		Glfw.WindowHint(Glfw.Hint.Resizable, options.Resizable);

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
		}
		catch
		{
			DestroyWindowAfterFailedInitialization();

			throw;
		}

		Width = options.Width;
		Height = options.Height;
		RegisterCallbacks();

		if (options.CenterWindow)
		{
			Center();
		}

		CaptureCursor = false;
		Graphics = new GraphicsContext(this);
	}

	public event EventHandler<MouseMoveEventArgs> MouseMoved;

	public event EventHandler<MouseMoveEventArgs> MouseDelta;

	public event EventHandler<KeyEventArgs> KeyChanged;

	public event EventHandler<MouseButtonEventArgs> MouseButtonChanged;

	public event EventHandler<TextInputEventArgs> TextInput;

	public event EventHandler<ScrollEventArgs> Scrolled;

	public event EventHandler<WindowResizeEventArgs> Resized;

	public int Width { get; private set; }

	public int Height { get; private set; }

	public Vector2 Size => new(Width, Height);

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

		Vector2 desktopSize = GetDesktopResolution();
		GetNativeWindowSize(out int width, out int height);
		int x = (int)desktopSize.X / 2 - width / 2;
		int y = (int)desktopSize.Y / 2 - height / 2;

		Glfw.SetWindowPos(nativeWindow, x, y);
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
		GetNativeWindowSize(out int width, out int height);

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
		GetNativeWindowSize(out int width, out int height);

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
		Internal_OpenGL.InitGLFW();

		Glfw.VideoMode videoMode = Glfw.GetVideoMode(Glfw.GetPrimaryMonitor());

		return new Vector2(videoMode.Width, videoMode.Height);
	}

	internal void MakeNativeCurrent()
	{
		Glfw.MakeContextCurrent(nativeWindow);
		Internal_OpenGL.InitOpenGL();
		Internal_OpenGL.SetupOpenGL();
		Internal_OpenGL.GL.Enable(EnableCap.Multisample);
	}

	internal void SwapNativeBuffers()
	{
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

	private void ThrowIfDisposed()
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(RenderWindow));
		}
	}
}
