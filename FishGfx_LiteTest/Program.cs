using FishGfx;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx_LiteTest {
	class Program {
		static void Main(string[] args) {
			Vector2 WindowSize = RenderWindow.GetDesktopResolution() * 0.9f;
			RenderWindow RWind = new RenderWindow((int)WindowSize.X, (int)WindowSize.Y, "Test Lite");
			
			while (!RWind.ShouldClose) {
				Gfx.Clear();

				RWind.SwapBuffers();
				Events.Poll();
			}
		}
	}
}
