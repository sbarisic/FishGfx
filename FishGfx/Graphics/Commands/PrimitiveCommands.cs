using System;
using System.Collections.Generic;

namespace FishGfx.Graphics;

public sealed class Point2DCommand : RenderCommand
{
	private readonly Vertex2[] points;

	public Point2DCommand(Vertex2[] points, float? thickness = null)
	{
		ArgumentNullException.ThrowIfNull(points);

		this.points = (Vertex2[])points.Clone();
		Points = Array.AsReadOnly(this.points);
		Thickness = thickness;
	}

	public IReadOnlyList<Vertex2> Points { get; }

	public float? Thickness { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);

		if (Thickness.HasValue)
		{
			pass.DrawPoint(points, Thickness.Value);
			return;
		}

		pass.DrawPoint(points);
	}
}

public sealed class Line2DCommand : RenderCommand
{
	public Line2DCommand(Vertex2 start, Vertex2 end, float thickness = 1)
	{
		Start = start;
		End = end;
		Thickness = thickness;
	}

	public Vertex2 Start { get; }

	public Vertex2 End { get; }

	public float Thickness { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.DrawLine(Start, End, Thickness);
	}
}

public sealed class LineStrip2DCommand : RenderCommand
{
	private readonly Vertex2[] points;

	public LineStrip2DCommand(Vertex2[] points, float thickness = 1)
	{
		ArgumentNullException.ThrowIfNull(points);

		this.points = (Vertex2[])points.Clone();
		Points = Array.AsReadOnly(this.points);
		Thickness = thickness;
	}

	public IReadOnlyList<Vertex2> Points { get; }

	public float Thickness { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.DrawLineStrip(points, Thickness);
	}
}

public sealed class Point3DCommand : RenderCommand
{
	private readonly Vertex3[] points;

	public Point3DCommand(Vertex3[] points, float? thickness = null)
	{
		ArgumentNullException.ThrowIfNull(points);

		this.points = (Vertex3[])points.Clone();
		Points = Array.AsReadOnly(this.points);
		Thickness = thickness;
	}

	public IReadOnlyList<Vertex3> Points { get; }

	public float? Thickness { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);

		if (Thickness.HasValue)
		{
			pass.DrawPoint(points, Thickness.Value);
			return;
		}

		pass.DrawPoint(points);
	}
}

public sealed class Line3DCommand : RenderCommand
{
	public Line3DCommand(Vertex3 start, Vertex3 end, float thickness = 1)
	{
		Start = start;
		End = end;
		Thickness = thickness;
	}

	public Vertex3 Start { get; }

	public Vertex3 End { get; }

	public float Thickness { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.DrawLine(Start, End, Thickness);
	}
}
