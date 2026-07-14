using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using FishGfx.Formats;
using FishGfx.Game;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;

namespace FishGfx.SmokeTest;

internal sealed class PrimitiveGallery
{
	internal const int Width = 1920;
	internal const int Height = 1080;

	private const double AutoSceneDuration = 1;
	private const float AutoRenderTime = 0.5f;

	private readonly bool autoMode;
	private readonly bool exactOpenGl40;
	private RenderWindow window;
	private GalleryScene[] scenes;
	private int sceneIndex;

	internal PrimitiveGallery(string[] arguments)
	{
		autoMode = arguments.Contains("--auto", StringComparer.OrdinalIgnoreCase);
		exactOpenGl40 = arguments.Contains("--gl40", StringComparer.OrdinalIgnoreCase);
	}

	internal int SceneIndex => sceneIndex;

	internal void Run()
	{
		using RenderWindow renderWindow = CreateWindow();
		using InputManager input = new(renderWindow);
		using Texture texture = LoadGridTexture(renderWindow.Graphics);
		using TrueTypeFont titleFont = new(AssetPath("fonts", "Aaargh.ttf"));
		using GalleryConsole galleryConsole = CreateGalleryConsole(renderWindow);
		window = renderWindow;
		Camera camera = CreateCamera();
		ScreenshotRun screenshots = autoMode ? ScreenshotRun.Create(scenes) : null;

		try
		{
			if (autoMode)
			{
				RunResourcePreflight(renderWindow.Graphics, camera);
				RunTextPreflight(renderWindow.Graphics, titleFont, camera);
			}

			RunLoop(input, texture, titleFont, galleryConsole, camera, screenshots);
		}
		finally
		{
			PrimitiveScenes.DisposeFonts();
			renderWindow.Graphics.CollectGarbage();
		}

		Console.WriteLine(
			$"Primitive gallery completed using {renderWindow.Graphics.Capabilities.Renderer}"
		);
	}

	internal void SelectScene(int index)
	{
		sceneIndex = ((index % scenes.Length) + scenes.Length) % scenes.Length;
	}

	internal void NextScene()
	{
		SelectScene(sceneIndex + 1);
	}

	internal void PreviousScene()
	{
		SelectScene(sceneIndex - 1);
	}

	internal void RequestClose()
	{
		window.IsCloseRequested = true;
	}

	private RenderWindow CreateWindow()
	{
		return new RenderWindow(
			new RenderWindowOptions
			{
				Width = Width,
				Height = Height,
				Title = "FishGfx Primitive Scene Gallery",
				PreferredVersion = exactOpenGl40
					? new OpenGlVersion(4, 0)
					: new OpenGlVersion(4, 6),
				MinimumVersion = new OpenGlVersion(4, 0),
				RequireExactVersion = exactOpenGl40,
			}
		);
	}

	private GalleryConsole CreateGalleryConsole(RenderWindow renderWindow)
	{
		PrimitiveScenes.InitializeFonts();
		scenes = PrimitiveScenes.Create();

		return new GalleryConsole(renderWindow, scenes, this);
	}

	private static Camera CreateCamera()
	{
		Camera camera = new();
		camera.SetOrthogonal(0, 0, Width, Height);

		return camera;
	}

	private void RunLoop(
		InputManager input,
		Texture texture,
		TrueTypeFont titleFont,
		GalleryConsole galleryConsole,
		Camera camera,
		ScreenshotRun screenshots
	)
	{
		sceneIndex = 0;
		double autoSceneStartedAt = 0;
		int autoFramesRendered = 0;
		bool autoSceneCaptured = false;
		Stopwatch runtime = Stopwatch.StartNew();

		while (!window.IsCloseRequested)
		{
			input.BeginFrame();
			window.PollEvents();
			double elapsedSeconds = runtime.Elapsed.TotalSeconds;

			HandleInput(
				input,
				galleryConsole,
				elapsedSeconds,
				ref autoSceneStartedAt,
				ref autoFramesRendered,
				ref autoSceneCaptured
			);

			if (window.IsCloseRequested)
			{
				break;
			}

			RenderFrameContent(
				texture,
				titleFont,
				galleryConsole,
				camera,
				(float)elapsedSeconds
			);

			if (autoMode)
			{
				autoFramesRendered++;

				if (autoFramesRendered == 2)
				{
					screenshots.Capture(window, scenes[sceneIndex], sceneIndex);
					autoSceneCaptured = true;
				}
			}
		}
	}

