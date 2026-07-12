using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using FishGfx.Formats;
using FishGfx.Game;
using FishGfx.Graphics;
using FishGfx.Voxels;

namespace FishGfx.VoxelTest
{
	internal sealed class VoxelTestApplication
	{
		private const int Width = 1920;
		private const int Height = 1080;
		private const float EditReach = 12;
		private static readonly VoxelFogSettings UnderwaterFog = new VoxelFogSettings(
			new Color(30, 111, 145),
			0.06f,
			0.70f
		);
		private static readonly Color AirClearColor = new Color(14, 19, 30);
		private static readonly Color UnderwaterClearColor = new Color(7, 32, 48);
		private static readonly Color UnderwaterTint = new Color(20, 101, 140, 48);
		private readonly bool autoMode;
		private Vector2 mouseDelta;
		private float scrollDelta;
		private bool boundaryBlockEnabled = true;
		private bool cullingEnabled = true;

		internal VoxelTestApplication(string[] args)
		{
			autoMode = args.Contains("--auto", StringComparer.OrdinalIgnoreCase);
		}

		internal void Run()
		{
			RenderWindow window = new RenderWindow(Width, Height, "FishGfx Voxel Chunk Renderer");
			InputManager input = new InputManager(window);
			window.CaptureCursor = !autoMode;
			window.OnMouseMoveDelta += (_, x, y) => mouseDelta += new Vector2(x, y);
			window.OnScroll += (_, _, y) => scrollDelta += y;
			VoxelTestModelAssets modelAssets = VoxelTestCompatibilityAssets.LoadModels();
			VoxelPalette palette = VoxelTestWorldGenerator.CreatePalette(modelAssets, out VoxelTestMaterialIds materials);
			VoxelTestWorldData worldData = VoxelTestWorldGenerator.Generate(materials);
			VoxelTestChunkStreamer streamer = new VoxelTestChunkStreamer(worldData, materials);
			VoxelWorld world = streamer.World;
			VoxelHotbarSelection hotbar = new VoxelHotbarSelection(materials.Placeable);
			Camera camera = CreateCamera(worldData);
			Camera uiCamera = new Camera();
			uiCamera.SetOrthogonal(0, 0, Width, Height);
			ShaderUniforms.Current.Resolution = new Vector2(Width, Height);
			Texture atlas = VoxelTestCompatibilityAssets.CreateTexture();
			TTFFont font = new TTFFont(AssetPath("fonts", "Consolas-Regular.ttf"));
			VoxelRenderer renderer = new VoxelRenderer(
				world,
				palette,
				atlas,
				VoxelTestCompatibilityAssets.AtlasLayout,
				new VoxelRendererOptions
				{
					MaxWorkers = Math.Max(2, Environment.ProcessorCount - 1),
					RenderDistance = 108,
					UploadBudget = 24,
				}
			);
			DeferredRenderQueue renderQueue = new DeferredRenderQueue();
			RenderState overlayState = Gfx.CreateDefaultRenderState();
			overlayState.EnableCullFace = false;
			overlayState.EnableDepthTest = false;
			overlayState.EnableDepthMask = false;
			overlayState.EnableDepthClamp = false;
			Stopwatch timer = Stopwatch.StartNew();
			RollingFrameRateCounter frameRate = new RollingFrameRateCounter();
			double previousTime = timer.Elapsed.TotalSeconds;
			VoxelRendererStatistics finalStatistics = default;
			VoxelRendererFrameDiagnostics finalDiagnostics = default;
			int autoValidationStage = 0;
			int autoGpuChunkCount = 0;
			bool underwaterValidated = false;
			bool staleEditsForced = false;

			try
			{
				while (!window.ShouldClose)
				{
					input.BeginNewFrame();
					Events.Poll();
					double now = timer.Elapsed.TotalSeconds;
					float deltaTime = (float)(now - previousTime);
					previousTime = now;
					frameRate.Update(now, Math.Max(deltaTime, float.Epsilon));

					if (!autoMode && input.GetKeyPressed(Key.Escape))
						window.ShouldClose = true;

					if (!autoMode)
					{
						UpdateCamera(camera, input, deltaTime, streamer);
						UpdateHotbar(input, hotbar);

						if (scrollDelta != 0)
						{
							hotbar.Move(scrollDelta > 0 ? -1 : 1);
							scrollDelta = 0;
						}

						if (input.GetKeyPressed(Key.E))
							ToggleBoundaryBlock(streamer, materials);

						if (input.GetKeyPressed(Key.C))
						{
							cullingEnabled = !cullingEnabled;
							renderer.CullingEnabled = cullingEnabled;
						}

						if (input.GetKeyPressed(Key.MouseLeft))
							DestroyTargetedVoxel(streamer, world, camera);

						if (input.GetKeyPressed(Key.MouseRight))
							PlaceTargetedVoxel(streamer, world, camera, hotbar.Selected.Id);
					}

					if (window.ShouldClose)
						break;

					streamer.Update(camera.Position);

					if (autoMode && streamer.IsSettled && !staleEditsForced)
					{
						renderer.UpdateMeshing(0);
						ForceStaleMeshEdits(world, materials);
						staleEditsForced = true;
					}

					bool underwater = VoxelMediumQuery.IsInsideMaterial(world, camera.Position, materials.Water);
					renderer.Fog = underwater ? UnderwaterFog : VoxelFogSettings.Disabled;
					renderer.UpdateMeshing();
					renderQueue.BeginFrame();
					Gfx.Clear(underwater ? UnderwaterClearColor : AirClearColor);

					if (!autoMode || (streamer.IsSettled && renderer.IsIdle))
					{
						renderer.SubmitVisible(renderQueue, camera);
						ShaderUniforms.Current.Camera = camera;
						renderQueue.Execute(
							RenderBucket.Opaque,
							RenderSubmissionComparers.OpaqueFrontToBack(camera)
						);
						renderQueue.Execute(
							VoxelRenderBuckets.Cutout,
							RenderSubmissionComparers.OpaqueFrontToBack(camera)
						);
						renderQueue.Execute(
							RenderBucket.Transparent,
							RenderSubmissionComparers.TransparentBackToFront(camera)
						);
					}

					if (underwater)
						DrawUnderwaterTint(uiCamera, camera, overlayState);

					DrawOverlay(
						font,
						uiCamera,
						camera,
						overlayState,
						streamer,
						frameRate,
						renderer.Statistics,
						renderer.FrameDiagnostics,
						hotbar,
						renderer.CullingEnabled
					);
					window.SwapBuffers();

					VoxelRendererStatistics statistics = renderer.Statistics;
					VoxelRendererFrameDiagnostics diagnostics = renderer.FrameDiagnostics;

					bool renderValidationReady = streamer.IsSettled
						&& staleEditsForced
						&& renderer.IsIdle
						&& statistics.AcceptedMeshes > 0
						&& statistics.DiscardedMeshes > 0
						&& statistics.VisibleChunks > 0
						&& statistics.OpaqueVertices > 0
						&& statistics.CutoutVertices > 0
						&& statistics.TransparentFaces > 0;

					if (
						autoMode
						&& renderValidationReady
						&& (diagnostics.PassSubmissions > 3 || diagnostics.ShaderBinds > 3 || diagnostics.TextureBinds > 3)
					)
						throw new InvalidOperationException("Voxel rendering issued redundant pass submissions or common resource binds.");

					if (
						autoMode
						&& autoValidationStage == 0
						&& renderValidationReady
						&& diagnostics.TransparentCacheHit
					)
					{
						if (underwater || renderer.Fog.Enabled)
							throw new InvalidOperationException("The normal voxel validation frame unexpectedly used underwater fog.");
						ValidateCompatibilityShowcase(world, worldData, materials);
						if (
							streamer.LoadedHorizontalCount > streamer.MaximumResidentHorizontalCount
							|| statistics.LoadedChunks > streamer.MaximumResidentChunkCount
							|| statistics.GpuChunks > streamer.MaximumResidentChunkCount
						)
							throw new InvalidOperationException("Voxel streaming exceeded its bounded resident chunk budget.");

						autoGpuChunkCount = statistics.GpuChunks;
						camera.Position = worldData.UnderwaterCameraPosition;
						camera.LookAt(camera.Position + Vector3.UnitX);
						autoValidationStage = 1;
					}
					else if (
						autoMode
						&& autoValidationStage == 1
						&& renderValidationReady
						&& underwater
						&& renderer.Fog == UnderwaterFog
					)
					{
						autoGpuChunkCount = statistics.GpuChunks;
						autoValidationStage = 2;
					}
					else if (
						autoMode
						&& autoValidationStage == 2
						&& renderValidationReady
						&& underwater
						&& diagnostics.TransparentCacheHit
					)
					{
						if (statistics.GpuChunks != autoGpuChunkCount)
							throw new InvalidOperationException("A stationary underwater frame unexpectedly changed GPU chunk meshes.");

						underwaterValidated = true;
						window.ShouldClose = true;
					}
					else if (autoMode && timer.Elapsed.TotalSeconds > 60)
						throw new TimeoutException(
							$"Voxel streaming validation did not settle within 60 seconds; stream={streamer.PendingHorizontalCount}, "
								+ $"meshes={statistics.PendingJobs}, accepted={statistics.AcceptedMeshes}, stale={statistics.DiscardedMeshes}."
						);
				}

				if (autoMode && !underwaterValidated)
					throw new InvalidOperationException(
						$"Voxel automatic validation closed before completion at stage {autoValidationStage}."
					);

				finalStatistics = renderer.Statistics;
				finalDiagnostics = renderer.FrameDiagnostics;
			}
			finally
			{
				renderer.Dispose();
				font.Dispose();
				atlas.Dispose();
				RenderAPI.CollectGarbage();
				window.Close();
			}

			Console.WriteLine(
				$"Voxel test completed using {RenderAPI.Renderer}; accepted={finalStatistics.AcceptedMeshes}; "
					+ $"discarded={finalStatistics.DiscardedMeshes}; underwater={(underwaterValidated ? "validated" : "interactive")}"
					+ $"; stream={streamer.LoadedHorizontalCount}; fps={frameRate.FramesPerSecond:F1}"
					+ $"; prep={finalDiagnostics.CullingMilliseconds + finalDiagnostics.TransparentBuildMilliseconds:F2}ms"
					+ $"; draws={finalDiagnostics.DrawCalls}; binds={finalDiagnostics.ShaderBinds}"
			);
		}

