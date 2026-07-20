using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using FishGfx.Game;
using FishGfx.Graphics;
using FishGfx.Voxels;

namespace FishGfx.VoxelTest;

internal sealed partial class VoxelTestApplication
{
	private const int Width = 1920;
	private const int Height = 1080;
	private const float EditReach = 12;
	private const int InteractiveLightingBudget = 65_536;
	private const int InteractiveStartupLightingBudget = 8_192;
	private const int InteractiveStreamingLightingBudget = 4_096;
	private const int AutomaticLightingBudget = 1_048_576;
	private const float MaxRenderDistance = 108;
	private static readonly VoxelFogSettings UnderwaterFog = new VoxelFogSettings(
		new Color(30, 111, 145),
		0.06f,
		0.70f
	);
	private static readonly Color AirClearColor = new Color(14, 19, 30);
	private static readonly Color UnderwaterClearColor = new Color(7, 32, 48);
	private static readonly Color UnderwaterTint = new Color(20, 101, 140, 48);
	private readonly bool autoMode;
	private readonly bool benchmarkMode;
	private Vector2 mouseDelta;
	private float scrollDelta;
	private bool boundaryBlockEnabled = true;
	private bool cullingEnabled = true;

	internal VoxelTestApplication(string[] args)
	{
		autoMode = args.Contains("--auto", StringComparer.OrdinalIgnoreCase);
		benchmarkMode = args.Contains(
			"--streaming-benchmark",
			StringComparer.OrdinalIgnoreCase
		);
	}