	private void HandleInput(
		InputManager input,
		GalleryConsole galleryConsole,
		double elapsedSeconds,
		ref double autoSceneStartedAt,
		ref int autoFramesRendered,
		ref bool autoSceneCaptured
	)
	{
		if (autoMode)
		{
			galleryConsole.Close();

			if (input.WasKeyPressed(Key.Escape))
			{
				RequestClose();
			}

			if (autoSceneCaptured
				&& elapsedSeconds - autoSceneStartedAt >= AutoSceneDuration)
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

			return;
		}

		if (galleryConsole.IsOpen)
		{
			if (input.WasKeyPressed(Key.Escape))
			{
				galleryConsole.Close();
			}

			return;
		}

		if (input.WasKeyPressed(Key.Space))
		{
			NextScene();
		}

		if (input.WasKeyPressed(Key.Backspace))
		{
			PreviousScene();
		}

		if (input.WasKeyPressed(Key.Escape))
		{
			RequestClose();
		}
	}

	private void RenderFrameContent(
		Texture texture,
		TrueTypeFont titleFont,
		GalleryConsole galleryConsole,
		Camera camera,
		float elapsedSeconds
	)
	{
		using RenderFrame frame = window.Graphics.BeginFrame();

		using (RenderPass pass = frame.BeginPass(
			window.Graphics.Backbuffer,
			CreatePassDescriptor(camera, elapsedSeconds)
		))
		{
			float sceneTime = autoMode ? AutoRenderTime : elapsedSeconds;
			scenes[sceneIndex].Draw(pass, sceneTime, texture);
			SceneMenu.Draw(pass, titleFont, scenes, sceneIndex);
			galleryConsole.Draw(pass);
		}

		frame.Present();
	}

	private static RenderPassDescriptor CreatePassDescriptor(Camera camera, float time = 0)
	{
		return new RenderPassDescriptor
		{
			View = new RenderView(camera),
			State = RenderState.Default,
			ColorLoadAction = RenderLoadAction.Clear,
			DepthLoadAction = RenderLoadAction.Clear,
			StencilLoadAction = RenderLoadAction.Clear,
			ClearColor = new Color(18, 23, 36),
			Time = time,
		};
	}

	private static void RunTextPreflight(
		GraphicsContext graphics,
		TrueTypeFont font,
		Camera camera
	)
	{
		using RenderFrame frame = graphics.BeginFrame();
		using RenderPass pass = frame.BeginPass(
			graphics.Backbuffer,
			CreatePassDescriptor(camera)
		);
		pass.DrawText(
			font,
			new Vector2(20, 20),
			"Text-first preflight",
			Color.Transparent,
			16
		);
		pass.DrawText(
			font,
			new Vector2(20, 20),
			"\n\t",
			Color.Transparent,
			16,
			debugDraw: true
		);
	}

	private static void RunResourcePreflight(GraphicsContext graphics, Camera camera)
	{
		PreflightBuffers(graphics);
		using Texture destinationTexture = PreflightTextures(graphics);
		using RenderTarget target = graphics.CreateRenderTarget(
			new RenderTargetDescriptor(2, 2)
		);
		using Mesh3D mesh = CreatePreflightMesh(graphics);
		using RenderFrame frame = graphics.BeginFrame();
		using RenderPass pass = frame.BeginPass(
			graphics.Backbuffer,
			CreatePassDescriptor(camera)
		);
		pass.DrawTexturedRectangle(
			0,
			0,
			2,
			2,
			texture: destinationTexture
		);
	}

