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
			Camera camera = CreateCamera();
			Camera uiCamera = new Camera();
			uiCamera.SetOrthogonal(0, 0, Width, Height);
			ShaderUniforms.Current.Resolution = new Vector2(Width, Height);
			VoxelPalette palette = CreatePalette(out MaterialIds materials);
			VoxelWorld world = CreateWorld(materials);
			Texture atlas = CreateAtlasTexture();
			TTFFont font = new TTFFont(AssetPath("fonts", "Consolas-Regular.ttf"));
			VoxelRenderer renderer = new VoxelRenderer(
				world,
				palette,
				atlas,
				new VoxelAtlasLayout(3, 2, 192, 128),
				new VoxelRendererOptions
				{
					MaxWorkers = 2,
					RenderDistance = 180,
					UploadBudget = 6,
				}
			);
			DeferredRenderQueue renderQueue = new DeferredRenderQueue();
			RenderState overlayState = Gfx.CreateDefaultRenderState();
			overlayState.EnableCullFace = false;
			overlayState.EnableDepthTest = false;
			overlayState.EnableDepthMask = false;
			overlayState.EnableDepthClamp = false;
			Stopwatch timer = Stopwatch.StartNew();
			double previousTime = timer.Elapsed.TotalSeconds;
			VoxelRendererStatistics finalStatistics = default;

			if (autoMode)
			{
				renderer.UpdateMeshing(0);
				ForceStaleBoundaryEdit(world, materials);
			}

			try
			{
				while (!window.ShouldClose)
				{
					input.BeginNewFrame();
					Events.Poll();
					double now = timer.Elapsed.TotalSeconds;
					float deltaTime = (float)(now - previousTime);
					previousTime = now;

					if (input.GetKeyPressed(Key.Escape))
						window.ShouldClose = true;

					if (!autoMode)
					{
						UpdateCamera(camera, input, deltaTime);

						if (input.GetKeyPressed(Key.E))
							ToggleBoundaryBlock(world, materials);

						if (input.GetKeyPressed(Key.C))
						{
							cullingEnabled = !cullingEnabled;
							renderer.CullingEnabled = cullingEnabled;
						}

						if (input.GetKeyPressed(Key.MouseLeft))
							DestroyTargetedVoxel(world, camera);

						if (input.GetKeyPressed(Key.MouseRight))
							PlaceTargetedVoxel(world, camera, materials.Stone);
					}

					if (window.ShouldClose)
						break;

					renderer.UpdateMeshing();
					renderQueue.BeginFrame();
					renderer.SubmitVisible(renderQueue, camera);
					Gfx.Clear(new Color(14, 19, 30));
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

					DrawOverlay(
						font,
						uiCamera,
						camera,
						overlayState,
						renderer.Statistics,
						renderer.CullingEnabled
					);
					window.SwapBuffers();

					VoxelRendererStatistics statistics = renderer.Statistics;

					if (
						autoMode
						&& renderer.IsIdle
						&& statistics.AcceptedMeshes > 0
						&& statistics.DiscardedMeshes > 0
						&& statistics.VisibleChunks > 0
						&& statistics.OpaqueVertices > 0
						&& statistics.CutoutVertices > 0
						&& statistics.TransparentFaces > 0
					)
						window.ShouldClose = true;
					else if (autoMode && timer.Elapsed.TotalSeconds > 20)
						throw new TimeoutException("Voxel renderer did not become idle within 20 seconds.");
				}

				finalStatistics = renderer.Statistics;
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
				$"Voxel test completed using {RenderAPI.Renderer}; accepted={finalStatistics.AcceptedMeshes}; discarded={finalStatistics.DiscardedMeshes}"
			);
		}

		private void UpdateCamera(Camera camera, InputManager input, float deltaTime)
		{
			camera.MouseMovement = true;
			// RenderWindow reports the previous cursor position minus the current one.
			// Camera.Update expects movement in the current-minus-previous direction.
			camera.Update(-mouseDelta);
			mouseDelta = Vector2.Zero;
			float speed = input.GetKeyDown(Key.LeftShift) ? 28 : 14;
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
				camera.Position += Vector3.Normalize(movement) * speed * deltaTime;
		}

		private void ToggleBoundaryBlock(VoxelWorld world, MaterialIds materials)
		{
			boundaryBlockEnabled = !boundaryBlockEnabled;
			world.SetVoxel(15, 8, 0, boundaryBlockEnabled ? new VoxelCell(materials.Glass) : VoxelCell.Air);
		}

		private static void DestroyTargetedVoxel(VoxelWorld world, Camera camera)
		{
			if (VoxelRaycast.Cast(world, camera.Position, camera.WorldForwardNormal, EditReach, out VoxelRaycastHit hit))
				world.SetVoxel(hit.X, hit.Y, hit.Z, VoxelCell.Air);
		}

		private static void PlaceTargetedVoxel(VoxelWorld world, Camera camera, ushort materialId)
		{
			if (
				VoxelRaycast.Cast(world, camera.Position, camera.WorldForwardNormal, EditReach, out VoxelRaycastHit hit)
				&& hit.HasSurfaceNormal
			)
				world.SetVoxel(hit.AdjacentX, hit.AdjacentY, hit.AdjacentZ, new VoxelCell(materialId));
		}

		private static void ForceStaleBoundaryEdit(VoxelWorld world, MaterialIds materials)
		{
			const int coordinate = -17;
			world.SetVoxel(coordinate, -1, coordinate, VoxelCell.Air);
			world.SetVoxel(coordinate, -1, coordinate, new VoxelCell(materials.Stone));
			world.SetVoxel(coordinate, -1, coordinate, VoxelCell.Air);
		}

		private static Camera CreateCamera()
		{
			Camera camera = new Camera();
			camera.SetPerspective(Width, Height, MathF.PI / 2.2f, 0.1f, 500);
			camera.Position = new Vector3(38, 28, 52);
			camera.LookAt(new Vector3(0, 5, 0));

			return camera;
		}

		private static VoxelPalette CreatePalette(out MaterialIds ids)
		{
			VoxelPaletteBuilder builder = new VoxelPaletteBuilder();
			ids = new MaterialIds
			{
				Stone = builder.Add(new VoxelMaterial("Stone", VoxelRenderMode.Opaque, new VoxelFaceTiles(0))),
				Dirt = builder.Add(new VoxelMaterial("Dirt", VoxelRenderMode.Opaque, new VoxelFaceTiles(1))),
				Grass = builder.Add(
					new VoxelMaterial(
						"Grass",
						VoxelRenderMode.Opaque,
						new VoxelFaceTiles(1, 1, 2, 1, 1, 1)
					)
				),
				Leaves = builder.Add(
					new VoxelMaterial(
						"Leaves",
						VoxelRenderMode.Cutout,
						new VoxelFaceTiles(3),
						occludesFaces: false,
						doubleSided: true
					)
				),
				Glass = builder.Add(
					new VoxelMaterial(
						"Glass",
						VoxelRenderMode.Transparent,
						new VoxelFaceTiles(4),
						occludesFaces: false,
						doubleSided: true
					)
				),
				Water = builder.Add(
					new VoxelMaterial(
						"Water",
						VoxelRenderMode.Transparent,
						new VoxelFaceTiles(5),
						occludesFaces: false
					)
				),
			};

			return builder.Build();
		}

		private static VoxelWorld CreateWorld(MaterialIds materials)
		{
			VoxelWorld world = new VoxelWorld();

			for (int z = -32; z < 32; z++)
				for (int x = -32; x < 32; x++)
				{
					int height = 3 + (int)MathF.Round(MathF.Sin(x * 0.17f) * 1.4f + MathF.Cos(z * 0.14f) * 1.2f);

					for (int y = -2; y <= height; y++)
					{
						ushort material = y == height ? materials.Grass : y >= height - 2 ? materials.Dirt : materials.Stone;
						world.SetVoxel(x, y, z, new VoxelCell(material));
					}
				}

			for (int x = -8; x <= 8; x++)
				for (int z = -8; z <= 8; z++)
					for (int y = 4; y <= 6; y++)
						world.SetVoxel(x, y, z, new VoxelCell(materials.Water));

			for (int y = 4; y <= 13; y++)
				for (int x = 14; x <= 17; x++)
					world.SetVoxel(x, y, -4, new VoxelCell(materials.Glass));

			CreateTree(world, materials, -15, 5, -10);
			CreateTree(world, materials, 10, 5, 12);

			return world;
		}

		private static void CreateTree(VoxelWorld world, MaterialIds materials, int x, int y, int z)
		{
			for (int trunkY = y; trunkY < y + 5; trunkY++)
				world.SetVoxel(x, trunkY, z, new VoxelCell(materials.Dirt));

			for (int offsetY = 3; offsetY <= 6; offsetY++)
				for (int offsetZ = -2; offsetZ <= 2; offsetZ++)
					for (int offsetX = -2; offsetX <= 2; offsetX++)
						if (Math.Abs(offsetX) + Math.Abs(offsetZ) < 4)
							world.SetVoxel(x + offsetX, y + offsetY, z + offsetZ, new VoxelCell(materials.Leaves));
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
			VoxelRendererStatistics stats,
			bool cullingEnabled
		)
		{
			Gfx.PushRenderState(overlayState);

			try
			{
				ShaderUniforms.Current.Camera = uiCamera;
				Gfx.FilledRoundedRectangle(20, 740, 670, 310, new CornerRadii(16), new Color(10, 14, 24, 210));
				Gfx.DrawText(font, new Vector2(45, 980), "FishGfx voxel chunks", new Color(110, 205, 255), 34);
				Gfx.DrawText(
					font,
					new Vector2(45, 780),
					$"chunks loaded/gpu/visible: {stats.LoadedChunks} / {stats.GpuChunks} / {stats.VisibleChunks}\n"
						+ $"jobs: {stats.PendingJobs}   accepted: {stats.AcceptedMeshes}   stale: {stats.DiscardedMeshes}\n"
						+ $"vertices opaque/cutout: {stats.OpaqueVertices} / {stats.CutoutVertices}\n"
						+ $"transparent faces/vertices: {stats.TransparentFaces} / {stats.TransparentVertices}\n"
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

		private struct MaterialIds
		{
			public ushort Stone;
			public ushort Dirt;
			public ushort Grass;
			public ushort Leaves;
			public ushort Glass;
			public ushort Water;
		}
	}
}
