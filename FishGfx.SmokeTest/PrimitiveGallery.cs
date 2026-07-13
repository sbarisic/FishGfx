using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using FishGfx;
using FishGfx.Formats;
using FishGfx.Game;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;

namespace FishGfx.SmokeTest
{
	internal sealed class PrimitiveGallery
	{
		internal const int Width = 1920;
		internal const int Height = 1080;
		private const double AutoSceneDuration = 1.0;
		private const float AutoRenderTime = 0.5f;

		private readonly bool autoMode;
		private readonly bool exactOpenGl40;
		private RenderWindow window;
		private GalleryScene[] scenes;
		private int sceneIndex;

		internal int SceneIndex => sceneIndex;

		internal PrimitiveGallery(string[] args)
		{
			autoMode = args.Contains("--auto", StringComparer.OrdinalIgnoreCase);
			exactOpenGl40 = args.Contains("--gl40", StringComparer.OrdinalIgnoreCase);
		}

		internal void Run()
		{
			window = new RenderWindow(new RenderWindowOptions
			{
				Width = Width,
				Height = Height,
				Title = "FishGfx Primitive Scene Gallery",
				PreferredVersion = exactOpenGl40 ? new OpenGLVersion(4, 0) : new OpenGLVersion(4, 6),
				MinimumVersion = new OpenGLVersion(4, 0),
				RequireExactVersion = exactOpenGl40,
			});
			InputManager input = new InputManager(window);
			Texture texture = LoadGridTexture(window.Graphics);
			TTFFont titleFont = window.Graphics.CreateTrueTypeFont(AssetPath("fonts", "Aaargh.ttf"));
			PrimitiveScenes.InitializeFonts();
			scenes = PrimitiveScenes.Create();
			GalleryConsole galleryConsole = new GalleryConsole(window, scenes, this);
			string screenshotDirectory = null;
			string[] screenshotFileNames = null;

			if (autoMode)
			{
				screenshotDirectory = GalleryScreenshot.FindPicturesDirectory();
				screenshotFileNames = GalleryScreenshot.FileNamesForTitles(scenes.Select(scene => scene.Title));
				Directory.CreateDirectory(screenshotDirectory);
			}

			Camera galleryCamera = new Camera();
			galleryCamera.SetOrthogonal(0, 0, Width, Height);

			if (autoMode)
				RunResourcePreflight(window.Graphics, galleryCamera);

			if (autoMode)
				RunTextPreflight(window.Graphics, titleFont, galleryCamera, texture.Size);

			sceneIndex = 0;
			double autoSceneStartedAt = 0;
			int autoFramesRendered = 0;
			bool autoSceneCaptured = false;
			Stopwatch runtime = Stopwatch.StartNew();

			while (!window.ShouldClose)
			{
				input.BeginNewFrame();
				Events.Poll();
				double elapsedSeconds = runtime.Elapsed.TotalSeconds;

				if (autoMode)
				{
					galleryConsole.Close();

					if (input.GetKeyPressed(Key.Escape))
						RequestClose();

					if (autoSceneCaptured && elapsedSeconds - autoSceneStartedAt >= AutoSceneDuration)
					{
						if (sceneIndex == scenes.Length - 1)
						{
							RequestClose();
						}
						else
						{
							sceneIndex++;
							autoSceneStartedAt = elapsedSeconds;
							autoFramesRendered = 0;
							autoSceneCaptured = false;
						}
					}
				}
				else if (galleryConsole.IsOpen)
				{
					if (input.GetKeyPressed(Key.Escape))
						galleryConsole.Close();
				}
				else
				{
					if (input.GetKeyPressed(Key.Space))
						NextScene();
					if (input.GetKeyPressed(Key.Backspace))
						PreviousScene();
					if (input.GetKeyPressed(Key.Escape))
						RequestClose();
				}

				if (window.ShouldClose)
					break;

				using GraphicsFrame frame = window.Graphics.BeginFrame();
				using (RenderPass pass = frame.BeginPass(window.Graphics.Backbuffer, new RenderPassDescriptor
				{
					View = new RenderView(galleryCamera),
					ColorLoadAction = RenderLoadAction.Clear,
					DepthLoadAction = RenderLoadAction.Clear,
					StencilLoadAction = RenderLoadAction.Clear,
					ClearColor = new Color(18, 23, 36),
					TextureSize = texture.Size,
				}))
				{
					scenes[sceneIndex].Draw(pass, autoMode ? AutoRenderTime : (float)elapsedSeconds, texture);
					SceneMenu.Draw(pass, titleFont, scenes, sceneIndex);
					galleryConsole.Draw(pass);
				}

				if (autoMode)
				{
					autoFramesRendered++;

					if (autoFramesRendered == 2)
					{
						GalleryScreenshot.Capture(
							window,
							screenshotDirectory,
							screenshotFileNames[sceneIndex]
						);
						autoSceneCaptured = true;
						Console.WriteLine(
							$"Captured {scenes[sceneIndex].Title} to {screenshotFileNames[sceneIndex]}"
						);
					}
				}

				frame.Present();
			}

			galleryConsole.Dispose();
			PrimitiveScenes.DisposeFonts();
			titleFont.Dispose();
			texture.Dispose();
			window.Graphics.CollectGarbage();
			window.Dispose();
			Console.WriteLine($"Primitive gallery completed using {RenderAPI.Renderer}");
		}

