using FishGfx.Formats;
using FishGfx.Graphics;

namespace FishGfx.SmokeTest;

internal static partial class PrimitiveScenes
{
	private const int Width = PrimitiveGallery.Width;
	private const int Height = PrimitiveGallery.Height;

	private static TrueTypeFont proportionalFont;
	private static TrueTypeFont monoFont;
	private static RenderCommandList commandListScene;
	private static RenderQueue deferredQueueScene;
	private static RenderCommandList[] deferredOpaqueCommands;
	private static RenderCommandList[] deferredTransparentCommands;
	private static RenderCommandList deferredOverlayCommands;

	internal static void InitializeFonts()
	{
		proportionalFont = new TrueTypeFont(
			PrimitiveGallery.AssetPath("fonts", "Aaargh.ttf")
		);
		monoFont = new TrueTypeFont(
			PrimitiveGallery.AssetPath("fonts", "Consolas-Regular.ttf")
		);
	}

	internal static void DisposeFonts()
	{
		commandListScene?.Clear();
		commandListScene = null;

		deferredQueueScene?.Clear();
		deferredQueueScene = null;
		deferredOpaqueCommands = null;
		deferredTransparentCommands = null;
		deferredOverlayCommands = null;

		proportionalFont?.Dispose();
		monoFont?.Dispose();
		proportionalFont = null;
		monoFont = null;
	}

	internal static GalleryScene[] Create()
	{
		return new[]
		{
			new GalleryScene("RenderPass.DrawLine", DrawLines),
			new GalleryScene("RenderPass.DrawRectangle", DrawRectangles),
			new GalleryScene("RenderPass.FillRectangle", FillRectangles),
			new GalleryScene("RenderPass.DrawRoundedRectangle", DrawRoundedRectangles),
			new GalleryScene("RenderPass.FillRoundedRectangle", FillRoundedRectangles),
			new GalleryScene("RenderPass.DrawLineStrip", DrawLineStrips),
			new GalleryScene("RenderPass.DrawPoint", DrawPoints),
			new GalleryScene("RenderPass.DrawTexturedRectangle", DrawTexturedRectangles),
			new GalleryScene(
				"RenderPass.DrawTexturedRoundedRectangle",
				DrawTexturedRoundedRectangles
			),
			new GalleryScene("RenderPass.DrawTexturedCircle", DrawTexturedCircles),
			new GalleryScene("RenderPass.DrawTexturedEllipse", DrawTexturedEllipses),
			new GalleryScene("RenderPass.DrawNinePatch", DrawNinePatches),
			new GalleryScene("RenderPass.DrawCircle", DrawCircles),
			new GalleryScene("RenderPass.FillCircle", FillCircles),
			new GalleryScene("RenderPass.FillRing", FillRings),
			new GalleryScene("RenderPass.DrawRing", DrawRings),
			new GalleryScene("RenderPass.DrawEllipse", DrawEllipses),
			new GalleryScene("RenderPass.FillEllipse", FillEllipses),
			new GalleryScene("RenderPass.DrawQuadraticBezier", DrawQuadraticBeziers),
			new GalleryScene("RenderPass.DrawCubicBezier", DrawCubicBeziers),
			new GalleryScene("RenderPass.DrawText (TrueType/SDF)", DrawTrueTypeText),
			new GalleryScene("RenderCommandList", DrawCommandList),
			new GalleryScene("RenderQueue", DrawRenderQueue),
		};
	}
}
