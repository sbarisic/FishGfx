using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FishGfx.Cad;

internal enum NativeStatus
{
	Ok,
	InvalidArgument,
	NotFound,
	UnsupportedTopology,
	ImportFailed,
	ModelingFailed,
	IoFailed,
	InternalError,
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativePoint3(double X, double Y, double Z)
{
	internal NativePoint3(CadPoint3 value)
		: this(value.X, value.Y, value.Z)
	{
	}

	internal CadPoint3 ToManaged()
	{
		return new CadPoint3(X, Y, Z);
	}
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeQuaternion(double X, double Y, double Z, double W)
{
	internal NativeQuaternion(CadQuaternion value)
		: this(value.X, value.Y, value.Z, value.W)
	{
	}
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeTransform(NativePoint3 Translation, NativeQuaternion Rotation)
{
	internal NativeTransform(CadTransform value)
		: this(new NativePoint3(value.Translation), new NativeQuaternion(value.Rotation))
	{
	}
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeFrame(NativePoint3 Origin, NativePoint3 Tangent, NativePoint3 Normal)
{
	internal CadFrame ToManaged()
	{
		return new CadFrame(Origin.ToManaged(), Tangent.ToManaged(), Normal.ToManaged());
	}
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeTopologyInfo(
	ulong Id,
	CadTopologyKind Kind,
	NativePoint3 Center,
	NativePoint3 Axis,
	double Radius
);

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeMeshVertex(
	float X,
	float Y,
	float Z,
	float NormalX,
	float NormalY,
	float NormalZ
);

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal unsafe struct NativeFaceRange
{
	internal ulong TopologyId;
	internal fixed byte SourceNodeId[40];
	internal uint FirstIndex;
	internal uint IndexCount;

	internal Guid? GetSourceNodeId()
	{
		fixed (byte* pointer = SourceNodeId)
		{
			int length = 0;

			while (length < 40 && pointer[length] != 0)
			{
				length++;
			}

			string value = Marshal.PtrToStringUTF8((nint)pointer, length);

			return Guid.TryParse(value, out Guid id) ? id : null;
		}
	}
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeEdgeRange(
	ulong TopologyId,
	CadTopologyKind Kind,
	uint FirstPoint,
	uint PointCount
);

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal unsafe struct NativeRunnerSegment
{
	internal int Kind;
	internal fixed byte SourceNodeId[40];
	internal NativePoint3 Start;
	internal NativePoint3 End;
	internal NativePoint3 StartTangent;
	internal NativePoint3 EndTangent;
	internal NativePoint3 Center;
	internal double Radius;
	internal double SweepRadians;

	internal static NativeRunnerSegment FromManaged(RunnerSegment segment)
	{
		NativeRunnerSegment result = new()
		{
			Kind = (int)segment.Kind,
			Start = new NativePoint3(segment.Start),
			End = new NativePoint3(segment.End),
			StartTangent = new NativePoint3(segment.StartTangent),
			EndTangent = new NativePoint3(segment.EndTangent),
			Center = new NativePoint3(segment.Center),
			Radius = segment.RadiusMillimetres,
			SweepRadians = segment.SweepRadians,
		};
		byte[] id = System.Text.Encoding.UTF8.GetBytes(segment.NodeId.ToString("D"));

		byte* destination = result.SourceNodeId;

		for (int index = 0; index < Math.Min(id.Length, 39); index++)
		{
			destination[index] = id[index];
		}

		return result;
	}
}

internal sealed class CadDocumentSafeHandle : SafeHandle
{
	private CadDocumentSafeHandle()
		: base(nint.Zero, true)
	{
	}

	internal CadDocumentSafeHandle(nint value)
		: base(nint.Zero, true)
	{
		SetHandle(value);
	}

	public override bool IsInvalid => handle == nint.Zero;

	protected override bool ReleaseHandle()
	{
		NativeMethods.DocumentDestroy(handle);
		return true;
	}
}

internal sealed class CadTessellationSafeHandle : SafeHandle
{
	private CadTessellationSafeHandle()
		: base(nint.Zero, true)
	{
	}

	internal CadTessellationSafeHandle(nint value)
		: base(nint.Zero, true)
	{
		SetHandle(value);
	}

	public override bool IsInvalid => handle == nint.Zero;

	protected override bool ReleaseHandle()
	{
		NativeMethods.TessellationDestroy(handle);
		return true;
	}
}

internal static partial class NativeMethods
{
	internal const string Library = "FishGfx.CadKernel.Native";

	static NativeMethods()
	{
		NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, ResolveLibrary);
	}

	[LibraryImport(Library, EntryPoint = "fgcad_api_version")]
	internal static partial uint ApiVersion();

	[LibraryImport(Library, EntryPoint = "fgcad_last_error")]
	private static partial nint LastErrorPointer();

	[LibraryImport(Library, EntryPoint = "fgcad_document_create")]
	internal static partial NativeStatus DocumentCreate(out nint document);

	[LibraryImport(Library, EntryPoint = "fgcad_document_destroy")]
	internal static partial void DocumentDestroy(nint document);

	[LibraryImport(Library, EntryPoint = "fgcad_document_import_step", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial NativeStatus DocumentImportStep(
		CadDocumentSafeHandle document,
		string partId,
		string path,
		string name
	);

	[LibraryImport(Library, EntryPoint = "fgcad_document_replace_step", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial NativeStatus DocumentReplaceStep(
		CadDocumentSafeHandle document,
		string partId,
		string path,
		string name
	);

	[LibraryImport(Library, EntryPoint = "fgcad_document_set_part_transform", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial NativeStatus DocumentSetPartTransform(
		CadDocumentSafeHandle document,
		string partId,
		in NativeTransform transform
	);

	[LibraryImport(Library, EntryPoint = "fgcad_document_get_topology_count", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial NativeStatus DocumentGetTopologyCount(
		CadDocumentSafeHandle document,
		string partId,
		out nuint count
	);

	[LibraryImport(Library, EntryPoint = "fgcad_document_copy_topology", StringMarshalling = StringMarshalling.Utf8)]
	internal static unsafe partial NativeStatus DocumentCopyTopology(
		CadDocumentSafeHandle document,
		string partId,
		NativeTopologyInfo* items,
		nuint capacity
	);

	[LibraryImport(Library, EntryPoint = "fgcad_document_get_mate_frame", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial NativeStatus DocumentGetMateFrame(
		CadDocumentSafeHandle document,
		string partId,
		ulong topologyId,
		in NativePoint3 localHit,
		out NativeFrame frame,
		out double radius
	);

	[LibraryImport(Library, EntryPoint = "fgcad_document_bind_topology_selector", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial NativeStatus DocumentBindTopologySelector(
		CadDocumentSafeHandle document,
		string selectorId,
		string partId,
		ulong topologyId
	);

	[LibraryImport(Library, EntryPoint = "fgcad_document_build_runner")]
	internal static unsafe partial NativeStatus DocumentBuildRunner(
		CadDocumentSafeHandle document,
		NativeRunnerSegment* segments,
		nuint segmentCount,
		double outerDiameter,
		double wallThickness
	);

	[LibraryImport(Library, EntryPoint = "fgcad_document_tessellate_part", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial NativeStatus DocumentTessellatePart(
		CadDocumentSafeHandle document,
		string partId,
		double linearDeflection,
		double angularDeflection,
		out nint tessellation
	);

	[LibraryImport(Library, EntryPoint = "fgcad_document_tessellate_runner")]
	internal static partial NativeStatus DocumentTessellateRunner(
		CadDocumentSafeHandle document,
		double linearDeflection,
		double angularDeflection,
		out nint tessellation
	);

	[LibraryImport(Library, EntryPoint = "fgcad_tessellation_destroy")]
	internal static partial void TessellationDestroy(nint tessellation);

	[LibraryImport(Library, EntryPoint = "fgcad_tessellation_vertex_count")]
	internal static partial nuint TessellationVertexCount(CadTessellationSafeHandle tessellation);

	[LibraryImport(Library, EntryPoint = "fgcad_tessellation_index_count")]
	internal static partial nuint TessellationIndexCount(CadTessellationSafeHandle tessellation);

	[LibraryImport(Library, EntryPoint = "fgcad_tessellation_face_count")]
	internal static partial nuint TessellationFaceCount(CadTessellationSafeHandle tessellation);

	[LibraryImport(Library, EntryPoint = "fgcad_tessellation_edge_count")]
	internal static partial nuint TessellationEdgeCount(CadTessellationSafeHandle tessellation);

	[LibraryImport(Library, EntryPoint = "fgcad_tessellation_edge_point_count")]
	internal static partial nuint TessellationEdgePointCount(CadTessellationSafeHandle tessellation);

	[LibraryImport(Library, EntryPoint = "fgcad_tessellation_copy")]
	internal static unsafe partial NativeStatus TessellationCopy(
		CadTessellationSafeHandle tessellation,
		NativeMeshVertex* vertices,
		nuint vertexCapacity,
		uint* indices,
		nuint indexCapacity,
		NativeFaceRange* faces,
		nuint faceCapacity,
		NativeEdgeRange* edges,
		nuint edgeCapacity,
		NativePoint3* edgePoints,
		nuint edgePointCapacity,
		out NativePoint3 minimum,
		out NativePoint3 maximum
	);

	[LibraryImport(Library, EntryPoint = "fgcad_document_save_xcaf", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial NativeStatus DocumentSaveXcaf(CadDocumentSafeHandle document, string path);

	[LibraryImport(Library, EntryPoint = "fgcad_document_load_xcaf", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial NativeStatus DocumentLoadXcaf(CadDocumentSafeHandle document, string path);

	[LibraryImport(Library, EntryPoint = "fgcad_document_export_step_ap242", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial NativeStatus DocumentExportStep(CadDocumentSafeHandle document, string path);

	internal static string LastError()
	{
		return Marshal.PtrToStringUTF8(LastErrorPointer()) ?? "Unknown native CAD error.";
	}

	private static nint ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
	{
		if (!string.Equals(libraryName, Library, StringComparison.Ordinal))
		{
			return nint.Zero;
		}

		string fileName = Library + ".dll";
		string configured = Environment.GetEnvironmentVariable("FISHGFX_CAD_NATIVE_DIR");
		string[] candidates =
		{
			configured == null ? null : Path.Combine(configured, fileName),
			Path.Combine(AppContext.BaseDirectory, fileName),
			Path.GetFullPath(Path.Combine(
				AppContext.BaseDirectory,
				"..", "..", "..", "..", "FishGfx.CadKernel.Native", "out", "build", "windows-x64", "Release", fileName
			)),
			Path.GetFullPath(Path.Combine(
				AppContext.BaseDirectory,
				"..", "..", "..", "..", "FishGfx.CadKernel.Native", "out", "build", "windows-x64", "Debug", fileName
			)),
			Path.GetFullPath(Path.Combine(
				AppContext.BaseDirectory,
				"..", "..", "..", "..", "..", "FishGfx.CadKernel.Native", "out", "build", "windows-x64", "Release", fileName
			)),
			Path.GetFullPath(Path.Combine(
				AppContext.BaseDirectory,
				"..", "..", "..", "..", "..", "FishGfx.CadKernel.Native", "out", "build", "windows-x64", "Debug", fileName
			)),
		};

		foreach (string candidate in candidates.Where(candidate => candidate != null))
		{
			if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out nint handle))
			{
				return handle;
			}
		}

		return nint.Zero;
	}
}
