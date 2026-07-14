using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

namespace FishGfx.Graphics.Drawables;

public sealed class Terrain : IRenderable, IDisposable
{
	private readonly GraphicsContext graphics;
	private readonly Mesh3D mesh;
	private float[] heightData = Array.Empty<float>();
	private Texture generatedOverlayTexture;
	private bool disposed;

	public Terrain(GraphicsContext graphics, ShaderProgram shader)
	{
		this.graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
		Shader = shader ?? throw new ArgumentNullException(nameof(shader));
		Shader.EnsureOwner(graphics);
		mesh = graphics.CreateMesh3D(BufferUsage.Dynamic);
		mesh.PrimitiveType = PrimitiveType.Triangles;
	}

	public int Width { get; private set; }

	public int Height { get; private set; }

	public ShaderProgram Shader { get; }

	public Texture OverlayTexture { get; private set; }

	public AxisAlignedBoundingBox Bounds { get; private set; } = AxisAlignedBoundingBox.Empty;

	public void LoadFromGenerator(
		Func<float, float, float> generator,
		float heightScale,
		int width,
		int height,
		bool generatePickerColors = true
	)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(generator);
		ValidateDimensions(width, height);

		if (!float.IsFinite(heightScale))
		{
			throw new ArgumentOutOfRangeException(nameof(heightScale));
		}

		ResizeHeightData(width, height);

		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				float normalizedX = x / (float)width;
				float normalizedY = y / (float)height;
				heightData[y * width + x] = generator(normalizedX, normalizedY) * heightScale;
			}
		}

		RebuildMesh(generatePickerColors);
	}

	public void LoadFromImage(
		Image image,
		float heightScale = 255,
		bool createOverlayTexture = true,
		bool generatePickerColors = true
	)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(image);

		if (!float.IsFinite(heightScale))
		{
			throw new ArgumentOutOfRangeException(nameof(heightScale));
		}

		ResizeHeightData(image.Width, image.Height);

		using Bitmap bitmap = new(image);

		for (int y = 0; y < bitmap.Height; y++)
		{
			for (int x = 0; x < bitmap.Width; x++)
			{
				System.Drawing.Color pixel = bitmap.GetPixel(x, y);
				float brightness = (pixel.R + pixel.G + pixel.B) / (3f * byte.MaxValue);
				heightData[y * Width + x] = brightness * heightScale;
			}
		}

		if (createOverlayTexture)
		{
			generatedOverlayTexture?.Dispose();
			generatedOverlayTexture = graphics.CreateTextureFromImage(
				image,
				new TextureLoadOptions
				{
					Sampling = new TextureSamplingState(
						TextureFilter.Linear,
						TextureFilter.Linear
					),
				}
			);
			OverlayTexture = generatedOverlayTexture;
		}

		RebuildMesh(generatePickerColors);
	}

	public void SetOverlayTexture(Texture texture)
	{
		ThrowIfDisposed();
		texture?.EnsureOwner(graphics);
		generatedOverlayTexture?.Dispose();
		generatedOverlayTexture = null;
		OverlayTexture = texture;
	}

	public float GetHeight(int x, int y)
	{
		ThrowIfDisposed();

		if (Width == 0 || Height == 0)
		{
			throw new InvalidOperationException("Terrain height data has not been loaded.");
		}

		x = Math.Clamp(x, 0, Width - 1);
		y = Math.Clamp(y, 0, Height - 1);

		return heightData[y * Width + x];
	}

	public void Render(RenderPass pass)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(pass);

		if (Width == 0 || Height == 0)
		{
			return;
		}

		pass.DrawMesh(mesh, OverlayTexture, Shader);
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		generatedOverlayTexture?.Dispose();
		mesh.Dispose();
	}

	private void ResizeHeightData(int width, int height)
	{
		ValidateDimensions(width, height);
		Width = width;
		Height = height;
		heightData = new float[checked(width * height)];
	}

	private void RebuildMesh(bool generatePickerColors)
	{
		List<Vertex3> vertices = new(checked(Width * Height * 6));

		for (int y = 0; y < Height; y++)
		{
			for (int x = 0; x < Width; x++)
			{
				AppendCell(vertices, x, y, generatePickerColors);
			}
		}

		Vertex3[] vertexArray = vertices.ToArray();
		mesh.SetVertices(vertexArray);
		Bounds = AxisAlignedBoundingBox.FromPoints(
			Array.ConvertAll(vertexArray, vertex => vertex.Position)
		);
	}

	private void AppendCell(List<Vertex3> vertices, int x, int y, bool generatePickerColors)
	{
		float uMinimum = x / (float)Width;
		float uMaximum = (x + 1) / (float)Width;
		float vMinimum = (Height - y) / (float)Height;
		float vMaximum = (Height - y + 1) / (float)Height;
		Color color = generatePickerColors
			? new Color(y * Width + x) { A = byte.MaxValue }
			: Color.White;

		Vertex3 topLeft = CreateVertex(x, y + 1, GetHeight(x, y), uMinimum, vMinimum, color);
		Vertex3 topRight = CreateVertex(x + 1, y + 1, GetHeight(x + 1, y), uMaximum, vMinimum, color);
		Vertex3 bottomLeft = CreateVertex(x, y, GetHeight(x, y - 1), uMinimum, vMaximum, color);
		Vertex3 bottomRight = CreateVertex(x + 1, y, GetHeight(x + 1, y - 1), uMaximum, vMaximum, color);

		vertices.Add(topLeft);
		vertices.Add(topRight);
		vertices.Add(bottomLeft);
		vertices.Add(bottomLeft);
		vertices.Add(topRight);
		vertices.Add(bottomRight);
	}

	private static Vertex3 CreateVertex(
		float x,
		float z,
		float height,
		float u,
		float v,
		Color color
	)
	{
		return new Vertex3(new Vector3(x, height, z), new Vector2(u, v), color);
	}

	private static void ValidateDimensions(int width, int height)
	{
		if (width <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width));
		}

		if (height <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(height));
		}
	}

	private void ThrowIfDisposed()
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(Terrain));
		}
	}
}
