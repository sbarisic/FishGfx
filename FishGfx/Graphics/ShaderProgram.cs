using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics;

public unsafe sealed class ShaderProgram : GraphicsResource
{
	private readonly Dictionary<string, int> uniformLocations = new();
	private readonly Stack<uint> previousPrograms = new();
	private readonly IReadOnlyList<ShaderStage> stages;

	internal ShaderProgram(GraphicsContext owner, IReadOnlyList<ShaderStage> stages)
		: base(owner)
	{
		if (stages == null)
		{
			throw new ArgumentNullException(nameof(stages));
		}

		if (stages.Count == 0)
		{
			throw new ArgumentException("A shader program requires at least one stage.", nameof(stages));
		}

		ShaderStage[] stageCopy = new ShaderStage[stages.Count];

		for (int index = 0; index < stages.Count; index++)
		{
			ShaderStage stage = stages[index] ?? throw new ArgumentException(
				"Shader stages cannot contain null values.",
				nameof(stages)
			);

			stage.EnsureOwner(owner);
			stageCopy[index] = stage;
		}

		this.stages = Array.AsReadOnly(stageCopy);
		Handle = Internal_OpenGL.GL.CreateProgram();

		try
		{
			Link();
			RegisterResource();
		}
		catch
		{
			Internal_OpenGL.GL.DeleteProgram(Handle);
			Handle = 0;

			throw;
		}
	}

	public IReadOnlyList<ShaderStage> Stages => stages;

	public int GetAttributeLocation(string name)
	{
		EnsureCurrentOwner();
		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		return Internal_OpenGL.GL.GetAttribLocation(Handle, name);
	}

	public bool SetUniform(string name, Matrix4x4 value, bool transpose = false)
	{
		int location = GetUniformLocation(name);

		if (location == -1)
		{
			return false;
		}

		if (Owner.Capabilities.SupportsProgramUniforms)
		{
			Internal_OpenGL.GL.ProgramUniformMatrix4(Handle, location, 1, transpose, (float*)&value);
		}
		else
		{
			Internal_OpenGL.GL.GetInteger(GetPName.CurrentProgram, out int previousProgram);

			try
			{
				Internal_OpenGL.GL.UseProgram(Handle);
				Internal_OpenGL.GL.UniformMatrix4(location, 1, transpose, (float*)&value);
			}
			finally
			{
				Internal_OpenGL.GL.UseProgram((uint)previousProgram);
			}
		}

		return true;
	}

	public bool SetUniform(string name, Vector2 value)
	{
		int location = GetUniformLocation(name);

		if (location == -1)
		{
			return false;
		}

		if (Owner.Capabilities.SupportsProgramUniforms)
		{
			Internal_OpenGL.GL.ProgramUniform2(Handle, location, value);
		}
		else
		{
			WithProgramBound(() => Internal_OpenGL.GL.Uniform2(location, value));
		}

		return true;
	}

	public bool SetUniform(string name, Vector3 value)
	{
		int location = GetUniformLocation(name);

		if (location == -1)
		{
			return false;
		}

		if (Owner.Capabilities.SupportsProgramUniforms)
		{
			Internal_OpenGL.GL.ProgramUniform3(Handle, location, value);
		}
		else
		{
			WithProgramBound(() => Internal_OpenGL.GL.Uniform3(location, value));
		}

		return true;
	}

	public bool SetUniform(string name, Vector4 value)
	{
		int location = GetUniformLocation(name);

		if (location == -1)
		{
			return false;
		}

		if (Owner.Capabilities.SupportsProgramUniforms)
		{
			Internal_OpenGL.GL.ProgramUniform4(Handle, location, value);
		}
		else
		{
			WithProgramBound(() => Internal_OpenGL.GL.Uniform4(location, value));
		}

		return true;
	}

	public bool SetUniform(string name, float value)
	{
		int location = GetUniformLocation(name);

		if (location == -1)
		{
			return false;
		}

		if (Owner.Capabilities.SupportsProgramUniforms)
		{
			Internal_OpenGL.GL.ProgramUniform1(Handle, location, value);
		}
		else
		{
			WithProgramBound(() => Internal_OpenGL.GL.Uniform1(location, value));
		}

		return true;
	}

	public bool SetUniform(string name, int value)
	{
		int location = GetUniformLocation(name);

		if (location == -1)
		{
			return false;
		}

		if (Owner.Capabilities.SupportsProgramUniforms)
		{
			Internal_OpenGL.GL.ProgramUniform1(Handle, location, value);
		}
		else
		{
			WithProgramBound(() => Internal_OpenGL.GL.Uniform1(location, value));
		}

		return true;
	}

	internal void Bind(RenderUniformState uniforms)
	{
		EnsureCurrentOwner();
		ArgumentNullException.ThrowIfNull(uniforms);
		Internal_OpenGL.GL.GetInteger(GetPName.CurrentProgram, out int previousProgram);

		Internal_OpenGL.GL.UseProgram(Handle);

		try
		{
			uniforms.Apply(this);
			previousPrograms.Push((uint)previousProgram);
		}
		catch
		{
			Internal_OpenGL.GL.UseProgram((uint)previousProgram);

			throw;
		}
	}

	internal void Unbind()
	{
		EnsureCurrentOwner();

		if (previousPrograms.Count == 0)
		{
			throw new InvalidOperationException("The shader program is not bound.");
		}

		Internal_OpenGL.GL.UseProgram(previousPrograms.Pop());
	}

	internal override void DeleteResource()
	{
		Internal_OpenGL.GL.DeleteProgram(Handle);
	}

	public override string ToString()
	{
		return string.Join(";", stages);
	}

	private void Link()
	{
		foreach (ShaderStage stage in stages)
		{
			Internal_OpenGL.GL.AttachShader(Handle, stage.Handle);
		}

		Internal_OpenGL.GL.LinkProgram(Handle);
		Internal_OpenGL.GL.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out int status);

		foreach (ShaderStage stage in stages)
		{
			Internal_OpenGL.GL.DetachShader(Handle, stage.Handle);
		}

		if (status != 0)
		{
			return;
		}

		string log = Internal_OpenGL.GL.GetProgramInfoLog(Handle);

		throw new InvalidOperationException($"Failed to link shader program.{Environment.NewLine}{log}");
	}

	private int GetUniformLocation(string name)
	{
		EnsureCurrentOwner();
		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		if (uniformLocations.TryGetValue(name, out int location))
		{
			return location;
		}

		location = Internal_OpenGL.GL.GetUniformLocation(Handle, name);
		uniformLocations.Add(name, location);

		return location;
	}

	private void WithProgramBound(Action action)
	{
		Internal_OpenGL.GL.GetInteger(GetPName.CurrentProgram, out int previousProgram);

		try
		{
			Internal_OpenGL.GL.UseProgram(Handle);
			action();
		}
		finally
		{
			Internal_OpenGL.GL.UseProgram((uint)previousProgram);
		}
	}
}
