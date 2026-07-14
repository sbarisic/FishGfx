using System;
using System.Numerics;

namespace FishGfx.Graphics;

public sealed partial class RenderCommandList
{
	public EllipseCommand RecordDrawEllipse(
		Vector2 center,
		Vector2 radii,
		float thickness = 1,
		Color? color = null,
		int segments = 0
	)
	{
		return Add(new EllipseCommand(center, radii, thickness, color, segments));
	}

	public EllipseCommand RecordDrawCircle(
		Vector2 center,
		float radius,
		float thickness = 1,
		Color? color = null,
		int segments = 0
	)
	{
		return RecordDrawEllipse(center, new Vector2(radius), thickness, color, segments);
	}

	public FillEllipseCommand RecordFillEllipse(
		Vector2 center,
		Vector2 radii,
		Color? color = null,
		int segments = 0
	)
	{
		return Add(new FillEllipseCommand(center, radii, color, segments));
	}

	public FillEllipseCommand RecordFillCircle(
		Vector2 center,
		float radius,
		Color? color = null,
		int segments = 0
	)
	{
		return RecordFillEllipse(center, new Vector2(radius), color, segments);
	}

	public TexturedEllipseCommand RecordDrawTexturedEllipse(
		Vector2 center,
		Vector2 radii,
		Texture texture,
		float u0 = 0,
		float v0 = 0,
		float u1 = 1,
		float v1 = 1,
		Color? color = null,
		ShaderProgram shader = null,
		int segments = 0
	)
	{
		return RecordDrawTexturedEllipse(
			center,
			radii,
			texture,
			new Vector2(u0, v0),
			new Vector2(u1, v1),
			color,
			shader,
			segments
		);
	}

	public TexturedEllipseCommand RecordDrawTexturedEllipse(
		Vector2 center,
		Vector2 radii,
		Texture texture,
		Vector2 uvMinimum,
		Vector2 uvMaximum,
		Color? color = null,
		ShaderProgram shader = null,
		int segments = 0
	)
	{
		return Add(
			new TexturedEllipseCommand(
				center,
				radii,
				texture,
				uvMinimum,
				uvMaximum,
				color,
				shader,
				segments
			)
		);
	}

	public TexturedEllipseCommand RecordDrawTexturedCircle(
		Vector2 center,
		float radius,
		Texture texture,
		float u0 = 0,
		float v0 = 0,
		float u1 = 1,
		float v1 = 1,
		Color? color = null,
		ShaderProgram shader = null,
		int segments = 0
	)
	{
		return RecordDrawTexturedCircle(
			center,
			radius,
			texture,
			new Vector2(u0, v0),
			new Vector2(u1, v1),
			color,
			shader,
			segments
		);
	}

	public TexturedEllipseCommand RecordDrawTexturedCircle(
		Vector2 center,
		float radius,
		Texture texture,
		Vector2 uvMinimum,
		Vector2 uvMaximum,
		Color? color = null,
		ShaderProgram shader = null,
		int segments = 0
	)
	{
		return RecordDrawTexturedEllipse(
			center,
			new Vector2(radius),
			texture,
			uvMinimum,
			uvMaximum,
			color,
			shader,
			segments
		);
	}

	public FillRingCommand RecordFillRing(
		Vector2 center,
		float innerRadius,
		float outerRadius,
		float startAngle,
		float endAngle,
		Color? color = null,
		int segments = 0
	)
	{
		return Add(
			new FillRingCommand(
				center,
				innerRadius,
				outerRadius,
				startAngle,
				endAngle,
				color,
				segments
			)
		);
	}

	public FillRingCommand RecordFillRing(
		Vector2 center,
		float innerRadius,
		float outerRadius,
		Color? color = null,
		int segments = 0
	)
	{
		return RecordFillRing(
			center,
			innerRadius,
			outerRadius,
			0,
			MathF.Tau,
			color,
			segments
		);
	}

	public RingCommand RecordDrawRing(
		Vector2 center,
		float innerRadius,
		float outerRadius,
		float startAngle,
		float endAngle,
		float thickness = 1,
		Color? color = null,
		int segments = 0
	)
	{
		return Add(
			new RingCommand(
				center,
				innerRadius,
				outerRadius,
				startAngle,
				endAngle,
				thickness,
				color,
				segments
			)
		);
	}

	public RingCommand RecordDrawRing(
		Vector2 center,
		float innerRadius,
		float outerRadius,
		float thickness = 1,
		Color? color = null,
		int segments = 0
	)
	{
		return RecordDrawRing(
			center,
			innerRadius,
			outerRadius,
			0,
			MathF.Tau,
			thickness,
			color,
			segments
		);
	}

	public QuadraticBezierCommand RecordDrawQuadraticBezier(
		Vector2 start,
		Vector2 control,
		Vector2 end,
		float thickness = 1,
		Color? color = null,
		int segments = 0
	)
	{
		return Add(new QuadraticBezierCommand(start, control, end, thickness, color, segments));
	}

	public CubicBezierCommand RecordDrawCubicBezier(
		Vector2 start,
		Vector2 control1,
		Vector2 control2,
		Vector2 end,
		float thickness = 1,
		Color? color = null,
		int segments = 0
	)
	{
		return Add(
			new CubicBezierCommand(
				start,
				control1,
				control2,
				end,
				thickness,
				color,
				segments
			)
		);
	}
}
