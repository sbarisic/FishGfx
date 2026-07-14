using System;
using System.Collections.Generic;
using FishGfx.Graphics;

namespace FishGfx.FishUI;

internal sealed class FishUIInputState
{
	private readonly HashSet<global::FishUI.FishKey> keysDown = new();
	private readonly HashSet<global::FishUI.FishKey> keysPressed = new();
	private readonly HashSet<global::FishUI.FishKey> keysReleased = new();
	private readonly HashSet<global::FishUI.FishMouseButton> mouseButtonsDown = new();
	private readonly HashSet<global::FishUI.FishMouseButton> mouseButtonsPressed = new();
	private readonly HashSet<global::FishUI.FishMouseButton> mouseButtonsReleased = new();
	private readonly Queue<global::FishUI.FishKey> keyQueue = new();
	private readonly Queue<int> characterQueue = new();
	private bool enabled;

	internal bool Enabled
	{
		get => enabled;
		set
		{
			enabled = value;

			if (!value)
			{
				ClearTransient();
			}
		}
	}

	internal float MouseWheel { get; private set; }

	internal void BeginFrame()
	{
		ClearTransient();
	}

	internal void OnKey(Key key, bool isPressed, bool isRepeat)
	{
		if (!FishUIInputAdapter.TryMapKey(key, out global::FishUI.FishKey fishKey))
		{
			return;
		}

		if (isPressed)
		{
			bool firstPress = keysDown.Add(fishKey);

			if (enabled && (firstPress || isRepeat))
			{
				if (firstPress)
				{
					keysPressed.Add(fishKey);
				}

				keyQueue.Enqueue(fishKey);
			}

			return;
		}

		keysDown.Remove(fishKey);

		if (enabled)
		{
			keysReleased.Add(fishKey);
		}
	}

	internal void OnMouseButton(MouseButton button, bool isPressed, bool isRepeat)
	{
		if (!FishUIInputAdapter.TryMapMouseButton(
			button,
			out global::FishUI.FishMouseButton fishButton
		))
		{
			return;
		}

		if (isPressed)
		{
			bool firstPress = mouseButtonsDown.Add(fishButton);

			if (enabled && firstPress && !isRepeat)
			{
				mouseButtonsPressed.Add(fishButton);
			}

			return;
		}

		mouseButtonsDown.Remove(fishButton);

		if (enabled)
		{
			mouseButtonsReleased.Add(fishButton);
		}
	}

	internal void OnCharacter(uint codepoint)
	{
		if (enabled && codepoint != 0)
		{
			characterQueue.Enqueue((int)codepoint);
		}
	}

	internal void OnScroll(float delta)
	{
		if (enabled && float.IsFinite(delta))
		{
			MouseWheel += delta;
		}
	}

	internal global::FishUI.FishKey GetKeyPressed()
	{
		return enabled && keyQueue.Count > 0
			? keyQueue.Dequeue()
			: global::FishUI.FishKey.None;
	}

	internal int GetCharPressed()
	{
		return enabled && characterQueue.Count > 0
			? characterQueue.Dequeue()
			: 0;
	}

	internal bool IsKeyDown(global::FishUI.FishKey key)
	{
		return enabled && keysDown.Contains(key);
	}

	internal bool IsKeyPressed(global::FishUI.FishKey key)
	{
		return enabled && keysPressed.Contains(key);
	}

	internal bool IsKeyReleased(global::FishUI.FishKey key)
	{
		return enabled && keysReleased.Contains(key);
	}

	internal bool IsMouseDown(global::FishUI.FishMouseButton button)
	{
		return enabled && mouseButtonsDown.Contains(button);
	}

	internal bool IsMousePressed(global::FishUI.FishMouseButton button)
	{
		return enabled && mouseButtonsPressed.Contains(button);
	}

	internal bool IsMouseReleased(global::FishUI.FishMouseButton button)
	{
		return enabled && mouseButtonsReleased.Contains(button);
	}

	private void ClearTransient()
	{
		keysPressed.Clear();
		keysReleased.Clear();
		mouseButtonsPressed.Clear();
		mouseButtonsReleased.Clear();
		keyQueue.Clear();
		characterQueue.Clear();
		MouseWheel = 0;
	}
}
