using System;
using System.Collections.Generic;
using System.Numerics;
using Glfw3;

namespace FishGfx.Graphics;

public unsafe sealed partial class RenderWindow
{
	public WindowMode Mode
	{
		get => mode;
		set => SetWindowMode(value);
	}

	public bool VSyncEnabled
	{
		get => vSyncEnabled;
		set
		{
			ThrowIfDisposed();

			if (vSyncEnabled == value)
			{
				return;
			}

			if (Graphics == null)
			{
				MakeNativeCurrent();
			}
			else
			{
				Graphics.MakeCurrent();
			}

			Glfw.SwapInterval(value ? 1 : 0);
			vSyncEnabled = value;
		}
	}

	public Vector2 ClientSize
	{
		get => Size;
		set => SetClientSize(value);
	}

	public Vector2 Position
	{
		get
		{
			ThrowIfDisposed();
			Glfw.GetWindowPos(nativeWindow, out int x, out int y);

			return new Vector2(x, y);
		}
		set => SetPosition(value);
	}

	public Vector2 CursorPosition
	{
		get
		{
			ThrowIfDisposed();
			Glfw.GetCursorPos(nativeWindow, out double x, out double y);

			return new Vector2((float)x, (float)y);
		}
		set => SetCursorPosition(value);
	}

	public MonitorInfo Monitor
	{
		get
		{
			ThrowIfDisposed();

			return GetSelectedMonitorInfo();
		}
		set => SelectMonitor(value);
	}

	public MonitorVideoMode? ExclusiveVideoMode => exclusiveVideoMode;

	/// <summary>
	/// Enumerates connected monitors on the process-wide GLFW owner thread.
	/// The first FishGfx windowing call establishes that owner, so applications
	/// should make this call from the main thread used to create and poll windows.
	/// </summary>
	public static IReadOnlyList<MonitorInfo> GetMonitors()
	{
		Internal_OpenGL.EnsureGlfwThread();
		GlfwNativeExtensions.EnablePerMonitorDpiAwareness();
		Internal_OpenGL.InitGLFW();

		Glfw.Monitor[] nativeMonitors = Glfw.GetMonitors();
		Glfw.Monitor primary = Glfw.GetPrimaryMonitor();
		MonitorInfo[] monitors = new MonitorInfo[nativeMonitors.Length];

		for (int index = 0; index < nativeMonitors.Length; index++)
		{
			monitors[index] = CreateMonitorInfo(
				nativeMonitors[index],
				index,
				primary
			);
		}

		return Array.AsReadOnly(monitors);
	}

	public void SelectMonitor(MonitorInfo monitor)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(monitor);

		Glfw.Monitor resolvedMonitor = ResolveMonitor(monitor);

		if (resolvedMonitor == selectedMonitor)
		{
			return;
		}

		if (mode == WindowMode.Windowed)
		{
			selectedMonitor = resolvedMonitor;

			return;
		}

