using OpenGL;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Matrix4 = System.Numerics.Matrix4x4;
using Vector2 = System.Numerics.Vector2;

namespace FishGfx.Graphics {
	public enum ShaderType {
		FragmentShader = 35632,
		VertexShader = 35633,
		GeometryShader = 36313,
		TessEvaluationShader = 36487,
		TessControlShader = 36488,
		ComputeShader = 37305
	}

	public class ShaderUniforms {
		static Stack<ShaderUniforms> Uniforms;

		public static ShaderUniforms Current {
			get {
				if (Uniforms.Count < 1)
					throw new Exception("No shader uniforms assigned");

				return Uniforms.Peek();
			}
		}

		static ShaderUniforms() {
			Uniforms = new Stack<ShaderUniforms>();
			Push(CreateDefault());
		}

		public static ShaderUniforms CreateDefault() {
			ShaderUniforms Uniforms = new ShaderUniforms();

			Uniforms.Camera = new Camera();
			Uniforms.Camera.SetOrthogonal(-1, -1, 1, 1, 1, -1);
			Uniforms.Model = Matrix4.Identity;

			return Uniforms;
		}

		public static void Push(ShaderUniforms U) {
			Uniforms.Push(U);
		}

		public static ShaderUniforms Pop() {
			return Uniforms.Pop();
		}

		public static ShaderProgram NopShader;

		public Camera Camera;
		public Matrix4 Model;
		public float AlphaTest;

		public Vector2 Resolution;
		public Vector2 TextureSize;
		public int MultisampleCount;

		public void Bind(ShaderProgram Shader) {
			Shader.UniformMatrix4f("View", Camera.View);
			Shader.UniformMatrix4f("Project", Camera.Projection);
			Shader.UniformMatrix4f("Model", Model);

			// If it's an occlusion test, we most likely don't need these and
			// should be using the NOP shader
			//if (!(OcclusionQuery.CurrentQuery?.IsOcclusionTest ?? false)) {
				Shader.Uniform2f("Viewport", Camera.ViewportSize);
				Shader.Uniform1f("AlphaTest", AlphaTest);
				Shader.Uniform1f("MultisampleCount", (float)MultisampleCount);
				Shader.Uniform2f("TextureSize", TextureSize);
				Shader.Uniform2f("Resolution", Resolution);
				Shader.Uniform3f("ViewPos", Camera.Position);
			//}
		}
	}

	public unsafe class ShaderProgram : GraphicsObject {
		List<ShaderStage> ShaderStages;
		Dictionary<string, int> UniformLocations;

		public ShaderProgram(params ShaderStage[] Stages) {
			ID = Gl.CreateProgram();

			ShaderStages = new List<ShaderStage>();
			UniformLocations = new Dictionary<string, int>();

			foreach (var S in Stages)
				AttachShader(S);
			Link();
		}

		public void AttachShader(ShaderStage S) {
			ShaderStages.Add(S);

			Gl.AttachShader(ID, S.ID);
		}

