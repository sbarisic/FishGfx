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
using FishGfx.System;

namespace FishGfx.Graphics {
	internal static class OpenGL_BODGES {
		public static bool INTEL_BIND_ZERO_TEXTURE_BUG = false;
	}

	internal static unsafe class Internal_OpenGL {
		static bool GLFWInitialized = false;
		static bool OpenGLInitialized = false;

		public static string[] Extensions { get; private set; }
		public static string Version { get; private set; }

		public static bool Is45OrAbove {
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
//#if !DEBUG
				if (Err == Glfw.ErrorCode.VersionUnavailable)
					return;
//#endif

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
			bool IS_GL_DEBUG = Environment.GetCommandLineArgs().Contains("-debug");
			const string LogName = "opengl_log.txt";

			if (File.Exists(LogName))
				File.Delete(LogName);

			Gl.DebugMessageCallback((Src, DbgType, ID, Severity, Len, Buffer, UserPtr) => {
				Khronos.KhronosApi.LogComment(string.Format("OpenGL {0} {1} {2}, {3}: {4}", Src, DbgType, ID, Severity, Encoding.ASCII.GetString((byte*)Buffer, Len)));

				/*if (Severity == Gl.DebugSeverity.Notification)
					return;*/

				Console.WriteLine("OpenGL {0} {1} {2}, {3}", Src, DbgType, ID, Severity);
				Console.WriteLine(Encoding.ASCII.GetString((byte*)Buffer, Len));

				if ((/*Severity == Gl.DebugSeverity.Medium ||*/ Severity == DebugSeverity.DebugSeverityHigh) && Debugger.IsAttached)
					Debugger.Break();

			}, IntPtr.Zero);

			Gl.Enable((EnableCap)Gl.DEBUG_OUTPUT);
			Gl.Enable((EnableCap)Gl.DEBUG_OUTPUT_SYNCHRONOUS);
#endif

			Khronos.KhronosVersion Ver = Gl.QueryContextVersion();
			Is45OrAbove = ((Ver.Major == 4 && Ver.Minor >= 5) || Ver.Major > 4);
			Version = Ver.ToString();

			Gl.Extensions Exts = new Gl.Extensions();
			Exts.Query();

			List<string> SupportedExtensions = new List<string>();
			FieldInfo[] Fields = typeof(Gl.Extensions).GetFields();
			foreach (var F in Fields) {
				if ((bool)F.GetValue(Exts))
					SupportedExtensions.Add(F.Name);
			}

			Extensions = SupportedExtensions.ToArray();

			string Renderer = Gl.GetString(StringName.Renderer);
			string GLSLVer = Gl.GetString(StringName.ShadingLanguageVersion);
			string Vendor = Gl.GetString(StringName.Vendor);
			string Vers = Gl.GetString(StringName.Version);

			RenderAPI.Renderer = string.Format("{0} by {1}; GL {2}; GLSL {3}", Renderer, Vendor, Vers, GLSLVer);

#if DEBUG
			Khronos.KhronosApi.Log += (S, E) => {
				if (E.Name == "glGetError")
					return;

				if (IS_GL_DEBUG) {
					ConsoleColor Clr = Console.ForegroundColor;
					Console.ForegroundColor = ConsoleColor.DarkYellow;

					Console.WriteLine(E.ToString());
					File.AppendAllText(LogName, E.ToString() + "\r\n");

					Console.ForegroundColor = Clr;
				}
			};

			Khronos.KhronosApi.LogEnabled = true;
#endif
		}

		public static void ResetGLState() {
			//Gl.Disable(EnableCap.DepthTest);
			Gl.Enable(EnableCap.DepthTest);

			Gl.FrontFace(FrontFaceDirection.Ccw);
			Gl.CullFace(CullFaceMode.Back);
			Gl.Enable(EnableCap.CullFace);
			//Gl.Disable(EnableCap.CullFace);

			//Gl.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
			//Gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

			Gl.BlendEquation(BlendEquationMode.FuncAdd);
			Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
			Gl.Enable(EnableCap.Blend);
			//Gl.Disable(EnableCap.Blend);
		}
	}
}