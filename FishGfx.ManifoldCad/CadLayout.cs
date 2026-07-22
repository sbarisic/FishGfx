using System.Numerics;

namespace FishGfx.ManifoldCad;

internal readonly record struct CadRect(float X, float Y, float Width, float Height)
{
	internal Vector2 Minimum => new(X, Y);

	internal Vector2 Maximum => new(X + Width, Y + Height);

	internal bool Contains(Vector2 point)
	{
		return point.X >= X
			&& point.X <= X + Width
			&& point.Y >= Y
			&& point.Y <= Y + Height;
	}
}

internal static class CadLayout
{
	internal const float ToolbarHeight = 48;
	internal const float LeftWidth = 260;
	internal const float RightWidth = 320;
	internal const float GraphHeight = 320;

	internal static CadRect Viewport(int width, int height)
	{
		return new CadRect(
			LeftWidth,
			GraphHeight,
			Math.Max(1, width - LeftWidth - RightWidth),
			Math.Max(1, height - ToolbarHeight - GraphHeight)
		);
	}

	internal static CadRect Graph(int width)
	{
		return new CadRect(LeftWidth, 0, Math.Max(1, width - LeftWidth - RightWidth), GraphHeight);
	}
}
