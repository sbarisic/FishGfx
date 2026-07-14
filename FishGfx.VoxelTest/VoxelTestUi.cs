using System;
using System.Collections.Generic;
using System.Numerics;
using FishGfx.FishUI;
using FishGfx.Graphics;
using FishGfx.Voxels;
using FishUI.Controls;
using FishUIRuntime = global::FishUI.FishUI;

namespace FishGfx.VoxelTest
{
	internal sealed class VoxelTestUi : IDisposable
	{
		private const float HotbarSlotWidth = 150;
		private const float HotbarSlotHeight = 58;
		private readonly RenderWindow window;
		private readonly VoxelHotbarSelection hotbar;
		private readonly FishGfxFishUIGraphics graphics;
		private readonly FishGfxFishUIInput input;
		private readonly FishUIRuntime ui;
		private readonly Label fpsLabel;
		private readonly Label statisticsLabel;
		private readonly List<Button> hotbarButtons = new List<Button>();
		private bool disposed;

		internal VoxelTestUi(RenderWindow window, VoxelHotbarSelection hotbar)
		{
			this.window = window ?? throw new ArgumentNullException(nameof(window));
			this.hotbar = hotbar ?? throw new ArgumentNullException(nameof(hotbar));
			graphics = new FishGfxFishUIGraphics(window);
			input = new FishGfxFishUIInput(window);
			global::FishUI.FishUISettings settings = new global::FishUI.FishUISettings();
			ui = new FishUIRuntime(settings, graphics, input, new FishGfxFishUIEvents(), graphics.FileSystem);
			ui.Init();
			settings.LoadTheme("data/themes/gwen.yaml");

			Panel panel = new Panel
			{
				ID = "voxelStatsPanel",
				Position = Absolute(20, 30),
				Size = new Vector2(680, 390),
				Variant = PanelVariant.Dark,
				Opacity = 0.5f,
				BorderStyle = BorderStyle.Outset,
			};
			Label title = new Label("FishGfx voxel chunks")
			{
				ID = "voxelTitle",
				Position = new Vector2(24, 18),
				Size = new Vector2(320, 30),
				Alignment = Align.Left,
			};
			title.SetColorOverride("Text", new global::FishUI.FishColor(20, 90, 145));
			fpsLabel = new Label
			{
				ID = "voxelFps",
				Position = new Vector2(385, 18),
				Size = new Vector2(260, 30),
				Alignment = Align.Right,
			};
			fpsLabel.SetColorOverride("Text", new global::FishUI.FishColor(20, 115, 55));
			statisticsLabel = new Label
			{
				ID = "voxelStatistics",
				Position = new Vector2(24, 62),
				Size = new Vector2(630, 305),
				Alignment = Align.None,
			};
			statisticsLabel.SetColorOverride("Text", new global::FishUI.FishColor(25, 30, 40));
			panel.AddChild(title);
			panel.AddChild(fpsLabel);
			panel.AddChild(statisticsLabel);
			ui.AddControl(panel);

			for (int slot = 0; slot < hotbar.VisibleCount; slot++)
			{
				int selectedSlot = slot;
				VoxelTestMaterialEntry entry = hotbar.GetVisible(slot);
				Button button = new Button
				{
					ID = $"hotbar{slot + 1}",
					Size = new Vector2(HotbarSlotWidth - 6, HotbarSlotHeight),
					Text = $"{slot + 1} {entry.Name}",
					IsToggleButton = true,
					TooltipText = $"Select {entry.Name}",
					TabIndex = slot,
				};
				button.OnButtonPressed += (_, mouseButton, _) =>
				{
					if (mouseButton == global::FishUI.FishMouseButton.Left)
					{
						hotbar.SelectVisibleSlot(selectedSlot);
						UpdateHotbarButtons();
					}
				};
				hotbarButtons.Add(button);
				ui.AddControl(button);
			}

			Resize(window.WindowWidth, window.WindowHeight);
			window.OnWindowResize += OnWindowResize;
			IsInitialized = true;
		}

