using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using FishGfx.Graphics;

namespace FishGfx.SmokeTest;

internal static class WindowingSmokeTest
{
	internal static void Run()
	{
		IReadOnlyList<MonitorInfo> monitors = RenderWindow.GetMonitors();

		if (monitors.Count == 0)
		{
			throw new InvalidOperationException("No monitors were enumerated.");
		}

		using RenderWindow window = new(new RenderWindowOptions
		{
			Width = 320,
			Height = 240,
			Title = "FishGfx windowing smoke test",
			Resizable = true,
			CenterWindow = false,
		});

		int logicalResizeCount = 0;
		int framebufferResizeCount = 0;
		int modeChangeCount = 0;
		window.Resized += (_, _) => logicalResizeCount++;
		window.FramebufferResized += (_, _) => framebufferResizeCount++;
		window.ModeChanged += (_, _) => modeChangeCount++;

		Assert(window.Width == 320 && window.Height == 240, "Initial logical size is wrong.");
		Assert(
			CaptureWorkerException(window.PollEvents) is InvalidOperationException,
			"Polling events from a non-owner thread was not rejected."
		);
		Assert(
			CaptureWorkerException(() => RenderWindow.GetMonitors())
				is InvalidOperationException,
			"Static monitor enumeration did not preserve GLFW thread affinity."
		);
		AssertBackbufferMatches(window);
		Assert(
			window.ContentScale.X > 0 && window.ContentScale.Y > 0,
			"The content scale is invalid."
		);

		window.VSyncEnabled = true;
		Assert(window.VSyncEnabled, "VSync was not enabled.");
		window.VSyncEnabled = false;
		Assert(!window.VSyncEnabled, "VSync was not disabled.");

		window.SetClientSize(400, 300);
		window.PollEvents();
		Assert(window.ClientSize == new Vector2(400, 300), "Runtime resize failed.");
		AssertBackbufferMatches(window);
		Assert(logicalResizeCount > 0, "The logical resize event was not raised.");
		Assert(framebufferResizeCount > 0, "The framebuffer resize event was not raised.");

		MonitorInfo monitor = monitors[0];
		window.SetPosition(monitor.Position + new Vector2(64, 64));
		window.PollEvents();
		Vector2 windowedPosition = window.Position;

		window.SetCursorPosition(new Vector2(40, 30));
		window.PollEvents();
		Assert(
			Vector2.Distance(window.CursorPosition, new Vector2(40, 30)) < 1,
			"Logical cursor positioning failed."
		);

		window.SetWindowMode(WindowMode.BorderlessFullscreen, monitor);
		window.PollEvents();
		Assert(window.Mode == WindowMode.BorderlessFullscreen, "Borderless mode failed.");
		Assert(window.Monitor.Index == monitor.Index, "Monitor selection failed.");
		AssertBackbufferMatches(window);

		window.Mode = WindowMode.Windowed;
		window.PollEvents();
		Assert(window.ClientSize == new Vector2(400, 300), "Windowed bounds were not restored.");
		Assert(
			Vector2.Distance(window.Position, windowedPosition) < 1,
			"Borderless mode did not restore the windowed position."
		);
		AssertBackbufferMatches(window);

		window.SetWindowMode(
			WindowMode.ExclusiveFullscreen,
			monitor,
			monitor.CurrentVideoMode
		);
		window.PollEvents();
		Assert(window.Mode == WindowMode.ExclusiveFullscreen, "Exclusive mode failed.");
		AssertBackbufferMatches(window);

		window.Mode = WindowMode.Windowed;
		window.PollEvents();
		Assert(window.ClientSize == new Vector2(400, 300), "Exclusive mode lost windowed bounds.");
		Assert(
			Vector2.Distance(window.Position, windowedPosition) < 1,
			"Exclusive mode did not restore the windowed position."
		);
		AssertBackbufferMatches(window);
		Assert(modeChangeCount == 4, "Window mode events were not raised exactly once.");

		Console.WriteLine(
			$"Windowing smoke test passed on {monitor.Name}; "
				+ $"logical={window.Width}x{window.Height}, "
				+ $"framebuffer={window.FramebufferWidth}x{window.FramebufferHeight}, "
				+ $"scale={window.ContentScale.X:0.##}x{window.ContentScale.Y:0.##}."
		);
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException(message);
		}
	}

	private static void AssertBackbufferMatches(RenderWindow window)
	{
		Assert(
			window.Graphics.Backbuffer.Width == window.FramebufferWidth
				&& window.Graphics.Backbuffer.Height == window.FramebufferHeight,
			"The backbuffer does not match framebuffer pixel dimensions."
		);
	}

	private static Exception CaptureWorkerException(Action action)
	{
		Exception captured = null;
		Thread thread = new(() =>
		{
			try
			{
				action();
			}
			catch (Exception exception)
			{
				captured = exception;
			}
		});
		thread.Start();
		thread.Join();

		return captured;
	}
}
