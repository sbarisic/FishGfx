using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FishGfx;
using FishGfx.Graphics;

namespace FishGfx.Game
{
	public abstract class FishGfxGame
	{
		protected RenderWindow Window;
		protected Camera Camera { get; private set; }
		protected RenderPass RenderPass { get; private set; }
		protected int Framerate = 60;

		private int WindowWidth;
		private int WindowHeight;

		Stopwatch GameStopwatch;
		public float GameTime
		{
			get { return GameStopwatch.ElapsedMilliseconds / 1000.0f; }
		}

		public InputManager Input { get; private set; }

		public FishGfxGame()
			: this(1366, 768) { }

		public FishGfxGame(int Width, int Height)
		{
			WindowWidth = Width;
			WindowHeight = Height;
		}

		protected virtual RenderWindow CreateWindow()
		{
			string CurTypeName = GetType().Name;

			return new RenderWindow(WindowWidth, WindowHeight, CurTypeName);
		}

		public ShaderProgram DefaultShader;

		protected virtual void CreateShaders()
		{
			DefaultShader = Window.Graphics.CreateShaderProgram(
				Window.Graphics.CreateShaderStage(ShaderType.VertexShader, "data/shaders/default3d.vert"),
				Window.Graphics.CreateShaderStage(ShaderType.FragmentShader, "data/shaders/default.frag")
			);
		}

		protected virtual void CreateResources()
		{
			CreateShaders();
		}

		protected abstract void Init();

		protected abstract void Update(float Dt);

		protected abstract void Draw(float Dt);

		public static void Run(FishGfxGame Game)
		{
			Stopwatch SWatch = Stopwatch.StartNew();
			float Dt;

			Game.Window = Game.CreateWindow();
			Game.GameStopwatch = Stopwatch.StartNew();
			Game.Input = new InputManager(Game.Window);

			Game.Camera = new Camera();
			Game.Camera.SetOrthogonal(0, 0, Game.Window.WindowWidth, Game.Window.WindowHeight);

			Game.CreateResources();
			Game.Init();
			SWatch.Restart();

			while (!Game.Window.ShouldClose)
			{
				if (Game.Framerate > 0)
					while (SWatch.ElapsedMilliseconds / 1000.0f < (1.0f / Game.Framerate))
						;

				Dt = SWatch.ElapsedMilliseconds / 1000.0f;
				SWatch.Restart();

				Game.Input.BeginNewFrame();
				Events.Poll();

				// TODO: Decouple draw and update

				using GraphicsFrame frame = Game.Window.Graphics.BeginFrame();
				using (RenderPass pass = frame.BeginPass(Game.Window.Graphics.Backbuffer, new RenderPassDescriptor
				{
					View = new RenderView(Game.Camera),
					State = Gfx.CreateDefaultRenderState(),
				}))
				{
					Game.RenderPass = pass;
					try { Game.Draw(Dt); }
					finally { Game.RenderPass = null; }
				}
				frame.Present();

				Game.Update(Dt);
			}
			Game.Window.Dispose();
		}
	}
}