		private static void DrawUnderwaterTint(
			Camera uiCamera,
			Camera worldCamera,
			RenderState overlayState
		)
		{
			Gfx.PushRenderState(overlayState);

			try
			{
				ShaderUniforms.Current.Camera = uiCamera;
				Gfx.FilledRectangle(0, 0, Width, Height, UnderwaterTint);
			}
			finally
			{
				ShaderUniforms.Current.Camera = worldCamera;
				Gfx.PopRenderState();
			}
		}

		private void UpdateCamera(
			Camera camera,
			InputManager input,
			float deltaTime,
			VoxelTestChunkStreamer streamer
		)
		{
			camera.MouseMovement = true;
			// RenderWindow reports the previous cursor position minus the current one.
			// Camera.Update expects movement in the current-minus-previous direction.
			camera.Update(-mouseDelta);
			mouseDelta = Vector2.Zero;
			float speed = input.GetKeyDown(Key.LeftShift) ? 80 : 20;
			Vector3 movement = Vector3.Zero;

			if (input.GetKeyDown(Key.W))
				movement += camera.WorldForwardNormal;
			if (input.GetKeyDown(Key.S))
				movement -= camera.WorldForwardNormal;
			if (input.GetKeyDown(Key.D))
				movement += camera.WorldRightNormal;
			if (input.GetKeyDown(Key.A))
				movement -= camera.WorldRightNormal;
			if (input.GetKeyDown(Key.Space))
				movement += Vector3.UnitY;
			if (input.GetKeyDown(Key.LeftControl))
				movement -= Vector3.UnitY;

			if (movement.LengthSquared() > 0)
				camera.Position = streamer.ClampPosition(
					camera.Position + Vector3.Normalize(movement) * speed * deltaTime
				);
		}

