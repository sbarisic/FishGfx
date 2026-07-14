using System;
using System.Numerics;
using FishGfx;
using FishGfx.Graphics;

namespace FishGfx.SmokeTest;

internal static partial class PrimitiveScenes
{
	private static void DrawCommandList(RenderPass pass, float _, Texture texture)
	{
		if (commandListScene == null)
		{
			commandListScene = CreateCommandListScene(texture);
		}

		pass.Execute(commandListScene);
	}

	private static RenderCommandList CreateCommandListScene(Texture texture)
	{
		RenderCommandList commands = new();

		commands.RecordDrawText(
			proportionalFont,
			new Vector2(500, 875),
			"Typed commands, recorded once and replayed every frame",
			new Color(110, 205, 255),
			42
		);
		commands.RecordFillRoundedRectangle(
			new Vector2(500, 575),
			new Vector2(420, 220),
			new CornerRadii(55, 18, 55, 18),
			new Color(60, 115, 205, 210)
		);
		commands.RecordDrawTexturedRoundedRectangle(
			new Vector2(1030, 575),
			new Vector2(420, 220),
			new CornerRadii(48),
			texture,
			Vector2.Zero,
			Vector2.One,
			new Color(235, 245, 255, 225)
		);
		commands.RecordFillRing(
			new Vector2(610, 330),
			55,
			110,
			-MathF.PI / 5,
			MathF.PI * 1.45f,
			new Color(255, 170, 80, 225)
		);
		commands.RecordDrawCircle(
			new Vector2(940, 330),
			105,
			9,
			new Color(90, 225, 160)
		);
		commands.RecordDrawCubicBezier(
			new Vector2(1120, 260),
			new Vector2(1260, 500),
			new Vector2(1480, 120),
			new Vector2(1660, 365),
			12,
			new Color(225, 125, 245)
		);
		commands.RecordDrawLine(
			new Vertex2(new Vector2(490, 145), new Color(90, 205, 255)),
			new Vertex2(new Vector2(1680, 145), new Color(255, 110, 155)),
			7
		);
		commands.RecordDrawText(
			monoFont,
			new Vector2(500, 95),
			$"{commands.Count + 2} immutable commands | caller-owned resources | execution-time camera",
			new Color(195, 205, 220),
			25
		);

		return commands;
	}

	private static void DrawRenderQueue(RenderPass pass, float _, Texture __)
	{
		if (deferredQueueScene == null)
		{
			CreateRenderQueueScene();
		}

		deferredQueueScene.BeginFrame();

		deferredQueueScene.SubmitOpaque(
			deferredOpaqueCommands[0],
			Matrix4x4.CreateTranslation(480, 610, -24),
			sortKey: 2,
			tag: "far"
		);
		deferredQueueScene.SubmitOpaque(
			deferredOpaqueCommands[2],
			Matrix4x4.CreateTranslation(1260, 610, -3),
			sortKey: 1,
			tag: "near"
		);
		deferredQueueScene.SubmitOpaque(
			deferredOpaqueCommands[1],
			Matrix4x4.CreateTranslation(870, 610, -12),
			sortKey: 2,
			tag: "middle"
		);

		deferredQueueScene.SubmitTransparent(
			deferredTransparentCommands[1],
			Matrix4x4.CreateTranslation(1030, 300, -10),
			tag: "middle"
		);
		deferredQueueScene.SubmitTransparent(
			deferredTransparentCommands[2],
			Matrix4x4.CreateTranslation(1150, 300, -3),
			tag: "near"
		);
		deferredQueueScene.SubmitTransparent(
			deferredTransparentCommands[0],
			Matrix4x4.CreateTranslation(910, 300, -20),
			tag: "far"
		);

		RenderQueueBucket overlay = new("Overlay");
		deferredQueueScene.Submit(
			overlay,
			deferredOverlayCommands,
			Matrix4x4.Identity
		);

		RenderView view = pass.View;
		pass.Execute(
			deferredQueueScene,
			RenderQueueBucket.Opaque,
			RenderItemComparers.OpaqueFrontToBack(view)
		);
		pass.Execute(
			deferredQueueScene,
			RenderQueueBucket.Transparent,
			RenderItemComparers.TransparentBackToFront(view)
		);

		foreach (RenderItem item in deferredQueueScene.Query(overlay))
		{
			pass.Execute(item);
		}
	}

	private static void CreateRenderQueueScene()
	{
		deferredQueueScene = new RenderQueue();
		deferredOpaqueCommands = new[]
		{
			CreateDeferredBox(new Color(70, 135, 235), "FAR  depth 24"),
			CreateDeferredBox(new Color(75, 195, 145), "MIDDLE  depth 12"),
			CreateDeferredBox(new Color(235, 145, 75), "NEAR  depth 3"),
		};
		deferredTransparentCommands = new[]
		{
			CreateDeferredCircle(new Color(80, 150, 255, 145)),
			CreateDeferredCircle(new Color(95, 235, 155, 145)),
			CreateDeferredCircle(new Color(255, 105, 145, 145)),
		};

		deferredOverlayCommands = new RenderCommandList();
		deferredOverlayCommands.RecordDrawText(
			proportionalFont,
			new Vector2(480, 900),
			"Deferred entity submissions",
			new Color(110, 205, 255),
			48
		);
		deferredOverlayCommands.RecordDrawText(
			monoFont,
			new Vector2(480, 840),
			"OPAQUE: submitted far / near / middle, sorted front-to-back",
			new Color(205, 215, 230),
			25
		);
		deferredOverlayCommands.RecordDrawText(
			monoFont,
			new Vector2(480, 535),
			"TRANSPARENT: submitted middle / near / far, sorted back-to-front",
			new Color(205, 215, 230),
			25
		);
		deferredOverlayCommands.RecordDrawText(
			monoFont,
			new Vector2(480, 120),
			"Overlay is a custom queried bucket | transforms captured at submission",
			new Color(255, 210, 105),
			24
		);
	}

	private static RenderCommandList CreateDeferredBox(Color color, string label)
	{
		RenderCommandList commands = new();
		commands.RecordFillRoundedRectangle(
			Vector2.Zero,
			new Vector2(330, 150),
			new CornerRadii(24),
			color
		);
		commands.RecordDrawRoundedRectangle(
			Vector2.Zero,
			new Vector2(330, 150),
			new CornerRadii(24),
			4,
			new Color(235, 240, 250)
		);
		commands.RecordDrawText(
			monoFont,
			new Vector2(22, 58),
			label,
			Color.White,
			23
		);

		return commands;
	}

	private static RenderCommandList CreateDeferredCircle(Color color)
	{
		RenderCommandList commands = new();
		commands.RecordFillCircle(Vector2.Zero, 145, color);
		commands.RecordDrawCircle(
			Vector2.Zero,
			145,
			5,
			new Color(235, 240, 250, 210)
		);

		return commands;
	}

}
