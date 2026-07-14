using System;
using System.Diagnostics;
using System.Threading;
using FishGfx.Graphics;

namespace FishGfx.Game;

public abstract class FishGfxGame
{
	private readonly int initialWidth;
	private readonly int initialHeight;
	private Stopwatch gameStopwatch;

	protected FishGfxGame()
		: this(1366, 768)
	{
	}

	protected FishGfxGame(int width, int height)
	{
		if (width <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width));
		}

		if (height <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(height));
		}

		initialWidth = width;
		initialHeight = height;
	}

	protected RenderWindow Window { get; private set; }

	protected Camera Camera { get; private set; }

	protected InputManager Input { get; private set; }

	protected ShaderProgram DefaultShader { get; private set; }

	protected int Framerate { get; set; } = 60;

	public float GameTime => (float)gameStopwatch.Elapsed.TotalSeconds;

	protected virtual RenderWindow CreateWindow()
	{
		return new RenderWindow(initialWidth, initialHeight, GetType().Name);
	}

	protected virtual void CreateResources()
	{
		ShaderStage vertexShader = Window.Graphics.LoadShaderStage(
			ShaderStageType.Vertex,
			"data/shaders/default3d.vert"
		);
		ShaderStage fragmentShader = Window.Graphics.LoadShaderStage(
			ShaderStageType.Fragment,
			"data/shaders/default.frag"
		);

		DefaultShader = Window.Graphics.CreateShaderProgram(vertexShader, fragmentShader);
	}

	protected abstract void Initialize();

	protected abstract void Update(float deltaTime);

	protected abstract void Draw(RenderPass pass, float deltaTime);

	public static void Run(FishGfxGame game)
	{
		ArgumentNullException.ThrowIfNull(game);

		game.Run();
	}

	private void Run()
	{
		using RenderWindow window = CreateWindow();
		using InputManager input = new(window);
		Window = window;
		Input = input;
		Camera = new Camera();
		Camera.SetOrthogonal(0, 0, window.Width, window.Height);
		CreateResources();
		Initialize();
		gameStopwatch = Stopwatch.StartNew();
		Stopwatch frameStopwatch = Stopwatch.StartNew();

		while (!window.IsCloseRequested)
		{
			WaitForFrame(frameStopwatch);

			float deltaTime = (float)frameStopwatch.Elapsed.TotalSeconds;
			frameStopwatch.Restart();
			input.BeginFrame();
			window.PollEvents();

			if (window.IsCloseRequested)
			{
				break;
			}

			using RenderFrame frame = window.Graphics.BeginFrame();
			using (RenderPass pass = frame.BeginPass(
				window.Graphics.Backbuffer,
				new RenderPassDescriptor
				{
					View = new RenderView(Camera),
					State = RenderState.Default,
				}
			))
			{
				Draw(pass, deltaTime);
			}

			frame.Present();
			Update(deltaTime);
		}

		DefaultShader?.Dispose();
		window.Graphics.CollectGarbage();
	}

	private void WaitForFrame(Stopwatch frameStopwatch)
	{
		if (Framerate <= 0)
		{
			return;
		}

		double targetSeconds = 1d / Framerate;

		while (frameStopwatch.Elapsed.TotalSeconds < targetSeconds)
		{
			Thread.Sleep(0);
		}
	}
}
