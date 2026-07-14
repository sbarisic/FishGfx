using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Glfw3;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics;

internal static unsafe class Internal_OpenGL
{
#if DEBUG
	private static DebugProc debugCallback;
#endif

	private static bool glfwInitialized;
	private static bool openGlInitialized;

	internal static GL GL { get; private set; }

	internal static string[] Extensions { get; private set; } = Array.Empty<string>();

	internal static string Version { get; private set; } = string.Empty;

	internal static string Renderer { get; private set; } = string.Empty;

	internal static int MajorVersion { get; private set; }

	internal static int MinorVersion { get; private set; }

	internal static bool Is42OrAbove { get; private set; }

	internal static bool Is43OrAbove { get; private set; }

	internal static bool Is45OrAbove { get; private set; }

	internal static void InitGLFW()
	{
		if (glfwInitialized)
		{
			return;
		}

		if (!Glfw.Init())
		{
			throw new InvalidOperationException("Could not initialize GLFW.");
		}

		Glfw.SetErrorCallback((error, message) =>
		{
			if (error == Glfw.ErrorCode.VersionUnavailable)
			{
				return;
			}

			throw new InvalidOperationException($"GLFW ({error}): {message}");
		});
		glfwInitialized = true;
	}

	internal static void InitOpenGL()
	{
		if (GL != null)
		{
			return;
		}

		GL = GL.GetApi(Glfw.GetProcAddress);
	}

	internal static void SetupOpenGL()
	{
		if (GL == null)
		{
			throw new InvalidOperationException("OpenGL has not been initialized.");
		}

		ReadVersion();
		ReadExtensions();
		InitializeDebugOutput();
		ReadRenderer();
	}

	internal static void Scissor(
		int x,
		int y,
		int width,
		int height,
		bool enabled
	)
	{
		if (width < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width));
		}

		if (height < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(height));
		}

		GL.Scissor(x, y, (uint)width, (uint)height);

		if (enabled)
		{
			GL.Enable(EnableCap.ScissorTest);
		}
		else
		{
			GL.Disable(EnableCap.ScissorTest);
		}
	}

	private static void InitializeDebugOutput()
	{
		if (openGlInitialized)
		{
			return;
		}

		bool supportsDebugOutput = Is43OrAbove || Array.IndexOf(
			Extensions,
			"GL_KHR_debug"
		) >= 0;

		if (!supportsDebugOutput)
		{
			return;
		}

		openGlInitialized = true;

#if DEBUG
		debugCallback = (
			source,
			type,
			id,
			severity,
			length,
			messagePointer,
			userPointer
		) =>
		{
			if (IsIgnoredDebugMessage(source, type, id))
			{
				return;
			}

			string message = Encoding.ASCII.GetString(
				(byte*)messagePointer,
				length
			);
			Console.WriteLine(
				"OpenGL {0} {1} {2}, {3}",
				source,
				type,
				id,
				severity
			);
			Console.WriteLine(message);

			if (severity == GLEnum.DebugSeverityHigh
				&& Debugger.IsAttached
				&& !message.Contains(
					"GL_INVALID_OPERATION in BindTextureUnit",
					StringComparison.Ordinal
				))
			{
				Debugger.Break();
			}
		};
		GL.DebugMessageCallback(debugCallback, null);
		GL.Enable(EnableCap.DebugOutput);
		GL.Enable(EnableCap.DebugOutputSynchronous);
#endif
	}

	private static void ReadVersion()
	{
		GL.GetInteger(GetPName.MajorVersion, out int major);
		GL.GetInteger(GetPName.MinorVersion, out int minor);
		MajorVersion = major;
		MinorVersion = minor;
		Is42OrAbove = major > 4 || major == 4 && minor >= 2;
		Is43OrAbove = major > 4 || major == 4 && minor >= 3;
		Is45OrAbove = major > 4 || major == 4 && minor >= 5;
		Version = $"{major}.{minor}";
	}

	private static void ReadExtensions()
	{
		GL.GetInteger(GetPName.NumExtensions, out int extensionCount);
		List<string> extensions = new(extensionCount);

		for (uint index = 0; index < extensionCount; index++)
		{
			extensions.Add(GL.GetStringS(StringName.Extensions, index));
		}

		Extensions = extensions.ToArray();
	}

	private static void ReadRenderer()
	{
		string renderer = GL.GetStringS(StringName.Renderer);
		string shadingLanguageVersion = GL.GetStringS(
			StringName.ShadingLanguageVersion
		);
		string vendor = GL.GetStringS(StringName.Vendor);
		string version = GL.GetStringS(StringName.Version);
		Renderer = $"{renderer} by {vendor}; GL {version}; " +
			$"GLSL {shadingLanguageVersion}";
	}

#if DEBUG
	private static bool IsIgnoredDebugMessage(
		GLEnum source,
		GLEnum type,
		int id
	)
	{
		if (source == GLEnum.DebugSourceApi
			&& type == GLEnum.DebugTypeOther
			&& id == 131185)
		{
			return true;
		}

		if (source != GLEnum.DebugSourceApplication)
		{
			return false;
		}

		return type is
			GLEnum.DebugTypeMarker or
			GLEnum.DebugTypePushGroup or
			GLEnum.DebugTypePopGroup;
	}
#endif
}