		SetWindowMode(mode, monitor);
	}

	public void SelectMonitor(int monitorIndex)
	{
		IReadOnlyList<MonitorInfo> monitors = GetMonitors();

		if (monitorIndex < 0 || monitorIndex >= monitors.Count)
		{
			throw new ArgumentOutOfRangeException(nameof(monitorIndex));
		}

		SelectMonitor(monitors[monitorIndex]);
	}

	public void SetWindowMode(
		WindowMode newMode,
		MonitorInfo monitor = null,
		MonitorVideoMode? videoMode = null
	)
	{
		ThrowIfDisposed();

		if (!Enum.IsDefined(newMode))
		{
			throw new ArgumentOutOfRangeException(nameof(newMode));
		}

		if (newMode != WindowMode.ExclusiveFullscreen && videoMode.HasValue)
		{
			throw new ArgumentException(
				"A video mode may only be selected for exclusive fullscreen.",
				nameof(videoMode)
			);
		}

		Glfw.Monitor targetMonitor = monitor == null
			? ResolveSelectedMonitor()
			: ResolveMonitor(monitor);
		MonitorInfo targetInfo = GetMonitorInfo(targetMonitor);
		MonitorVideoMode? targetVideoMode = videoMode;

		if (newMode == WindowMode.ExclusiveFullscreen)
		{
			if (videoMode.HasValue)
			{
				ValidateVideoMode(videoMode.Value, nameof(videoMode));

				if (!targetInfo.Supports(videoMode.Value))
				{
					throw new ArgumentException(
						$"Monitor '{targetInfo.Name}' does not support the requested video mode.",
						nameof(videoMode)
					);
				}
			}
			else if (exclusiveVideoMode.HasValue
				&& targetInfo.Supports(exclusiveVideoMode.Value))
			{
				targetVideoMode = exclusiveVideoMode;
			}
			else
			{
				targetVideoMode = targetInfo.CurrentVideoMode;
			}

			ValidateVideoMode(targetVideoMode.Value, nameof(videoMode));
		}

		bool monitorChanged = targetMonitor != selectedMonitor;
		bool videoModeChanged = newMode == WindowMode.ExclusiveFullscreen
			&& targetVideoMode != exclusiveVideoMode;

		if (newMode == mode && !monitorChanged && !videoModeChanged)
		{
			return;
		}

		if (mode == WindowMode.Windowed)
		{
			CaptureWindowedBounds();
		}

		WindowMode previousMode = mode;
		mode = newMode;
		selectedMonitor = targetMonitor;

		if (newMode == WindowMode.ExclusiveFullscreen)
		{
			exclusiveVideoMode = targetVideoMode;
		}

		switch (newMode)
		{
			case WindowMode.Windowed:
				ApplyWindowedMode(previousMode);
				break;
			case WindowMode.BorderlessFullscreen:
				ApplyBorderlessMode(previousMode, targetInfo);
				break;
			case WindowMode.ExclusiveFullscreen:
				ApplyExclusiveMode(targetVideoMode.Value);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(newMode));
		}

		RefreshWindowMetrics(true);

		if (previousMode != newMode)
		{
			ModeChanged?.Invoke(
				this,
				new WindowModeChangedEventArgs(previousMode, newMode)
			);
		}
	}

	public void SetClientSize(int width, int height)
	{
		ThrowIfDisposed();
		ValidateClientSize(width, height);

		if (mode != WindowMode.Windowed)
		{
			throw new InvalidOperationException(
				"The client size is controlled by the selected fullscreen mode."
			);
		}

		Glfw.SetWindowSize(nativeWindow, width, height);
		RefreshWindowMetrics(true);
		CaptureWindowedBounds();
	}

	public void SetClientSize(Vector2 size)
	{
		if (!float.IsFinite(size.X) || !float.IsFinite(size.Y))
		{
			throw new ArgumentOutOfRangeException(nameof(size));
		}

		SetClientSize((int)MathF.Round(size.X), (int)MathF.Round(size.Y));
	}

	public void SetPosition(int x, int y)
	{
		ThrowIfDisposed();

		if (mode != WindowMode.Windowed)
		{
			throw new InvalidOperationException(
				"The window position is controlled by the selected fullscreen monitor."
			);
		}

		Glfw.SetWindowPos(nativeWindow, x, y);
		windowedPosition = new Vector2(x, y);
		hasWindowedBounds = true;
	}

	public void SetPosition(Vector2 position)
	{
		if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
		{
			throw new ArgumentOutOfRangeException(nameof(position));
		}

		SetPosition(
			(int)MathF.Round(position.X),
			(int)MathF.Round(position.Y)
		);
	}

	public void SetCursorPosition(Vector2 position)
	{
		ThrowIfDisposed();

		if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
		{
			throw new ArgumentOutOfRangeException(nameof(position));
		}

		Glfw.SetCursorPos(nativeWindow, position.X, position.Y);
		MousePosition = position;
	}

	internal static Vector2 CalculateContentScale(
		int logicalWidth,
		int logicalHeight,
		int framebufferWidth,
		int framebufferHeight
	)
	{
		float x = logicalWidth > 0
			? framebufferWidth / (float)logicalWidth
			: 1;
		float y = logicalHeight > 0
			? framebufferHeight / (float)logicalHeight
			: 1;

		return new Vector2(
			float.IsFinite(x) && x > 0 ? x : 1,
			float.IsFinite(y) && y > 0 ? y : 1
		);
	}

	internal static void ValidateVideoMode(
		MonitorVideoMode videoMode,
		string parameterName
	)
	{
		if (videoMode.Width <= 0
			|| videoMode.Height <= 0
			|| videoMode.RefreshRate <= 0)
		{
			throw new ArgumentOutOfRangeException(
				parameterName,
				"Video-mode dimensions and refresh rate must be positive."
			);
		}
	}

	private static MonitorInfo CreateMonitorInfo(
		Glfw.Monitor monitor,
		int index,
		Glfw.Monitor primary
	)
	{
		Glfw.GetMonitorPos(monitor, out int x, out int y);
		Glfw.GetMonitorPhysicalSize(
			monitor,
			out int physicalWidth,
			out int physicalHeight
		);
		Glfw.VideoMode[] nativeModes = Glfw.GetVideoModes(monitor);
		List<MonitorVideoMode> videoModes = new(nativeModes.Length);

		for (int modeIndex = 0; modeIndex < nativeModes.Length; modeIndex++)
		{
			MonitorVideoMode videoMode = ConvertVideoMode(nativeModes[modeIndex]);

			if (!videoModes.Contains(videoMode))
			{
				videoModes.Add(videoMode);
			}
		}

		return new MonitorInfo(
			index,
			monitor.Ptr,
			Glfw.GetMonitorName(monitor),
			new Vector2(x, y),
			new Vector2(physicalWidth, physicalHeight),
			ConvertVideoMode(Glfw.GetVideoMode(monitor)),
			videoModes.ToArray(),
			monitor == primary
		);
	}

	private static MonitorVideoMode ConvertVideoMode(Glfw.VideoMode videoMode)
	{
		return new MonitorVideoMode(
			videoMode.Width,
			videoMode.Height,
			videoMode.RefreshRate
		);
	}

	private Glfw.Monitor ResolveInitialMonitor(int monitorIndex)
	{
		Glfw.Monitor[] monitors = Glfw.GetMonitors();

		if (monitors.Length == 0)
		{
			throw new InvalidOperationException("No monitors are available.");
		}

		if (monitorIndex == -1)
		{
			return Glfw.GetPrimaryMonitor();
		}

		if (monitorIndex >= monitors.Length)
		{
			throw new ArgumentOutOfRangeException(
				nameof(monitorIndex),
				$"Monitor index {monitorIndex} is not available."
			);
		}

		return monitors[monitorIndex];
	}

	private Glfw.Monitor ResolveSelectedMonitor()
	{
		Glfw.Monitor[] monitors = Glfw.GetMonitors();
		Glfw.Monitor primary = Glfw.GetPrimaryMonitor();

		return ResolveSelectedMonitor(selectedMonitor, monitors, primary);
	}

	internal static Glfw.Monitor ResolveSelectedMonitor(
		Glfw.Monitor selected,
		Glfw.Monitor[] monitors,
		Glfw.Monitor primary
	)
	{
		ArgumentNullException.ThrowIfNull(monitors);

		foreach (Glfw.Monitor monitor in monitors)
		{
			if (monitor == selected)
			{
				return monitor;
			}
		}

		return primary;
	}

	private static Glfw.Monitor ResolveMonitor(MonitorInfo monitor)
	{
		Glfw.Monitor[] monitors = Glfw.GetMonitors();

		foreach (Glfw.Monitor candidate in monitors)
		{
			if (candidate.Ptr == monitor.NativeHandle)
			{
				return candidate;
			}
		}

		throw new InvalidOperationException(
			$"Monitor '{monitor.Name}' is no longer connected."
		);
	}

	private MonitorInfo GetSelectedMonitorInfo()
	{
		return GetMonitorInfo(ResolveSelectedMonitor());
	}

	private static MonitorInfo GetMonitorInfo(Glfw.Monitor monitor)
	{
		Glfw.Monitor[] monitors = Glfw.GetMonitors();
		Glfw.Monitor primary = Glfw.GetPrimaryMonitor();

		for (int index = 0; index < monitors.Length; index++)
		{
			if (monitors[index] == monitor)
			{
				return CreateMonitorInfo(monitor, index, primary);
			}
		}

		throw new InvalidOperationException("The selected monitor is no longer connected.");
	}

	private void ApplyWindowedMode(WindowMode previousMode)
	{
		EnsureWindowedBounds();

		int x = (int)windowedPosition.X;
		int y = (int)windowedPosition.Y;
		int width = Math.Max(1, (int)windowedSize.X);
		int height = Math.Max(1, (int)windowedSize.Y);

		if (previousMode == WindowMode.ExclusiveFullscreen
			|| borderlessUsesAttachedMonitor)
		{
			Glfw.SetWindowMonitor(
				nativeWindow,
				Glfw.Monitor.None,
				x,
				y,
				width,
				height,
				Glfw.DontCare
			);
		}

		RestoreDecorations();
		Glfw.SetWindowPos(nativeWindow, x, y);
		Glfw.SetWindowSize(nativeWindow, width, height);
		borderlessUsesAttachedMonitor = false;
	}

	private void ApplyBorderlessMode(
		WindowMode previousMode,
		MonitorInfo monitor
	)
	{
		RestoreDecorations();

		int x = (int)monitor.Position.X;
		int y = (int)monitor.Position.Y;
		int width = monitor.CurrentVideoMode.Width;
		int height = monitor.CurrentVideoMode.Height;

		if (previousMode == WindowMode.ExclusiveFullscreen
			|| borderlessUsesAttachedMonitor)
		{
			Glfw.SetWindowMonitor(
				nativeWindow,
				Glfw.Monitor.None,
				x,
				y,
				width,
				height,
				Glfw.DontCare
			);
		}

		if (GlfwNativeExtensions.TryEnterBorderless(
			nativeWindow,
			x,
			y,
			width,
			height,
			out decoratedStyle,
			out decoratedExtendedStyle
		))
		{
			decorationsRemoved = true;
			borderlessUsesAttachedMonitor = false;

			return;
		}

		GlfwNativeExtensions.RestoreDecorations(
			nativeWindow,
			decoratedStyle,
			decoratedExtendedStyle
		);
		Glfw.SetWindowMonitor(
			nativeWindow,
			selectedMonitor,
			0,
			0,
			width,
			height,
			monitor.CurrentVideoMode.RefreshRate
		);
		decorationsRemoved = false;
		borderlessUsesAttachedMonitor = true;
	}

	private void ApplyExclusiveMode(MonitorVideoMode videoMode)
	{
		RestoreDecorations();
		Glfw.SetWindowMonitor(
			nativeWindow,
			selectedMonitor,
			0,
			0,
			videoMode.Width,
			videoMode.Height,
			videoMode.RefreshRate
		);
		borderlessUsesAttachedMonitor = false;
	}

	private void RestoreDecorations()
	{
		if (!decorationsRemoved)
		{
			return;
		}

		GlfwNativeExtensions.RestoreDecorations(
			nativeWindow,
			decoratedStyle,
			decoratedExtendedStyle
		);
		decorationsRemoved = false;
	}

	private void CaptureWindowedBounds()
	{
		Glfw.GetWindowPos(nativeWindow, out int x, out int y);
		GetNativeWindowSize(out int width, out int height);

		if (width <= 0 || height <= 0)
		{
			return;
		}

		windowedPosition = new Vector2(x, y);
		windowedSize = new Vector2(width, height);
		hasWindowedBounds = true;
	}

	private void EnsureWindowedBounds()
	{
		if (hasWindowedBounds)
		{
			return;
		}

		windowedPosition = Vector2.Zero;
		windowedSize = new Vector2(Math.Max(1, Width), Math.Max(1, Height));
		hasWindowedBounds = true;
	}

	private void RefreshWindowMetrics(bool notify)
	{
		GetNativeWindowSize(out int width, out int height);
		GetNativeFramebufferSize(out int newFramebufferWidth, out int newFramebufferHeight);
		Vector2 newContentScale = GlfwNativeExtensions.GetContentScale(
			nativeWindow,
			width,
			height,
			newFramebufferWidth,
			newFramebufferHeight
		);
		bool sizeChanged = width != Width || height != Height;
		bool framebufferChanged = newFramebufferWidth != framebufferWidth
			|| newFramebufferHeight != framebufferHeight;
		bool scaleChanged = Vector2.DistanceSquared(
			newContentScale,
			contentScale
		) > 0.000001f;

		Width = width;
		Height = height;
		framebufferWidth = newFramebufferWidth;
		framebufferHeight = newFramebufferHeight;
		contentScale = newContentScale;

		if (framebufferChanged)
		{
			Graphics?.ResizeBackbuffer(framebufferWidth, framebufferHeight);
		}

		if (!notify)
		{
			return;
		}

		if (sizeChanged)
		{
			Resized?.Invoke(this, new WindowResizeEventArgs(Width, Height));
		}

		if (framebufferChanged)
		{
			FramebufferResized?.Invoke(
				this,
				new WindowResizeEventArgs(framebufferWidth, framebufferHeight)
			);
		}

		if (scaleChanged)
		{
			ContentScaleChanged?.Invoke(
				this,
				new ContentScaleChangedEventArgs(contentScale)
			);
		}
	}

	private static void ValidateClientSize(int width, int height)
	{
		if (width <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width));
		}

		if (height <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(height));
		}
	}
}
