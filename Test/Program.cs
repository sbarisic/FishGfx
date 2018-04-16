using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

using FishGfx;
using FishGfx.Graphics;
using FishGfx.System;

namespace Test {
	class Program {
		static void Main(string[] args) {
			RenderWindow RWind = new RenderWindow(800, 600, "FishGfx Test");
			RWind.MakeCurrent();

			while (!RWind.ShouldClose) {
				Gfx.Clear();
				Gfx.Line(new Vector2(0, 0), new Vector2(100, 100));

				RWind.SwapBuffers();
				Events.Poll();
			}
		}
	}
}
