using System;
using System.Numerics;
using FishGfx.Graphics;

namespace FishGfx.FishUI;

public sealed class FishUIInputAdapter : global::FishUI.IFishUIInput, IDisposable
{
	private readonly RenderWindow window;
	private readonly FishUIInputState state = new();
	private bool disposed;

	public FishUIInputAdapter(RenderWindow window)
	{
		this.window = window ?? throw new ArgumentNullException(nameof(window));
		window.KeyChanged += HandleKeyChanged;
		window.MouseButtonChanged += HandleMouseButtonChanged;
		window.TextInput += HandleTextInput;
		window.Scrolled += HandleScroll;
	}

	public bool Enabled
	{
		get => state.Enabled;
		set => state.Enabled = value;
	}

	public void BeginFrame()
	{
		state.BeginFrame();
	}

	public global::FishUI.FishKey GetKeyPressed()
	{
		return state.GetKeyPressed();
	}

	public int GetCharPressed()
	{
		return state.GetCharPressed();
	}

	public bool IsKeyDown(global::FishUI.FishKey key)
	{
		return state.IsKeyDown(key);
	}

	public bool IsKeyUp(global::FishUI.FishKey key)
	{
		return !IsKeyDown(key);
	}

	public bool IsKeyPressed(global::FishUI.FishKey key)
	{
		return state.IsKeyPressed(key);
	}

	public bool IsKeyReleased(global::FishUI.FishKey key)
	{
		return state.IsKeyReleased(key);
	}

	public Vector2 GetMousePosition()
	{
		return Enabled ? window.MousePosition : new Vector2(-1);
	}

	public float GetMouseWheelMove()
	{
		return state.MouseWheel;
	}

	public global::FishUI.FishTouchPoint[] GetTouchPoints()
	{
		return Array.Empty<global::FishUI.FishTouchPoint>();
	}

	public bool IsMouseDown(global::FishUI.FishMouseButton button)
	{
		return state.IsMouseDown(button);
	}

	public bool IsMouseUp(global::FishUI.FishMouseButton button)
	{
		return !IsMouseDown(button);
	}

	public bool IsMousePressed(global::FishUI.FishMouseButton button)
	{
		return state.IsMousePressed(button);
	}

	public bool IsMouseReleased(global::FishUI.FishMouseButton button)
	{
		return state.IsMouseReleased(button);
	}

	public string GetClipboardText()
	{
		return window.ClipboardText ?? string.Empty;
	}

	public void SetClipboardText(string text)
	{
		window.ClipboardText = text ?? string.Empty;
	}

	public static bool TryMapKey(Key key, out global::FishUI.FishKey fishKey)
	{
		fishKey = (global::FishUI.FishKey)(int)key;

		return fishKey != global::FishUI.FishKey.None
			&& Enum.IsDefined(typeof(global::FishUI.FishKey), fishKey);
	}

	public static bool TryMapMouseButton(
		MouseButton mouseButton,
		out global::FishUI.FishMouseButton fishButton
	)
	{
		int value = (int)mouseButton;

		if (value < 0 || value > (int)global::FishUI.FishMouseButton.Back)
		{
			fishButton = default;
			return false;
		}

		fishButton = (global::FishUI.FishMouseButton)value;

		return true;
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		window.KeyChanged -= HandleKeyChanged;
		window.MouseButtonChanged -= HandleMouseButtonChanged;
		window.TextInput -= HandleTextInput;
		window.Scrolled -= HandleScroll;
		state.Enabled = false;
		disposed = true;
	}

	private void HandleKeyChanged(object sender, KeyEventArgs args)
	{
		state.OnKey(args.Key, args.IsPressed, args.IsRepeat);
	}

	private void HandleMouseButtonChanged(object sender, MouseButtonEventArgs args)
	{
		state.OnMouseButton(args.Button, args.IsPressed, args.IsRepeat);
	}

	private void HandleTextInput(object sender, TextInputEventArgs args)
	{
		state.OnCharacter(args.Codepoint);
	}

	private void HandleScroll(object sender, ScrollEventArgs args)
	{
		state.OnScroll(args.Offset.Y);
	}
}
