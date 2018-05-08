using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using FishGfx;
using FishGfx.Graphics;
using FishGfx.System;

namespace VectorPFM {
	class Program {
		static RenderWindow RWind;

		static void Main(string[] args) {
			const float Scale = 0.9f;

			RenderAPI.GetDesktopResolution(out int W, out int H);
			RWind = new RenderWindow((int)(W * Scale), (int)(H * Scale), "Vector PFM");

			while (!RWind.ShouldClose) {
				Gfx.Clear();



				RWind.SwapBuffers();
				Events.Poll();
			}
		}
	}
}
