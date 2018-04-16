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
		static RenderWindow() {
			Glfw.ConfigureNativesDirectory(Path.GetFullPath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)));

			if (!Glfw.Init())
				throw new Exception("Could not initialize glfw");

			Glfw.SetErrorCallback((Err, Msg) => {
				throw new Exception(string.Format("glfw({0}) {1}", Err, Msg));
			});

			Gl.Initialize();
		}

		static void ResetGLState() {
#if DEBUG
			Gl.DebugMessageCallback((Src, DbgType, ID, Severity, Len, Buffer, UserPtr) => {
				if (Severity == Gl.DebugSeverity.Notification)
					return;

				Console.WriteLine("OpenGL {0} {1} {2}, {3}", Src, DbgType, ID, Severity);
				Console.WriteLine(Encoding.ASCII.GetString((byte*)Buffer, Len));

				if ((/*Severity == Gl.DebugSeverity.Medium ||*/ Severity == Gl.DebugSeverity.High) && Debugger.IsAttached)
					Debugger.Break();
			}, IntPtr.Zero);

			Gl.Enable((EnableCap)Gl.DEBUG_OUTPUT);
			Gl.Enable((EnableCap)Gl.DEBUG_OUTPUT_SYNCHRONOUS);
#endif

			Gl.Disable(EnableCap.DepthTest);

			Gl.FrontFace(FrontFaceDirection.Cw);
			Gl.CullFace(CullFaceMode.Back);
			Gl.Enable(EnableCap.CullFace);

			Gl.Enable(EnableCap.Blend);
			//Gl.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
			//Gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

			Gl.BlendEquation(BlendEquationMode.FuncAdd);
			Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
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
			MakeCurrent();
		}

		public void MakeCurrent() {
			Glfw.MakeContextCurrent(Wnd);
			ResetGLState();
		}

		public void SwapBuffers() {
			Glfw.SwapBuffers(Wnd);
		}
	}
}
