using System;
using System.Numerics;

namespace FishGfx.Graphics.Drawables;

public sealed class Sprite : IRenderable, IDisposable
{
	private readonly GraphicsContext graphics;
	private readonly Mesh3D mesh;
	private Texture texture;
	private bool disposed;

	public Sprite(
		GraphicsContext graphics,
		ShaderProgram shader,
		Texture texture = null
	)
	{
		ArgumentNullException.ThrowIfNull(graphics);
		this.graphics = graphics;
		Shader = shader ?? throw new ArgumentNullException(nameof(shader));
		Shader.EnsureOwner(graphics);

		if (texture != null)
		{
			texture.EnsureOwner(graphics);
		}

		Texture = texture;
		Scale = Vector2.One;
		mesh = graphics.CreateMesh3D(BufferUsage.Dynamic);
		mesh.PrimitiveType = PrimitiveType.Triangles;
		mesh.SetVertices(CreateVertices());
	}

	public ShaderProgram Shader { get; }

	public Texture Texture
	{
		get => texture;
		set
		{
			value?.EnsureOwner(graphics);
			texture = value;
		}
	}

	public Vector2 Center { get; set; }

	public Vector2 Position { get; set; }

	public Vector2 Scale { get; set; }

	public bool IsFlippedHorizontally { get; set; }

	public void SetUvRectangle(Vector2 minimum, Vector2 maximum)
	{
		ThrowIfDisposed();

		mesh.SetUVs(
			new[]
			{
				minimum,
				new Vector2(minimum.X, maximum.Y),
				maximum,
				maximum,
				new Vector2(maximum.X, minimum.Y),
				minimum,
			}
		);
	}

	public void Render(RenderPass pass)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(pass);
		pass.EnsureActive();
		Shader.EnsureOwner(pass.Context);
		Texture?.EnsureOwner(pass.Context);

		using IDisposable modelScope = pass.PushModel(CreateTransform());

		Shader.Bind(pass.Uniforms);

		try
		{
			Texture?.BindTextureUnit();

			try
			{
				mesh.Draw();
			}
			finally
			{
				Texture?.UnbindTextureUnit();
			}
		}
		finally
		{
			Shader.Unbind();
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		mesh.Dispose();
	}

	private Matrix4x4 CreateTransform()
	{
		float horizontalDirection = IsFlippedHorizontally ? -1 : 1;
		Vector2 directionalCenter = new(Center.X * horizontalDirection, Center.Y);
		Matrix4x4 scale = Matrix4x4.CreateScale(
			Scale.X * horizontalDirection,
			Scale.Y,
			1
		);
		Matrix4x4 translation = Matrix4x4.CreateTranslation(
			Position.X - directionalCenter.X,
			Position.Y - directionalCenter.Y,
			0
		);

		return scale * translation;
	}

	private static Vertex3[] CreateVertices()
	{
		return new[]
		{
			new Vertex3(new Vector3(0, 0, 0), new Vector2(0, 0)),
			new Vertex3(new Vector3(0, 1, 0), new Vector2(0, 1)),
			new Vertex3(new Vector3(1, 1, 0), new Vector2(1, 1)),
			new Vertex3(new Vector3(1, 1, 0), new Vector2(1, 1)),
			new Vertex3(new Vector3(1, 0, 0), new Vector2(1, 0)),
			new Vertex3(new Vector3(0, 0, 0), new Vector2(0, 0)),
		};
	}

	private void ThrowIfDisposed()
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(Sprite));
		}
	}
}
