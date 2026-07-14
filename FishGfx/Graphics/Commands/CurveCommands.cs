using System;
using System.Numerics;

namespace FishGfx.Graphics;

public sealed class EllipseCommand : RenderCommand
{
	public EllipseCommand(
		Vector2 center,
		Vector2 radii,
		float thickness = 1,
		Color? color = null,
		int segments = 0
	)
	{
		Center = center;
		Radii = radii;
		Thickness = thickness;
		Color = color;
		Segments = segments;
	}

	public Vector2 Center { get; }

	public Vector2 Radii { get; }

	public float Thickness { get; }

	public Color? Color { get; }

	public int Segments { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.DrawEllipse(Center, Radii, Thickness, Color, Segments);
	}
}

public sealed class FillEllipseCommand : RenderCommand
{
	public FillEllipseCommand(Vector2 center, Vector2 radii, Color? color = null, int segments = 0)
	{
		Center = center;
		Radii = radii;
		Color = color;
		Segments = segments;
	}

	public Vector2 Center { get; }

	public Vector2 Radii { get; }

	public Color? Color { get; }

	public int Segments { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.FillEllipse(Center, Radii, Color, Segments);
	}
}

public sealed class TexturedEllipseCommand : RenderCommand
{
	public TexturedEllipseCommand(
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
		Texture = texture ?? throw new ArgumentNullException(nameof(texture));
		Center = center;
		Radii = radii;
		UvMinimum = uvMinimum;
		UvMaximum = uvMaximum;
		Color = color;
		Shader = shader;
		Segments = segments;
	}

	public Vector2 Center { get; }

	public Vector2 Radii { get; }

	public Texture Texture { get; }

	public Vector2 UvMinimum { get; }

	public Vector2 UvMaximum { get; }

	public Color? Color { get; }

	public ShaderProgram Shader { get; }

	public int Segments { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.DrawTexturedEllipse(
			Center,
			Radii,
			Texture,
			UvMinimum.X,
			UvMinimum.Y,
			UvMaximum.X,
			UvMaximum.Y,
			Color,
			Shader,
			Segments
		);
	}
}

public sealed class FillRingCommand : RenderCommand
{
	public FillRingCommand(
		Vector2 center,
		float innerRadius,
		float outerRadius,
		float startAngle,
		float endAngle,
		Color? color = null,
		int segments = 0
	)
	{
		Center = center;
		InnerRadius = innerRadius;
		OuterRadius = outerRadius;
		StartAngle = startAngle;
		EndAngle = endAngle;
		Color = color;
		Segments = segments;
	}

	public Vector2 Center { get; }

	public float InnerRadius { get; }

	public float OuterRadius { get; }

	public float StartAngle { get; }

	public float EndAngle { get; }

	public Color? Color { get; }

	public int Segments { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.FillRing(
			Center,
			InnerRadius,
			OuterRadius,
			StartAngle,
			EndAngle,
			Color,
			Segments
		);
	}
}

public sealed class RingCommand : RenderCommand
{
	public RingCommand(
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
		Center = center;
		InnerRadius = innerRadius;
		OuterRadius = outerRadius;
		StartAngle = startAngle;
		EndAngle = endAngle;
		Thickness = thickness;
		Color = color;
		Segments = segments;
	}

	public Vector2 Center { get; }

	public float InnerRadius { get; }

	public float OuterRadius { get; }

	public float StartAngle { get; }

	public float EndAngle { get; }

	public float Thickness { get; }

	public Color? Color { get; }

	public int Segments { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.DrawRing(
			Center,
			InnerRadius,
			OuterRadius,
			StartAngle,
			EndAngle,
			Thickness,
			Color,
			Segments
		);
	}
}

public sealed class QuadraticBezierCommand : RenderCommand
{
	public QuadraticBezierCommand(
		Vector2 start,
		Vector2 control,
		Vector2 end,
		float thickness = 1,
		Color? color = null,
		int segments = 0
	)
	{
		Start = start;
		Control = control;
		End = end;
		Thickness = thickness;
		Color = color;
		Segments = segments;
	}

	public Vector2 Start { get; }

	public Vector2 Control { get; }

	public Vector2 End { get; }

	public float Thickness { get; }

	public Color? Color { get; }

	public int Segments { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.DrawQuadraticBezier(Start, Control, End, Thickness, Color, Segments);
	}
}

public sealed class CubicBezierCommand : RenderCommand
{
	public CubicBezierCommand(
		Vector2 start,
		Vector2 control1,
		Vector2 control2,
		Vector2 end,
		float thickness = 1,
		Color? color = null,
		int segments = 0
	)
	{
		Start = start;
		Control1 = control1;
		Control2 = control2;
		End = end;
		Thickness = thickness;
		Color = color;
		Segments = segments;
	}

	public Vector2 Start { get; }

	public Vector2 Control1 { get; }

	public Vector2 Control2 { get; }

	public Vector2 End { get; }

	public float Thickness { get; }

	public Color? Color { get; }

	public int Segments { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.DrawCubicBezier(
			Start,
			Control1,
			Control2,
			End,
			Thickness,
			Color,
			Segments
		);
	}
}
