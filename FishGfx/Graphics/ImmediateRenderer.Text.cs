using System;
using System.Numerics;

namespace FishGfx.Graphics;

internal sealed partial class ImmediateRenderer
{
	internal Vector2 DrawText(
		RenderPass pass,
		GraphicsFont font,
		Vector2 position,
		string text,
		Color color,
		float size,
		float characterSpacing,
		bool debugDraw
	)
	{
		ArgumentNullException.ThrowIfNull(font);
		ArgumentNullException.ThrowIfNull(text);

		if (!float.IsFinite(size) || size <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(size));
		}

		if (!float.IsFinite(characterSpacing))
		{
			throw new ArgumentOutOfRangeException(nameof(characterSpacing));
		}

		if (text.Length == 0)
		{
			return Vector2.Zero;
		}

		FontAtlas atlas = font.PrepareAtlas(context, text);
		PositionedGlyph[] glyphs = font.LayoutAndMeasure(
			text,
			size,
			characterSpacing,
			out Vector2 measuredSize
		);

		ValidateAtlas(atlas);

		if (glyphs.Length > 0)
		{
			ShaderProgram shader = SelectTextShader(atlas);
			Vertex2[] vertices = CreateTextVertices(
				glyphs,
				position,
				atlas,
				color
			);

			DrawTexturedTriangles(pass, vertices, atlas.Texture, shader);
		}

		if (debugDraw)
		{
			DrawTextDebugBounds(pass, font, glyphs, position);
		}

		return measuredSize;
	}

	private ShaderProgram SelectTextShader(FontAtlas atlas)
	{
		Ensure2DResources(PrimitiveType.Triangles);

		switch (atlas.RenderMode)
		{
			case FontRenderMode.Bitmap:
				return default2D;
			case FontRenderMode.SignedDistanceField:
				sdfText2D.SetUniform("uSdfPixelRange", atlas.SdfPixelRange);

				return sdfText2D;
			default:
				throw new ArgumentOutOfRangeException(nameof(atlas));
		}
	}

	private void ValidateAtlas(FontAtlas atlas)
	{
		if (atlas == null)
		{
			throw new InvalidOperationException("The font did not provide an atlas.");
		}

		if (atlas.IsDisposed)
		{
			throw new ObjectDisposedException(nameof(atlas));
		}

		if (!ReferenceEquals(atlas.Owner, context))
		{
			throw new InvalidOperationException(
				"The font atlas belongs to another graphics context."
			);
		}
	}

	private static Vertex2[] CreateTextVertices(
		PositionedGlyph[] glyphs,
		Vector2 position,
		FontAtlas atlas,
		Color color
	)
	{
		Vertex2[] vertices = new Vertex2[checked(glyphs.Length * 6)];
		Vector2 inverseAtlasSize = new(1f / atlas.Width, 1f / atlas.Height);

		for (int index = 0; index < glyphs.Length; index++)
		{
			PositionedGlyph positionedGlyph = glyphs[index];
			GlyphMetrics glyph = positionedGlyph.Glyph;
			Vector2 uvMinimum = glyph.AtlasPosition * inverseAtlasSize;
			Vector2 uvSize = glyph.AtlasSize * inverseAtlasSize;
			float u0 = uvMinimum.X;
			float v0 = 1 - uvMinimum.Y - uvSize.Y;
			float u1 = uvMinimum.X + uvSize.X;
			float v1 = 1 - uvMinimum.Y;
			Vector2 glyphPosition = position + positionedGlyph.Position;

			EmitRectangleTriangles(
				vertices,
				index * 6,
				glyphPosition.X,
				glyphPosition.Y,
				positionedGlyph.Size.X,
				positionedGlyph.Size.Y,
				u0,
				v0,
				u1,
				v1,
				color
			);
		}

		return vertices;
	}

	private void DrawTextDebugBounds(
		RenderPass pass,
		GraphicsFont font,
		PositionedGlyph[] glyphs,
		Vector2 position
	)
	{
		FillRectangle(pass, position.X, position.Y, 5, 5, Color.Yellow);

		if (glyphs.Length == 0)
		{
			return;
		}

		Vector2 firstPosition = position + glyphs[0].Position;
		FillRectangle(pass, firstPosition.X, firstPosition.Y, 5, 5, Color.Red);
		font.MeasureBounds(glyphs, out Vector2 minimum, out Vector2 maximum);
		Vector2 boundsPosition = position + minimum;
		Vector2 boundsSize = maximum - minimum;
		DrawRectangle(
			pass,
			boundsPosition.X,
			boundsPosition.Y,
			boundsSize.X,
			boundsSize.Y,
			1,
			Color.Red
		);
	}
}