	internal void Run()
	{
		RenderWindow window = new RenderWindow(Width, Height, "FishGfx Voxel Chunk Renderer");
		InputManager input = new InputManager(window);
		string rendererName = window.Graphics.Capabilities.Renderer;
		window.CaptureCursor = !autoMode && !benchmarkMode;
		window.MouseDelta += (_, args) => mouseDelta += args.Delta;
		window.Scrolled += (_, args) => scrollDelta += args.Offset.Y;
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
				UpdateBudget = autoMode
					? AutomaticLightingBudget
					: InteractiveLightingBudget,
			}
		);
		streamer.AttachLighting(lighting);
		VoxelHotbarSelection hotbar = new VoxelHotbarSelection(materials.Placeable);
		Camera camera = CreateCamera(worldData);
		Camera uiCamera = new Camera();
		uiCamera.SetOrthogonal(0, 0, Width, Height);
		VoxelSurfaceTextureSet textures = VoxelTestCompatibilityAssets.CreateTextures(
			window.Graphics
		);
		VoxelRenderer renderer = new VoxelRenderer(
			window.Graphics,
			world,
			palette,
			textures,
			VoxelTestCompatibilityAssets.AtlasLayout,
			lighting,
			new VoxelRendererOptions
			{
				WorkerCount = Math.Max(2, Environment.ProcessorCount - 1),
				MaxRenderDistance = MaxRenderDistance,
				MeshUploadBudget = autoMode ? 96 : 24,
				MeshUploadTimeBudgetMilliseconds = autoMode
					? double.PositiveInfinity
					: 2,
			}
		);
		RenderQueue renderQueue = new RenderQueue();
		RenderState overlayState = RenderState.Default with
		{
			CullMode = CullMode.None,
			DepthTestEnabled = false,
			DepthWriteEnabled = false,
			DepthClampEnabled = false,
		};
		VoxelTestUi voxelUi = new VoxelTestUi(window, hotbar);
		window.Resized += (_, args) => uiCamera.SetOrthogonal(0, 0, args.Width, args.Height);
		Stopwatch timer = Stopwatch.StartNew();
		RollingFrameRateCounter frameRate = new RollingFrameRateCounter();
		double previousTime = timer.Elapsed.TotalSeconds;
		List<double> benchmarkFrames = new List<double>();
		List<double> benchmarkStreaming = new List<double>();
		List<double> benchmarkLighting = new List<double>();
		List<double> benchmarkMeshing = new List<double>();
		int benchmarkFrame = 0;
		int benchmarkGen0Start = GC.CollectionCount(0);
		int benchmarkGen1Start = GC.CollectionCount(1);
		int benchmarkGen2Start = GC.CollectionCount(2);
		long benchmarkAllocatedStart = GC.GetTotalAllocatedBytes();
		VoxelRendererStatistics finalStatistics = default;
		VoxelRendererFrameDiagnostics finalDiagnostics = default;
		int autoValidationStage = 0;
		int autoGpuChunkCount = 0;
		int autoSunAcceptedMeshes = 0;
		bool underwaterValidated = false;
		bool staleEditsForced = false;
		bool focusLightingSettling = false;
		bool uiMode = false;
		bool uiValidated = false;
		bool worldPixelsValidated = false;

		try
		{
			while (!window.IsCloseRequested)
			{
				input.BeginFrame();
				voxelUi.BeginFrame();
				window.PollEvents();
				double now = timer.Elapsed.TotalSeconds;
				float deltaTime = (float)(now - previousTime);
				previousTime = now;
				frameRate.Update(now, Math.Max(deltaTime, float.Epsilon));

				if (benchmarkMode)
				{
					if (benchmarkFrame >= 5)
					{
						benchmarkFrames.Add(deltaTime * 1000);
					}

					benchmarkFrame++;
				}

				if (!autoMode && input.WasKeyPressed(Key.Escape))
				{
					window.IsCloseRequested = true;
				}

				if (!autoMode)
				{
					if (input.WasKeyPressed(Key.Tab))
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
						{
							hotbar.Move(scrollDelta > 0 ? -1 : 1);
						}

						if (input.WasMouseButtonPressed(MouseButton.Left))
						{
							DestroyTargetedVoxel(streamer, world, camera);
						}

						if (input.WasMouseButtonPressed(MouseButton.Right))
						{
							PlaceTargetedVoxel(streamer, world, camera, hotbar.Selected.Id);
						}
					}
					else
					{
						mouseDelta = Vector2.Zero;
					}

					if (scrollDelta != 0)
					{
						scrollDelta = 0;
					}

					if (input.WasKeyPressed(Key.E))
					{
						ToggleBoundaryBlock(streamer, materials);
					}

					if (input.WasKeyPressed(Key.C))
					{
						cullingEnabled = !cullingEnabled;
						renderer.IsCullingEnabled = cullingEnabled;
					}
				}
				else
				{
					mouseDelta = Vector2.Zero;
					scrollDelta = 0;
				}

				if (window.IsCloseRequested)
				{
					break;
				}

				long streamingStart = Stopwatch.GetTimestamp();
				streamer.Update(camera, MaxRenderDistance, cullingEnabled);
				if (streamer.PromotedLightingThisFrame.Count > 0
					|| streamer.PendingLightingHorizontalCount > 0)
				{
					focusLightingSettling = true;
				}
				double streamingMilliseconds = Stopwatch.GetElapsedTime(
					streamingStart
				).TotalMilliseconds;
				int lightingBudget = AutomaticLightingBudget;
				if (!autoMode)
				{
					if (!focusLightingSettling)
					{
						lightingBudget = InteractiveLightingBudget;
					}
					else if (streamer.GeneratedThisFrame.Count > 0)
					{
						lightingBudget = InteractiveStreamingLightingBudget;
					}
					else
					{
						lightingBudget = InteractiveStartupLightingBudget;
					}
				}
				long lightingStart = Stopwatch.GetTimestamp();
				lighting.Update(lightingBudget);
				double lightingMilliseconds = Stopwatch.GetElapsedTime(
					lightingStart
				).TotalMilliseconds;
				if (focusLightingSettling
					&& streamer.PendingLightingHorizontalCount == 0
					&& lighting.IsIdle)
				{
					focusLightingSettling = false;
				}

				if (autoMode && streamer.IsSettled && !staleEditsForced)
				{
					renderer.UpdateMeshes(0);
					ForceStaleMeshEdits(world, materials);
					staleEditsForced = true;
				}

				bool underwater = VoxelMediumQuery.IsInsideMaterial(world, camera.Position, materials.Water);
				renderer.FogSettings = underwater
					? UnderwaterFog
					: VoxelFogSettings.Disabled;
				long meshingStart = Stopwatch.GetTimestamp();
				renderer.UpdateMeshes(camera);
				double meshingMilliseconds = Stopwatch.GetElapsedTime(
					meshingStart
				).TotalMilliseconds;

				if (benchmarkMode && benchmarkFrame > 5)
				{
					benchmarkStreaming.Add(streamingMilliseconds);
					benchmarkLighting.Add(lightingMilliseconds);
					benchmarkMeshing.Add(meshingMilliseconds);
				}
				renderQueue.BeginFrame();
				using RenderFrame frame = window.Graphics.BeginFrame();

				using (RenderPass worldPass = frame.BeginPass(
					window.Graphics.Backbuffer,
					new RenderPassDescriptor
					{
						View = new RenderView(camera),
						ColorLoadAction = RenderLoadAction.Clear,
						DepthLoadAction = RenderLoadAction.Clear,
						StencilLoadAction = RenderLoadAction.Clear,
						ClearColor = underwater ? UnderwaterClearColor : AirClearColor,
						Time = (float)now,
					}
				))
				{
					if (!autoMode || (streamer.IsSettled && lighting.IsIdle && renderer.IsIdle))
					{
						renderer.EnqueueVisible(renderQueue, camera);
						worldPass.Execute(renderQueue);
					}
				}

				if (
					autoMode
						&& !worldPixelsValidated
						&& !underwater
						&& streamer.IsSettled
						&& lighting.IsIdle
						&& staleEditsForced
						&& renderer.IsIdle
				)
				{
					VoxelRendererStatistics pixelStatistics = renderer.Statistics;

					if (
						pixelStatistics.AcceptedMeshes > 0
							&& pixelStatistics.VisibleChunks > 0
							&& pixelStatistics.OpaqueVertices > 0
					)
					{
						window.ReadPixels();
						ValidateRenderedWorldPixels(window.PixelData.Span, AirClearColor);
						worldPixelsValidated = true;
					}
				}

				voxelUi.Update(
					camera,
					streamer,
					lighting,
					frameRate,
					renderer.Statistics,
					renderer.FrameDiagnostics,
					renderer.IsCullingEnabled,
					uiMode
				);

				using (RenderPass overlayPass = frame.BeginPass(
					window.Graphics.Backbuffer,
					new RenderPassDescriptor
					{
						View = new RenderView(uiCamera),
						State = overlayState,
						ColorLoadAction = RenderLoadAction.Load,
						DepthLoadAction = RenderLoadAction.Load,
						StencilLoadAction = RenderLoadAction.Load,
						Time = (float)now,
					}
				))
				{
					if (underwater)
					{
						RenderUnderwaterTint(overlayPass);
					}

					voxelUi.Render(overlayPass, deltaTime, (float)now);
					RenderCrosshair(overlayPass);

					if (autoMode && !uiValidated)
					{
						if (!voxelUi.IsInitialized || voxelUi.HotbarButtonCount != hotbar.VisibleCount)
						{
							throw new InvalidOperationException("FishUI did not initialize the expected VoxelTest controls.");
						}

						if (voxelUi.LastDrawCallCount <= 0)
						{
							throw new InvalidOperationException("FishUI did not emit any VoxelTest draw operations.");
						}

						uiValidated = true;
					}
				}

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
						&& (
							diagnostics.PassSubmissions > 3
								|| diagnostics.ShaderBinds > 3
								|| diagnostics.TextureBinds > 3
						)
				)
				{
					throw new InvalidOperationException(
						"Voxel rendering issued redundant pass submissions or common resource binds."
					);
				}

				if (
					autoMode
					&& autoValidationStage == 0
					&& renderValidationReady
					&& diagnostics.TransparentCacheHit
				)
				{
					if (underwater || renderer.FogSettings.Enabled)
					{
						throw new InvalidOperationException("The normal voxel validation frame unexpectedly used underwater fog.");
					}

					ValidateCompatibilityShowcase(world, worldData, materials);
					ValidateVoxelLighting(lighting, worldData, materials);
					if (
						streamer.LoadedHorizontalCount > streamer.MaximumResidentHorizontalCount
						|| statistics.LoadedChunks > streamer.MaximumResidentChunkCount
						|| statistics.GpuChunks > streamer.MaximumResidentChunkCount
					)
					{
						throw new InvalidOperationException("Voxel streaming exceeded its bounded resident chunk budget.");
					}

					autoGpuChunkCount = statistics.GpuChunks;
					autoSunAcceptedMeshes = statistics.AcceptedMeshes;
					renderer.SunSettings = new VoxelSunSettings(
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
					&& !renderer.FogSettings.Enabled
					&& diagnostics.TransparentCacheHit
				)
				{
					if (statistics.AcceptedMeshes != autoSunAcceptedMeshes || !lighting.IsIdle)
					{
						throw new InvalidOperationException(
							"Changing the runtime sun unexpectedly recalculated lighting or uploaded voxel chunk meshes."
						);
					}

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
					&& !renderer.FogSettings.Enabled
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
					&& renderer.FogSettings == UnderwaterFog
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
					{
						throw new InvalidOperationException("A stationary underwater frame unexpectedly changed GPU chunk meshes.");
					}

					if (!diagnostics.TransparentCacheHit || diagnostics.TransparentUploadBytes != 0)
					{
						throw new InvalidOperationException(
							"Advancing shader time unexpectedly rebuilt or uploaded transparent geometry."
						);
					}

					underwaterValidated = true;
					window.IsCloseRequested = true;
				}
				else if (autoMode && timer.Elapsed.TotalSeconds > 60)
				{
					throw new TimeoutException(
						$"Voxel streaming validation did not settle within 60 seconds; stream={streamer.PendingHorizontalCount}, "
							+ $"lighting={lighting.PendingCount}, meshes={statistics.PendingJobs}, "
							+ $"accepted={statistics.AcceptedMeshes}, stale={statistics.DiscardedMeshes}."
					);
				}
				else if (
					benchmarkMode
						&& streamer.IsSettled
						&& lighting.IsIdle
						&& renderer.IsIdle
				)
				{
					window.IsCloseRequested = true;
				}
				else if (benchmarkMode && timer.Elapsed.TotalSeconds > 60)
				{
					throw new TimeoutException(
						"Voxel streaming benchmark did not settle within 60 seconds."
					);
				}
			}

			if (
				autoMode
					&& (!underwaterValidated || !uiValidated || !worldPixelsValidated)
			)
			{
				throw new InvalidOperationException(
					$"Voxel automatic validation closed before completion at stage {autoValidationStage}; "
						+ $"UI={uiValidated}; world pixels={worldPixelsValidated}."
				);
			}

			finalStatistics = renderer.Statistics;
			finalDiagnostics = renderer.FrameDiagnostics;
		}
		finally
		{
			renderer.Dispose();
			lighting.Dispose();
			voxelUi.Dispose();
			input.Dispose();
			textures.CubeBaseColor.Dispose();
			textures.Normal.Dispose();
			textures.Specular.Dispose();
			textures.Roughness.Dispose();
			textures.ModelAtlas.Dispose();
			window.Graphics.CollectGarbage();
			window.Dispose();
		}

		Console.WriteLine(
			$"Voxel test completed using {rendererName}; accepted={finalStatistics.AcceptedMeshes}; "
				+ $"discarded={finalStatistics.DiscardedMeshes}; underwater={(underwaterValidated ? "validated" : "interactive")}"
				+ $"; stream={streamer.LoadedHorizontalCount}; lit={streamer.LitHorizontalCount}"
				+ $"; fps={frameRate.FramesPerSecond:F1}"
				+ $"; prep={finalDiagnostics.CullingMilliseconds + finalDiagnostics.TransparentBuildMilliseconds:F2}ms"
				+ $"; mesh={finalDiagnostics.MeshSchedulingMilliseconds:F2}+{finalDiagnostics.MeshUploadMilliseconds:F2}ms"
				+ $"; draws={finalDiagnostics.DrawCalls}; binds={finalDiagnostics.ShaderBinds}"
		);

		if (benchmarkMode && benchmarkFrames.Count > 0)
		{
			double[] ordered = benchmarkFrames.OrderBy(value => value).ToArray();
			Console.WriteLine(
				$"Streaming benchmark frames={ordered.Length}; "
					+ $"p50={Percentile(ordered, 0.50):F2}ms; "
					+ $"p95={Percentile(ordered, 0.95):F2}ms; "
					+ $"p99={Percentile(ordered, 0.99):F2}ms; "
					+ $"max={ordered[^1]:F2}ms"
			);
			Console.WriteLine(
				$"Benchmark phase p99/max: "
					+ $"stream={Percentile(benchmarkStreaming, 0.99):F2}/{benchmarkStreaming.Max():F2}ms; "
					+ $"light={Percentile(benchmarkLighting, 0.99):F2}/{benchmarkLighting.Max():F2}ms; "
					+ $"mesh={Percentile(benchmarkMeshing, 0.99):F2}/{benchmarkMeshing.Max():F2}ms; "
					+ $"GC={GC.CollectionCount(0) - benchmarkGen0Start}/"
					+ $"{GC.CollectionCount(1) - benchmarkGen1Start}/"
					+ $"{GC.CollectionCount(2) - benchmarkGen2Start}; "
					+ $"allocated={(GC.GetTotalAllocatedBytes() - benchmarkAllocatedStart) / 1_048_576d:F1}MiB"
			);
		}
	}

	private static double Percentile(double[] ordered, double percentile)
	{
		int index = (int)Math.Ceiling(percentile * ordered.Length) - 1;
		return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
	}

	private static double Percentile(List<double> samples, double percentile)
	{
		return Percentile(samples.OrderBy(value => value).ToArray(), percentile);
	}

	private static void ValidateRenderedWorldPixels(
		ReadOnlySpan<Color> pixels,
		Color clearColor
	)
	{
		const int minimumRenderedPixels = 64;
		int renderedPixels = 0;

		foreach (Color pixel in pixels)
		{
			if (
				pixel.R == clearColor.R
					&& pixel.G == clearColor.G
					&& pixel.B == clearColor.B
			)
			{
				continue;
			}

			renderedPixels++;

			if (renderedPixels >= minimumRenderedPixels)
			{
				return;
			}
		}

		throw new InvalidOperationException(
			$"Voxel rendering produced only {renderedPixels} non-clear pixels; "
				+ "the world shader may not be consuming the render-pass uniforms."
		);
	}
}
