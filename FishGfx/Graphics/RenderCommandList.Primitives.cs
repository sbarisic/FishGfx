namespace FishGfx.Graphics;

public sealed partial class RenderCommandList
{
	public Point2DCommand RecordDrawPoint(Vertex2[] points, float thickness = 1)
	{
		return Add(new Point2DCommand(points, thickness));
	}

	public Point2DCommand RecordDrawPoint(Vertex2 point, float thickness = 1)
	{
		return RecordDrawPoint(new[] { point }, thickness);
	}

	public Point3DCommand RecordDrawPoint(Vertex3[] points, float thickness = 1)
	{
		return Add(new Point3DCommand(points, thickness));
	}

	public Point3DCommand RecordDrawPoint(Vertex3 point, float thickness = 1)
	{
		return RecordDrawPoint(new[] { point }, thickness);
	}

	public Line2DCommand RecordDrawLine(Vertex2 start, Vertex2 end, float thickness = 1)
	{
		return Add(new Line2DCommand(start, end, thickness));
	}

	public Line3DCommand RecordDrawLine(Vertex3 start, Vertex3 end, float thickness = 1)
	{
		return Add(new Line3DCommand(start, end, thickness));
	}

	public LineStrip2DCommand RecordDrawLineStrip(Vertex2[] points, float thickness = 1)
	{
		return Add(new LineStrip2DCommand(points, thickness));
	}
}
