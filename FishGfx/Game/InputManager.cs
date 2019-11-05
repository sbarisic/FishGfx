using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FishGfx;
using System.Numerics;
using FishGfx.Graphics;

namespace FishGfx.Game {
	public class InputManager {
		Key[] AllKeys;

		Dictionary<Key, bool> KeysPressed = new Dictionary<Key, bool>();
		Dictionary<Key, bool> KeysReleased = new Dictionary<Key, bool>();
		Dictionary<Key, bool> KeysDown = new Dictionary<Key, bool>();

		float MouseX;
		float MouseY;

		RenderWindow Window;

		public InputManager(RenderWindow Window) {
			AllKeys = (Key[])Enum.GetValues(typeof(Key));
			this.Window = Window;

			foreach (var K in AllKeys)
				KeysDown[K] = false;

			Window.OnKey += Window_OnKey;
			Window.OnMouseMove += Window_OnMouseMove;
		}

		private void Window_OnMouseMove(RenderWindow Wnd, float X, float Y) {
			MouseX = X;
			MouseY = Y;
		}

		private void Window_OnKey(RenderWindow Wnd, Key Key, int Scancode, bool Pressed, bool Repeat, KeyMods Mods) {
			if (Repeat)
				return;

			KeysDown[Key] = Pressed;

			if (Pressed)
				KeysPressed[Key] = true;
			else
				KeysReleased[Key] = true;
		}

		public Vector2 GetMousePos() {
			return new Vector2(MouseX, MouseY);
		}

		public Vector2 GetMousePosNormal() {
			return GetMousePos() / Window.WindowSize;
		}

		public void BeginNewFrame() {
			for (int i = 0; i < AllKeys.Length; i++) {
				KeysPressed[AllKeys[i]] = false;
				KeysReleased[AllKeys[i]] = false;
			}
		}

		public bool GetKeyDown(Key K) {
			return KeysDown[K];
		}

		public bool GetKeyPressed(Key K) {
			return KeysPressed[K];
		}

		public bool GetKeyReleased(Key K) {
			return KeysReleased[K];
		}
	}
}
