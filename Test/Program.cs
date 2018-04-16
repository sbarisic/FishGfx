﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

using FishGfx;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using FishGfx.System;

namespace Test {
	class Program {
		static void Main(string[] args) {
			RenderWindow RWind = new RenderWindow(800, 600, "FishGfx Test");

			ShaderProgram Default = new ShaderProgram(new ShaderStage(ShaderType.VertexShader, "data/default.vert"),
				new ShaderStage(ShaderType.FragmentShader, "data/default.frag"));
			Default.Uniforms.Viewport = new Vector2(800, 600);
			Default.Uniforms.Project = Matrix4x4.CreateOrthographicOffCenter(0, 800, 0, 600, -10, 10);

			Texture QTex = Texture.FromFile("data/quake.png");
			QTex.SetFilterSmooth();

			Mesh2D Msh = new Mesh2D();
			Msh.PrimitiveType = PrimitiveType.Triangles;

			Msh.SetVertices(new Vector2[] {
				new Vector2(0, 0),
				new Vector2(0, 600),
				new Vector2(800, 600),
				new Vector2(0, 0),
				new Vector2(800, 600),
				new Vector2(800, 0)
			});

			Msh.SetUVs(new Vector2[] {
				new Vector2(0, 0),
				new Vector2(0, 1),
				new Vector2(1, 1),
				new Vector2(0, 0),
				new Vector2(1, 1),
				new Vector2(1, 0)
			});

			while (!RWind.ShouldClose) {
				Gfx.Clear();
				//Gfx.Line(new Vector2(0, 0), new Vector2(100, 100));

				Default.Bind();
				QTex.BindTextureUnit();
				Msh.Draw();
				QTex.UnbindTextureUnit();
				Default.Unbind();

				RWind.SwapBuffers();
				Events.Poll();
			}
		}
	}
}
