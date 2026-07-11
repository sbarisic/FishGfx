using System;
using System.Numerics;

namespace FishGfx.Graphics
{
	public sealed class RectangleCommand : GraphicsCommand
	{
		public RectangleCommand(float x, float y, float width, float height, float thickness = 1, Color? color = null)
		{
			X = x;
			Y = y;
			Width = width;
			Height = height;
			Thickness = thickness;
			Color = color;
		}

		public float X { get; }
		public float Y { get; }
		public float Width { get; }
		public float Height { get; }
		public float Thickness { get; }
		public Color? Color { get; }

		public override void Execute() => Gfx.Rectangle(X, Y, Width, Height, Thickness, Color);
	}

	public sealed class FilledRectangleCommand : GraphicsCommand
	{
		public FilledRectangleCommand(float x, float y, float width, float height, Color? color = null)
		{
			X = x;
			Y = y;
			Width = width;
			Height = height;
			Color = color;
		}

		public float X { get; }
		public float Y { get; }
		public float Width { get; }
		public float Height { get; }
		public Color? Color { get; }

		public override void Execute() => Gfx.FilledRectangle(X, Y, Width, Height, Color);
	}

	public sealed class TexturedRectangleCommand : GraphicsCommand
	{
		public TexturedRectangleCommand(
			float x,
			float y,
			float width,
			float height,
			float u0 = 0,
			float v0 = 0,
			float u1 = 1,
			float v1 = 1,
			Color? color = null,
			Texture texture = null,
			ShaderProgram shader = null
		)
		{
			X = x;
			Y = y;
			Width = width;
			Height = height;
			U0 = u0;
			V0 = v0;
			U1 = u1;
			V1 = v1;
			Color = color;
			Texture = texture;
			Shader = shader;
		}

		public float X { get; }
		public float Y { get; }
		public float Width { get; }
		public float Height { get; }
		public float U0 { get; }
		public float V0 { get; }
		public float U1 { get; }
		public float V1 { get; }
		public Color? Color { get; }
		public Texture Texture { get; }
		public ShaderProgram Shader { get; }

		public override void Execute() =>
			Gfx.TexturedRectangle(X, Y, Width, Height, U0, V0, U1, V1, Color, Texture, Shader);
	}

	public sealed class NinePatchCommand : GraphicsCommand
	{
		public NinePatchCommand(
			Vector2 position,
			Vector2 size,
			Texture texture,
			NinePatchInsets insets,
			Color? color = null,
			ShaderProgram shader = null
		)
		{
			Texture = texture ?? throw new ArgumentNullException(nameof(texture));
			Position = position;
			Size = size;
			Insets = insets;
			Color = color;
			Shader = shader;
		}

		public Vector2 Position { get; }
		public Vector2 Size { get; }
		public Texture Texture { get; }
		public NinePatchInsets Insets { get; }
		public Color? Color { get; }
		public ShaderProgram Shader { get; }

		public override void Execute() => Gfx.NinePatch(Position, Size, Texture, Insets, Color, Shader);
	}

	public sealed class RoundedRectangleCommand : GraphicsCommand
	{
		public RoundedRectangleCommand(
			Vector2 position,
			Vector2 size,
			CornerRadii radii,
			float thickness = 1,
			Color? color = null,
			int cornerSegments = 0
		)
		{
			Position = position;
			Size = size;
			Radii = radii;
			Thickness = thickness;
			Color = color;
			CornerSegments = cornerSegments;
		}

		public Vector2 Position { get; }
		public Vector2 Size { get; }
		public CornerRadii Radii { get; }
		public float Thickness { get; }
		public Color? Color { get; }
		public int CornerSegments { get; }

		public override void Execute() =>
			Gfx.RoundedRectangle(Position, Size, Radii, Thickness, Color, CornerSegments);
	}

	public sealed class FilledRoundedRectangleCommand : GraphicsCommand
	{
		public FilledRoundedRectangleCommand(
			Vector2 position,
			Vector2 size,
			CornerRadii radii,
			Color? color = null,
			int cornerSegments = 0
		)
		{
			Position = position;
			Size = size;
			Radii = radii;
			Color = color;
			CornerSegments = cornerSegments;
		}