		private void ToggleBoundaryBlock(VoxelTestChunkStreamer streamer, VoxelTestMaterialIds materials)
		{
			boundaryBlockEnabled = !boundaryBlockEnabled;
			streamer.SetVoxel(
				VoxelTestWorldGenerator.BoundaryEditX,
				VoxelTestWorldGenerator.BoundaryEditY,
				VoxelTestWorldGenerator.BoundaryEditZ,
				boundaryBlockEnabled ? new VoxelCell(materials.Glass) : VoxelCell.Air
			);
		}

		private static void DestroyTargetedVoxel(
			VoxelTestChunkStreamer streamer,
			VoxelWorld world,
			Camera camera
		)
		{
			if (VoxelRaycast.Cast(world, camera.Position, camera.WorldForwardNormal, EditReach, out VoxelRaycastHit hit))
				streamer.SetVoxel(hit.X, hit.Y, hit.Z, VoxelCell.Air);
		}

		private static void PlaceTargetedVoxel(
			VoxelTestChunkStreamer streamer,
			VoxelWorld world,
			Camera camera,
			ushort materialId
		)
		{
			if (
				VoxelRaycast.Cast(world, camera.Position, camera.WorldForwardNormal, EditReach, out VoxelRaycastHit hit)
				&& hit.HasSurfaceNormal
			)
				streamer.SetVoxel(hit.AdjacentX, hit.AdjacentY, hit.AdjacentZ, new VoxelCell(materialId));
		}

