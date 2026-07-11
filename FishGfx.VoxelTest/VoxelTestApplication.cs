using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using FishGfx.Formats;
using FishGfx.Game;
using FishGfx.Graphics;
using FishGfx.Voxels;
using Bitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using SolidBrush = System.Drawing.SolidBrush;

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
			VoxelPalette palette = VoxelTestWorldGenerator.CreatePalette(out VoxelTestMaterialIds materials);
			VoxelTestWorldData worldData = VoxelTestWorldGenerator.Generate(materials);
			VoxelTestChunkStreamer streamer = new VoxelTestChunkStreamer(worldData, materials);
			VoxelWorld world = streamer.World;
			Camera camera = CreateCamera(worldData);
			Camera uiCamera = new Camera();
			uiCamera.SetOrthogonal(0, 0, Width, Height);
			ShaderUniforms.Current.Resolution = new Vector2(Width, Height);
			Texture atlas = CreateAtlasTexture();
			TTFFont font = new TTFFont(AssetPath("fonts", "Consolas-Regular.ttf"));
			VoxelRenderer renderer = new VoxelRenderer(
				world,
				palette,
				atlas,
				new VoxelAtlasLayout(3, 2, 192, 128),
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

					if (input.GetKeyPressed(Key.Escape))
						window.ShouldClose = true;

					if (!autoMode)
					{
						UpdateCamera(camera, input, deltaTime, streamer);

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
							PlaceTargetedVoxel(streamer, world, camera, materials.Stone);
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
						if (statistics.GpuChunks != autoGpuChunkCount)
							throw new InvalidOperationException("Changing voxel fog unexpectedly recreated GPU chunk meshes.");

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
						underwaterValidated = true;
						window.ShouldClose = true;
					}
					else if (autoMode && timer.Elapsed.TotalSeconds > 60)
						throw new TimeoutException(
							$"Voxel streaming validation did not settle within 60 seconds; stream={streamer.PendingHorizontalCount}, "
								+ $"meshes={statistics.PendingJobs}, accepted={statistics.AcceptedMeshes}, stale={statistics.DiscardedMeshes}."
						);
				}

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
			Vector3 water = worldData.UnderwaterCameraPosition;
			camera.Position = new Vector3(water.X, water.Y + 45, water.Z);
			camera.LookAt(new Vector3(water.X + 18, water.Y, water.Z + 18));

			return camera;
		}


		private static Texture CreateAtlasTexture()
		{
			using Bitmap bitmap = new Bitmap(192, 128, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
			using DrawingGraphics graphics = DrawingGraphics.FromImage(bitmap);
			graphics.Clear(System.Drawing.Color.Transparent);
			DrawTile(graphics, 0, 0, System.Drawing.Color.FromArgb(255, 105, 112, 128), System.Drawing.Color.FromArgb(255, 75, 82, 95));
			DrawTile(graphics, 1, 0, System.Drawing.Color.FromArgb(255, 130, 86, 55), System.Drawing.Color.FromArgb(255, 95, 58, 38));
			DrawTile(graphics, 2, 0, System.Drawing.Color.FromArgb(255, 82, 155, 73), System.Drawing.Color.FromArgb(255, 55, 115, 48));
			DrawCutoutTile(graphics, 0, 1);
			DrawTile(graphics, 1, 1, System.Drawing.Color.FromArgb(105, 155, 220, 245), System.Drawing.Color.FromArgb(55, 220, 245, 255));
			DrawTile(graphics, 2, 1, System.Drawing.Color.FromArgb(145, 50, 125, 220), System.Drawing.Color.FromArgb(115, 80, 170, 245));

			Texture texture = Texture.FromImage(bitmap);
			texture.SetFilter(TextureFilter.Nearest);
			texture.SetWrap(TextureWrap.ClampToEdge);

			return texture;
		}

		private static void DrawTile(
			DrawingGraphics graphics,
			int tileX,
			int tileY,
			System.Drawing.Color first,
			System.Drawing.Color second
		)
		{
			const int tileSize = 64;
			int left = tileX * tileSize;
			int top = tileY * tileSize;

			using SolidBrush firstBrush = new SolidBrush(first);
			using SolidBrush secondBrush = new SolidBrush(second);
			graphics.FillRectangle(firstBrush, left, top, tileSize, tileSize);

			for (int y = 0; y < 8; y++)
				for (int x = 0; x < 8; x++)
					if ((x + y) % 2 == 0)
						graphics.FillRectangle(secondBrush, left + x * 8, top + y * 8, 8, 8);
		}

		private static void DrawCutoutTile(DrawingGraphics graphics, int tileX, int tileY)
		{
			const int tileSize = 64;
			int left = tileX * tileSize;
			int top = tileY * tileSize;
			using SolidBrush leaf = new SolidBrush(System.Drawing.Color.FromArgb(255, 65, 160, 75));
			using SolidBrush dark = new SolidBrush(System.Drawing.Color.FromArgb(255, 35, 105, 50));

			for (int y = 0; y < 8; y++)
				for (int x = 0; x < 8; x++)
					if ((x * 3 + y * 5) % 7 < 5)
						graphics.FillRectangle((x + y) % 2 == 0 ? leaf : dark, left + x * 8, top + y * 8, 8, 8);
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
						+ "Left destroy, Right place stone\n"
						+ "WASD + mouse, Space/Ctrl vertical, Shift fast",
					new Color(220, 228, 240),
					22
				);
				Gfx.Line(
					new Vertex2(new Vector2(Width / 2 - 10, Height / 2), new Color(245, 245, 245, 220)),
					new Vertex2(new Vector2(Width / 2 + 10, Height / 2), new Color(245, 245, 245, 220)),
					2
				);
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

		private static string AssetPath(params string[] parts)
		{
			return Path.Combine(new[] { AppContext.BaseDirectory, "data" }.Concat(parts).ToArray());
		}

	}
}
