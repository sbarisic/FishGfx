using System;
using System.Collections.Generic;
using System.Numerics;
using FishGfx.Graphics;

namespace FishGfx.Game;

public sealed class InputManager : IDisposable
{
	private readonly RenderWindow window;
	private readonly HashSet<Key> keysDown = new();
	private readonly HashSet<Key> keysPressed = new();
	private readonly HashSet<Key> keysReleased = new();
	private readonly HashSet<MouseButton> mouseButtonsDown = new();
	private readonly HashSet<MouseButton> mouseButtonsPressed = new();
	private readonly HashSet<MouseButton> mouseButtonsReleased = new();
	private Vector2 nativeMousePosition;
	private bool disposed;

	public InputManager(RenderWindow window)
	{
		this.window = window ?? throw new ArgumentNullException(nameof(window));
		nativeMousePosition = window.MousePosition;
		window.KeyChanged += HandleKeyChanged;
		window.MouseButtonChanged += HandleMouseButtonChanged;
		window.MouseMoved += HandleMouseMoved;
	}

	public Vector2 MousePosition => new(nativeMousePosition.X, window.Height - nativeMousePosition.Y);

	public Vector2 NormalizedMousePosition
	{
		get
		{
			if (window.Width == 0 || window.Height == 0)
			{
				return Vector2.Zero;
			}

			return MousePosition / window.Size;
		}
	}

	public void BeginFrame()
	{
		ThrowIfDisposed();
		keysPressed.Clear();
		keysReleased.Clear();
		mouseButtonsPressed.Clear();
		mouseButtonsReleased.Clear();
	}

	public bool IsKeyDown(Key key)
	{
		ThrowIfDisposed();

		return keysDown.Contains(key);
	}

	public bool WasKeyPressed(Key key)
	{
		ThrowIfDisposed();

		return keysPressed.Contains(key);
	}

	public bool WasKeyReleased(Key key)
	{
		ThrowIfDisposed();

		return keysReleased.Contains(key);
	}

	public bool IsMouseButtonDown(MouseButton button)
	{
		ThrowIfDisposed();

		return mouseButtonsDown.Contains(button);
	}

	public bool WasMouseButtonPressed(MouseButton button)
	{
		ThrowIfDisposed();

		return mouseButtonsPressed.Contains(button);
	}

	public bool WasMouseButtonReleased(MouseButton button)
	{
		ThrowIfDisposed();

		return mouseButtonsReleased.Contains(button);
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		window.KeyChanged -= HandleKeyChanged;
		window.MouseButtonChanged -= HandleMouseButtonChanged;
		window.MouseMoved -= HandleMouseMoved;
		disposed = true;
	}

	private void HandleKeyChanged(object sender, KeyEventArgs args)
	{
		if (args.IsRepeat)
		{
			return;
		}

		if (args.IsPressed)
		{
			keysDown.Add(args.Key);
			keysPressed.Add(args.Key);
			return;
		}

		keysDown.Remove(args.Key);
		keysReleased.Add(args.Key);
	}

	private void HandleMouseButtonChanged(object sender, MouseButtonEventArgs args)
	{
		if (args.IsRepeat)
		{
			return;
		}

		if (args.IsPressed)
		{
			mouseButtonsDown.Add(args.Button);
			mouseButtonsPressed.Add(args.Button);
			return;
		}

		mouseButtonsDown.Remove(args.Button);
		mouseButtonsReleased.Add(args.Button);
	}

	private void HandleMouseMoved(object sender, MouseMoveEventArgs args)
	{
		nativeMousePosition = args.Position;
	}

	private void ThrowIfDisposed()
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(InputManager));
		}
	}
}
