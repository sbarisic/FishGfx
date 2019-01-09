using FishGfx;
using FishGfx.Formats;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using FishGfx_Nuklear;
using NuklearDotNet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Test_Nuklear {
	class Program {
		static void Main(string[] args) {
			Run();
		}

		static FishGfxDevice Device;
		static RenderWindow Window;
		static Camera Cam;

		static void Run() {
			Vector2 Size = RenderWindow.GetDesktopResolution() * 0.9f;
			Window = new RenderWindow((int)Size.X, (int)Size.Y, "FishGfx Nuklear Test");

#if DEBUG
			Console.WriteLine("Running {0}", RenderAPI.Version);
			Console.WriteLine(RenderAPI.Renderer);
			//File.WriteAllLines("gl_extensions.txt", RenderAPI.Extensions);
#endif

			Cam = ShaderUniforms.Default.Camera;
			Cam.SetOrthogonal(0, 0, Size.X, Size.Y);

			Stopwatch SWatch = Stopwatch.StartNew();
			float Dt = 0;

			Device = new FishGfxDevice(Size, new ShaderProgram(new ShaderStage(ShaderType.VertexShader, "data/shaders/gui.vert"), new ShaderStage(ShaderType.FragmentShader, "data/shaders/gui.frag")));
			Device.RegisterEvents(Window);
			NuklearAPI.Init(Device);

			while (!Window.ShouldClose) {
				while (SWatch.ElapsedMilliseconds / 1000.0f < (1.0f / 60))
					;

				Dt = SWatch.ElapsedMilliseconds / 1000.0f;
				SWatch.Restart();

				Gfx.Clear();
				{
					NuklearAPI.Frame(NuklearUpdate);
				}
				Update(Dt);
				Window.SwapBuffers();
				Events.Poll();
			}
		}

		static void Update(float Dt) {

		}

		static void NuklearUpdate() {
			const NkPanelFlags Flags = NkPanelFlags.BorderTitle | NkPanelFlags.MovableScalable | NkPanelFlags.Minimizable | NkPanelFlags.ScrollAutoHide;

			NuklearAPI.Window("Test Window", 100, 100, 200, 200, Flags, () => {
				NuklearAPI.LayoutRowDynamic(35);

				for (int i = 0; i < 100; i++) {
					if (NuklearAPI.ButtonLabel("Some Button #" + i))
						Console.WriteLine("You pressed Some Button #" + i);
				}
			});
		}
	}
}
