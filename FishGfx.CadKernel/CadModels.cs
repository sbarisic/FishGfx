namespace FishGfx.Cad;

public enum CadTopologyKind
{
	Unknown,
	Face,
	Edge,
	CylindricalFace,
	CircularEdge,
	ClosedProfile,
}

public readonly record struct CadTopologyRef(
	Guid PartId,
	ulong TopologyId,
	CadTopologyKind Kind
);

public sealed class CadPart
{
	public Guid Id { get; set; } = Guid.NewGuid();

	public string Name { get; set; } = "Part";

	public string SourcePath { get; set; }

	public CadTransform Transform { get; set; } = CadTransform.Identity;
}

public sealed class CadMate
{
	public Guid Id { get; set; } = Guid.NewGuid();

	public Guid PartId { get; set; }

	public string Name { get; set; } = "Mate";

	public CadTopologyRef? Topology { get; private set; }

	public CadFrame? LocalFrame { get; private set; }

	public double RadiusMillimetres { get; private set; }

	public bool IsResolved => Topology.HasValue && LocalFrame.HasValue;

	public void Rebind(CadTopologyRef topology, CadFrame localFrame, double radiusMillimetres)
	{
		if (topology.PartId != PartId)
		{
			throw new ArgumentException("A mate can only bind to topology on its owning part.", nameof(topology));
		}

		if (!double.IsFinite(radiusMillimetres) || radiusMillimetres <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(radiusMillimetres));
		}

		Topology = topology;
		LocalFrame = localFrame;
		RadiusMillimetres = radiusMillimetres;
	}

	public void Invalidate()
	{
		Topology = null;
		LocalFrame = null;
		RadiusMillimetres = 0;
	}

	public void Flip()
	{
		if (!LocalFrame.HasValue)
		{
			throw new InvalidOperationException("An unresolved mate cannot be flipped.");
		}

		LocalFrame = LocalFrame.Value.Flipped();
	}

}

public enum CadDiagnosticSeverity
{
	Information,
	Warning,
	Error,
}

public sealed record CadDiagnostic(
	string Code,
	string Message,
	CadDiagnosticSeverity Severity,
	Guid? NodeId = null
);

public readonly record struct CadMeshVertex(float X, float Y, float Z, float NormalX, float NormalY, float NormalZ);

public sealed record CadFaceRange(ulong TopologyId, Guid? SourceNodeId, int FirstIndex, int IndexCount);

public sealed record CadEdgePolyline(ulong TopologyId, CadTopologyKind Kind, CadPoint3[] Points);

public sealed class CadTessellation
{
	public CadMeshVertex[] Vertices { get; init; } = Array.Empty<CadMeshVertex>();

	public uint[] Indices { get; init; } = Array.Empty<uint>();

	public CadFaceRange[] Faces { get; init; } = Array.Empty<CadFaceRange>();

	public CadEdgePolyline[] Edges { get; init; } = Array.Empty<CadEdgePolyline>();

	public CadPoint3 Minimum { get; init; }

	public CadPoint3 Maximum { get; init; }
}