		private static void ForceStaleMeshEdits(VoxelWorld world, VoxelTestMaterialIds materials)
		{
			foreach (VoxelChunk chunk in world.LoadedChunks)
			{
				Vector3 origin = chunk.Coordinate.WorldOrigin;
				int x = (int)origin.X + 8;
				int y = (int)origin.Y + 8;
				int z = (int)origin.Z + 8;
				VoxelCell original = world.GetVoxel(x, y, z);
				VoxelCell temporary = original.IsAir ? new VoxelCell(materials.Stone) : VoxelCell.Air;

				world.SetVoxel(x, y, z, temporary);
				world.SetVoxel(x, y, z, original);
			}
		}

		private static Camera CreateCamera(VoxelTestWorldData worldData)
		{
			Camera camera = new Camera();
			camera.SetPerspective(Width, Height, MathF.PI / 2.2f, 0.1f, 500);
			camera.Position = worldData.ShowcaseCameraPosition;
			camera.LookAt(worldData.ShowcaseTarget);

			return camera;
		}

		private static void ValidateCompatibilityShowcase(
			VoxelWorld world,
			VoxelTestWorldData worldData,
			VoxelTestMaterialIds materials
		)
		{
			for (int index = 0; index < materials.Placeable.Count; index++)
			{
				(int x, int y, int z) = worldData.GetShowcasePosition(index);

				if (world.GetVoxel(x, y, z).MaterialId != materials.Placeable[index].Id)
					throw new InvalidOperationException($"Compatibility showcase material '{materials.Placeable[index].Name}' is missing.");
			}
		}

		private static void UpdateHotbar(InputManager input, VoxelHotbarSelection hotbar)
		{
			for (int slot = 0; slot < VoxelHotbarSelection.VisibleSlots; slot++)
			{
				Key key = (Key)((int)Key.Alpha1 + slot);

				if (input.GetKeyPressed(key) && slot < hotbar.VisibleCount)
					hotbar.SelectVisibleSlot(slot);
			}
		}


