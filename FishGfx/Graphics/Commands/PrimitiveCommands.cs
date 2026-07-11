using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FishGfx.Graphics
{
	public sealed class Point2DCommand : GraphicsCommand
	{
		private readonly Vertex2[] points;

		public IReadOnlyList<Vertex2> Points { get; }
		public float? Thickness { get; }

		public Point2DCommand(Vertex2[] points, float? thickness = null)
		{
			if (points == null)
				throw new ArgumentNullException(nameof(points));

			this.points = (Vertex2[])points.Clone();
			Points = new ReadOnlyCollection<Vertex2>(this.points);
			Thickness = thickness;
		}

		public override void Execute()
		{
			if (Thickness.HasValue)
				Gfx.Point(points, Thickness.Value);
			else
				Gfx.Point(points);
		}
	}

	public sealed class Line2DCommand : GraphicsCommand
	{
		public Vertex2 Start { get; }
		public Vertex2 End { get; }
		public float Thickness { get; }

		public Line2DCommand(Vertex2 start, Vertex2 end, float thickness = 1)
		{
			Start = start;
			End = end;
			Thickness = thickness;
		}

		public override void Execute() => Gfx.Line(Start, End, Thickness);
	}

	public sealed class LineStrip2DCommand : GraphicsCommand
	{
		private readonly Vertex2[] points;

		public IReadOnlyList<Vertex2> Points { get; }
		public float Thickness { get; }

		public LineStrip2DCommand(Vertex2[] points, float thickness = 1)
		{
			if (points == null)
				throw new ArgumentNullException(nameof(points));

			this.points = (Vertex2[])points.Clone();
			Points = new ReadOnlyCollection<Vertex2>(this.points);
			Thickness = thickness;
		}

		public override void Execute() => Gfx.LineStrip(points, Thickness);
	}

	public sealed class Point3DCommand : GraphicsCommand
	{
		private readonly Vertex3[] points;

		public IReadOnlyList<Vertex3> Points { get; }
		public float? Thickness { get; }

		public Point3DCommand(Vertex3[] points, float? thickness = null)
		{
			if (points == null)
				throw new ArgumentNullException(nameof(points));

			this.points = (Vertex3[])points.Clone();
			Points = new ReadOnlyCollection<Vertex3>(this.points);
			Thickness = thickness;
		}

		public override void Execute()
		{
			if (Thickness.HasValue)
				Gfx.Point(points, Thickness.Value);
			else
				Gfx.Point(points);
		}
	}

	public sealed class Line3DCommand : GraphicsCommand
	{
		public Vertex3 Start { get; }
		public Vertex3 End { get; }
		public float Thickness { get; }

		public Line3DCommand(Vertex3 start, Vertex3 end, float thickness = 1)
		{
			Start = start;
			End = end;
			Thickness = thickness;
		}

		public override void Execute() => Gfx.Line(Start, End, Thickness);
	}
}
