using System;
using System.Numerics;
using FishGfx.Formats;
using FishGfx.Graphics;

namespace FishGfx.FishUI;

public sealed partial class FishUIGraphicsBackend
{
	public override void DrawLine(
		Vector2 position1,
		Vector2 position2,
		float thickness,
		global::FishUI.FishColor color
	)
	{
		RenderPass activePass = RequireDrawing();
		activePass.DrawLine(
			new Vertex2(
				FishUIConversions.ToFishGfxPoint(position1, GetWindowHeight()),
				FishUIConversions.ToFishGfxColor(color)
			),
			new Vertex2(
				FishUIConversions.ToFishGfxPoint(position2, GetWindowHeight()),
				FishUIConversions.ToFishGfxColor(color)
			),
			thickness
		);
		LastFrameDrawCallCount++;
	}

	public override void DrawRectangle(
		Vector2 position,
		Vector2 size,
		global::FishUI.FishColor color
	)
	{
		Vector2 converted = FishUIConversions.ToFishGfxRectanglePosition(
			position,
			size,
			GetWindowHeight()
		);
		RequireDrawing().FillRectangle(
			converted.X,
			converted.Y,
			size.X,
			size.Y,
			FishUIConversions.ToFishGfxColor(color)
		);
		LastFrameDrawCallCount++;
	}

	public override void DrawRectangleOutline(
		Vector2 position,
		Vector2 size,
		global::FishUI.FishColor color
	)
	{
		Vector2 converted = FishUIConversions.ToFishGfxRectanglePosition(
			position,
			size,
			GetWindowHeight()
		);
		RequireDrawing().DrawRectangle(
			converted.X,
			converted.Y,
			size.X,
			size.Y,
			1,
			FishUIConversions.ToFishGfxColor(color)
		);
		LastFrameDrawCallCount++;
	}

	public override void DrawCircle(
		Vector2 center,
		float radius,
		global::FishUI.FishColor color
	)
	{
		RequireDrawing().FillCircle(
			FishUIConversions.ToFishGfxPoint(center, GetWindowHeight()),
			radius,
			FishUIConversions.ToFishGfxColor(color)
		);
		LastFrameDrawCallCount++;
	}

	public override void DrawCircleOutline(
		Vector2 center,
		float radius,
		global::FishUI.FishColor color,
		float thickness = 1
	)
	{
		RequireDrawing().DrawCircle(
			FishUIConversions.ToFishGfxPoint(center, GetWindowHeight()),
			radius,
			thickness,
			FishUIConversions.ToFishGfxColor(color)
		);
		LastFrameDrawCallCount++;
	}

	public override void DrawImage(
		global::FishUI.ImageRef image,
		Vector2 position,
		float rotation,
		float scale,
		global::FishUI.FishColor color
	)
	{
		DrawImage(
			image,
			position,
			new Vector2(image.Width, image.Height),
			rotation,
			scale,
			color
		);
	}

	public override void DrawImage(
		global::FishUI.ImageRef image,
		Vector2 position,
		Vector2 size,
		float rotation,
		float scale,
		global::FishUI.FishColor color
	)
	{
		if (!float.IsFinite(scale) || scale < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(scale));
		}

