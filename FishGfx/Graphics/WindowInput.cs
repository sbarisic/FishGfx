using System;
using System.Numerics;

namespace FishGfx.Graphics;

[Flags]
public enum KeyModifiers
{
	None = 0,
	Shift = 1,
	Control = 2,
	Alt = 4,
	Super = 8,
}

public enum Key
{
	Unknown = -1,
	Space = 32,
	Apostrophe = 39,
	Comma = 44,
	Minus = 45,
	Period = 46,
	Slash = 47,
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
	Semicolon = 59,
	Equal = 61,
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
	LeftBracket = 91,
	Backslash = 92,
	RightBracket = 93,
	GraveAccent = 96,
	World1 = 161,
	World2 = 162,
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
}

public enum MouseButton
{
	Button1 = 0,
	Button2 = 1,
	Button3 = 2,
	Button4 = 3,
	Button5 = 4,
	Button6 = 5,
	Button7 = 6,
	Button8 = 7,
	Left = Button1,
	Right = Button2,
	Middle = Button3,
}

public sealed class KeyEventArgs : EventArgs
{
	internal KeyEventArgs(
		Key key,
		int scanCode,
		bool isPressed,
		bool isRepeat,
		KeyModifiers modifiers
	)
	{
		Key = key;
		ScanCode = scanCode;
		IsPressed = isPressed;
		IsRepeat = isRepeat;
		Modifiers = modifiers;
	}

	public Key Key { get; }

	public int ScanCode { get; }

	public bool IsPressed { get; }

	public bool IsRepeat { get; }

	public KeyModifiers Modifiers { get; }
}

public sealed class MouseButtonEventArgs : EventArgs
{
	internal MouseButtonEventArgs(
		MouseButton button,
		bool isPressed,
		bool isRepeat,
		KeyModifiers modifiers
	)
	{
		Button = button;
		IsPressed = isPressed;
		IsRepeat = isRepeat;
		Modifiers = modifiers;
	}

	public MouseButton Button { get; }

	public bool IsPressed { get; }

	public bool IsRepeat { get; }

	public KeyModifiers Modifiers { get; }
}

public sealed class MouseMoveEventArgs : EventArgs
{
	internal MouseMoveEventArgs(Vector2 position, Vector2 delta)
	{
		Position = position;
		Delta = delta;
	}

	public Vector2 Position { get; }

	public Vector2 Delta { get; }
}

public sealed class TextInputEventArgs : EventArgs
{
	internal TextInputEventArgs(string text, uint codepoint)
	{
		Text = text;
		Codepoint = codepoint;
	}

	public string Text { get; }

	public uint Codepoint { get; }
}

public sealed class ScrollEventArgs : EventArgs
{
	internal ScrollEventArgs(Vector2 offset)
	{
		Offset = offset;
	}

	public Vector2 Offset { get; }
}

public sealed class WindowResizeEventArgs : EventArgs
{
	internal WindowResizeEventArgs(int width, int height)
	{
		Width = width;
		Height = height;
	}

	public int Width { get; }

	public int Height { get; }

	public Vector2 Size => new(Width, Height);
}
