using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using OpenGL;
using Glfw3;
using System.Reflection;
using System.Diagnostics;
using FishGfx.System;
using System.Numerics;

namespace FishGfx.Graphics {
	[Flags]
	public enum KeyMods {
		Shift = 1,
		Control = 2,
		Alt = 4,
		Super = 8
	}

	public enum Key {
		Unknown = -1,

		// Printable keys
		Space = 32,
		Apostrophe = 39,  // '
		Comma = 44,  // ,
		Minus = 45,  // -
		Period = 46,  // .
		Slash = 47,  // /
		Alpha0 = 48,
		Alpha1 = 49,
		Alpha2 = 50,
		Alpha3 = 51,
		Alpha4 = 52,
		Alpha5 = 53,
		Alpha6 = 54,
		Alpha7 = 55,
		Alpha8 = 56,
		Alpha9 = 57,
		SemiColon = 59,  // ;
		Equal = 61,  // =
		A = 65,
		B = 66,
		C = 67,
		D = 68,
		E = 69,
		F = 70,
		G = 71,
		H = 72,
		I = 73,
		J = 74,
		K = 75,
		L = 76,
		M = 77,
		N = 78,
		O = 79,
		P = 80,
		Q = 81,
		R = 82,
		S = 83,
		T = 84,
		U = 85,
		V = 86,
		W = 87,
		X = 88,
		Y = 89,
		Z = 90,
		LeftBracket = 91,  // [
		Backslash = 92,  // \
		RightBracket = 93,  // ]
		GraveAccent = 96,  // `
		World1 = 161, // Non-US #1
		World2 = 162, // Non-US #2

		// Function keys
		Escape = 256,
		Enter = 257,
		Tab = 258,
		Backspace = 259,
		Insert = 260,
		Delete = 261,
		Right = 262,
		Left = 263,
		Down = 264,
		Up = 265,
		PageUp = 266,
		PageDown = 267,
		Home = 268,
		End = 269,
		CapsLock = 280,
		ScrollLock = 281,
		NumLock = 282,
		PrintScreen = 283,
		Pause = 284,
		F1 = 290,
		F2 = 291,
		F3 = 292,
		F4 = 293,
		F5 = 294,
		F6 = 295,
		F7 = 296,
		F8 = 297,
		F9 = 298,
		F10 = 299,
		F11 = 300,
		F12 = 301,
		F13 = 302,
		F14 = 303,
		F15 = 304,
		F16 = 305,
		F17 = 306,
		F18 = 307,
		F19 = 308,
		F20 = 309,
		F21 = 310,
		F22 = 311,
		F23 = 312,
		F24 = 313,
		F25 = 314,
		Numpad0 = 320,
		Numpad1 = 321,
		Numpad2 = 322,
		Numpad3 = 323,
		Numpad4 = 324,
		Numpad5 = 325,
		Numpad6 = 326,
		Numpad7 = 327,
		Numpad8 = 328,
		Numpad9 = 329,
		NumpadDecimal = 330,
		NumpadDivide = 331,
		NumpadMultiply = 332,
		NumpadSubtract = 333,
		NumpadAdd = 334,
		NumpadEnter = 335,
		NumpadEqual = 336,
		LeftShift = 340,
		LeftControl = 341,
		LeftAlt = 342,
		LeftSuper = 343,
		RightShift = 344,
		RightControl = 345,
		RightAlt = 346,
		RightSuper = 347,
		Menu = 348,

		// Mouse buttons
		MouseButton1 = 400,
		MouseButton2 = 401,
		MouseButton3 = 402,
		MouseButton4 = 403,
		MouseButton5 = 404,
		MouseButton6 = 405,
		MouseButton7 = 406,
		MouseButton8 = 407,
		MouseLast = MouseButton8,
		MouseLeft = MouseButton1,
		MouseRight = MouseButton2,
		MouseMiddle = MouseButton3
	}

	public delegate void OnMouseMoveFunc(RenderWindow Wnd, float X, float Y);
	public delegate void OnKeyFunc(RenderWindow Wnd, Key Key, int Scancode, bool Pressed, bool Repeat, KeyMods Mods);
	public delegate void OnCharFunc(RenderWindow Wnd, string Char, uint Unicode);

	public unsafe class RenderWindow {
		static int SupportedMajor = 0;
		static int SupportedMinor = 0;

		Glfw.Window Wnd;
		Glfw.CursorPosFunc GlfwOnMouseMove;
		Glfw.KeyFunc GlfwOnKey;
		Glfw.MouseButtonFunc GlfwOnMouseButton;
		Glfw.CharFunc GlfwOnChar;

		public event OnMouseMoveFunc OnMouseMove;
		public event OnMouseMoveFunc OnMouseMoveDelta;
		public event OnKeyFunc OnKey;
		public event OnCharFunc OnChar;

		public Color[] PixelData;
		public int MouseX { get; private set; }
		public int MouseY { get; private set; }

		public Vector2 MousePos {
			get {
				return new Vector2(MouseX, MouseY);
			}
		}

		public bool ShouldClose {
			get {
				return Glfw.WindowShouldClose(Wnd);
			}

			set {
				Glfw.SetWindowShouldClose(Wnd, value);
			}
		}

		static void SetOpenGLHints(int Major, int Minor) {
			Glfw.WindowHint(Glfw.Hint.ClientApi, Glfw.ClientApi.OpenGL);
			Glfw.WindowHint(Glfw.Hint.ContextCreationApi, Glfw.ContextApi.Native);
			Glfw.WindowHint(Glfw.Hint.OpenglProfile, Glfw.OpenGLProfile.Core);
			Glfw.WindowHint(Glfw.Hint.OpenglForwardCompat, false);
#if DEBUG
			Glfw.WindowHint(Glfw.Hint.OpenglDebugContext, true);
#endif
			// TODO: Allow external version select

			Glfw.WindowHint(Glfw.Hint.Doublebuffer, true);
			Glfw.WindowHint(Glfw.Hint.ContextVersionMajor, Major);
			Glfw.WindowHint(Glfw.Hint.ContextVersionMinor, Minor);
			Glfw.WindowHint(Glfw.Hint.Samples, 0);
		}

