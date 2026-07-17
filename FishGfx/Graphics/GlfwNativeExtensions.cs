using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Glfw3;

namespace FishGfx.Graphics;

internal static class GlfwNativeExtensions
{
	private const int WindowStyleIndex = -16;
	private const int WindowExtendedStyleIndex = -20;
	private const uint PositionNoSize = 0x0001;
	private const uint PositionNoMove = 0x0002;
	private const uint PositionNoZOrder = 0x0004;
	private const uint PositionNoOwnerZOrder = 0x0200;
	private const uint PositionFrameChanged = 0x0020;
	private const long WindowOverlapped = 0x00CF0000L;
	private const long WindowPopup = 0x80000000L;
	private const long ExtendedDialogModalFrame = 0x00000001L;
	private const long ExtendedWindowEdge = 0x00000100L;
	private const long ExtendedClientEdge = 0x00000200L;
	private const long ExtendedStaticEdge = 0x00020000L;
	private const int DefaultDpi = 96;

	internal static void EnablePerMonitorDpiAwareness()
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		try
		{
			SetProcessDpiAwarenessContext(new IntPtr(-4));
		}
		catch (EntryPointNotFoundException)
		{
		}
	}

	internal static Vector2 GetContentScale(
		Glfw.Window window,
		int logicalWidth,
		int logicalHeight,
		int framebufferWidth,
		int framebufferHeight
	)
	{
		if (OperatingSystem.IsWindows())
		{
			IntPtr nativeHandle = GetNativeWindow(window);

			if (nativeHandle != IntPtr.Zero)
			{
				uint dpi = TryGetDpiForWindow(nativeHandle);

				if (dpi > 0)
				{
					float scale = dpi / (float)DefaultDpi;

					return new Vector2(scale, scale);
				}
			}
		}

		return RenderWindow.CalculateContentScale(
			logicalWidth,
			logicalHeight,
			framebufferWidth,
			framebufferHeight
		);
	}

	internal static bool TryEnterBorderless(
		Glfw.Window window,
		int x,
		int y,
		int width,
		int height,
		out IntPtr originalStyle,
		out IntPtr originalExtendedStyle
	)
	{
		originalStyle = IntPtr.Zero;
		originalExtendedStyle = IntPtr.Zero;

		if (!OperatingSystem.IsWindows())
		{
			return false;
		}

		IntPtr nativeHandle = GetNativeWindow(window);

		if (nativeHandle == IntPtr.Zero)
		{
			return false;
		}

		originalStyle = GetWindowLongPtr(nativeHandle, WindowStyleIndex);
		originalExtendedStyle = GetWindowLongPtr(
			nativeHandle,
			WindowExtendedStyleIndex
		);
		long borderlessStyle = originalStyle.ToInt64()
			& ~WindowOverlapped
			| WindowPopup;
		long borderlessExtendedStyle = originalExtendedStyle.ToInt64()
			& ~(
				ExtendedDialogModalFrame
				| ExtendedWindowEdge
				| ExtendedClientEdge
				| ExtendedStaticEdge
			);

		SetWindowLongPtr(
			nativeHandle,
			WindowStyleIndex,
			new IntPtr(borderlessStyle)
		);
		SetWindowLongPtr(
			nativeHandle,
			WindowExtendedStyleIndex,
			new IntPtr(borderlessExtendedStyle)
		);

		return SetWindowPos(
			nativeHandle,
			IntPtr.Zero,
			x,
			y,
			width,
			height,
			PositionNoOwnerZOrder | PositionFrameChanged
		);
	}

	internal static void RestoreDecorations(
		Glfw.Window window,
		IntPtr style,
		IntPtr extendedStyle
	)
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		IntPtr nativeHandle = GetNativeWindow(window);

		if (nativeHandle == IntPtr.Zero)
		{
			return;
		}

		SetWindowLongPtr(nativeHandle, WindowStyleIndex, style);
		SetWindowLongPtr(
			nativeHandle,
			WindowExtendedStyleIndex,
			extendedStyle
		);
		SetWindowPos(
			nativeHandle,
			IntPtr.Zero,
			0,
			0,
			0,
			0,
			PositionNoMove
				| PositionNoSize
				| PositionNoZOrder
				| PositionNoOwnerZOrder
				| PositionFrameChanged
		);
	}

	private static IntPtr GetNativeWindow(Glfw.Window window)
	{
		try
		{
			return GetWin32Window(window.Ptr);
		}
		catch (EntryPointNotFoundException)
		{
			return IntPtr.Zero;
		}
	}

	private static uint TryGetDpiForWindow(IntPtr window)
	{
		try
		{
			return GetDpiForWindow(window);
		}
		catch (EntryPointNotFoundException)
		{
			return 0;
		}
	}

	[DllImport(
		"glfw3",
		CallingConvention = CallingConvention.Cdecl,
		EntryPoint = "glfwGetWin32Window"
	)]
	private static extern IntPtr GetWin32Window(IntPtr window);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

	[DllImport("user32.dll")]
	private static extern uint GetDpiForWindow(IntPtr window);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
	private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
	private static extern IntPtr SetWindowLongPtr(
		IntPtr window,
		int index,
		IntPtr value
	);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetWindowPos(
		IntPtr window,
		IntPtr insertAfter,
		int x,
		int y,
		int width,
		int height,
		uint flags
	);
}