		private static void DrawOverlay(
			TTFFont font,
			Camera uiCamera,
			Camera worldCamera,
			RenderState overlayState,
			VoxelTestChunkStreamer streamer,
			RollingFrameRateCounter frameRate,
			VoxelRendererStatistics stats,
			VoxelRendererFrameDiagnostics diagnostics,
			VoxelHotbarSelection hotbar,
			bool cullingEnabled
		)
		{
			Gfx.PushRenderState(overlayState);

			try
			{
				ShaderUniforms.Current.Camera = uiCamera;
				Gfx.FilledRoundedRectangle(20, 700, 670, 350, new CornerRadii(16), new Color(10, 14, 24, 210));
				Gfx.DrawText(font, new Vector2(45, 980), "FishGfx voxel chunks", new Color(110, 205, 255), 34);
				Gfx.DrawText(
					font,
					new Vector2(400, 985),
					$"FPS: {frameRate.FramesPerSecond:F1} | {frameRate.FrameMilliseconds:F1} ms",
					new Color(145, 255, 170),
					22
				);
				Gfx.DrawText(
					font,
					new Vector2(45, 735),
					$"stream loaded/pending: {streamer.LoadedHorizontalCount} / {streamer.PendingHorizontalCount}\n"
						+ $"chunks loaded/gpu/visible: {stats.LoadedChunks} / {stats.GpuChunks} / {stats.VisibleChunks}\n"
						+ $"jobs: {stats.PendingJobs}   accepted: {stats.AcceptedMeshes}   stale: {stats.DiscardedMeshes}\n"
						+ $"vertices opaque/cutout: {stats.OpaqueVertices} / {stats.CutoutVertices}\n"
						+ $"transparent faces/vertices: {stats.TransparentFaces} / {stats.TransparentVertices}\n"
						+ $"render prep: {diagnostics.CullingMilliseconds:F2} + {diagnostics.TransparentBuildMilliseconds:F2} ms"
						+ $"   draws/binds: {diagnostics.DrawCalls} / {diagnostics.ShaderBinds}\n"
						+ $"culling: {(cullingEnabled ? "on" : "off")}   C toggles, E edits a chunk boundary\n"
						+ $"Left destroy, Right place: {hotbar.Selected.Name}; wheel or 1-9 selects\n"
						+ "WASD + mouse, Space/Ctrl vertical, Shift fast",
					new Color(220, 228, 240),
					22
				);
				Gfx.Line(
					new Vertex2(new Vector2(Width / 2 - 10, Height / 2), new Color(245, 245, 245, 220)),
					new Vertex2(new Vector2(Width / 2 + 10, Height / 2), new Color(245, 245, 245, 220)),
					2
				);
				DrawHotbar(font, hotbar);
				Gfx.Line(
					new Vertex2(new Vector2(Width / 2, Height / 2 - 10), new Color(245, 245, 245, 220)),
					new Vertex2(new Vector2(Width / 2, Height / 2 + 10), new Color(245, 245, 245, 220)),
					2
				);
			}
			finally
			{
				ShaderUniforms.Current.Camera = worldCamera;
				Gfx.PopRenderState();
			}
		}

		private static void DrawHotbar(TTFFont font, VoxelHotbarSelection hotbar)
		{
			const float SlotWidth = 150;
			const float SlotHeight = 58;
			float startX = (Width - SlotWidth * hotbar.VisibleCount) / 2;

			for (int slot = 0; slot < hotbar.VisibleCount; slot++)
			{
				float x = startX + slot * SlotWidth;
				bool selected = hotbar.IsSelectedSlot(slot);
				Color background = selected ? new Color(52, 112, 150, 235) : new Color(12, 18, 28, 215);
				Color foreground = selected ? new Color(255, 235, 125) : new Color(215, 225, 235);
				VoxelTestMaterialEntry entry = hotbar.GetVisible(slot);
				Gfx.FilledRoundedRectangle(
					x + 3,
					18,
					SlotWidth - 6,
					SlotHeight,
					new CornerRadii(8),
					background
				);
				Gfx.DrawText(font, new Vector2(x + 12, 53), $"{slot + 1} {entry.Name}", foreground, 16);
			}
		}

		private static string AssetPath(params string[] parts)
		{
			return Path.Combine(new[] { AppContext.BaseDirectory, "data" }.Concat(parts).ToArray());
		}

	}
}
