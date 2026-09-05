using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics.Drawables;

public sealed class Mesh2D : IDisposable
{
	private const uint VertexAttribute = 0;
	private const uint ColorAttribute = 1;
	private const uint UvAttribute = 2;

	private readonly GraphicsContext owner;
	private readonly BufferUsage usage;
	private GraphicsBuffer vertexBuffer;
	private GraphicsBuffer colorBuffer;
	private GraphicsBuffer uvBuffer;
	private GraphicsBuffer elementBuffer;
	private int vertexCount;
	private int elementCount;
	private bool hasColors;
	private bool disposed;
    private Vector2[] stagedPositions = Array.Empty<Vector2>(), stagedUvs = Array.Empty<Vector2>();
    private Color[] stagedColors = Array.Empty<Color>();

	internal Mesh2D(
		GraphicsContext owner,
		BufferUsage usage = BufferUsage.Static
	)
	{
		this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
		this.usage = usage;
		owner.EnsureCurrent();
		VertexArray = owner.CreateVertexArray();
	}

	public GraphicsContext Owner => owner;

	public bool IsDisposed => disposed;

	public Color DefaultColor { get; set; } = Color.White;

	public PolygonMode PolygonMode { get; set; } = PolygonMode.Fill;

	public PrimitiveType PrimitiveType
	{
		get => VertexArray.PrimitiveType;
		set => VertexArray.PrimitiveType = value;
	}

	internal VertexArray VertexArray { get; }

	public void SetVertices(Vector2[] vertices) => SetVertices((ReadOnlySpan<Vector2>)vertices);

	public void SetVertices(ReadOnlySpan<Vector2> vertices)
	{
		ThrowIfDisposed();

		if (vertices.Length == 0)
		{
			VertexArray.AttribEnable(VertexAttribute, false);
			vertexCount = 0;

			return;
		}

		if (vertexBuffer == null)
		{
			VertexArray.AttribFormat(VertexAttribute, 2);
			vertexBuffer = CreateBuffer(vertices, BufferBindFlags.Vertex);
			uint binding = VertexArray.BindVertexBuffer(
				vertexBuffer,
				stride: 2 * sizeof(float)
			);
			VertexArray.AttribBinding(VertexAttribute, binding);
		}
		else
		{
			Upload(vertexBuffer, vertices);
		}

		vertexCount = vertices.Length;
		VertexArray.AttribEnable(VertexAttribute);
	}

	public void SetColors(Color[] colors) => SetColors((ReadOnlySpan<Color>)colors);

	public void SetColors(ReadOnlySpan<Color> colors)
	{
		ThrowIfDisposed();

		if (colors.Length == 0)
		{
			VertexArray.AttribEnable(ColorAttribute, false);
			hasColors = false;

			return;
		}

		if (colorBuffer == null)
		{
			VertexArray.AttribFormat(
				ColorAttribute,
				4,
				VertexElementType.UnsignedByte,
				true
			);
			colorBuffer = CreateBuffer(colors, BufferBindFlags.Vertex);
			uint binding = VertexArray.BindVertexBuffer(
				colorBuffer,
				stride: 4 * sizeof(byte)
			);
			VertexArray.AttribBinding(ColorAttribute, binding);
		}
		else
		{
			Upload(colorBuffer, colors);
		}

		VertexArray.AttribEnable(ColorAttribute);
		hasColors = true;
	}

	public void SetUVs(Vector2[] uvs) => SetUVs((ReadOnlySpan<Vector2>)uvs);

	public void SetUVs(ReadOnlySpan<Vector2> uvs)
	{
		ThrowIfDisposed();

		if (uvs.Length == 0)
		{
			VertexArray.AttribEnable(UvAttribute, false);

			return;
		}

		if (uvBuffer == null)
		{
			VertexArray.AttribFormat(UvAttribute, 2);
			uvBuffer = CreateBuffer(uvs, BufferBindFlags.Vertex);
			uint binding = VertexArray.BindVertexBuffer(
				uvBuffer,
				stride: 2 * sizeof(float)
			);
			VertexArray.AttribBinding(UvAttribute, binding);
		}
		else
		{
			Upload(uvBuffer, uvs);
		}

		VertexArray.AttribEnable(UvAttribute);
	}