		internal bool IsInitialized { get; }
		internal bool InteractionEnabled
		{
			get => input.Enabled;
			set => input.Enabled = value;
		}

		internal int HotbarButtonCount => hotbarButtons.Count;
		internal int LastDrawCallCount => graphics.LastFrameDrawCallCount;

		internal void BeginFrame() => input.BeginFrame();
		internal void TickUpdate(float deltaTime, float time) => ui.TickUpdate(deltaTime, time);

		internal void Update(
			Camera camera,
			VoxelTestChunkStreamer streamer,
			VoxelLighting lighting,
			RollingFrameRateCounter frameRate,
			VoxelRendererStatistics stats,
			VoxelRendererFrameDiagnostics diagnostics,
			bool cullingEnabled,
			bool uiMode
		)
		{
			fpsLabel.Text = $"FPS: {frameRate.FramesPerSecond:F1} | {frameRate.FrameMilliseconds:F1} ms";
			statisticsLabel.Text =
				$"camera: {camera.Position.X:F1}, {camera.Position.Y:F1}, {camera.Position.Z:F1}\n"
				+ $"stream loaded/pending: {streamer.LoadedHorizontalCount} / {streamer.PendingHorizontalCount}\n"
				+ $"lighting resident/pending: {lighting.ResidentChunkCount} / {lighting.PendingCount}\n"
				+ $"chunks loaded/gpu/visible: {stats.LoadedChunks} / {stats.GpuChunks} / {stats.VisibleChunks}\n"
				+ $"jobs: {stats.PendingJobs}   accepted: {stats.AcceptedMeshes}   stale: {stats.DiscardedMeshes}\n"
				+ $"vertices opaque/cutout: {stats.OpaqueVertices} / {stats.CutoutVertices}\n"
				+ $"transparent faces/vertices: {stats.TransparentFaces} / {stats.TransparentVertices}\n"
				+ $"render prep: {diagnostics.CullingMilliseconds:F2} + {diagnostics.TransparentBuildMilliseconds:F2} ms"
				+ $"   draws/binds: {diagnostics.DrawCalls} / {diagnostics.ShaderBinds}\n"
				+ $"culling: {(cullingEnabled ? "on" : "off")}   C toggles, E edits a chunk boundary\n"
				+ $"Left destroy, Right place: {hotbar.Selected.Name}; wheel or 1-9 selects\n"
				+ $"mode: {(uiMode ? "UI" : "FPS")}   Tab toggles UI, WASD + mouse, Space/Ctrl, Shift fast";
			UpdateHotbarButtons();
		}

		internal void Draw(RenderPass pass, RenderView view, RenderState state, float deltaTime, float time)
		{
			using (graphics.UseRenderPass(pass, view, state))
				ui.TickDraw(deltaTime, time);
		}

		internal void Resize(int width, int height)
		{
			ui.Resized(width, height);
			float startX = (width - HotbarSlotWidth * hotbarButtons.Count) / 2;
			float top = height - HotbarSlotHeight - 18;
			for (int slot = 0; slot < hotbarButtons.Count; slot++)
				hotbarButtons[slot].Position = Absolute(startX + slot * HotbarSlotWidth + 3, top);
		}

		private static global::FishUI.FishUIPosition Absolute(float x, float y)
		{
			return new global::FishUI.FishUIPosition(global::FishUI.PositionMode.Absolute, new Vector2(x, y));
		}

		private void UpdateHotbarButtons()
		{
			for (int slot = 0; slot < hotbarButtons.Count; slot++)
			{
				VoxelTestMaterialEntry entry = hotbar.GetVisible(slot);
				Button button = hotbarButtons[slot];
				button.Text = $"{slot + 1} {entry.Name}";
				button.TooltipText = $"Select {entry.Name}";
				button.IsToggled = hotbar.IsSelectedSlot(slot);
			}
		}

		private void OnWindowResize(RenderWindow sender, int width, int height) => Resize(width, height);

		public void Dispose()
		{
			if (disposed)
				return;
			disposed = true;
			window.OnWindowResize -= OnWindowResize;
			input.Dispose();
			graphics.Dispose();
		}
	}
}