	private static void PreflightBuffers(GraphicsContext graphics)
	{
		int[] values = { 1, 2, 3, 4 };
		using GraphicsBuffer source = graphics.CreateBuffer<int>(
			values,
			BufferBindFlags.TransferSource | BufferBindFlags.Vertex,
			BufferUsage.Dynamic
		);
		using GraphicsBuffer destination = graphics.CreateBuffer(
			new GraphicsBufferDescriptor(
				source.SizeInBytes,
				BufferBindFlags.TransferDestination | BufferBindFlags.Vertex,
				BufferUsage.Dynamic
			)
		);
		source.CopyTo(destination);
		destination.Write<int>(new[] { 9 }, sizeof(int));
	}

	private static Texture PreflightTextures(GraphicsContext graphics)
	{
		TextureUsageFlags sourceUsage = TextureUsageFlags.Sampled
			| TextureUsageFlags.TransferSource
			| TextureUsageFlags.TransferDestination;
		using Texture source = graphics.CreateTexture(
			new TextureDescriptor(2, 2, usage: sourceUsage, mipLevels: 2)
		);
		Texture destination = graphics.CreateTexture(
			new TextureDescriptor(
				2,
				2,
				usage: TextureUsageFlags.Sampled | TextureUsageFlags.TransferDestination,
				mipLevels: 2
			)
		);

		try
		{
			source.Write<Color>(
				new[] { Color.Red, Color.Green, Color.Blue, Color.White },
				TextureDataFormat.RGBA8Unorm
			);
			source.Write<Color>(
				new[] { new Color(100, 149, 237) },
				TextureDataFormat.RGBA8Unorm,
				new TextureRegion(1, 1, 1, 1)
			);
			source.GenerateMipmaps();
			source.CopyTo(destination);

			return destination;
		}
		catch
		{
			destination.Dispose();

			throw;
		}
	}

	private static Mesh3D CreatePreflightMesh(GraphicsContext graphics)
	{
		Mesh3D mesh = graphics.CreateMesh3D(BufferUsage.Dynamic);
		mesh.SetVertices(
			new[]
			{
				new Vertex3(Vector3.Zero, Vector2.Zero, Color.Red),
				new Vertex3(Vector3.UnitX, Vector2.UnitX, Color.Green),
				new Vertex3(Vector3.UnitY, Vector2.UnitY, Color.Blue),
			}
		);
		mesh.SetElements(0, 1, 2);

		return mesh;
	}

	private static Texture LoadGridTexture(GraphicsContext graphics)
	{
		return graphics.LoadTexture(AssetPath("textures", "grid.png"));
	}

	internal static string AssetPath(params string[] parts)
	{
		return Path.Combine(
			new[] { AppContext.BaseDirectory, "data" }.Concat(parts).ToArray()
		);
	}

	private sealed class ScreenshotRun
	{
		private readonly string directory;
		private readonly string[] fileNames;

		private ScreenshotRun(string directory, string[] fileNames)
		{
			this.directory = directory;
			this.fileNames = fileNames;
		}

		internal static ScreenshotRun Create(GalleryScene[] scenes)
		{
			string directory = GalleryScreenshot.FindPicturesDirectory();
			string[] fileNames = GalleryScreenshot.FileNamesForTitles(
				scenes.Select(scene => scene.Title)
			);
			Directory.CreateDirectory(directory);

			return new ScreenshotRun(directory, fileNames);
		}

		internal void Capture(RenderWindow window, GalleryScene scene, int index)
		{
			GalleryScreenshot.Capture(window, directory, fileNames[index]);
			Console.WriteLine($"Captured {scene.Title} to {fileNames[index]}");
		}
	}
}
