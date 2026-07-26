using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FishGfx.Im3d;

internal enum Im3dStatus
{
	Ok,
	InvalidArgument,
	InvalidState,
	BufferTooSmall,
	InternalError,
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeVector3
{
	internal float X;
	internal float Y;
	internal float Z;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeQuaternion
{
	internal float X;
	internal float Y;
	internal float Z;
	internal float W;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePose
{
	internal NativeVector3 Position;
	internal NativeQuaternion Rotation;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeFrameInput
{
	internal NativeVector3 CursorRayOrigin;
	internal NativeVector3 CursorRayDirection;
	internal NativeVector3 WorldUp;
	internal NativeVector3 ViewOrigin;
	internal NativeVector3 ViewDirection;
	internal float ViewportWidth;
	internal float ViewportHeight;
	internal float ProjectionScaleY;
	internal float DeltaTime;
	internal int SelectDown;
	internal int ProjectionOrthographic;
	internal int FlipGizmoWhenBehind;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeInteraction
{
	internal uint ActiveId;
	internal uint HotId;
	internal uint ActivatedId;
	internal int Changed;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct NativeVertex
{
	internal float X;
	internal float Y;
	internal float Z;
	internal float Size;
	internal byte R;
	internal byte G;
	internal byte B;
	internal byte A;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeDrawCommand
{
	internal uint Primitive;
	internal uint Layer;
	internal uint SourceOrder;
	internal uint FirstVertex;
	internal uint VertexCount;
}

internal sealed class Im3dSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
	private Im3dSafeHandle()
		: base(true)
	{
	}

	protected override bool ReleaseHandle()
	{
		return Im3dNative.ContextDestroy(handle) == Im3dStatus.Ok;
	}
}

internal static class Im3dNative
{
	private const string Library = "FishGfxIm3dNative";

	[DllImport(Library, EntryPoint = "fgim3d_get_abi_version", CallingConvention = CallingConvention.Cdecl)]
	internal static extern uint GetAbiVersion();

	[DllImport(Library, EntryPoint = "fgim3d_get_last_error", CallingConvention = CallingConvention.Cdecl)]
	private static extern IntPtr GetLastErrorPointer();

	[DllImport(Library, EntryPoint = "fgim3d_context_create", CallingConvention = CallingConvention.Cdecl)]
	internal static extern Im3dStatus ContextCreate(out Im3dSafeHandle context);

	[DllImport(Library, EntryPoint = "fgim3d_context_destroy", CallingConvention = CallingConvention.Cdecl)]
	internal static extern Im3dStatus ContextDestroy(IntPtr context);

	[DllImport(Library, EntryPoint = "fgim3d_context_reset_interaction", CallingConvention = CallingConvention.Cdecl)]
	internal static extern Im3dStatus ContextResetInteraction(Im3dSafeHandle context);

	[DllImport(Library, EntryPoint = "fgim3d_begin_frame", CallingConvention = CallingConvention.Cdecl)]
	internal static extern Im3dStatus BeginFrame(Im3dSafeHandle context, in NativeFrameInput input);

	[DllImport(Library, EntryPoint = "fgim3d_manipulate_pose", CallingConvention = CallingConvention.Cdecl)]
	internal static extern Im3dStatus ManipulatePose(
		Im3dSafeHandle context,
		uint id,
		Im3dPoseOperation operation,
		ref NativePose pose,
		out NativeInteraction interaction);

	[DllImport(Library, EntryPoint = "fgim3d_manipulate_axis_translation", CallingConvention = CallingConvention.Cdecl)]
	internal static extern Im3dStatus ManipulateAxisTranslation(
		Im3dSafeHandle context,
		uint id,
		NativeVector3 axisOrigin,
		NativeVector3 axisDirection,
		uint rgba,
		ref NativeVector3 position,
		out NativeInteraction interaction);

	[DllImport(Library, EntryPoint = "fgim3d_end_frame", CallingConvention = CallingConvention.Cdecl)]
	internal static extern Im3dStatus EndFrame(
		Im3dSafeHandle context,
		out NativeInteraction interaction,
		out uint vertexCount,
		out uint commandCount);

	[DllImport(Library, EntryPoint = "fgim3d_copy_draw_data", CallingConvention = CallingConvention.Cdecl)]
	internal static extern Im3dStatus CopyDrawData(
		Im3dSafeHandle context,
		[Out] NativeVertex[] vertices,
		uint vertexCapacity,
		[Out] NativeDrawCommand[] commands,
		uint commandCapacity);

	[DllImport(Library, EntryPoint = "fgim3d_test_pose_round_trip", CallingConvention = CallingConvention.Cdecl)]
	internal static extern Im3dStatus TestPoseRoundTrip(in NativePose input, out NativePose output);

	internal static void ThrowIfFailed(Im3dStatus status, string operation)
	{
		if (status == Im3dStatus.Ok)
		{
			return;
		}

		string? detail = Marshal.PtrToStringUTF8(GetLastErrorPointer());
		throw new InvalidOperationException($"{operation} failed ({status}): {detail ?? "No native diagnostic."}");
	}
}
