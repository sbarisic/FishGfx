using System;
using System.Numerics;
using FishGfx.Graphics.Drawables;

namespace FishGfx.Graphics;

internal sealed partial class ImmediateRenderer
{
	internal void DrawPoint(RenderPass pass, Vertex2[] points, float thickness)
	{
		ArgumentNullException.ThrowIfNull(points);
		PrimitiveTessellator.ValidateThickness(thickness);

		if (points.Length == 0)
		{
			return;
		}

		Ensure2DResources(PrimitiveType.Points);
		scratchMesh2D.SetVertices(points);
		point2D.SetUniform("uThickness", thickness);
		DrawMesh2D(pass, scratchMesh2D, whiteTexture, point2D);
	}

	internal void DrawPoint(RenderPass pass, Vertex3[] points, float thickness)
	{
		ArgumentNullException.ThrowIfNull(points);
		PrimitiveTessellator.ValidateThickness(thickness);

		if (points.Length == 0)
		{
			return;
		}

		Ensure3DResources(PrimitiveType.Points);
		scratchMesh3D.SetVertices(points);

		using IDisposable stateScope = pass.PushState(pass.State with
		{
			PointSize = thickness,
		});

		DrawMesh3D(pass, scratchMesh3D, whiteTexture, default3D);
	}

	internal void DrawLine(RenderPass pass, Vertex2 start, Vertex2 end, float thickness)
	{
		DrawLineStrip(pass, new[] { start, end }, thickness, PrimitiveType.Lines);
	}

	internal void DrawLine(RenderPass pass, Vertex3 start, Vertex3 end, float thickness)
	{
		PrimitiveTessellator.ValidateThickness(thickness);
		Ensure3DResources(PrimitiveType.Lines);
		scratchMesh3D.SetVertices(start, end);
		Internal_OpenGL.GL.LineWidth(thickness);

		try
		{
			DrawMesh3D(pass, scratchMesh3D, whiteTexture, default3D);
		}
		finally
		{
			Internal_OpenGL.GL.LineWidth(1);
		}
	}

	internal void DrawLineStrip(RenderPass pass, Vertex2[] points, float thickness)
	{
		DrawLineStrip(pass, points, thickness, PrimitiveType.LineStrip);
	}

	internal void DrawMesh(
		RenderPass pass,
		Mesh2D mesh,
		Texture texture,
		ShaderProgram shader
	)
	{
		ArgumentNullException.ThrowIfNull(mesh);
		EnsureMeshOwner(mesh.Owner, mesh.IsDisposed, nameof(mesh));
		Ensure2DResources(mesh.PrimitiveType);
		DrawMesh2D(pass, mesh, texture ?? whiteTexture, shader ?? default2D);
	}

	internal void DrawMesh(
		RenderPass pass,
		Mesh3D mesh,
		Texture texture,
		ShaderProgram shader
	)
	{
		ArgumentNullException.ThrowIfNull(mesh);
		EnsureMeshOwner(mesh.Owner, mesh.IsDisposed, nameof(mesh));
		Ensure3DResources(mesh.PrimitiveType);
		DrawMesh3D(pass, mesh, texture ?? whiteTexture, shader ?? default3D);
	}

	private void DrawLineStrip(
		RenderPass pass,
		Vertex2[] points,
		float thickness,
		PrimitiveType primitiveType
	)
	{
		ArgumentNullException.ThrowIfNull(points);
		PrimitiveTessellator.ValidateThickness(thickness);

		if (points.Length == 0)
		{
			return;
		}

		Ensure2DResources(primitiveType);
		scratchMesh2D.SetVertices(points);
		line2D.SetUniform("uThickness", thickness);
		DrawMesh2D(pass, scratchMesh2D, whiteTexture, line2D);
	}

	private void DrawMesh2D(
		RenderPass pass,
		Mesh2D mesh,
		Texture texture,
		ShaderProgram shader
	)
	{
		using IDisposable stateScope = pass.PushState(pass.State with
		{
			Winding = Winding.CounterClockwise,
		});

		DrawBound(pass, texture, shader, mesh.Draw);
	}

	private void DrawMesh3D(
		RenderPass pass,
		Mesh3D mesh,
		Texture texture,
		ShaderProgram shader
	)
	{
		DrawBound(pass, texture, shader, mesh.Draw);
	}

	private void DrawBound(
		RenderPass pass,
		Texture texture,
		ShaderProgram shader,
		Action draw
	)
	{
		pass.EnsureActive();
		ArgumentNullException.ThrowIfNull(texture);
		ArgumentNullException.ThrowIfNull(shader);
		texture.EnsureOwner(context);
		shader.EnsureOwner(context);

		Vector2 previousTextureSize = pass.Uniforms.TextureSize;
		pass.Uniforms.TextureSize = texture.Size;
		texture.BindTextureUnit();

		try
		{
			shader.Bind(pass.Uniforms);

			try
			{
				draw();
			}
			finally
			{
				shader.Unbind();
			}
		}
		finally
		{
			texture.UnbindTextureUnit();
			pass.Uniforms.TextureSize = previousTextureSize;
		}
	}

	private void EnsureMeshOwner(GraphicsContext owner, bool isDisposed, string parameterName)
	{
		if (isDisposed)
		{
			throw new ObjectDisposedException(parameterName);
		}

		if (!ReferenceEquals(owner, context))
		{
			throw new InvalidOperationException("The mesh belongs to another graphics context.");
		}
	}
}