		public bool Link(out string ErrorString) {
#if DEBUG
			SetLabel(ObjectIdentifier.Program, ToString());
#endif

			Gl.LinkProgram(ID);

			Gl.GetProgram(ID, ProgramProperty.LinkStatus, out int Linked);
			if (Linked != Gl.TRUE) {
				StringBuilder Log = new StringBuilder(4096);
				Gl.GetProgramInfoLog(ID, Log.Capacity, out int Len, Log);
				ErrorString = Log.ToString();
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

		public void Link() {
			if (!Link(out string ErrorString))
				throw new Exception("Failed to link program\n" + ErrorString);
		}

		public virtual void Bind(ShaderUniforms Uniforms) {
			if ((OcclusionQuery.CurrentQuery?.IsOcclusionTest ?? false) && ShaderUniforms.NopShader != null) {
				if (this != ShaderUniforms.NopShader) {
					ShaderUniforms.NopShader.Bind(Uniforms);
					return;
				}
			}

			Uniforms.Bind(this);
			Gl.UseProgram(ID);
		}

		public override void Unbind() {
			if ((OcclusionQuery.CurrentQuery?.IsOcclusionTest ?? false) && ShaderUniforms.NopShader != null) {
				if (this != ShaderUniforms.NopShader) {
					ShaderUniforms.NopShader.Unbind();
					return;
				}

			}
			Gl.UseProgram(0);
		}

		public int GetAttribLocation(string Name) {
			return Gl.GetAttribLocation(ID, Name);
		}

		public int GetUniformLocation(string Name) {
			if (UniformLocations.ContainsKey(Name))
				return UniformLocations[Name];

			int Loc = Gl.GetUniformLocation(ID, Name);
			if (Loc != -1)
				UniformLocations.Add(Name, Loc);

			return Loc;
		}

		public void UniformMatrix4f(string Uniform, Matrix4 M, bool Transpose = false) {
			Gl.ProgramUniformMatrix4f(ID, GetUniformLocation(Uniform), 1, Transpose, M);
		}

		public bool Uniform3f<T>(string Uniform, T Val) where T : struct {
			int Loc = GetUniformLocation(Uniform);
			if (Loc == -1)
				return false;

			Gl.ProgramUniform3f(ID, Loc, 1, Val);
			return true;
		}

		public bool Uniform2f<T>(string Uniform, T Val) where T : struct {
			int Loc = GetUniformLocation(Uniform);
			if (Loc == -1)
				return false;

			Gl.ProgramUniform2f(ID, Loc, 1, Val);
			return true;
		}

		public bool Uniform1f<T>(string Uniform, T Val) where T : struct {
			int Loc = GetUniformLocation(Uniform);
			if (Loc == -1)
				return false;

			Gl.ProgramUniform1f(ID, Loc, 1, Val);
			return true;
		}

		public bool Uniform1(string Uniform, int Val) {
			int Loc = GetUniformLocation(Uniform);
			if (Loc == -1)
				return false;

			Gl.ProgramUniform1(ID, Loc, 1, &Val);
			return true;
		}

		public override void GraphicsDispose() {
			Gl.DeleteProgram(ID);
		}

		public override string ToString() {
			return string.Join(";", ShaderStages);
		}
	}

	public class ShaderStage : GraphicsObject {
		static Dictionary<string, ShaderStage> ShaderStages = new Dictionary<string, ShaderStage>();

		//public FileWatchHandle WatchHandle;

		string Source;
		string SrcFile;
		ShaderType ShaderType;

		public ShaderStage(ShaderType T, string SourceFile) {
			ID = Gl.CreateShader((OpenGL.ShaderType)T);
			ShaderType = T;

			SetSourceFile(SourceFile);
			Compile();
		}

		public ShaderStage SetSourceCode(string Code) {
			Source = Code;
			SrcFile = null;
			//WatchHandle = null;
			return this;
		}

		public ShaderStage SetSourceFile(string FilePath) {
			Source = null;
			SrcFile = Path.GetFullPath(FilePath);
			//WatchHandle = FileWatcher.Watch(FilePath);
			return this;
		}

		public bool Compile(out string ErrorString) {
#if DEBUG
			SetLabel(ObjectIdentifier.Shader, ToString());
#endif

			// TODO: Find something better
			if (SrcFile != null) {
				bool Succeeded = false;
				int TryCount = 0;

				while (!Succeeded) {
					try {
						Source = File.ReadAllText(SrcFile);
						Succeeded = true;
					} catch (Exception) {
						Thread.Sleep(50);
						TryCount++;

						if (TryCount >= 10)
							throw;
					}
				}
			}

			Gl.ShaderSource(ID, new string[] { Source });
			Gl.CompileShader(ID);

			Gl.GetShader(ID, ShaderParameterName.CompileStatus, out int Status);
			if (Status != Gl.TRUE) {
				StringBuilder Log = new StringBuilder(4096);
				Gl.GetShaderInfoLog(ID, Log.Capacity, out int Len, Log);

				if (SrcFile != null)
					ErrorString = SrcFile + "\n" + Log.ToString();
				else
					ErrorString = Log.ToString();
				return false;
			}

			ErrorString = "";
			return true;
		}

		public ShaderStage Compile() {
			if (!Compile(out string ErrorString))
				throw new Exception("Failed to compile shader\n" + ErrorString);

			return this;
		}

		public override void GraphicsDispose() {
			Gl.DeleteShader(ID);
		}

		public override string ToString() {
			if (SrcFile != null)
				return SrcFile;

			return ShaderType.ToString();
		}
	}
}