		Glfw.Window TryCreateWindow(int Major, int Minor, int W, int H, string Title) {
			SetOpenGLHints(Major, Minor);
			return Glfw.CreateWindow(W, H, Title);
		}

		public RenderWindow(int Width, int Height, string Title, bool Resizable = false, bool CenterWindow = true) {
			Internal_OpenGL.InitGLFW();
			Glfw.WindowHint(Glfw.Hint.Resizable, Resizable);

			// YOLO
			if (SupportedMajor != 0 && SupportedMinor != 0 && (Wnd = TryCreateWindow(SupportedMajor, SupportedMinor, Width, Height, Title))) {
			} else if (Wnd = TryCreateWindow(SupportedMajor = 4, SupportedMinor = 6, Width, Height, Title)) {
			} else if (Wnd = TryCreateWindow(SupportedMajor = 4, SupportedMinor = 5, Width, Height, Title)) {
			} else if (Wnd = TryCreateWindow(SupportedMajor = 4, SupportedMinor = 4, Width, Height, Title)) {
			} else if (Wnd = TryCreateWindow(SupportedMajor = 4, SupportedMinor = 3, Width, Height, Title)) {
			} else if (Wnd = TryCreateWindow(SupportedMajor = 4, SupportedMinor = 2, Width, Height, Title)) {
			} else if (Wnd = TryCreateWindow(SupportedMajor = 4, SupportedMinor = 1, Width, Height, Title)) {
			} else if (Wnd = TryCreateWindow(SupportedMajor = 4, SupportedMinor = 0, Width, Height, Title)) {
			} else
				throw new Exception("Could not create any supported OpenGL context");


			if (CenterWindow)
				Center();

			{
				float OldMouseX = 0, OldMouseY = 0;
				bool MouseDeltaInitialized = false;

				Glfw.SetCursorPosCallback(Wnd, GlfwOnMouseMove = (W, X, Y) => {
					MouseX = (int)X;
					MouseY = (int)Y;
					OnMouseMove?.Invoke(this, (float)X, (float)Y);

					if (MouseDeltaInitialized)
						OnMouseMoveDelta?.Invoke(this, OldMouseX - (float)X, OldMouseY - (float)Y);
					else
						MouseDeltaInitialized = true;

					OldMouseX = (float)X;
					OldMouseY = (float)Y;
				});
			}

			Glfw.SetKeyCallback(Wnd, GlfwOnKey = (Wnd, Key, Scancode, Action, Mods) => {
				if (OnKey != null) {
					bool IsPressed = Action == Glfw.InputState.Press || Action == Glfw.InputState.Repeat ? true : false;
					bool IsRepeat = Action == Glfw.InputState.Repeat;
					OnKey(this, (Key)Key, Scancode, IsPressed, IsRepeat, (KeyMods)Mods);
				}
			});

			Glfw.SetMouseButtonCallback(Wnd, GlfwOnMouseButton = (Wnd, Button, State, Mods) => {
				if (OnKey != null) {
					bool IsPressed = State == Glfw.InputState.Press || State == Glfw.InputState.Repeat ? true : false;
					bool IsRepeat = State == Glfw.InputState.Repeat;
					OnKey(this, Key.MouseButton1 + (int)Button, -1, IsPressed, IsRepeat, (KeyMods)Mods);
				}
			});


			Glfw.SetCharCallback(Wnd, GlfwOnChar = (Wnd, Unicode) => {
				OnChar?.Invoke(this, ((char)Unicode).ToString(), Unicode);
			});

			MakeCurrent();
		}

		public void MakeCurrent() {
			Internal_OpenGL.InitOpenGL();
			Glfw.MakeContextCurrent(Wnd);
			Internal_OpenGL.SetupOpenGL();
			Internal_OpenGL.ResetGLState();
		}

		public void SwapBuffers() {
			RenderAPI.CollectGarbage();
			Glfw.SwapBuffers(Wnd);
		}

		public void ReadPixels() {
			GetWindowSize(out int W, out int H);

			if (PixelData == null)
				PixelData = new Color[W * H];

			if (PixelData.Length < W * H)
				PixelData = new Color[W * H];

			fixed (Color* ClrPtr = PixelData)
				Gl.ReadPixels(0, 0, W, H, PixelFormat.Rgba, PixelType.UnsignedByte, (IntPtr)ClrPtr);
		}

		public Color GetPixel(int X, int Y) {
			GetWindowSize(out int W, out int H);
			int Idx = (H - Y - 1) * W + X;

			if (Idx < 0 || Idx >= PixelData.Length)
				return Color.Black;

			return PixelData[Idx];
		}

		public void Close() {
			ShouldClose = true;
			Glfw.DestroyWindow(Wnd);
		}

		public void GetWindowSize(out int Width, out int Height) {
			Glfw.GetWindowSize(Wnd, out Width, out Height);
		}

		public Vector2 GetWindowSizeVec() {
			GetWindowSize(out int W, out int H);
			return new Vector2(W, H);
		}

		public void Center() {
			RenderAPI.GetDesktopResolution(out int W, out int H);
			GetWindowSize(out int WW, out int WH);

			int X = W / 2 - WW / 2;
			int Y = H / 2 - WH / 2;

			Glfw.SetWindowPos(Wnd, X, Y);
		}
	}
}
