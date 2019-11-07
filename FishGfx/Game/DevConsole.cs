using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FishGfx;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;

namespace FishGfx.Game {
	public class DevConsole {
		Tilemap Tiles;

		int CursorX;
		int CursorY;

		char[] CharBuffer;

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

		public DevConsole(Texture FontTileset, int Size, int Width, int Height, int BufferHeight, ShaderProgram DrawShader) {
			Tiles = new Tilemap(Size, Width, Height, FontTileset);
			Tiles.Shader = DrawShader;
			Tiles.ClearTiles('0');

			this.BufferHeight = BufferHeight;
			BufferWidth = Width;
			CursorX = 0;
			CursorY = BufferHeight - 1;
			ViewScroll = 0;
			CharSize = Size;

			CharBuffer = new char[Width * BufferHeight];
			Dirty = true;
		}

		void CheckScroll() {
			if (CursorY >= BufferHeight)
				CursorY = BufferHeight - 1;

			if (CursorY < 0) {
				CursorY = 0;

				// TODO
				for (int X = 0; X < BufferWidth; X++) {
					char CC = GetChar(X, CursorY - 1);
					SetChar(X, CursorY, CC);
					SetChar(X, CursorY - 1, (char)0);
				}
			}

			return;

			if (CursorY >= BufferHeight) {
				CursorY--;
				// TODO: Scroll

				/*for (int X = 0; X < BufferWidth; X++) {
					char CC = GetChar(X, CursorY - 1);
					SetChar(X, CursorY, CC);
					SetChar(X, CursorY - 1, (char)0);
				}*/
			}
		}

		void SetChar(int X, int Y, char C) {
			Y = BufferHeight - Y - 1;

			CharBuffer[Y * BufferWidth + X] = C;
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

		void NewLine() {
			CursorX = 0;
			CursorY--;
			CheckScroll();
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

		public void PutChar(char Chr) {
			if (Chr == '\n') {
				NewLine();
				return;
			}

			//Tiles.SetTile((int)Chr, CursorX++, Height - CursorY - 1);
			SetChar(CursorX++, CursorY, Chr);

			if (CursorX >= BufferWidth) {
				CursorX = 0;
				CursorY++;
			}

			CheckScroll();
		}

		public void Print(string Str) {
			foreach (var C in Str)
				PutChar(C);
		}

		public void PrintLine(string Str) {
			Print(Str + "\n");
		}

		void Refresh() {
			for (int Y = 0; Y < Tiles.Height; Y++) {
				for (int X = 0; X < Tiles.Width; X++) {
					Tiles.SetTile(X, Y, GetChar(X, Y + ViewScroll));
				}
			}
		}

		public void Draw() {
			if (Dirty) {
				Dirty = false;
				Refresh();
			}

			Tiles.Draw();
		}
	}
}
