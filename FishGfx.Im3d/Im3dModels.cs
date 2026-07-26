using System.Numerics;

namespace FishGfx.Im3d;

public enum Im3dPoseOperation
{
	Translate,
	Rotate,
}

public enum Im3dPrimitive
{
	Triangles,
	Lines,
	Points,
}

public readonly record struct Im3dPose(Vector3 Position, Quaternion Rotation)
{
	public Im3dPose Normalized()
	{
		if (!IsFinite(Position) || !IsFinite(Rotation) || Rotation.LengthSquared() < 1e-12f)
		{
			throw new ArgumentException("An im3d pose must contain a finite, nonzero quaternion.");
		}

		return this with { Rotation = Quaternion.Normalize(Rotation) };
	}

	private static bool IsFinite(Vector3 value) =>
		float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

	private static bool IsFinite(Quaternion value) =>
		float.IsFinite(value.X) && float.IsFinite(value.Y) &&
		float.IsFinite(value.Z) && float.IsFinite(value.W);
}

public readonly record struct Im3dFrameInput(
	Vector3 CursorRayOrigin,
	Vector3 CursorRayDirection,
	Vector3 WorldUp,
	Vector3 ViewOrigin,
	Vector3 ViewDirection,
	Vector2 ViewportSize,
	float ProjectionScaleY,
	float DeltaTime,
	bool SelectDown,
	bool ProjectionOrthographic,
	bool FlipGizmoWhenBehind = true);

public readonly record struct Im3dInteraction(
	uint ActiveId,
	uint HotId,
	uint ActivatedId,
	bool Changed)
{
	public const uint InvalidId = 0;

	public bool OwnsPointer => ActiveId != InvalidId || HotId != InvalidId;
}

public readonly record struct Im3dVertex(Vector3 Position, float Size, Color Color);

public readonly record struct Im3dDrawCommand(
	Im3dPrimitive Primitive,
	uint Layer,
	uint SourceOrder,
	int FirstVertex,
	int VertexCount);

public sealed class Im3dFrameData
{
	public static Im3dFrameData Empty { get; } = new([], []);

	public Im3dFrameData(Im3dVertex[] vertices, Im3dDrawCommand[] commands)
	{
		Vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
		Commands = commands ?? throw new ArgumentNullException(nameof(commands));
	}

	public Im3dVertex[] Vertices { get; }

	public Im3dDrawCommand[] Commands { get; }
}

public static class Im3dId
{
	public static uint FromGuid(Guid value, uint salt = 0)
	{
		Span<byte> bytes = stackalloc byte[16];
		value.TryWriteBytes(bytes);
		uint hash = 2166136261u ^ salt;
		foreach (byte valueByte in bytes)
		{
			hash = (hash ^ valueByte) * 16777619u;
		}

		return hash == 0 ? 1u : hash;
	}
}
