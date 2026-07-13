using System;
using System.Collections.Generic;
using System.Numerics;
using FishGfx.Graphics;

namespace FishGfx.FishUI
{
	/// <summary>FishUI input backend driven by <see cref="RenderWindow"/> events.</summary>
	public sealed class FishGfxFishUIInput : global::FishUI.IFishUIInput, IDisposable
	{
		private readonly RenderWindow window;
		private readonly FishUIInputState state = new FishUIInputState();
		private bool disposed;

		public FishGfxFishUIInput(RenderWindow window)
		{
			this.window = window ?? throw new ArgumentNullException(nameof(window));
			window.OnKey += OnKey;
			window.OnChar += OnChar;
			window.OnScroll += OnScroll;
		}

		public bool Enabled
		{
			get => state.Enabled;
			set => state.Enabled = value;
		}

		/// <summary>Clears one-frame transitions before the application polls window events.</summary>
		public void BeginFrame() => state.BeginFrame();

		public global::FishUI.FishKey GetKeyPressed() => state.GetKeyPressed();
		public int GetCharPressed() => state.GetCharPressed();
		public bool IsKeyDown(global::FishUI.FishKey key) => state.IsKeyDown(key);
		public bool IsKeyUp(global::FishUI.FishKey key) => !IsKeyDown(key);
		public bool IsKeyPressed(global::FishUI.FishKey key) => state.IsKeyPressed(key);
		public bool IsKeyReleased(global::FishUI.FishKey key) => state.IsKeyReleased(key);
		public Vector2 GetMousePosition() => Enabled ? window.MousePos : new Vector2(-1, -1);
		public float GetMouseWheelMove() => state.MouseWheel;
		public global::FishUI.FishTouchPoint[] GetTouchPoints() => Array.Empty<global::FishUI.FishTouchPoint>();
		public bool IsMouseDown(global::FishUI.FishMouseButton button) => state.IsMouseDown(button);
		public bool IsMouseUp(global::FishUI.FishMouseButton button) => !IsMouseDown(button);
		public bool IsMousePressed(global::FishUI.FishMouseButton button) => state.IsMousePressed(button);
		public bool IsMouseReleased(global::FishUI.FishMouseButton button) => state.IsMouseReleased(button);
		public string GetClipboardText() => window.ClipboardString ?? string.Empty;
		public void SetClipboardText(string text) => window.ClipboardString = text ?? string.Empty;

		public static bool TryMapKey(Key key, out global::FishUI.FishKey fishKey)
		{
			int value = (int)key;
			if (value >= (int)Key.MouseButton1)
			{
				fishKey = global::FishUI.FishKey.None;
				return false;
			}
			fishKey = (global::FishUI.FishKey)value;
			return Enum.IsDefined(typeof(global::FishUI.FishKey), fishKey) && fishKey != global::FishUI.FishKey.None;
		}

		public static bool TryMapMouseButton(Key key, out global::FishUI.FishMouseButton button)
		{
			int index = (int)key - (int)Key.MouseButton1;
			if (index >= 0 && index <= (int)global::FishUI.FishMouseButton.Back)
			{
				button = (global::FishUI.FishMouseButton)index;
				return true;
			}
			button = default;
			return false;
		}

		private void OnKey(RenderWindow sender, Key key, int scancode, bool pressed, bool repeat, KeyMods modifiers)
		{
			state.OnKey(key, pressed, repeat);
		}

		private void OnChar(RenderWindow sender, string character, uint unicode) => state.OnChar(unicode);
		private void OnScroll(RenderWindow sender, float x, float y) => state.OnScroll(y);

		public void Dispose()
		{
			if (disposed)
				return;
			disposed = true;
			window.OnKey -= OnKey;
			window.OnChar -= OnChar;
			window.OnScroll -= OnScroll;
			state.Enabled = false;
		}
	}

	internal sealed class FishUIInputState
	{
		private readonly HashSet<global::FishUI.FishKey> keysDown = new HashSet<global::FishUI.FishKey>();
		private readonly HashSet<global::FishUI.FishKey> keysPressed = new HashSet<global::FishUI.FishKey>();
		private readonly HashSet<global::FishUI.FishKey> keysReleased = new HashSet<global::FishUI.FishKey>();
		private readonly HashSet<global::FishUI.FishMouseButton> mouseDown = new HashSet<global::FishUI.FishMouseButton>();
		private readonly HashSet<global::FishUI.FishMouseButton> mousePressed = new HashSet<global::FishUI.FishMouseButton>();
		private readonly HashSet<global::FishUI.FishMouseButton> mouseReleased = new HashSet<global::FishUI.FishMouseButton>();
		private readonly Queue<global::FishUI.FishKey> keyQueue = new Queue<global::FishUI.FishKey>();
		private readonly Queue<int> charQueue = new Queue<int>();
		private bool enabled;

		internal bool Enabled
		{
			get => enabled;
			set
			{
				enabled = value;
				if (!value)
					ClearTransient();
			}
		}

		internal float MouseWheel { get; private set; }

		internal void BeginFrame() => ClearTransient();

		internal void OnKey(Key key, bool pressed, bool repeat)
		{
			if (FishGfxFishUIInput.TryMapMouseButton(key, out global::FishUI.FishMouseButton mouseButton))
			{
				if (pressed)
				{
					bool firstPress = mouseDown.Add(mouseButton);
					if (enabled && firstPress && !repeat)
						mousePressed.Add(mouseButton);
				}
				else
				{
					mouseDown.Remove(mouseButton);
					if (enabled)
						mouseReleased.Add(mouseButton);
				}
				return;
			}

			if (!FishGfxFishUIInput.TryMapKey(key, out global::FishUI.FishKey fishKey))
				return;

			if (pressed)
			{
				bool firstPress = keysDown.Add(fishKey);
				if (enabled && (firstPress || repeat))
				{
					if (firstPress)
						keysPressed.Add(fishKey);
					keyQueue.Enqueue(fishKey);
				}
			}
			else
			{
				keysDown.Remove(fishKey);
				if (enabled)
					keysReleased.Add(fishKey);
			}
		}

		internal void OnChar(uint unicode)
		{
			if (enabled && unicode != 0)
				charQueue.Enqueue((int)unicode);
		}

		internal void OnScroll(float delta)
		{
			if (enabled && float.IsFinite(delta))
				MouseWheel += delta;
		}

		internal global::FishUI.FishKey GetKeyPressed()
		{
			return enabled && keyQueue.Count > 0 ? keyQueue.Dequeue() : global::FishUI.FishKey.None;
		}

		internal int GetCharPressed() => enabled && charQueue.Count > 0 ? charQueue.Dequeue() : 0;
		internal bool IsKeyDown(global::FishUI.FishKey key) => enabled && keysDown.Contains(key);
		internal bool IsKeyPressed(global::FishUI.FishKey key) => enabled && keysPressed.Contains(key);
		internal bool IsKeyReleased(global::FishUI.FishKey key) => enabled && keysReleased.Contains(key);
		internal bool IsMouseDown(global::FishUI.FishMouseButton button) => enabled && mouseDown.Contains(button);
		internal bool IsMousePressed(global::FishUI.FishMouseButton button) => enabled && mousePressed.Contains(button);
		internal bool IsMouseReleased(global::FishUI.FishMouseButton button) => enabled && mouseReleased.Contains(button);

		private void ClearTransient()
		{
			keysPressed.Clear();
			keysReleased.Clear();
			mousePressed.Clear();
			mouseReleased.Clear();
			keyQueue.Clear();
			charQueue.Clear();
			MouseWheel = 0;
		}
	}
}
