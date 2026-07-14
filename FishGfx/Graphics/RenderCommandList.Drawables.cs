using System.Numerics;
using FishGfx.Graphics.Drawables;

namespace FishGfx.Graphics;

public sealed partial class RenderCommandList
{
	public DrawTextCommand RecordDrawText(
		GraphicsFont font,
		Vector2 position,
		string text,
		Color color,
		float size,
		bool debugDraw = false
	)
	{
		return Add(new DrawTextCommand(font, position, text, color, size, debugDraw));
	}

	public DrawTextCommand RecordDrawText(
		GraphicsFont font,
		Vector2 position,
		string text,
		Color color,
		float size,
		float characterSpacing,
		bool debugDraw = false
	)
	{
		return Add(
			new DrawTextCommand(
				font,
				position,
				text,
				color,
				size,
				characterSpacing,
				debugDraw
			)
		);
	}

	public DrawMeshCommand RecordDrawMesh(
		Mesh3D mesh,
		Texture texture = null,
		ShaderProgram shader = null
	)
	{
		return Add(new DrawMeshCommand(mesh, texture, shader));
	}

	public DrawModelCommand RecordDrawModel(
		RenderModel model,
		ShaderProgram shader = null
	)
	{
		return Add(new DrawModelCommand(model, shader));
	}
}