		Vector2 destinationSize = size * scale;
		(Vector2 sourcePosition, Vector2 sourceSize) = GetSourceRegion(image);
		DrawImageRegionCore(
			image,
			sourcePosition,
			sourceSize,
			position,
			destinationSize,
			rotation,
			color
		);
	}

	protected override void DrawImageRegion(
		global::FishUI.ImageRef image,
		Vector2 sourcePosition,
		Vector2 sourceSize,
		Vector2 destinationPosition,
		Vector2 destinationSize,
		global::FishUI.FishColor color
	)
	{
		(Vector2 basePosition, _) = GetSourceRegion(image);
		DrawImageRegionCore(
			image,
			basePosition + sourcePosition,
			sourceSize,
			destinationPosition,
			destinationSize,
			0,
			color
		);
	}

	public override void DrawNPatch(
		global::FishUI.NPatch patch,
		Vector2 position,
		Vector2 size,
		global::FishUI.FishColor color
	)
	{
		base.DrawNPatch(patch, position, size, color, 0);
	}

	public override void DrawNPatch(
		global::FishUI.NPatch patch,
		Vector2 position,
		Vector2 size,
		global::FishUI.FishColor color,
		float rotation
	)
	{
		if (rotation == 0)
		{
			base.DrawNPatch(patch, position, size, color, 0);

			return;
		}

		Vector2 center = FishUIConversions.ToFishGfxPoint(
			position + size / 2,
			GetWindowHeight()
		);
		Matrix4x4 transform = CreateScreenRotation(center, rotation);

		using (RequireDrawing().PushModel(transform))
		{
			base.DrawNPatch(patch, position, size, color, 0);
		}
	}

	public override void DrawTextColorScale(
		global::FishUI.FontRef fontReference,
		string text,
		Vector2 position,
		global::FishUI.FishColor color,
		float scale
	)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}

		if (!float.IsFinite(scale) || scale <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(scale));
		}

		TrueTypeFont font = GetFont(fontReference);
		FishUITextLayout layout = FishUITextLayout.Create(
			font,
			text,
			fontReference.Size,
			fontReference.Spacing,
			scale
		);
		Vector2 converted = FishUIConversions.ToFishGfxRectanglePosition(
			position,
			layout.Size,
			GetWindowHeight()
		);
		RequireDrawing().DrawText(
			font,
			converted,
			text,
			FishUIConversions.ToFishGfxColor(color),
			layout.FontSize,
			layout.CharacterSpacing
		);
		LastFrameDrawCallCount++;
	}

	private void DrawImageRegionCore(
		global::FishUI.ImageRef image,
		Vector2 sourcePosition,
		Vector2 sourceSize,
		Vector2 destinationPosition,
		Vector2 destinationSize,
		float rotation,
		global::FishUI.FishColor color
	)
	{
		ImageResource resource = GetImageResource(image);
		(Vector2 uvMinimum, Vector2 uvMaximum) = FishUIConversions.ToAtlasUv(
			sourcePosition,
			sourceSize,
			resource.Texture.Width,
			resource.Texture.Height
		);
		Vector2 converted = FishUIConversions.ToFishGfxRectanglePosition(
			destinationPosition,
			destinationSize,
			GetWindowHeight()
		);
		RenderPass activePass = RequireDrawing();

		if (rotation == 0)
		{
			DrawImageRectangle(
				activePass,
				resource.Texture,
				converted,
				destinationSize,
				uvMinimum,
				uvMaximum,
				color
			);
		}
		else
		{
			Vector2 anchor = FishUIConversions.ToFishGfxPoint(
				destinationPosition,
				GetWindowHeight()
			);

			using (activePass.PushModel(CreateScreenRotation(anchor, rotation)))
			{
				DrawImageRectangle(
					activePass,
					resource.Texture,
					converted,
					destinationSize,
					uvMinimum,
					uvMaximum,
					color
				);
			}
		}

		LastFrameDrawCallCount++;
	}

	private static void DrawImageRectangle(
		RenderPass pass,
		Texture texture,
		Vector2 position,
		Vector2 size,
		Vector2 uvMinimum,
		Vector2 uvMaximum,
		global::FishUI.FishColor color
	)
	{
		pass.DrawTexturedRectangle(
			position.X,
			position.Y,
			size.X,
			size.Y,
			uvMinimum.X,
			uvMinimum.Y,
			uvMaximum.X,
			uvMaximum.Y,
			FishUIConversions.ToFishGfxColor(color),
			texture
		);
	}

	private static Matrix4x4 CreateScreenRotation(Vector2 anchor, float degrees)
	{
		if (!float.IsFinite(degrees))
		{
			throw new ArgumentOutOfRangeException(nameof(degrees));
		}

		return Matrix4x4.CreateTranslation(-anchor.X, -anchor.Y, 0)
			* Matrix4x4.CreateRotationZ(-degrees * MathF.PI / 180)
			* Matrix4x4.CreateTranslation(anchor.X, anchor.Y, 0);
	}
}
