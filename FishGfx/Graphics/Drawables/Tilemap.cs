using System;
using System.Collections.Generic;
using System.Numerics;

namespace FishGfx.Graphics.Drawables;

public sealed class Tilemap : IRenderable, IDisposable
{
	private readonly GraphicsContext graphics;
	private readonly Mesh3D mesh;
	private readonly int[] tiles;
	private readonly Color[] tileColors;
	private Texture tileAtlas;
	private Vector2 tileUvSize;
	private int atlasColumns;
	private int drawableTileCount;
	private bool isDirty = true;
	private bool disposed;

	public Tilemap(
		GraphicsContext graphics,
		ShaderProgram shader,
		Texture tileAtlas,
		int tileSize,
		int width,
		int height
	)
	{
		this.graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
		Shader = shader ?? throw new ArgumentNullException(nameof(shader));
		Shader.EnsureOwner(graphics);

		if (tileSize <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(tileSize));
		}

		if (width <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width));
		}

		if (height <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(height));
		}

		TileSize = tileSize;
		Width = width;
		Height = height;
		tiles = new int[checked(width * height)];
		tileColors = new Color[tiles.Length];
		Scale = Vector2.One;

		mesh = graphics.CreateMesh3D(BufferUsage.Dynamic);
		mesh.PrimitiveType = PrimitiveType.Triangles;

		SetTileAtlas(tileAtlas);
		Clear();
	}

	public int Width { get; }

	public int Height { get; }

	public int TileSize { get; }

	public ShaderProgram Shader { get; }

	public Texture TileAtlas => tileAtlas;

	public Vector2 Position { get; set; }

	public Vector2 Scale { get; set; }

	public void Clear(int tile = -1, Color? color = null)
	{
		ThrowIfDisposed();
		Color tileColor = color ?? Color.White;

		for (int index = 0; index < tiles.Length; index++)
		{
			tiles[index] = tile;
			tileColors[index] = tileColor;
		}

		isDirty = true;
	}

	public void SetTileAtlas(Texture texture)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(texture);
		texture.EnsureOwner(graphics);

		if (texture.Width < TileSize || texture.Height < TileSize)
		{
			throw new ArgumentException(
				"The tile atlas must contain at least one complete tile.",
				nameof(texture)
			);
		}

		tileAtlas = texture;
		atlasColumns = texture.Width / TileSize;
		tileUvSize = new Vector2(
			TileSize / (float)texture.Width,
			TileSize / (float)texture.Height
		);
		isDirty = true;
	}

	public void SetTile(int x, int y, int tile, Color? color = null)
	{
		ThrowIfDisposed();
		int index = GetIndex(x, y);
		tiles[index] = tile;
		tileColors[index] = color ?? Color.White;
		isDirty = true;
	}

	public int GetTile(int x, int y)
	{
		ThrowIfDisposed();

		return tiles[GetIndex(x, y)];
	}

	public Color GetTileColor(int x, int y)
	{
		ThrowIfDisposed();

		return tileColors[GetIndex(x, y)];
	}

	public bool TryWorldPositionToTile(Vector2 worldPosition, out int x, out int y)
	{
		ThrowIfDisposed();
		x = 0;
		y = 0;

		if (Scale.X == 0 || Scale.Y == 0)
		{
			return false;
		}

		Vector2 localPosition = (worldPosition - Position) / Scale;

		if (localPosition.X < 0 || localPosition.Y < 0)
		{
			return false;
		}

		x = (int)(localPosition.X / TileSize);
		y = (int)(localPosition.Y / TileSize);

		return x < Width && y < Height;
	}

	public void Render(RenderPass pass)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(pass);

		if (isDirty)
		{
			RebuildMesh();
		}

		if (drawableTileCount == 0)
		{
			return;
		}

		Matrix4x4 transform = Matrix4x4.CreateScale(Scale.X, Scale.Y, 1)
			* Matrix4x4.CreateTranslation(Position.X, Position.Y, 0);

		using IDisposable modelScope = pass.PushModel(transform);
		pass.DrawMesh(mesh, tileAtlas, Shader);
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

	private void RebuildMesh()
	{
		List<Vertex3> vertices = new(tiles.Length * 6);
		drawableTileCount = 0;

		for (int index = 0; index < tiles.Length; index++)
		{
			int tile = tiles[index];

			if (tile < 0)
			{
				continue;
			}

			int x = index % Width;
			int y = index / Width;
			AppendTile(vertices, x, y, tile, tileColors[index]);
			drawableTileCount++;
		}

		mesh.SetVertices(vertices.ToArray());
		isDirty = false;
	}

	private void AppendTile(
		List<Vertex3> vertices,
		int x,
		int y,
		int tile,
		Color color
	)
	{
		int atlasX = tile % atlasColumns;
		int atlasY = tile / atlasColumns;
		Vector2 uvMinimum = new Vector2(atlasX, atlasY) * tileUvSize;
		uvMinimum.Y = 1 - uvMinimum.Y - tileUvSize.Y;
		Vector2 uvMaximum = uvMinimum + tileUvSize;
		Vector3 minimum = new(x * TileSize, y * TileSize, 0);
		Vector3 maximum = minimum + new Vector3(TileSize, TileSize, 0);

		vertices.Add(new Vertex3(minimum, uvMinimum, color));
		vertices.Add(
			new Vertex3(
				new Vector3(minimum.X, maximum.Y, 0),
				new Vector2(uvMinimum.X, uvMaximum.Y),
				color
			)
		);
		vertices.Add(new Vertex3(maximum, uvMaximum, color));
		vertices.Add(new Vertex3(maximum, uvMaximum, color));
		vertices.Add(
			new Vertex3(
				new Vector3(maximum.X, minimum.Y, 0),
				new Vector2(uvMaximum.X, uvMinimum.Y),
				color
			)
		);
		vertices.Add(new Vertex3(minimum, uvMinimum, color));
	}

	private int GetIndex(int x, int y)
	{
		if ((uint)x >= (uint)Width)
		{
			throw new ArgumentOutOfRangeException(nameof(x));
		}

		if ((uint)y >= (uint)Height)
		{
			throw new ArgumentOutOfRangeException(nameof(y));
		}

		return y * Width + x;
	}

	private void ThrowIfDisposed()
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(Tilemap));
		}
	}
}
