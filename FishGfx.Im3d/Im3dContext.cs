using System.Numerics;

namespace FishGfx.Im3d;

public sealed class Im3dContext : IDisposable
{
	private const uint SupportedAbiVersion = 1;

	private readonly Im3dSafeHandle handle;
	private readonly int ownerThreadId;
	private bool frameOpen;
	private bool resetQueued;
	private bool disposed;

	public Im3dContext()
	{
		uint nativeVersion = Im3dNative.GetAbiVersion();
		if (nativeVersion != SupportedAbiVersion)
		{
			throw new InvalidOperationException(
				$"FishGfx im3d ABI mismatch. Managed={SupportedAbiVersion}, native={nativeVersion}.");
		}

		Im3dNative.ThrowIfFailed(Im3dNative.ContextCreate(out handle), "Create im3d context");
		ownerThreadId = Environment.CurrentManagedThreadId;
	}

	public void BeginFrame(Im3dFrameInput input)
	{
		EnsureOwner();
		if (frameOpen)
		{
			throw new InvalidOperationException("An im3d frame is already open.");
		}

		if (resetQueued)
		{
			Im3dNative.ThrowIfFailed(Im3dNative.ContextResetInteraction(handle), "Reset im3d interaction");
			resetQueued = false;
		}

		NativeFrameInput native = new()
		{
			CursorRayOrigin = ToNative(input.CursorRayOrigin),
			CursorRayDirection = ToNative(Vector3.Normalize(input.CursorRayDirection)),
			WorldUp = ToNative(Vector3.Normalize(input.WorldUp)),
			ViewOrigin = ToNative(input.ViewOrigin),
			ViewDirection = ToNative(Vector3.Normalize(input.ViewDirection)),
			ViewportWidth = input.ViewportSize.X,
			ViewportHeight = input.ViewportSize.Y,
			ProjectionScaleY = input.ProjectionScaleY,
			DeltaTime = input.DeltaTime,
			SelectDown = input.SelectDown ? 1 : 0,
			ProjectionOrthographic = input.ProjectionOrthographic ? 1 : 0,
			FlipGizmoWhenBehind = input.FlipGizmoWhenBehind ? 1 : 0,
		};

		Im3dNative.ThrowIfFailed(Im3dNative.BeginFrame(handle, native), "Begin im3d frame");
		frameOpen = true;
	}

	public (Im3dPose Pose, Im3dInteraction Interaction) ManipulatePose(
		uint id,
		Im3dPoseOperation operation,
		Im3dPose pose)
	{
		EnsureOpenFrame();
		Im3dPose normalized = pose.Normalized();
		NativePose native = new()
		{
			Position = ToNative(normalized.Position),
			Rotation = ToNative(normalized.Rotation),
		};

		Im3dNative.ThrowIfFailed(
			Im3dNative.ManipulatePose(handle, id, operation, ref native, out NativeInteraction interaction),
			"Manipulate im3d pose");

		return (FromNative(native), FromNative(interaction));
	}

	public (Vector3 Position, Im3dInteraction Interaction) ManipulateAxisTranslation(
		uint id,
		Vector3 axisOrigin,
		Vector3 axisDirection,
		Vector3 position,
		Color color)
	{
		EnsureOpenFrame();
		NativeVector3 nativePosition = ToNative(position);
		uint rgba = ((uint)color.R << 24) | ((uint)color.G << 16) | ((uint)color.B << 8) | color.A;
		Im3dNative.ThrowIfFailed(
			Im3dNative.ManipulateAxisTranslation(
				handle,
				id,
				ToNative(axisOrigin),
				ToNative(Vector3.Normalize(axisDirection)),
				rgba,
				ref nativePosition,
				out NativeInteraction interaction),
			"Manipulate constrained im3d axis");

		return (FromNative(nativePosition), FromNative(interaction));
	}

	public (Im3dFrameData DrawData, Im3dInteraction Interaction) EndFrame()
	{
		EnsureOpenFrame();
		try
		{
			Im3dNative.ThrowIfFailed(
				Im3dNative.EndFrame(handle, out NativeInteraction interaction, out uint vertexCount, out uint commandCount),
				"End im3d frame");
			NativeVertex[] nativeVertices = new NativeVertex[checked((int)vertexCount)];
			NativeDrawCommand[] nativeCommands = new NativeDrawCommand[checked((int)commandCount)];
			Im3dNative.ThrowIfFailed(
				Im3dNative.CopyDrawData(handle, nativeVertices, vertexCount, nativeCommands, commandCount),
				"Copy im3d draw data");

			Im3dVertex[] vertices = new Im3dVertex[nativeVertices.Length];
			for (int index = 0; index < vertices.Length; index++)
			{
				NativeVertex source = nativeVertices[index];
				vertices[index] = new Im3dVertex(
					new Vector3(source.X, source.Y, source.Z),
					source.Size,
					new Color(source.R, source.G, source.B, source.A));
			}

			Im3dDrawCommand[] commands = new Im3dDrawCommand[nativeCommands.Length];
			for (int index = 0; index < commands.Length; index++)
			{
				NativeDrawCommand source = nativeCommands[index];
				commands[index] = new Im3dDrawCommand(
					(Im3dPrimitive)source.Primitive,
					source.Layer,
					source.SourceOrder,
					checked((int)source.FirstVertex),
					checked((int)source.VertexCount));
			}

			return (new Im3dFrameData(vertices, commands), FromNative(interaction));
		}
		finally
		{
			frameOpen = false;
		}
	}

	public void QueueInteractionReset()
	{
		EnsureOwner();
		resetQueued = true;
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		EnsureOwner();
		if (frameOpen)
		{
			throw new InvalidOperationException("The im3d context cannot be disposed during a frame.");
		}

		disposed = true;
		handle.Dispose();
	}

	internal static Im3dPose RoundTripForTest(Im3dPose pose)
	{
		Im3dPose normalized = pose.Normalized();
		NativePose input = new()
		{
			Position = ToNative(normalized.Position),
			Rotation = ToNative(normalized.Rotation),
		};
		Im3dNative.ThrowIfFailed(Im3dNative.TestPoseRoundTrip(input, out NativePose output), "Round-trip im3d pose");
		return FromNative(output);
	}

	private void EnsureOpenFrame()
	{
		EnsureOwner();
		if (!frameOpen)
		{
			throw new InvalidOperationException("No im3d frame is open.");
		}
	}

	private void EnsureOwner()
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		if (Environment.CurrentManagedThreadId != ownerThreadId)
		{
			throw new InvalidOperationException("The im3d context is render-thread owned.");
		}
	}

	private static NativeVector3 ToNative(Vector3 value) => new() { X = value.X, Y = value.Y, Z = value.Z };

	private static NativeQuaternion ToNative(Quaternion value) =>
		new() { X = value.X, Y = value.Y, Z = value.Z, W = value.W };

	private static Vector3 FromNative(NativeVector3 value) => new(value.X, value.Y, value.Z);

	private static Im3dPose FromNative(NativePose value) =>
		new(FromNative(value.Position), new Quaternion(value.Rotation.X, value.Rotation.Y, value.Rotation.Z, value.Rotation.W));

	private static Im3dInteraction FromNative(NativeInteraction value) =>
		new(value.ActiveId, value.HotId, value.ActivatedId, value.Changed != 0);
}
