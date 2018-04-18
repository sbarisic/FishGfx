using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Glfw3;
using OpenGL;

namespace FishGfx.Graphics {
	internal static unsafe class Internal_OpenGL {
		static bool GLFWInitialized = false;
		static bool OpenGLInitialized = false;

		public static bool SupportsDSA {
			get; private set;
		}

		public static bool Is45 {
			get; private set;
		}

		public static void InitGLFW() {
			if (GLFWInitialized)
				return;

			GLFWInitialized = true;

			Glfw.ConfigureNativesDirectory(Path.GetFullPath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)));

			if (!Glfw.Init())
				throw new Exception("Could not initialize glfw");

			Glfw.SetErrorCallback((Err, Msg) => {
				throw new Exception(string.Format("glfw({0}) {1}", Err, Msg));
			});
		}

		public static void InitOpenGL() {
			if (OpenGLInitialized)
				return;

			Gl.Initialize();
		}

		public static void SetupOpenGL() {
			if (OpenGLInitialized)
				return;

			OpenGLInitialized = true;
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

			Khronos.KhronosVersion Ver = Gl.QueryContextVersion();
			Is45 = Ver.Major == 4 && Ver.Minor == 5;

			Gl.Extensions Extensions = new Gl.Extensions();
			Extensions.Query();
			SupportsDSA = Extensions.DirectStateAccess_ARB || Extensions.DirectStateAccess_EXT;
		}

		public static void ResetGLState() {
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
	}
}