		internal void SelectScene(int index)
		{
			sceneIndex = ((index % scenes.Length) + scenes.Length) % scenes.Length;
		}

		internal void NextScene() => SelectScene(sceneIndex + 1);

		internal void PreviousScene() => SelectScene(sceneIndex - 1);

		internal void RequestClose() => window.ShouldClose = true;

		private static void RunTextPreflight(GraphicsContext graphics, TTFFont font, Camera camera, Vector2 textureSize)
		{
			using GraphicsFrame frame = graphics.BeginFrame();
			using RenderPass pass = frame.BeginPass(graphics.Backbuffer, new RenderPassDescriptor
			{
				View = new RenderView(camera),
				ColorLoadAction = RenderLoadAction.Clear,
				DepthLoadAction = RenderLoadAction.Clear,
				StencilLoadAction = RenderLoadAction.Clear,
				ClearColor = new Color(18, 23, 36),
				TextureSize = textureSize,
			});
			pass.DrawText(font, new Vector2(20, 20), "Text-first preflight", Color.Transparent, 16);
			pass.DrawText(font, new Vector2(20, 20), "\n\t", Color.Transparent, 16, debugDraw: true);
		}

		private static void RunResourcePreflight(GraphicsContext graphics, Camera camera)
		{
			int[] values = { 1, 2, 3, 4 };
			using GraphicsBuffer sourceBuffer = graphics.CreateBuffer<int>(values,
				BufferBindFlags.TransferSource | BufferBindFlags.Vertex, BufferUsage.Dynamic);
			using GraphicsBuffer destinationBuffer = graphics.CreateBuffer(new GraphicsBufferDescriptor(
				sourceBuffer.SizeInBytes, BufferBindFlags.TransferDestination | BufferBindFlags.Vertex, BufferUsage.Dynamic));
			sourceBuffer.CopyTo(destinationBuffer);
			destinationBuffer.Write<int>(new[] { 9 }, sizeof(int));

			TextureUsageFlags sourceUsage = TextureUsageFlags.Sampled | TextureUsageFlags.TransferSource | TextureUsageFlags.TransferDestination;
			using Texture sourceTexture = graphics.CreateTexture(new TextureDescriptor(2, 2, usage: sourceUsage, mipLevels: 2));
			using Texture destinationTexture = graphics.CreateTexture(new TextureDescriptor(2, 2,
				usage: TextureUsageFlags.Sampled | TextureUsageFlags.TransferDestination, mipLevels: 2));
			sourceTexture.Write<Color>(new[] { Color.Red, Color.Green, Color.Blue, Color.White }, TextureDataFormat.RGBA8Unorm);
			sourceTexture.Write<Color>(new[] { new Color(100, 149, 237) }, TextureDataFormat.RGBA8Unorm, new TextureRegion(1, 1, 1, 1));
			sourceTexture.GenerateMipmaps();
			sourceTexture.CopyTo(destinationTexture);

			TextureUsageFlags cubeSourceUsage = TextureUsageFlags.Sampled | TextureUsageFlags.TransferSource | TextureUsageFlags.TransferDestination;
			using Texture sourceCube = graphics.CreateTexture(new TextureDescriptor(2, 2, usage: cubeSourceUsage,
				dimension: TextureDimension.Cube, mipLevels: 2));
			using Texture destinationCube = graphics.CreateTexture(new TextureDescriptor(2, 2,
				usage: TextureUsageFlags.Sampled | TextureUsageFlags.TransferDestination,
				dimension: TextureDimension.Cube, mipLevels: 2));
			for (int face = 0; face < 6; face++)
				sourceCube.Write<Color>(new[] { Color.Red, Color.Green, Color.Blue, Color.White }, TextureDataFormat.RGBA8Unorm,
					new TextureSubresource(0, (CubeFace)face));
			sourceCube.GenerateMipmaps();
			sourceCube.CopyTo(destinationCube);

			using Texture attachment = graphics.CreateTexture(new TextureDescriptor(2, 2,
				usage: TextureUsageFlags.ColorAttachment));
			using Renderbuffer depthStencil = graphics.CreateRenderbuffer();
			depthStencil.Storage(RenderbufferFormat.Depth24Stencil8, 2, 2);
			using Framebuffer framebuffer = graphics.CreateFramebuffer();
			framebuffer.AttachColor(attachment);
			framebuffer.AttachDepth(depthStencil);
			framebuffer.DrawBuffers(0);
			framebuffer.Bind();
			framebuffer.Unbind();

			using Mesh3D mesh = new(BufferUsage.Dynamic);
			mesh.SetVertices(new[]
			{
				new Vertex3(new Vector3(0, 0, 0), Vector2.Zero, Color.Red),
				new Vertex3(new Vector3(1, 0, 0), Vector2.UnitX, Color.Green),
				new Vertex3(new Vector3(0, 1, 0), Vector2.UnitY, Color.Blue),
			});
			mesh.SetVertices(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY });
			mesh.SetColors(new[] { Color.Red, Color.Green, Color.Blue });
			mesh.SetUVs(new[] { Vector2.Zero, Vector2.UnitX, Vector2.UnitY });
			mesh.SetVertices(new[]
			{
				new Vertex3(new Vector3(0, 0, 0), Vector2.Zero, Color.White),
				new Vertex3(new Vector3(1, 0, 0), Vector2.UnitX, Color.White),
				new Vertex3(new Vector3(0, 1, 0), Vector2.UnitY, Color.White),
			});
			mesh.SetElements(0, 1, 2);
			mesh.SetElements();

			using GraphicsFrame frame = graphics.BeginFrame();
			using RenderPass pass = frame.BeginPass(graphics.Backbuffer, new RenderPassDescriptor
			{
				View = new RenderView(camera),
				ColorLoadAction = RenderLoadAction.Clear,
				DepthLoadAction = RenderLoadAction.Clear,
				ClearColor = Color.Black,
				TextureSize = destinationTexture.Size,
			});
			pass.TexturedRectangle(0, 0, 2, 2, Texture: destinationTexture);
		}

		private static Texture LoadGridTexture(GraphicsContext graphics)
		{
			return graphics.LoadTexture2D(AssetPath("textures", "grid.png"));
		}

		internal static string AssetPath(params string[] parts) =>
			Path.Combine(new[] { AppContext.BaseDirectory, "data" }.Concat(parts).ToArray());
	}
}
