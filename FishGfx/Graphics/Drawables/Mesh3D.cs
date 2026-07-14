using System;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using FishGfx.Formats;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics.Drawables;

public sealed class Mesh3D : IDisposable
{
	private const uint VertexAttribute = 0;
	private const uint ColorAttribute = 1;
	private const uint UvAttribute = 2;

	private readonly BufferUsage usage;
	private readonly GraphicsContext owner;
	private GraphicsBuffer vertexBuffer;
	private GraphicsBuffer colorBuffer;
	private GraphicsBuffer uvBuffer;
	private GraphicsBuffer elementBuffer;
	private int vertexBinding = -1;
	private int colorBinding = -1;
	private int uvBinding = -1;
	private int vertexCount;
	private int elementCount;
	private bool hasColors;
	private bool disposed;

	internal Mesh3D(
		GraphicsContext owner,
		BufferUsage usage = BufferUsage.Static
	)
	{
		this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
		this.usage = usage;
		owner.EnsureCurrent();
		VertexArray = owner.CreateVertexArray();
	}

	internal Mesh3D(
		GraphicsContext owner,
		Vertex3[] vertices,
		bool hasUvs = true,
		bool hasColors = true
	)
		: this(owner)
	{
		ArgumentNullException.ThrowIfNull(vertices);
		SetVertices(vertices, vertices.Length, hasUvs, hasColors);
	}

	internal Mesh3D(
		GraphicsContext owner,
		Vertex2[] vertices,
		bool hasUvs = true,
		bool hasColors = true
	)
		: this(owner)
	{
		ArgumentNullException.ThrowIfNull(vertices);

		Vertex3[] vertices3D = vertices
			.Select(vertex => new Vertex3(
				new Vector3(vertex.Position, 0),
				vertex.UV,
				vertex.Color
			))
			.ToArray();

		SetVertices(vertices3D, vertices3D.Length, hasUvs, hasColors);
	}

	internal Mesh3D(
		GraphicsContext owner,
		GenericMesh mesh,
		bool hasUvs = true,
		bool hasColors = true
	)
		: this(
			owner,
			(mesh ?? throw new ArgumentNullException(nameof(mesh))).Vertices.ToArray(),
			hasUvs,
			hasColors
		)
	{
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

	public void SetVertices(Vector3[] vertices)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(vertices);
		SetAttribute(
			ref vertexBuffer,
			ref vertexBinding,
			vertices,
			VertexAttribute,
			3,
			VertexElementType.Float,
			false,
			0,
			sizeof(float) * 3
		);
		vertexCount = vertices.Length;
	}

	public void SetColors(Color[] colors)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(colors);
		hasColors = SetAttribute(
			ref colorBuffer,
			ref colorBinding,
			colors,
			ColorAttribute,
			4,
			VertexElementType.UnsignedByte,
			true,
			0,
			sizeof(byte) * 4
		);
	}

	public void SetUVs(Vector2[] uvs)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(uvs);
		SetAttribute(
			ref uvBuffer,
			ref uvBinding,
			uvs,
			UvAttribute,
			2,
			VertexElementType.Float,
			false,
			0,
			sizeof(float) * 2
		);
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
			elementBuffer = owner.CreateBuffer(
				elements,
				BufferBindFlags.Index,
				usage
			);
		}
		else
		{
			Upload(elementBuffer, elements);
		}

		VertexArray.BindElementBuffer(elementBuffer);
		elementCount = elements.Length;
	}

	public void SetVertices(params Vertex3[] vertices)
	{
		ArgumentNullException.ThrowIfNull(vertices);
		SetVertices(vertices, vertices.Length);
	}

	public void SetVertices(
		Vertex3[] vertices,
		int count,
		bool hasUvs = true,
		bool hasColors = true
	)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(vertices);

		if (count < 0 || count > vertices.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(count));
		}

		ReadOnlySpan<Vertex3> data = vertices.AsSpan(0, count);
		SetInterleavedAttribute(
			ref vertexBuffer,
			ref vertexBinding,
			data,
			VertexAttribute,
			3,
			VertexElementType.Float,
			false,
			0
		);

		if (hasUvs)
		{
			SetInterleavedAttribute(
				ref uvBuffer,
				ref uvBinding,
				data,
				UvAttribute,
				2,
				VertexElementType.Float,
				false,
				3 * sizeof(float)
			);
		}
		else
		{
			VertexArray.AttribEnable(UvAttribute, false);
		}

		hasColors = hasColors && SetInterleavedAttribute(
			ref colorBuffer,
			ref colorBinding,
			data,
			ColorAttribute,
			4,
			VertexElementType.UnsignedByte,
			true,
			5 * sizeof(float)
		);

		if (!hasColors)
		{
			VertexArray.AttribEnable(ColorAttribute, false);
		}

		vertexCount = count;
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
				count: elementCount,
				elementType: IndexElementType.UnsignedInt
			);
		}
		else
		{
			VertexArray.Draw(0, vertexCount);
		}
	}

	private bool SetAttribute<T>(
		ref GraphicsBuffer buffer,
		ref int binding,
		ReadOnlySpan<T> data,
		uint attribute,
		int components,
		VertexElementType type,
		bool normalized,
		uint offset,
		int stride
	)
		where T : unmanaged
	{
		if (data.Length == 0)
		{
			VertexArray.AttribEnable(attribute, false);

			return false;
		}

		if (buffer == null)
		{
			buffer = owner.CreateBuffer(
				data,
				BufferBindFlags.Vertex,
				usage
			);
		}
		else
		{
			Upload(buffer, data);
		}

		if (binding < 0)
		{
			binding = (int)VertexArray.BindVertexBuffer(buffer, stride: stride);
		}
		else
		{
			VertexArray.BindVertexBuffer(buffer, binding, stride: stride);
		}

		VertexArray.AttribFormat(
			attribute,
			components,
			type,
			normalized,
			offset
		);
		VertexArray.AttribBinding(attribute, (uint)binding);

		return true;
	}

	private bool SetInterleavedAttribute(
		ref GraphicsBuffer buffer,
		ref int binding,
		ReadOnlySpan<Vertex3> data,
		uint attribute,
		int components,
		VertexElementType type,
		bool normalized,
		uint offset
	)
	{
		return SetAttribute(
			ref buffer,
			ref binding,
			data,
			attribute,
			components,
			type,
			normalized,
			offset,
			Unsafe.SizeOf<Vertex3>()
		);
	}

	private static void Upload<T>(
		GraphicsBuffer buffer,
		ReadOnlySpan<T> data
	)
		where T : unmanaged
	{
		int size = checked(data.Length * Unsafe.SizeOf<T>());

		if (buffer.SizeInBytes != size)
		{
			buffer.ResizeDiscard(size);
		}

		buffer.Write(data);
	}

	private void ThrowIfDisposed()
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(Mesh3D));
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
