using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using FishGfx;
using Glfw3;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics
{
	internal static class OpenGL_BODGES
	{
		public static bool INTEL_BIND_ZERO_TEXTURE_BUG = false;
	}

	internal static unsafe class Internal_OpenGL
	{
		public static GL GL { get; private set; }
#if DEBUG
		static DebugProc DebugCallback;
#endif
		static bool GLFWInitialized = false;
		static bool OpenGLInitialized = false;

		//static bool LastFrontFace;

		public static string[] Extensions { get; private set; }
		public static string Version { get; private set; }

		public static bool Is45OrAbove { get; private set; }

		public static void InitGLFW()
		{
			if (GLFWInitialized)
				return;

			GLFWInitialized = true;

			//Glfw.ConfigureNativesDirectory(Path.GetFullPath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)));

			if (!Glfw.Init())
				throw new Exception("Could not initialize glfw");

			Glfw.SetErrorCallback(
				(Err, Msg) =>
				{
					if (Err == Glfw.ErrorCode.VersionUnavailable)
						return;

					throw new Exception(string.Format("glfw({0}) {1}", Err, Msg));
				}
			);
		}

		public static void InitOpenGL()
		{
			if (GL != null)
				return;

			GL = GL.GetApi(Glfw.GetProcAddress);
		}

		public static void SetupOpenGL()
		{
			if (OpenGLInitialized)
				return;

			OpenGLInitialized = true;
#if DEBUG
			bool IS_GL_DEBUG = Environment.GetCommandLineArgs().Contains("-debug");
			const string LogName = "opengl_log.txt";

			if (File.Exists(LogName))
				File.Delete(LogName);

			DebugCallback = (Src, DbgType, ID, Severity, Len, Buffer, UserPtr) =>
			{
				string Msg = Encoding.ASCII.GetString((byte*)Buffer, Len);

				// Will use video memory blah blah
				if (Src == GLEnum.DebugSourceApi && DbgType == GLEnum.DebugTypeOther && ID == 131185)
					return;

				if (Src == GLEnum.DebugSourceApplication)
				{
					if (DbgType == GLEnum.DebugTypeMarker)
						return;

					if (DbgType == GLEnum.DebugTypePushGroup || DbgType == GLEnum.DebugTypePopGroup)
						return;
				}

				Console.WriteLine("OpenGL {0} {1} {2}, {3}", Src, DbgType, ID, Severity);
				Console.WriteLine(Msg);

				if ((Severity == GLEnum.DebugSeverityHigh) && Debugger.IsAttached)
				{
					if (!Msg.Contains("GL_INVALID_OPERATION in BindTextureUnit"))
						Debugger.Break();
				}
			};
			GL.DebugMessageCallback(DebugCallback, null);

			GL.Enable(EnableCap.DebugOutput);
			GL.Enable(EnableCap.DebugOutputSynchronous);
#endif

			GL.GetInteger(GetPName.MajorVersion, out int Major);
			GL.GetInteger(GetPName.MinorVersion, out int Minor);
			Is45OrAbove = Major > 4 || (Major == 4 && Minor >= 5);
			Version = $"{Major}.{Minor}";

			GL.GetInteger(GetPName.NumExtensions, out int ExtensionCount);
			List<string> SupportedExtensions = new List<string>(ExtensionCount);
			for (uint i = 0; i < ExtensionCount; i++)
				SupportedExtensions.Add(GL.GetStringS(StringName.Extensions, i));

			Extensions = SupportedExtensions.ToArray();

			string Renderer = GL.GetStringS(StringName.Renderer);
			string GLSLVer = GL.GetStringS(StringName.ShadingLanguageVersion);
			string Vendor = GL.GetStringS(StringName.Vendor);
			string Vers = GL.GetStringS(StringName.Version);

			RenderAPI.Renderer = string.Format("{0} by {1}; GL {2}; GLSL {3}", Renderer, Vendor, Vers, GLSLVer);

			Gfx.PushRenderState(Gfx.CreateDefaultRenderState());
		}

		public static void Scissor(int X, int Y, int W, int H, bool Enable)
		{
			Internal_OpenGL.GL.Scissor(X, Y, (uint)W, (uint)H);

			if (Enable)
				Internal_OpenGL.GL.Enable(EnableCap.ScissorTest);
			else
				Internal_OpenGL.GL.Disable(EnableCap.ScissorTest);
		}
	}
}
