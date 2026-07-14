using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
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
			VoxelTestChunkStreamer streamer = new VoxelTestChunkStreamer(
				worldData,
				materials,
				generationBudget: autoMode ? 16 : 4
			);
			VoxelWorld world = streamer.World;
			VoxelLighting lighting = new VoxelLighting(
				world,
				palette,
				new VoxelLightingOptions
				{
					UpdateBudget = autoMode ? 1_048_576 : 65_536,
				}
			);
			streamer.AttachLighting(lighting);
			VoxelHotbarSelection hotbar = new VoxelHotbarSelection(materials.Placeable);
			Camera camera = CreateCamera(worldData);
			Camera uiCamera = new Camera();
			uiCamera.SetOrthogonal(0, 0, Width, Height);
			Texture atlas = VoxelTestCompatibilityAssets.CreateTexture();
			VoxelRenderer renderer = new VoxelRenderer(
				window.Graphics,
				world,
				palette,
				atlas,
				VoxelTestCompatibilityAssets.AtlasLayout,
				new VoxelRendererOptions
				{
					MaxWorkers = Math.Max(2, Environment.ProcessorCount - 1),
					RenderDistance = 108,
					UploadBudget = autoMode ? 96 : 24,
					Lighting = lighting,
				}
			);
			DeferredRenderQueue renderQueue = new DeferredRenderQueue();
			RenderState overlayState = Gfx.CreateDefaultRenderState();
			overlayState.EnableCullFace = false;
			overlayState.EnableDepthTest = false;
			overlayState.EnableDepthMask = false;
			overlayState.EnableDepthClamp = false;
			VoxelTestUi voxelUi = new VoxelTestUi(window, hotbar);
			window.OnWindowResize += (_, width, height) => uiCamera.SetOrthogonal(0, 0, width, height);
			Stopwatch timer = Stopwatch.StartNew();
			RollingFrameRateCounter frameRate = new RollingFrameRateCounter();
			double previousTime = timer.Elapsed.TotalSeconds;
			VoxelRendererStatistics finalStatistics = default;
			VoxelRendererFrameDiagnostics finalDiagnostics = default;
			int autoValidationStage = 0;
			int autoGpuChunkCount = 0;
			int autoSunAcceptedMeshes = 0;
			bool underwaterValidated = false;
			bool staleEditsForced = false;
			bool uiMode = false;
			bool uiValidated = false;

			try
			{
				while (!window.ShouldClose)
				{
					input.BeginNewFrame();
					voxelUi.BeginFrame();
					Events.Poll();
					double now = timer.Elapsed.TotalSeconds;
					float deltaTime = (float)(now - previousTime);
					previousTime = now;
					frameRate.Update(now, Math.Max(deltaTime, float.Epsilon));

					if (!autoMode && input.GetKeyPressed(Key.Escape))
						window.ShouldClose = true;

					if (!autoMode)
					{
						if (input.GetKeyPressed(Key.Tab))
						{
							uiMode = !uiMode;
							window.CaptureCursor = !uiMode;
							mouseDelta = Vector2.Zero;
							scrollDelta = 0;
						}
					}

					voxelUi.InteractionEnabled = !autoMode && uiMode;
					voxelUi.TickUpdate(deltaTime, (float)now);

					if (!autoMode)
					{
						UpdateHotbar(input, hotbar);

						if (!uiMode)
						{
							UpdateCamera(camera, input, deltaTime, streamer);
							if (scrollDelta != 0)
								hotbar.Move(scrollDelta > 0 ? -1 : 1);
							if (input.GetKeyPressed(Key.MouseLeft))
								DestroyTargetedVoxel(streamer, world, camera);
							if (input.GetKeyPressed(Key.MouseRight))
								PlaceTargetedVoxel(streamer, world, camera, hotbar.Selected.Id);
						}
						else
						{
							mouseDelta = Vector2.Zero;
						}

						if (scrollDelta != 0)
							scrollDelta = 0;

						if (input.GetKeyPressed(Key.E))
							ToggleBoundaryBlock(streamer, materials);

						if (input.GetKeyPressed(Key.C))
						{
							cullingEnabled = !cullingEnabled;
							renderer.CullingEnabled = cullingEnabled;
						}
					}
					else
					{
						mouseDelta = Vector2.Zero;
						scrollDelta = 0;
					}

					if (window.ShouldClose)
						break;

					streamer.Update(camera.Position);
					lighting.Update();

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
					using GraphicsFrame frame = window.Graphics.BeginFrame();
					using RenderPass pass = frame.BeginPass(window.Graphics.Backbuffer, new RenderPassDescriptor
					{
						View = new RenderView(camera),
						ColorLoadAction = RenderLoadAction.Clear,
						DepthLoadAction = RenderLoadAction.Clear,
						StencilLoadAction = RenderLoadAction.Clear,
						ClearColor = underwater ? UnderwaterClearColor : AirClearColor,
						Time = (float)now,
					});

					if (!autoMode || (streamer.IsSettled && lighting.IsIdle && renderer.IsIdle))
					{
						renderer.SubmitVisible(renderQueue, camera);
						renderQueue.Execute(
							pass,
							RenderBucket.Opaque,
							RenderSubmissionComparers.OpaqueFrontToBack(camera)
						);
						renderQueue.Execute(
							pass,
							VoxelRenderBuckets.Cutout,
							RenderSubmissionComparers.OpaqueFrontToBack(camera)
						);
						renderQueue.Execute(
							pass,
							RenderBucket.Transparent,
							RenderSubmissionComparers.TransparentBackToFront(camera)
						);
					}

					if (underwater)
						DrawUnderwaterTint(pass, uiCamera, overlayState);

					voxelUi.Update(
						camera,
						streamer,
						lighting,
						frameRate,
						renderer.Statistics,
						renderer.FrameDiagnostics,
						renderer.CullingEnabled,
						uiMode
					);
					voxelUi.Draw(pass, new RenderView(uiCamera), overlayState, deltaTime, (float)now);
					DrawCrosshair(pass, uiCamera, overlayState);
					if (autoMode && !uiValidated)
					{
						if (!voxelUi.IsInitialized || voxelUi.HotbarButtonCount != hotbar.VisibleCount)
							throw new InvalidOperationException("FishUI did not initialize the expected VoxelTest controls.");
						if (voxelUi.LastDrawCallCount <= 0)
							throw new InvalidOperationException("FishUI did not emit any VoxelTest draw operations.");
						uiValidated = true;
					}
					pass.Dispose();
					frame.Present();

					VoxelRendererStatistics statistics = renderer.Statistics;
					VoxelRendererFrameDiagnostics diagnostics = renderer.FrameDiagnostics;

					bool renderValidationReady = streamer.IsSettled
						&& lighting.IsIdle
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
						ValidateVoxelLighting(lighting, worldData, materials);
						if (
							streamer.LoadedHorizontalCount > streamer.MaximumResidentHorizontalCount
							|| statistics.LoadedChunks > streamer.MaximumResidentChunkCount
							|| statistics.GpuChunks > streamer.MaximumResidentChunkCount
						)
							throw new InvalidOperationException("Voxel streaming exceeded its bounded resident chunk budget.");

						autoGpuChunkCount = statistics.GpuChunks;
						autoSunAcceptedMeshes = statistics.AcceptedMeshes;
						renderer.Sun = new VoxelSunSettings(
							new Vector3(-0.35f, -1, -0.25f),
							new Color(255, 238, 215),
							0.9f,
							0.35f
						);
						autoValidationStage = 1;
					}
					else if (
						autoMode
						&& autoValidationStage == 1
						&& renderValidationReady
						&& !underwater
						&& !renderer.Fog.Enabled
						&& diagnostics.TransparentCacheHit
					)
					{
						if (statistics.AcceptedMeshes != autoSunAcceptedMeshes || !lighting.IsIdle)
							throw new InvalidOperationException(
								"Changing the runtime sun unexpectedly recalculated lighting or uploaded voxel chunk meshes."
							);
						ValidateCompatibilityShowcase(world, worldData, materials);
						camera.Position = worldData.ShowcaseSouthCameraPosition;
						camera.LookAt(worldData.ShowcaseTarget);
						autoValidationStage = 2;
					}
					else if (
						autoMode
						&& autoValidationStage == 2
						&& renderValidationReady
						&& !underwater
						&& !renderer.Fog.Enabled
						&& diagnostics.TransparentCacheHit
					)
					{
						ValidateCompatibilityShowcase(world, worldData, materials);
						camera.Position = worldData.UnderwaterCameraPosition;
						camera.LookAt(camera.Position + Vector3.Normalize(new Vector3(0.2f, 1, 0.1f)));
						autoValidationStage = 3;
					}
					else if (
						autoMode
						&& autoValidationStage == 3
						&& renderValidationReady
						&& underwater
						&& renderer.Fog == UnderwaterFog
					)
					{
						autoGpuChunkCount = statistics.GpuChunks;
						autoValidationStage = 4;
					}
					else if (
						autoMode
						&& autoValidationStage == 4
						&& renderValidationReady
						&& underwater
					)
					{
						if (statistics.GpuChunks != autoGpuChunkCount)
							throw new InvalidOperationException("A stationary underwater frame unexpectedly changed GPU chunk meshes.");
						if (!diagnostics.TransparentCacheHit || diagnostics.TransparentUploadBytes != 0)
							throw new InvalidOperationException(
								"Advancing shader time unexpectedly rebuilt or uploaded transparent geometry."
							);

						underwaterValidated = true;
						window.ShouldClose = true;
					}
					else if (autoMode && timer.Elapsed.TotalSeconds > 60)
						throw new TimeoutException(
							$"Voxel streaming validation did not settle within 60 seconds; stream={streamer.PendingHorizontalCount}, "
								+ $"lighting={lighting.PendingCount}, meshes={statistics.PendingJobs}, "
								+ $"accepted={statistics.AcceptedMeshes}, stale={statistics.DiscardedMeshes}."
						);
				}

				if (autoMode && (!underwaterValidated || !uiValidated))
					throw new InvalidOperationException(
						$"Voxel automatic validation closed before completion at stage {autoValidationStage}; UI={uiValidated}."
					);

				finalStatistics = renderer.Statistics;
				finalDiagnostics = renderer.FrameDiagnostics;
			}
			finally
			{
				renderer.Dispose();
				lighting.Dispose();
				voxelUi.Dispose();
				atlas.Dispose();
				window.Graphics.CollectGarbage();
				window.Dispose();
			}

			Console.WriteLine(
				$"Voxel test completed using {RenderAPI.Renderer}; accepted={finalStatistics.AcceptedMeshes}; "
					+ $"discarded={finalStatistics.DiscardedMeshes}; underwater={(underwaterValidated ? "validated" : "interactive")}"
					+ $"; stream={streamer.LoadedHorizontalCount}; fps={frameRate.FramesPerSecond:F1}"
					+ $"; prep={finalDiagnostics.CullingMilliseconds + finalDiagnostics.TransparentBuildMilliseconds:F2}ms"
					+ $"; draws={finalDiagnostics.DrawCalls}; binds={finalDiagnostics.ShaderBinds}"
			);
		}

		private static void DrawUnderwaterTint(RenderPass pass, Camera uiCamera, RenderState overlayState)
		{
			using IDisposable stateScope = pass.PushState(overlayState);
			using IDisposable viewScope = pass.PushView(new RenderView(uiCamera));
			pass.FilledRectangle(0, 0, Width, Height, UnderwaterTint);
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

			for (int index = 0; index < VoxelTestWorldGenerator.OrientationShowcaseCount; index++)
			{
				(int x, int y, int z) = worldData.GetOrientationShowcasePosition(index);
				ushort expectedMaterial = VoxelTestWorldGenerator.GetOrientationShowcaseMaterial(materials, index);

				if (world.GetVoxel(x, y, z).MaterialId != expectedMaterial)
					throw new InvalidOperationException("The voxel texture-orientation showcase is incomplete.");
			}
		}

		private static void ValidateVoxelLighting(
			VoxelLighting lighting,
			VoxelTestWorldData worldData,
			VoxelTestMaterialIds materials
		)
		{
			int glowstoneIndex = materials.Placeable
				.Select((entry, index) => (entry, index))
				.Single(item => item.entry.Id == materials.Glowstone)
				.index;
			(int x, int y, int z) = worldData.GetShowcasePosition(glowstoneIndex);
			VoxelLight emitted = lighting.GetLight(x + 1, y, z);

			if (emitted.Block.Red < 14 || emitted.Block.Green < 11 || emitted.Block.Blue < 7)
				throw new InvalidOperationException("The glowstone showcase did not propagate its configured RGB light.");

			VoxelLight sky = lighting.GetLight(x, y + 2, z);

			if (sky.Sky == 0)
				throw new InvalidOperationException("The sky-exposed showcase column did not receive skylight.");
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


		private static void DrawCrosshair(RenderPass pass, Camera uiCamera, RenderState overlayState)
		{
			using IDisposable stateScope = pass.PushState(overlayState);
			using IDisposable viewScope = pass.PushView(new RenderView(uiCamera));
				pass.Line(
					new Vertex2(new Vector2(Width / 2 - 10, Height / 2), new Color(245, 245, 245, 220)),
					new Vertex2(new Vector2(Width / 2 + 10, Height / 2), new Color(245, 245, 245, 220)),
					2
				);
				pass.Line(
					new Vertex2(new Vector2(Width / 2, Height / 2 - 10), new Color(245, 245, 245, 220)),
					new Vertex2(new Vector2(Width / 2, Height / 2 + 10), new Color(245, 245, 245, 220)),
					2
				);
		}

	}
}
