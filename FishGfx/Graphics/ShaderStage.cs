using System;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics;

public sealed class ShaderStage : GraphicsResource
{
	internal ShaderStage(
		GraphicsContext owner,
		ShaderStageType type,
		string source,
		string sourceName = null
	)
		: base(owner)
	{
		if (!Enum.IsDefined(type))
		{
			throw new ArgumentOutOfRangeException(nameof(type));
		}

		ArgumentException.ThrowIfNullOrWhiteSpace(source);

		Type = type;
		SourceName = sourceName;
		Handle = Internal_OpenGL.GL.CreateShader(ToOpenGl(type));

		try
		{
			Compile(source);
			RegisterResource();
		}
		catch
		{
			Internal_OpenGL.GL.DeleteShader(Handle);
			Handle = 0;

			throw;
		}
	}

	public ShaderStageType Type { get; }

	public string SourceName { get; }

	public override string ToString()
	{
		return SourceName ?? Type.ToString();
	}

	internal override void DeleteResource()
	{
		Internal_OpenGL.GL.DeleteShader(Handle);
	}

	private void Compile(string source)
	{
		Internal_OpenGL.GL.ShaderSource(Handle, source);
		Internal_OpenGL.GL.CompileShader(Handle);
		Internal_OpenGL.GL.GetShader(
			Handle,
			ShaderParameterName.CompileStatus,
			out int status
		);

		if (status != 0)
		{
			return;
		}

		string log = Internal_OpenGL.GL.GetShaderInfoLog(Handle);
		string label = SourceName ?? Type.ToString();

		throw new InvalidOperationException(
			$"Failed to compile shader '{label}'.{Environment.NewLine}{log}"
		);
	}

	private static Silk.NET.OpenGL.ShaderType ToOpenGl(ShaderStageType type)
	{
		return type switch
		{
			ShaderStageType.Vertex => Silk.NET.OpenGL.ShaderType.VertexShader,
			ShaderStageType.Fragment => Silk.NET.OpenGL.ShaderType.FragmentShader,
			ShaderStageType.Geometry => Silk.NET.OpenGL.ShaderType.GeometryShader,
			ShaderStageType.TessellationControl => Silk.NET.OpenGL.ShaderType.TessControlShader,
			ShaderStageType.TessellationEvaluation => Silk.NET.OpenGL.ShaderType.TessEvaluationShader,
			ShaderStageType.Compute => Silk.NET.OpenGL.ShaderType.ComputeShader,
			_ => throw new ArgumentOutOfRangeException(nameof(type)),
		};
	}
}
