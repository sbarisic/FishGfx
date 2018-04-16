using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using FishGfx.Graphics;
using FishGfx.System;

namespace Test {
	class Program {
		static void Main(string[] args) {
			RenderWindow RWind = new RenderWindow(800, 600, "FishGfx Test");
			
			while (!RWind.ShouldClose) {
				RWind.MakeCurrent();
				Gfx.Clear();

				RWind.SwapBuffers();
				Events.Poll();
			}
		}
	}
}
