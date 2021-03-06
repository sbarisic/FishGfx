﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FishGfx;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using System.Numerics;

namespace FishGfx.Game {
	public delegate void ConsoleOnInput(string Input);

	public class DevConsole {
		Tilemap Tiles;

		int CursorX;
		int CursorY;

		int BackspaceCounter;

		char[] CharBuffer;
		Color[] ColorBuffer;

		StringBuilder InputBuffer;
		bool AwaitingInput;

		public event ConsoleOnInput OnInput;

		public int Width {
			get {
				return Tiles.Width;
			}
		}

		public int Height {
			get {
				return Tiles.Height;
			}
		}

		public int BufferHeight {
			get;
			private set;
		}

		public int BufferWidth {
			get;
			private set;
		}

		int ViewScroll;
		bool Dirty;

		public int CharSize {
			get;
			private set;
		}

		public Vector2 Position {
			get {
				return Tiles.Position;
			}

			set {
				Tiles.Position = value;
			}
		}

		public Color TextColor;
		public bool Enabled;

		public DevConsole(Texture FontTileset, int Size, int Width, int Height, int BufferHeight, ShaderProgram DrawShader) {
			Tiles = new Tilemap(Size, Width, Height, FontTileset);
			Tiles.Shader = DrawShader;
			Tiles.ClearTiles('0');

			InputBuffer = new StringBuilder();
			AwaitingInput = false;

			this.BufferHeight = BufferHeight;
			BufferWidth = Width;
			CursorX = 0;
			//CursorY = BufferHeight - 1;
			CursorY = 0;

			ViewScroll = 0;
			CharSize = Size;

			TextColor = Color.White;

			CharBuffer = new char[Width * BufferHeight];
			ColorBuffer = new Color[CharBuffer.Length];
			Dirty = true;
			Enabled = true;
		}

		void CheckScroll() {
			if (CursorY >= BufferHeight)
				CursorY = BufferHeight - 1;

			if (CursorY < 0) {
				CursorY = 0;

				for (int Y = BufferHeight - 2; Y >= 0; Y--)
					for (int X = 0; X < BufferWidth; X++) {
						char CC = GetChar(X, Y);
						Color CClr = GetColor(X, Y);

						SetChar(X, Y + 1, CC, CClr);
						SetChar(X, Y, (char)0, Color.White);
					}
			}
		}

		void NewLine() {
			CursorX = 0;
			CursorY--;
			CheckScroll();
		}

		void CursorForward() {
			CursorX++;

			if (CursorX >= BufferWidth)
				NewLine();

			CheckScroll();
		}

		void CursorBackward() {
			CursorX--;

			if (CursorX < 0) {
				CursorX = BufferWidth - 1;
				CursorY++;
			}

			CheckScroll();
		}

		void SetChar(int X, int Y, char C, Color Clr) {
			Y = BufferHeight - Y - 1;

			CharBuffer[Y * BufferWidth + X] = C;
			ColorBuffer[Y * BufferWidth + X] = Clr;
			Dirty = true;
		}

		char GetChar(int X, int Y) {
			Y = BufferHeight - Y - 1;

			if (X < 0 || X >= BufferWidth)
				throw new Exception("X out of range");

			if (Y < 0 || Y >= BufferHeight)
				throw new Exception("Y out of range");

			return CharBuffer[Y * BufferWidth + X];
		}

		Color GetColor(int X, int Y) {
			Y = BufferHeight - Y - 1;

			if (X < 0 || X >= BufferWidth)
				throw new Exception("X out of range");

			if (Y < 0 || Y >= BufferHeight)
				throw new Exception("Y out of range");

			return ColorBuffer[Y * BufferWidth + X];
		}

		void Backspace() {
			if (BackspaceCounter <= 0)
				return;

			BackspaceCounter--;

			if (AwaitingInput)
				InputBuffer.Length--;

			CursorBackward();
			SetChar(CursorX, CursorY, (char)0, Color.White);
		}

		public void OnCommand(string Cmd) {
			if (Cmd.Length != 0) {
				OnInput?.Invoke(Cmd);
			}

			//PrintLine(string.Format("You entered '{0}'", Cmd));
			BeginInput();
		}

		public void ResetBackspaceCounter() {
			BackspaceCounter = 0;
		}

		public int GetViewScroll() {
			return ViewScroll;
		}

		public void SetViewScroll(int Scroll) {
			if (Tiles.Height + Scroll > BufferHeight)
				return;

			if (Scroll < 0)
				return;

			ViewScroll = Scroll;
			Dirty = true;
		}

		public void BeginInput() {
			Print("> ");

			ResetBackspaceCounter();
			InputBuffer.Clear();

			AwaitingInput = true;
		}

		public void PutChar(char Chr) {
			if (Chr == '\b') {
				Backspace();
				return;
			}

			if (Chr == '\n') {
				NewLine();

				if (AwaitingInput) {
					AwaitingInput = false;
					OnCommand(InputBuffer.ToString());
				}

				return;
			}

			if (AwaitingInput)
				InputBuffer.Append(Chr);

			BackspaceCounter++;
			SetChar(CursorX, CursorY, Chr, TextColor);
			CursorForward();
		}

		public void Print(string Str) {
			foreach (var C in Str)
				PutChar(C);
		}

		public void PrintLine(string Str = "") {
			Print(Str + "\n");
		}

		public void PrintLine(string Fmt, params object[] Args) {
			PrintLine(string.Format(Fmt, Args));
		}

		void Refresh() {
			for (int Y = 0; Y < Tiles.Height; Y++) {
				for (int X = 0; X < Tiles.Width; X++) {
					char Chr = GetChar(X, Y + ViewScroll);
					Color Clr = GetColor(X, Y + ViewScroll);

					Tiles.SetTile(X, Y, Chr, Clr);
				}
			}
		}

		public void SendKey(RenderWindow Wnd, Key Key, int Scancode, bool Pressed, bool Repeat, KeyMods Mods) {
			if (!Pressed)
				return;

			// TODO: Handle somewhere else
			if (Key == Key.F1)
				Enabled = !Enabled;

			if (!Enabled)
				return;

			if (Key == Key.Enter || Key == Key.NumpadEnter)
				PutChar('\n');

			if (Key == Key.Backspace)
				PutChar('\b');

			if (Key == Key.Up)
				SetViewScroll(GetViewScroll() + 1);

			if (Key == Key.Down)
				SetViewScroll(GetViewScroll() - 1);
		}

		public void SendInput(string Str) {
			if (!Enabled)
				return;

			if (!AwaitingInput)
				return;

			Print(Str);
		}

		public void Draw() {
			if (!Enabled)
				return;

			if (Dirty) {
				Dirty = false;
				Refresh();
			}

			Gfx.FilledRectangle(Position.X, Position.Y, CharSize * Width, CharSize * Height, Color.Coal);
			Tiles.Draw();
		}
	}
}