		public Vector2 Position { get; }
		public Vector2 Size { get; }
		public CornerRadii Radii { get; }
		public Color? Color { get; }
		public int CornerSegments { get; }

		public override void Execute() => Gfx.FilledRoundedRectangle(Position, Size, Radii, Color, CornerSegments);
	}

	public sealed class TexturedRoundedRectangleCommand : GraphicsCommand
	{
		public TexturedRoundedRectangleCommand(
			Vector2 position,
			Vector2 size,
			CornerRadii radii,
			Texture texture,
			Vector2 uvMin,
			Vector2 uvMax,
			Color? color = null,
			ShaderProgram shader = null,
			int cornerSegments = 0
		)
		{
			Texture = texture ?? throw new ArgumentNullException(nameof(texture));
			Position = position;
			Size = size;
			Radii = radii;
			UVMin = uvMin;
			UVMax = uvMax;
			Color = color;
			Shader = shader;
			CornerSegments = cornerSegments;
		}

		public Vector2 Position { get; }
		public Vector2 Size { get; }
		public CornerRadii Radii { get; }
		public Texture Texture { get; }
		public Vector2 UVMin { get; }
		public Vector2 UVMax { get; }
		public Color? Color { get; }
		public ShaderProgram Shader { get; }
		public int CornerSegments { get; }

		public override void Execute() =>
			Gfx.TexturedRoundedRectangle(Position, Size, Radii, Texture, UVMin, UVMax, Color, Shader, CornerSegments);
	}

	public sealed class EllipseCommand : GraphicsCommand
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

		public override void Execute() => Gfx.Ellipse(Center, Radii, Thickness, Color, Segments);
	}

	public sealed class FilledEllipseCommand : GraphicsCommand
	{
		public FilledEllipseCommand(Vector2 center, Vector2 radii, Color? color = null, int segments = 0)
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

		public override void Execute() => Gfx.FilledEllipse(Center, Radii, Color, Segments);
	}

	public sealed class TexturedEllipseCommand : GraphicsCommand
	{
		public TexturedEllipseCommand(
			Vector2 center,
			Vector2 radii,
			Texture texture,
			Vector2 uvMin,
			Vector2 uvMax,
			Color? color = null,
			ShaderProgram shader = null,
			int segments = 0
		)
		{
			Texture = texture ?? throw new ArgumentNullException(nameof(texture));
			Center = center;
			Radii = radii;
			UVMin = uvMin;
			UVMax = uvMax;
			Color = color;
			Shader = shader;
			Segments = segments;
		}

		public Vector2 Center { get; }
		public Vector2 Radii { get; }
		public Texture Texture { get; }
		public Vector2 UVMin { get; }
		public Vector2 UVMax { get; }
		public Color? Color { get; }
		public ShaderProgram Shader { get; }
		public int Segments { get; }

		public override void Execute() => Gfx.TexturedEllipse(
			Center,
			Radii,
			Texture,
			UVMin.X,
			UVMin.Y,
			UVMax.X,
			UVMax.Y,
			Color,
			Shader,
			Segments
		);
	}

	public sealed class RingCommand : GraphicsCommand
	{
		public RingCommand(
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

		public override void Execute() =>
			Gfx.Ring(Center, InnerRadius, OuterRadius, StartAngle, EndAngle, Color, Segments);
	}

	public sealed class RingLinesCommand : GraphicsCommand
	{
		public RingLinesCommand(
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

		public override void Execute() =>
			Gfx.RingLines(Center, InnerRadius, OuterRadius, StartAngle, EndAngle, Thickness, Color, Segments);
	}

	public sealed class QuadraticBezierCommand : GraphicsCommand
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

		public override void Execute() => Gfx.QuadraticBezier(Start, Control, End, Thickness, Color, Segments);
	}

	public sealed class CubicBezierCommand : GraphicsCommand
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

		public override void Execute() =>
			Gfx.CubicBezier(Start, Control1, Control2, End, Thickness, Color, Segments);
	}
}
