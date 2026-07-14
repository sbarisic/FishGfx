using System;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics;

public enum GraphicsQueryType
{
	TimeElapsed,
	SamplesPassed,
	AnySamplesPassed,
	PrimitivesGenerated,
	TransformFeedbackPrimitivesWritten,
	AnySamplesPassedConservative,
}

public sealed class GraphicsQuery : GraphicsResource
{
	private bool active;

	internal GraphicsQuery(GraphicsContext owner, GraphicsQueryType type)
		: base(owner)
	{
		if (!Enum.IsDefined(type))
		{
			throw new ArgumentOutOfRangeException(nameof(type));
		}

		Type = type;
		Handle = Internal_OpenGL.Is45OrAbove
			? Internal_OpenGL.GL.CreateQuery(ToOpenGl(type))
			: Internal_OpenGL.GL.GenQuery();

		RegisterResource();
	}

	public GraphicsQueryType Type { get; }

	public bool IsResultAvailable
	{
		get
		{
			EnsureCurrentOwner();
			EnsureInactive();
			Internal_OpenGL.GL.GetQueryObject(Handle, QueryObjectParameterName.ResultAvailable, out int available);

			return available != 0;
		}
	}

	public ulong GetResult()
	{
		EnsureCurrentOwner();
		EnsureInactive();
		Internal_OpenGL.GL.GetQueryObject(Handle, QueryObjectParameterName.Result, out ulong result);

		return result;
	}

	internal void Begin()
	{
		EnsureCurrentOwner();

		if (active)
		{
			throw new InvalidOperationException("The graphics query is already active.");
		}

		Internal_OpenGL.GL.BeginQuery(ToOpenGl(Type), Handle);
		active = true;
	}

	internal void End()
	{
		EnsureCurrentOwner();

		if (!active)
		{
			throw new InvalidOperationException("The graphics query is not active.");
		}

		try
		{
			Internal_OpenGL.GL.EndQuery(ToOpenGl(Type));
		}
		finally
		{
			active = false;
		}
	}

	internal override void DeleteResource()
	{
		if (active)
		{
			throw new InvalidOperationException("An active graphics query cannot be deleted.");
		}

		Internal_OpenGL.GL.DeleteQuery(Handle);
	}

	private static QueryTarget ToOpenGl(GraphicsQueryType type)
	{
		return type switch
		{
			GraphicsQueryType.TimeElapsed => QueryTarget.TimeElapsed,
			GraphicsQueryType.SamplesPassed => QueryTarget.SamplesPassed,
			GraphicsQueryType.AnySamplesPassed => QueryTarget.AnySamplesPassed,
			GraphicsQueryType.PrimitivesGenerated => QueryTarget.PrimitivesGenerated,
			GraphicsQueryType.TransformFeedbackPrimitivesWritten => QueryTarget.TransformFeedbackPrimitivesWritten,
			GraphicsQueryType.AnySamplesPassedConservative => QueryTarget.AnySamplesPassedConservative,
			_ => throw new ArgumentOutOfRangeException(nameof(type)),
		};
	}

	private void EnsureInactive()
	{
		if (active)
		{
			throw new InvalidOperationException("The result of an active graphics query is not available.");
		}
	}
}
