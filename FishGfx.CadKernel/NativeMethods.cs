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
	internal NativeFrame(CadFrame value)
		: this(new NativePoint3(value.Origin), new NativePoint3(value.Tangent), new NativePoint3(value.Normal))
	{
	}

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

[StructLayout(LayoutKind.Sequential)]
internal struct NativeFaceRange
{
	internal ulong TopologyId;
	internal uint FirstSource;
	internal uint SourceCount;
	internal uint FirstIndex;
	internal uint IndexCount;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal unsafe struct NativeGeometrySourceRef
{
	internal CadGeometrySourceKind Kind;
	internal fixed byte OwnerId[40];
	internal fixed byte ElementId[40];

	internal CadGeometrySourceRef ToManaged()
	{
		fixed (byte* owner = OwnerId)
		fixed (byte* element = ElementId)
		{
			string ownerText = ReadText(owner);
			string elementText = ReadText(element);
			Guid.TryParse(ownerText, out Guid ownerId);
			return new CadGeometrySourceRef(Kind, ownerId, elementText);
		}
	}

	private static string ReadText(byte* pointer)
	{
		int length = 0;
		while (length < 40 && pointer[length] != 0)
			++length;
		return Marshal.PtrToStringUTF8((nint)pointer, length) ?? string.Empty;
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
internal unsafe struct NativeRunnerProfile
{
	internal int Kind;
	internal fixed byte MateId[40];
	internal double OuterDiameter;
	internal double WallThickness;
	internal double EquivalentRadius;

	internal static NativeRunnerProfile FromManaged(RunnerSectionProfile profile)
	{
		NativeRunnerProfile result = new()
		{
			Kind = (int)profile.Kind,
			OuterDiameter = profile.CircularProfile?.OuterDiameterMillimetres ?? 0,
			WallThickness = profile.WallThicknessMillimetres,
			EquivalentRadius = profile.MateEquivalentRadiusMillimetres,
		};
		if (profile.MateId.HasValue)
		{
			CopyId(result.MateId, profile.MateId.Value);
		}
		return result;
	}

	private static void CopyId(byte* destination, Guid id)
	{
		byte[] bytes = System.Text.Encoding.UTF8.GetBytes(id.ToString("D"));
		for (int index = 0; index < Math.Min(bytes.Length, 39); index++)
			destination[index] = bytes[index];
	}
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal unsafe struct NativeRunnerFeature
{
	internal int Kind;
	internal fixed byte SourceNodeId[40];
	internal NativeFrame EntryFrame;
	internal NativeFrame ExitFrame;
	internal NativeRunnerProfile InputProfile;
	internal NativeRunnerProfile OutputProfile;
	internal NativePoint3 Center;
	internal double Length;
	internal double Radius;
	internal double SweepRadians;
	internal double RotationRadians;
	internal NativePoint3 Control1;
	internal NativePoint3 Control2;

	internal static NativeRunnerFeature FromManaged(RunnerFeature feature)
	{
		NativeRunnerFeature result = new()
		{
			Kind = (int)feature.Kind,
			EntryFrame = new NativeFrame(feature.EntryFrame),
			ExitFrame = new NativeFrame(feature.ExitFrame),
			InputProfile = NativeRunnerProfile.FromManaged(feature.InputProfile),
			OutputProfile = NativeRunnerProfile.FromManaged(feature.OutputProfile),
			Center = new NativePoint3(feature.Center),
			Length = feature.LengthMillimetres,
			Radius = feature.RadiusMillimetres,
			SweepRadians = feature.SweepRadians,
			RotationRadians = feature.RotationRadians,
			Control1 = new NativePoint3(feature.Control1),
			Control2 = new NativePoint3(feature.Control2),
		};
		byte[] id = System.Text.Encoding.UTF8.GetBytes(feature.NodeId.ToString("D"));

		byte* destination = result.SourceNodeId;

		for (int index = 0; index < Math.Min(id.Length, 39); index++)
		{
			destination[index] = id[index];
		}

		return result;
	}
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal unsafe struct NativeRunnerFeatureSpec
{
	internal int Kind;
	internal fixed byte SourceNodeId[40];
	internal double Length;
	internal double Radius;
	internal double SweepRadians;
	internal double RotationRadians;
	internal double StartHandleLength;
	internal NativePoint3 Control2Local;
	internal NativePoint3 EndLocal;
	internal NativeRunnerProfile OutputProfile;
	internal int HasConstrainedEndFrame;
	internal NativeFrame ConstrainedEndFrame;
	internal double EndHandleLength;

	internal static NativeRunnerFeatureSpec FromManaged(RunnerFeatureSpec specification)
	{
		NativeRunnerFeatureSpec result = new()
		{
			Kind = (int)specification.Kind,
			Length = specification.LengthMillimetres,
			Radius = specification.RadiusMillimetres,
			SweepRadians = specification.SweepRadians,
			RotationRadians = specification.RotationRadians,
			StartHandleLength = specification.StartHandleLengthMillimetres,
			Control2Local = new NativePoint3(specification.Control2Local),
			EndLocal = new NativePoint3(specification.EndLocal),
			OutputProfile = NativeRunnerProfile.FromManaged(specification.OutputProfile),
			HasConstrainedEndFrame = specification.ConstrainedEndFrame.HasValue ? 1 : 0,
			ConstrainedEndFrame = specification.ConstrainedEndFrame.HasValue
				? new NativeFrame(specification.ConstrainedEndFrame.Value)
				: default,
			EndHandleLength = specification.EndHandleLengthMillimetres,
		};
		byte[] id = System.Text.Encoding.UTF8.GetBytes(specification.NodeId.ToString("D"));
		byte* destination = result.SourceNodeId;
		for (int index = 0; index < Math.Min(id.Length, 39); index++)
		{
			destination[index] = id[index];
		}
		return result;
	}
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal unsafe struct NativeCollectorInlet
{
	internal fixed byte InletId[40];
	internal fixed byte RunnerId[40];
	internal NativeFrame Frame;
	internal NativeFrame ProfileReferenceFrame;
	internal NativeRunnerProfile Profile;
	internal double MergeStation;
	internal double BranchStartHandleLength;

	internal static NativeCollectorInlet FromManaged(
		CadCollectorSystem system,
		CadCollectorInlet inlet,
		RunnerSectionProfile profile,
		CadFrame profileReferenceFrame
	)
	{
		NativeCollectorInlet result = new()
		{
			Frame = new NativeFrame(system.GetWorldInletFrame(inlet)),
			ProfileReferenceFrame = new NativeFrame(
				profile.Kind == RunnerProfileKind.MateProfile
					? profileReferenceFrame
					: system.GetWorldInletFrame(inlet)),
			Profile = NativeRunnerProfile.FromManaged(profile),
			MergeStation = inlet.MergeStation,
			BranchStartHandleLength = inlet.BranchStartHandleLength,
		};
		CopyText(result.InletId, inlet.Id.ToString("D"), 39);
		CopyText(result.RunnerId, inlet.Binding.RunnerId.ToString("D"), 39);
		return result;
	}

	private static void CopyText(byte* destination, string value, int capacity)
	{
		byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
		for (int index = 0; index < Math.Min(bytes.Length, capacity); ++index)
		{
			destination[index] = bytes[index];
		}
	}
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal unsafe struct NativeCollectorSystemSpec
{
	internal fixed byte SystemId[40];
	internal fixed byte Name[128];
	internal ulong GenerationRevision;
	internal NativeFrame OutletFrame;
	internal NativeRunnerProfile OutletProfile;
	internal double OutletStubLength;
	internal double MergeLength;
	internal double OverlapLength;
	internal double BranchEndHandleLength;

	internal static NativeCollectorSystemSpec FromManaged(CadCollectorSystem system)
	{
		NativeCollectorSystemSpec result = new()
		{
			GenerationRevision = checked((ulong)system.GenerationRevision),
			OutletFrame = new NativeFrame(system.OutletFrame),
			OutletProfile = NativeRunnerProfile.FromManaged(
				RunnerSectionProfile.FromCircular(system.OutletProfile)),
			OutletStubLength = system.OutletStubLength,
			MergeLength = system.MergeLength,
			OverlapLength = system.OverlapLength,
			BranchEndHandleLength = system.BranchEndHandleLength,
		};
		CopyText(result.SystemId, system.Id.ToString("D"), 39);
		CopyText(result.Name, system.Name, 127);
		return result;
	}

	private static void CopyText(byte* destination, string value, int capacity)
	{
		byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
		for (int index = 0; index < Math.Min(bytes.Length, capacity); ++index)
		{
			destination[index] = bytes[index];
		}
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

	[LibraryImport(Library, EntryPoint = "fgcad_evaluate_runner_features")]
	internal static unsafe partial NativeStatus EvaluateRunnerFeatures(
		in NativeFrame startFrame,
		in NativeRunnerProfile startProfile,
		NativeRunnerFeatureSpec* specifications,
		nuint specificationCount,
		NativeRunnerFeature* evaluatedFeatures,
		nuint evaluatedCapacity,
		out nuint evaluatedCount
	);

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

	[LibraryImport(Library, EntryPoint = "fgcad_document_build_runner", StringMarshalling = StringMarshalling.Utf8)]
	internal static unsafe partial NativeStatus DocumentBuildRunner(
		CadDocumentSafeHandle document,
		string runnerId,
		string runnerName,
		NativeRunnerFeature* features,
		nuint featureCount
	);

	[LibraryImport(Library, EntryPoint = "fgcad_document_remove_runner", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial NativeStatus DocumentRemoveRunner(CadDocumentSafeHandle document, string runnerId);

	[LibraryImport(Library, EntryPoint = "fgcad_document_rename_runner", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial NativeStatus DocumentRenameRunner(
		CadDocumentSafeHandle document,
		string runnerId,
		string runnerName
	);

	[LibraryImport(Library, EntryPoint = "fgcad_document_build_collector_system")]
	internal static unsafe partial NativeStatus DocumentBuildCollectorSystem(
		CadDocumentSafeHandle document,
		in NativeCollectorSystemSpec system,
		NativeCollectorInlet* inlets,
		nuint inletCount
	);

	[LibraryImport(
		Library,
		EntryPoint = "fgcad_document_begin_collector_system_build",
		StringMarshalling = StringMarshalling.Utf8
	)]
	internal static partial NativeStatus DocumentBeginCollectorSystemBuild(
		CadDocumentSafeHandle document,
		string systemId,
		ulong generationRevision
	);

	[LibraryImport(
		Library,
		EntryPoint = "fgcad_document_abort_collector_system_build",
		StringMarshalling = StringMarshalling.Utf8
	)]
	internal static partial NativeStatus DocumentAbortCollectorSystemBuild(
		CadDocumentSafeHandle document,
		string systemId,
		ulong generationRevision
	);

	[LibraryImport(Library, EntryPoint = "fgcad_document_remove_collector_system", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial NativeStatus DocumentRemoveCollectorSystem(
		CadDocumentSafeHandle document,
		string systemId
	);

	[LibraryImport(Library, EntryPoint = "fgcad_document_rename_collector_system", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial NativeStatus DocumentRenameCollectorSystem(
		CadDocumentSafeHandle document,
		string systemId,
		string name
	);

	[LibraryImport(Library, EntryPoint = "fgcad_document_tessellate_part", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial NativeStatus DocumentTessellatePart(
		CadDocumentSafeHandle document,
		string partId,
		double linearDeflection,
		double angularDeflection,
		out nint tessellation
	);

	[LibraryImport(Library, EntryPoint = "fgcad_document_tessellate_runner", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial NativeStatus DocumentTessellateRunner(
		CadDocumentSafeHandle document,
		string runnerId,
		double linearDeflection,
		double angularDeflection,
		out nint tessellation
	);

	[LibraryImport(Library, EntryPoint = "fgcad_document_tessellate_collector_system", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial NativeStatus DocumentTessellateCollectorSystem(
		CadDocumentSafeHandle document,
		string systemId,
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

	[LibraryImport(Library, EntryPoint = "fgcad_tessellation_source_count")]
	internal static partial nuint TessellationSourceCount(CadTessellationSafeHandle tessellation);

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
		NativeGeometrySourceRef* sources,
		nuint sourceCapacity,
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
		string buildConfiguration =
#if DEBUG
			"Debug";
#else
			"Release";
#endif
		string fallbackConfiguration = buildConfiguration == "Debug" ? "Release" : "Debug";
		string[] candidates =
		{
			configured == null ? null : Path.Combine(configured, fileName),
			Path.Combine(AppContext.BaseDirectory, fileName),
			Path.GetFullPath(Path.Combine(
				AppContext.BaseDirectory,
				"..", "..", "..", "..", "FishGfx.CadKernel.Native", "out", "build", "windows-x64", buildConfiguration, fileName
			)),
			Path.GetFullPath(Path.Combine(
				AppContext.BaseDirectory,
				"..", "..", "..", "..", "FishGfx.CadKernel.Native", "out", "build", "windows-x64", fallbackConfiguration, fileName
			)),
			Path.GetFullPath(Path.Combine(
				AppContext.BaseDirectory,
				"..", "..", "..", "..", "..", "FishGfx.CadKernel.Native", "out", "build", "windows-x64", buildConfiguration, fileName
			)),
			Path.GetFullPath(Path.Combine(
				AppContext.BaseDirectory,
				"..", "..", "..", "..", "..", "FishGfx.CadKernel.Native", "out", "build", "windows-x64", fallbackConfiguration, fileName
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
