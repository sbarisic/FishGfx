using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Glfw3;
using System.Reflection;

namespace FishGfx.Graphics {
	public class RenderWindow {
		static RenderWindow() {
			Glfw.ConfigureNativesDirectory(Path.GetFullPath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)));

			if (!Glfw.Init())
				throw new Exception("Could not initialize glfw");
		}

		Glfw.Window Wnd;

		public bool ShouldClose {
			get {
				return Glfw.WindowShouldClose(Wnd);
			}

			set {
				Glfw.SetWindowShouldClose(Wnd, value);
			}
		}

		static void SetOpenGLHints() {
			Glfw.WindowHint(Glfw.Hint.ClientApi, Glfw.ClientApi.OpenGL);
			Glfw.WindowHint(Glfw.Hint.ContextCreationApi, Glfw.ContextApi.Native);
			Glfw.WindowHint(Glfw.Hint.OpenglProfile, Glfw.OpenGLProfile.Core);
			Glfw.WindowHint(Glfw.Hint.OpenglForwardCompat, true);
#if DEBUG
			Glfw.WindowHint(Glfw.Hint.OpenglDebugContext, true);
#endif

			Glfw.WindowHint(Glfw.Hint.Doublebuffer, true);
			Glfw.WindowHint(Glfw.Hint.ContextVersionMajor, 4);
			Glfw.WindowHint(Glfw.Hint.ContextVersionMinor, 0);
		}

		public RenderWindow(int Width, int Height, string Title, bool Resizable = false) {
			Glfw.WindowHint(Glfw.Hint.Resizable, Resizable);
			SetOpenGLHints();

			Wnd = Glfw.CreateWindow(Width, Height, Title);
		}

		public void MakeCurrent() {
			Glfw.MakeContextCurrent(Wnd);
		}

		public void SwapBuffers() {
			Glfw.SwapBuffers(Wnd);
		}
	}
}
