using System;
using System.Numerics;
using Glfw3;

namespace FishGfx.Graphics;

public unsafe sealed partial class RenderWindow
{
	private Glfw.CursorPosFunc cursorPositionCallback;
	private Glfw.KeyFunc keyCallback;
	private Glfw.MouseButtonFunc mouseButtonCallback;
	private Glfw.CharFunc characterCallback;
	private Glfw.ScrollFunc scrollCallback;
	private Glfw.WindowSizeFunc windowSizeCallback;

	private void RegisterCallbacks()
	{
		Vector2 previousMousePosition = Vector2.Zero;
		bool hasPreviousMousePosition = false;

		Glfw.SetCursorPosCallback(
			nativeWindow,
			cursorPositionCallback = (_, x, y) =>
			{
				Vector2 position = new((float)x, (float)y);

				if (!CaptureCursor && !ContainsWindowPoint(position))
				{
					return;
				}

				Vector2 delta = hasPreviousMousePosition
					? previousMousePosition - position
					: Vector2.Zero;

				MousePosition = position;
				MouseMoved?.Invoke(this, new MouseMoveEventArgs(position, delta));

				if (hasPreviousMousePosition)
				{
					MouseDelta?.Invoke(this, new MouseMoveEventArgs(position, delta));
				}

				hasPreviousMousePosition = true;
				previousMousePosition = position;
			}
		);

		Glfw.SetKeyCallback(
			nativeWindow,
			keyCallback = (_, key, scanCode, action, modifiers) =>
			{
				bool isRepeat = action == Glfw.InputState.Repeat;
				bool isPressed = action != Glfw.InputState.Release;
				KeyEventArgs args = new(
					(Key)key,
					scanCode,
					isPressed,
					isRepeat,
					(KeyModifiers)modifiers
				);

				KeyChanged?.Invoke(this, args);
			}
		);

		Glfw.SetMouseButtonCallback(
			nativeWindow,
			mouseButtonCallback = (_, button, action, modifiers) =>
			{
				bool isRepeat = action == Glfw.InputState.Repeat;
				bool isPressed = action != Glfw.InputState.Release;
				MouseButtonEventArgs args = new(
					(MouseButton)button,
					isPressed,
					isRepeat,
					(KeyModifiers)modifiers
				);

				MouseButtonChanged?.Invoke(this, args);
			}
		);

		Glfw.SetCharCallback(
			nativeWindow,
			characterCallback = (_, codepoint) =>
			{
				string text = char.ConvertFromUtf32((int)codepoint);

				TextInput?.Invoke(this, new TextInputEventArgs(text, codepoint));
			}
		);

		Glfw.SetScrollCallback(
			nativeWindow,
			scrollCallback = (_, xOffset, yOffset) =>
			{
				Vector2 offset = new((float)xOffset, (float)yOffset);

				Scrolled?.Invoke(this, new ScrollEventArgs(offset));
			}
		);

		Glfw.SetWindowSizeCallback(
			nativeWindow,
			windowSizeCallback = (_, width, height) =>
			{
				Width = width;
				Height = height;
				Graphics?.ResizeBackbuffer(width, height);
				Resized?.Invoke(this, new WindowResizeEventArgs(width, height));
			}
		);
	}

	private bool ContainsWindowPoint(Vector2 point)
	{
		return point.X >= 0
			&& point.X < Width
			&& point.Y >= 0
			&& point.Y < Height;
	}
}
