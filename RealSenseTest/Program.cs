using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FishGfx;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using FishGfx.System;
using FishGfx.RealSense;

namespace RealSenseTest {
	class Program {
		static void Main(string[] args) {
			RenderWindow RWind = new RenderWindow(800, 600, "RealSense Test");

			RealSenseCamera.QueryResolution();
			RealSenseCamera.Start();


			while (!RWind.ShouldClose) {
				Gfx.Clear(Color.Black);

				RealSenseCamera.PollForFrames(OnFrameData);

				RWind.SwapBuffers();
				Events.Poll();
			}
		}

		static void OnFrameData(FrameData[] Frames) {

		}
	}
}
