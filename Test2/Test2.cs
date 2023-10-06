using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

using FishGfx;
using FishGfx.Game;
using FishGfx.Graphics;
using FishGfx.AdvGraphics;

namespace Test2 {
    class FishGfxTest2 : FishGfxGame {
        public DevConsole Con;
        public ParallaxSprite Background;

        bool ShouldClose = false;

        public FishGfxTest2() : base(1920, 1080) {
        }

        protected override void Init() {
            int FontSize = 16;
            int W = (int)Math.Floor((float)Window.WindowWidth / FontSize);
            int H = (int)((Window.WindowHeight * 0.4f) / FontSize);

            Con = new DevConsole(Texture.FromFile("data/fonts/tileset/cheepicus16.png"), FontSize, W, H, H * 2, DefaultShader);
            Con.Position = new Vector2(0, Window.WindowHeight - H * Con.CharSize);

            Con.OnInput += (In) => {
                if (In.Length == 0) {
                    Con.PrintLine();
                    return;
                }

                if (In == "quit") 
                    ShouldClose = true;

                if (In.StartsWith("rainbow")) {
                    foreach (var C in In.Substring(7).Trim()) {
                        Con.TextColor = GfxUtils.RandomColor();
                        Con.PutChar(C);
                    }

                    Con.TextColor = Color.White;
                    Con.PrintLine();
                    return;
                }

                Con.PrintLine("You wrote '{0}'", In);
            };

            Con.PrintLine("Welcome to the Developer Console");
            Con.BeginInput();
            Con.Enabled = false;

            Window.OnChar += (Wnd, Chr, Uni) => Con.SendInput(Chr);
            Window.OnKey += Con.SendKey;

            Background = new ParallaxSprite(this);
            Background.AddLayer(Texture.FromFile("data/textures/background/space1/1.png"));
            Background.AddLayer(Texture.FromFile("data/textures/background/space1/2.png"));
            Background.AddLayer(Texture.FromFile("data/textures/background/space1/3.png"));

            RenderState RS = Gfx.CreateDefaultRenderState();
            RS.EnableDepthTest = false;
            RS.EnableCullFace = false;
            Gfx.PushRenderState(RS);
        }

        protected override void Update(float Dt) {
            if (Input.GetKeyPressed(Key.Escape) || ShouldClose)
                Window.Close();
        }

        protected override void Draw(float Dt) {
            Camera Cam = ShaderUniforms.Current.Camera;

            Color[] Colors = new[] { Color.Red, Color.Green, Color.Blue, Color.Orange };
            Gfx.Clear(Color.Orange, true, true, true);
            Background.Draw(Cam);

            // Draw some fancy lines
            for (int i = 0; i < 10; i++) {
                Color ClrA = Colors[i % Colors.Length];
                Color ClrB = Colors[(i + 1) % Colors.Length];

                Vector2 XOffset = new Vector2(40, 0);
                Vertex2 A = new Vertex2(new Vector2(100, 100) + XOffset * i, ClrA);
                Vertex2 B = new Vertex2(new Vector2(150, 200) + XOffset * i, ClrB);

                Gfx.Line(A, B, i + 1);
            }

            // Draw rectangles

            for (int i = 0; i < 3; i++) {
                Vector2 Offset = new Vector2(20, 20);

                Gfx.FilledRectangle(100 + Offset.X * i, 300 + Offset.Y * i, 200, 90, Colors[i % Colors.Length]);
            }


            for (int i = 0; i < 3; i++) {
                Vector2 Offset = new Vector2(20, 20);

                Gfx.Rectangle(400 + Offset.X * i, 300 + Offset.Y * i, 200, 90, i + 1, Colors[i % Colors.Length]);
            }


            // TODO: Handle that shit better, what the fuck
            Vector2 ConPos = Con.Position;
            Con.Position = Cam.Position.XY() + ConPos;
            Con.Draw();
            Con.Position = ConPos;
        }
    }

    internal class Program {
        static void Main(string[] args) {
            FishGfxGame.Run(new FishGfxTest2());
        }
    }
}
