using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using OpenGL;
using Glfw3;
using System.Reflection;
using System.Diagnostics;

namespace FishGfx.Graphics {
	public unsafe class RenderWindow {
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
			Glfw.WindowHint(Glfw.Hint.ContextVersionMinor, 3);
		}

		public RenderWindow(int Width, int Height, string Title, bool Resizable = false) {
			Internal_OpenGL.InitGLFW();

			Glfw.WindowHint(Glfw.Hint.Resizable, Resizable);
			SetOpenGLHints();

			Wnd = Glfw.CreateWindow(Width, Height, Title);
			MakeCurrent();
		}

		public void MakeCurrent() {
			Internal_OpenGL.InitOpenGL();
			Glfw.MakeContextCurrent(Wnd);
			Internal_OpenGL.SetupOpenGL();
			Internal_OpenGL.ResetGLState();
		}

		public void SwapBuffers() {
			Glfw.SwapBuffers(Wnd);
		}
	}
}
