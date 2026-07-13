using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Silk.NET.OpenGL;
using Matrix4 = System.Numerics.Matrix4x4;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace FishGfx.Graphics
{
	public enum ShaderType
	{
		FragmentShader = 35632,
		VertexShader = 35633,
		GeometryShader = 36313,
		TessEvaluationShader = 36487,
		TessControlShader = 36488,
		ComputeShader = 37305,
	}

	public class ShaderUniforms
	{
		static readonly Stack<ShaderUniforms> LegacyUniforms = new Stack<ShaderUniforms>();
		static Stack<ShaderUniforms> Uniforms => GraphicsContext.CurrentOrNull?.Uniforms ?? LegacyUniforms;

		public static ShaderUniforms Current
		{
			get
			{
				if (Uniforms.Count < 1)
					throw new Exception("No shader uniforms assigned");

				return Uniforms.Peek();
			}
		}

		static ShaderUniforms()
		{
			LegacyUniforms.Push(CreateDefault());
		}

		public static ShaderUniforms CreateDefault()
		{
			ShaderUniforms Uniforms = new ShaderUniforms();

			Uniforms.Camera = new Camera();
			Uniforms.Camera.SetOrthogonal(-1, -1, 1, 1, 1, -1);
			Uniforms.Model = Matrix4.Identity;

			return Uniforms;
		}

		public static void Push(ShaderUniforms U)
		{
			Uniforms.Push(U);
		}

		public static ShaderUniforms Pop()
		{
			return Uniforms.Pop();
		}

		//public static ShaderProgram NopShader;

		public Camera Camera;
		public Matrix4 Model;
		public float AlphaTest;
		public float Time;

		public Vector2 Resolution;
		public Vector2 TextureSize;
		public int MultisampleCount;

		public bool HasRenderView { get; private set; }
		public RenderView RenderView { get; private set; }

		public void SetRenderView(RenderView view)
		{
			RenderView = view;
			HasRenderView = true;
			Resolution = view.ViewportSize;
		}

		public void Bind(ShaderProgram Shader)
		{
			Matrix4 view = HasRenderView ? RenderView.View : Camera.View;
			Matrix4 projection = HasRenderView ? RenderView.Projection : Camera.Projection;
			Vector3 position = HasRenderView ? RenderView.Position : Camera.Position;
			Vector2 viewport = HasRenderView ? RenderView.ViewportSize : Camera.ViewportSize;
			float near = HasRenderView ? RenderView.Near : Camera.Near;
			float far = HasRenderView ? RenderView.Far : Camera.Far;

			Shader.Uniform1f("Near", near);
			Shader.Uniform1f("Far", far);
			Shader.UniformMatrix4f("View", view);
			Shader.UniformMatrix4f("Project", projection);
			Shader.UniformMatrix4f("Model", Model);
			Shader.Uniform2f("Viewport", viewport);
			Shader.Uniform1f("AlphaTest", AlphaTest);
			Shader.Uniform1f("Time", Time);
			Shader.Uniform1f("MultisampleCount", (float)MultisampleCount);
			Shader.Uniform2f("TextureSize", TextureSize);
			Shader.Uniform2f("Resolution", Resolution);
			Shader.Uniform3f("ViewPos", position);
		}
	}

	public unsafe class ShaderProgram : GraphicsObject
	{
		List<ShaderStage> ShaderStages;
		Dictionary<string, int> UniformLocations;

		public ShaderProgram(params ShaderStage[] Stages)
		{
			ID = Internal_OpenGL.GL.CreateProgram();

			ShaderStages = new List<ShaderStage>();
			UniformLocations = new Dictionary<string, int>();

			foreach (var S in Stages)
				AttachShader(S);
			Link();
		}

		public void AttachShader(ShaderStage S)
		{
			EnsureCurrentOwner();
			if (S == null) throw new ArgumentNullException(nameof(S));
			S.EnsureOwner(GraphicsContext.Current);
			ShaderStages.Add(S);

			Internal_OpenGL.GL.AttachShader(ID, S.ID);
		}

		public bool Link(out string ErrorString)
		{
			EnsureCurrentOwner();
#if DEBUG
			SetLabel(ToString());
#endif

			Internal_OpenGL.GL.LinkProgram(ID);

			Internal_OpenGL.GL.GetProgram(ID, ProgramPropertyARB.LinkStatus, out int Linked);

			if (Linked == 0)
			{
				ErrorString = Internal_OpenGL.GL.GetProgramInfoLog(ID);
				return false;
			}

			string[] UniformKeys = UniformLocations.Keys.ToArray();
			UniformLocations.Clear();

			for (int i = 0; i < UniformKeys.Length; i++)
				GetUniformLocation(UniformKeys[i]);

			// Get some defaults
			GetUniformLocation("Model");
			GetUniformLocation("View");
			GetUniformLocation("Project");

			ErrorString = "";
			return true;
		}

		public void Link()
		{
			if (!Link(out string ErrorString))
				throw new Exception("Failed to link program\n" + ErrorString);
		}

		public virtual void Bind(ShaderUniforms Uniforms)
		{
			EnsureCurrentOwner();
			if (Uniforms == null) throw new ArgumentNullException(nameof(Uniforms));
			Internal_OpenGL.GL.GetInteger(GetPName.CurrentProgram, out int previous);
			Internal_OpenGL.GL.UseProgram(ID);
			if (GraphicsContext.CurrentOrNull != null) GraphicsContext.CurrentOrNull.BoundProgram = ID;
			try { Uniforms.Bind(this); }
			catch
			{
				Internal_OpenGL.GL.UseProgram((uint)previous);
				if (GraphicsContext.CurrentOrNull != null) GraphicsContext.CurrentOrNull.BoundProgram = (uint)previous;
				throw;
			}
		}

		public override void Unbind()
		{
			Internal_OpenGL.GL.UseProgram(0);
			if (GraphicsContext.CurrentOrNull != null) GraphicsContext.CurrentOrNull.BoundProgram = 0;
		}

		bool SupportsProgramUniforms => Internal_OpenGL.MajorVersion > 4 || (Internal_OpenGL.MajorVersion == 4 && Internal_OpenGL.MinorVersion >= 1);

		uint BindForUniformUpdate()
		{
			Internal_OpenGL.GL.GetInteger(GetPName.CurrentProgram, out int currentProgram);
			uint previous = (uint)currentProgram;
			GraphicsContext context = GraphicsContext.CurrentOrNull;
			if (previous != ID)
			{
				Internal_OpenGL.GL.UseProgram(ID);
				if (context != null) context.BoundProgram = ID;
			}
			return previous;
		}

		void RestoreProgram(uint previous)
		{
			if (previous == ID) return;
			Internal_OpenGL.GL.UseProgram(previous);
			if (GraphicsContext.CurrentOrNull != null) GraphicsContext.CurrentOrNull.BoundProgram = previous;
		}

		public int GetAttribLocation(string Name)
		{
			EnsureCurrentOwner();
			return Internal_OpenGL.GL.GetAttribLocation(ID, Name);
		}

		public int GetUniformLocation(string Name)
		{
			EnsureCurrentOwner();
			if (UniformLocations.ContainsKey(Name))
				return UniformLocations[Name];

			int Loc = Internal_OpenGL.GL.GetUniformLocation(ID, Name);

			if (Loc != -1)
				UniformLocations.Add(Name, Loc);

			return Loc;
		}

		public void UniformMatrix4f(string Uniform, Matrix4 M, bool Transpose = false)
		{
			int location = GetUniformLocation(Uniform);
			if (location == -1) return;
			if (SupportsProgramUniforms) Internal_OpenGL.GL.ProgramUniformMatrix4(ID, location, 1, Transpose, (float*)&M);
			else
			{
				uint previous = BindForUniformUpdate();
				try { Internal_OpenGL.GL.UniformMatrix4(location, 1, Transpose, (float*)&M); }
				finally { RestoreProgram(previous); }
			}
		}

		public bool Uniform3f<T>(string Uniform, T Val)
			where T : struct
		{
			int Loc = GetUniformLocation(Uniform);

			if (Loc == -1)
				return false;

			if (Val is Vector3 V)
			{
				if (SupportsProgramUniforms) Internal_OpenGL.GL.ProgramUniform3(ID, Loc, V);
				else { uint previous = BindForUniformUpdate(); try { Internal_OpenGL.GL.Uniform3(Loc, V); } finally { RestoreProgram(previous); } }
			}
			else
				throw new NotSupportedException($"Uniform3f does not support {typeof(T)}");
			return true;
		}

		public bool Uniform4f<T>(string Uniform, T Val)
			where T : struct
		{
			int Loc = GetUniformLocation(Uniform);

			if (Loc == -1)
				return false;

			if (Val is System.Numerics.Vector4 V)
			{
				if (SupportsProgramUniforms) Internal_OpenGL.GL.ProgramUniform4(ID, Loc, V);
				else { uint previous = BindForUniformUpdate(); try { Internal_OpenGL.GL.Uniform4(Loc, V); } finally { RestoreProgram(previous); } }
			}
			else
				throw new NotSupportedException($"Uniform4f does not support {typeof(T)}");
			return true;
		}

		public bool Uniform2f<T>(string Uniform, T Val)
			where T : struct
		{
			int Loc = GetUniformLocation(Uniform);

			if (Loc == -1)
				return false;

			if (Val is Vector2 V)
			{
				if (SupportsProgramUniforms) Internal_OpenGL.GL.ProgramUniform2(ID, Loc, V);
				else { uint previous = BindForUniformUpdate(); try { Internal_OpenGL.GL.Uniform2(Loc, V); } finally { RestoreProgram(previous); } }
			}
			else
				throw new NotSupportedException($"Uniform2f does not support {typeof(T)}");
			return true;
		}

		public bool Uniform1f<T>(string Uniform, T Val)
			where T : struct
		{
			int Loc = GetUniformLocation(Uniform);

			if (Loc == -1)
				return false;

			if (Val is float V)
			{
				if (SupportsProgramUniforms) Internal_OpenGL.GL.ProgramUniform1(ID, Loc, V);
				else { uint previous = BindForUniformUpdate(); try { Internal_OpenGL.GL.Uniform1(Loc, V); } finally { RestoreProgram(previous); } }
			}
			else
				throw new NotSupportedException($"Uniform1f does not support {typeof(T)}");
			return true;
		}

		public bool Uniform1(string Uniform, int Val)
		{
			int Loc = GetUniformLocation(Uniform);

			if (Loc == -1)
				return false;

			if (SupportsProgramUniforms) Internal_OpenGL.GL.ProgramUniform1(ID, Loc, Val);
			else { uint previous = BindForUniformUpdate(); try { Internal_OpenGL.GL.Uniform1(Loc, Val); } finally { RestoreProgram(previous); } }
			return true;
		}

		public override void GraphicsDispose()
		{
			Internal_OpenGL.GL.DeleteProgram(ID);
		}

		public override string ToString()
		{
			return string.Join(";", ShaderStages);
		}
	}

	public class ShaderStage : GraphicsObject
	{
		static Dictionary<string, ShaderStage> ShaderStages = new Dictionary<string, ShaderStage>();

		//public FileWatchHandle WatchHandle;

		string Source;
		string SrcFile;
		ShaderType ShaderType;

		public ShaderStage(ShaderType T, string SourceFile)
		{
			ID = Internal_OpenGL.GL.CreateShader((Silk.NET.OpenGL.ShaderType)T);
			ShaderType = T;

			SetSourceFile(SourceFile);
			Compile();
		}

		public ShaderStage SetSourceCode(string Code)
		{
			EnsureCurrentOwner();
			Source = Code;
			SrcFile = null;
			//WatchHandle = null;
			return this;
		}

		public ShaderStage SetSourceFile(string FilePath)
		{
			Source = null;
			SrcFile = Path.GetFullPath(FilePath);
			//WatchHandle = FileWatcher.Watch(FilePath);
			return this;
		}

		public bool Compile(out string ErrorString)
		{
			EnsureCurrentOwner();
#if DEBUG
			SetLabel(ToString());
#endif

			// TODO: Find something better
			if (SrcFile != null)
			{
				bool Succeeded = false;
				int TryCount = 0;

				while (!Succeeded)
				{
					try
					{
						Source = File.ReadAllText(SrcFile);
						Succeeded = true;
					}
					catch (Exception)
					{
						Thread.Sleep(50);
						TryCount++;

						if (TryCount >= 10)
							throw;
					}
				}
			}

			Internal_OpenGL.GL.ShaderSource(ID, Source);
			Internal_OpenGL.GL.CompileShader(ID);

			Internal_OpenGL.GL.GetShader(ID, ShaderParameterName.CompileStatus, out int Status);

			if (Status == 0)
			{
				string Log = Internal_OpenGL.GL.GetShaderInfoLog(ID);

				if (SrcFile != null)
					ErrorString = SrcFile + "\n" + Log;
				else
					ErrorString = Log;
				return false;
			}

			ErrorString = "";
			return true;
		}

		public ShaderStage Compile()
		{
			if (!Compile(out string ErrorString))
				throw new Exception("Failed to compile shader\n" + ErrorString);

			return this;
		}

		public override void GraphicsDispose()
		{
			Internal_OpenGL.GL.DeleteShader(ID);
		}

		public override string ToString()
		{
			if (SrcFile != null)
				return SrcFile;

			return ShaderType.ToString();
		}
	}
}
