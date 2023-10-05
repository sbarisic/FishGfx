using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

using FishGfx;
using FishGfx.Game;
using FishGfx.Graphics;

namespace Test2 {
class FishGfxTest2 : FishGfxGame {
    protected override void Init() {
        RenderState RS = Gfx.CreateDefaultRenderState();
        RS.EnableDepthTest = false;
        Gfx.PushRenderState(RS);
    }

    protected override void Update(float Dt) {
    }

    protected override void Draw(float Dt) {
        Color[] Colors = new[] { Color.Red, Color.Green, Color.Blue, Color.Orange };

        Gfx.Clear(Color.Coal, true, true, true);

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
    }
}

internal class Program {
    static void Main(string[] args) {
        FishGfxGame.Run(new FishGfxTest2());
    }
}
}