	public void SetElements(params uint[] elements)
	{
		ThrowIfDisposed();

		if (elements == null || elements.Length == 0)
		{
			VertexArray.BindElementBuffer(null);
			elementCount = 0;

			return;
		}

		if (elementBuffer == null)
		{
			elementBuffer = CreateBuffer<uint>(elements, BufferBindFlags.Index);
		}
		else
		{
			Upload<uint>(elementBuffer, elements);
		}

		VertexArray.BindElementBuffer(elementBuffer);
		elementCount = elements.Length;
	}

	public void SetVertices(params Vertex2[] vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        SetVertices((ReadOnlySpan<Vertex2>)vertices);
    }

    public void SetVertices(ReadOnlySpan<Vertex2> vertices)
    {

		if (stagedPositions.Length < vertices.Length)
        {
            int capacity = Math.Max(vertices.Length, stagedPositions.Length * 2);
            Array.Resize(ref stagedPositions, capacity); Array.Resize(ref stagedUvs, capacity); Array.Resize(ref stagedColors, capacity);
        }
        Span<Vector2> positions = stagedPositions.AsSpan(0, vertices.Length), uvs = stagedUvs.AsSpan(0, vertices.Length);
        Span<Color> colors = stagedColors.AsSpan(0, vertices.Length);

		for (int index = 0; index < vertices.Length; index++)
		{
			positions[index] = vertices[index].Position;
			uvs[index] = vertices[index].UV;
			colors[index] = vertices[index].Color;
		}

		SetVertices(positions);
		SetUVs(uvs);
		SetColors(colors);
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		vertexBuffer?.Dispose();
		colorBuffer?.Dispose();
		uvBuffer?.Dispose();
		elementBuffer?.Dispose();
		VertexArray.Dispose();
	}

	internal void Draw()
	{
		Draw(0, VertexArray.HasElementBuffer ? elementCount : vertexCount);
	}

	internal void Draw(int first, int count)
	{
		ThrowIfDisposed();
		Internal_OpenGL.GL.PolygonMode(
			TriangleFace.FrontAndBack,
			ToOpenGl(PolygonMode)
		);

		if (!hasColors)
		{
			VertexArray.VertexAttrib(ColorAttribute, DefaultColor);
		}

		if (VertexArray.HasElementBuffer)
		{
			VertexArray.DrawElements(
				first,
				count,
				IndexElementType.UnsignedInt
			);
		}
		else
		{
			VertexArray.Draw(first, count);
		}
	}

	private GraphicsBuffer CreateBuffer<T>(
		ReadOnlySpan<T> data,
		BufferBindFlags flags
	)
		where T : unmanaged
	{
		return owner.CreateBuffer<T>(data, flags, usage);
	}

	private static void Upload<T>(GraphicsBuffer buffer, ReadOnlySpan<T> data)
		where T : unmanaged
	{
		int size = checked(data.Length * Unsafe.SizeOf<T>());

		if (size == 0)
		{
			return;
		}

		if (buffer.SizeInBytes != size)
		{
			buffer.ResizeDiscard(size);
		}

		buffer.Write<T>(data);
	}

	private void ThrowIfDisposed()
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(Mesh2D));
		}
	}

	private static Silk.NET.OpenGL.PolygonMode ToOpenGl(PolygonMode mode)
	{
		return mode switch
		{
			PolygonMode.Point => Silk.NET.OpenGL.PolygonMode.Point,
			PolygonMode.Line => Silk.NET.OpenGL.PolygonMode.Line,
			PolygonMode.Fill => Silk.NET.OpenGL.PolygonMode.Fill,
			_ => throw new ArgumentOutOfRangeException(nameof(mode)),
		};
	}
}
