using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FishGfx.Cad;

public enum CadExactBuildStatus
{
	Missing,
	Stale,
	Requested,
	Building,
	Current,
	Failed,
	Cancelled,
}

public readonly record struct CadExactBuildSnapshot(
	CadExactBuildStatus Status,
	long RequestedRevision,
	long BuildingRevision,
	long PublishedRevision,
	string ExpectedDependencyHash,
	string PublishedDependencyHash,
	string Diagnostic
)
{
	public bool IsCurrent => Status == CadExactBuildStatus.Current
		&& RequestedRevision == PublishedRevision
		&& !string.IsNullOrWhiteSpace(ExpectedDependencyHash)
		&& string.Equals(
			ExpectedDependencyHash,
			PublishedDependencyHash,
			StringComparison.OrdinalIgnoreCase
		);
}

public sealed class CadExactBuildState
{
	private readonly object sync = new();
	private CadExactBuildSnapshot value = new(
		CadExactBuildStatus.Missing,
		0,
		0,
		0,
		null,
		null,
		null
	);

	public CadExactBuildSnapshot Snapshot
	{
		get
		{
			lock (sync)
			{
				return value;
			}
		}
	}

	public bool IsCurrent => Snapshot.IsCurrent;

	internal void MarkStale(long revision, string expectedHash = null)
	{
		lock (sync)
		{
			value = value with
			{
				Status = string.IsNullOrWhiteSpace(value.PublishedDependencyHash)
					? CadExactBuildStatus.Missing
					: CadExactBuildStatus.Stale,
				RequestedRevision = revision,
				BuildingRevision = 0,
				ExpectedDependencyHash = expectedHash,
				Diagnostic = null,
			};
		}
	}

	internal void Request(long revision, string expectedHash)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(expectedHash);
		lock (sync)
		{
			value = value with
			{
				Status = CadExactBuildStatus.Requested,
				RequestedRevision = revision,
				BuildingRevision = 0,
				ExpectedDependencyHash = expectedHash,
				Diagnostic = null,
			};
		}
	}

	internal bool TryBegin(long revision, string expectedHash)
	{
		lock (sync)
		{
			if (value.Status != CadExactBuildStatus.Requested
				|| value.RequestedRevision != revision
				|| !string.Equals(
					value.ExpectedDependencyHash,
					expectedHash,
					StringComparison.OrdinalIgnoreCase
				))
			{
				return false;
			}
			value = value with
			{
				Status = CadExactBuildStatus.Building,
				BuildingRevision = revision,
				Diagnostic = null,
			};
			return true;
		}
	}

	internal bool TryPublish(long revision, string dependencyHash)
	{
		return TryPublish(revision, dependencyHash, null);
	}

	internal bool TryPublish(
		long revision,
		string dependencyHash,
		Action publish
	)
	{
		lock (sync)
		{
			if (value.IsCurrent
				&& value.PublishedRevision == revision
				&& string.Equals(
					value.PublishedDependencyHash,
					dependencyHash,
					StringComparison.OrdinalIgnoreCase
				))
			{
				return true;
			}
			if (value.Status != CadExactBuildStatus.Building
				|| value.RequestedRevision != revision
				|| value.BuildingRevision != revision
				|| !string.Equals(
					value.ExpectedDependencyHash,
					dependencyHash,
					StringComparison.OrdinalIgnoreCase
				))
			{
				return false;
			}
			publish?.Invoke();
			value = value with
			{
				Status = CadExactBuildStatus.Current,
				PublishedRevision = revision,
				BuildingRevision = 0,
				PublishedDependencyHash = dependencyHash,
				Diagnostic = null,
			};
			return true;
		}
	}

	internal void Fail(long revision, string diagnostic)
	{
		lock (sync)
		{
			if (value.Status != CadExactBuildStatus.Building
				|| value.RequestedRevision != revision
				|| value.BuildingRevision != revision)
			{
				return;
			}
			value = value with
			{
				Status = CadExactBuildStatus.Failed,
				BuildingRevision = 0,
				Diagnostic = diagnostic,
			};
		}
	}

	internal void Cancel(long revision)
	{
		lock (sync)
		{
			if (value.Status != CadExactBuildStatus.Building
				|| value.RequestedRevision != revision
				|| value.BuildingRevision != revision)
			{
				return;
			}
			value = value with
			{
				Status = CadExactBuildStatus.Cancelled,
				BuildingRevision = 0,
				Diagnostic = "The exact build was superseded or cancelled.",
			};
		}
	}

	internal void Restore(CadExactBuildSnapshot snapshot)
	{
		lock (sync)
		{
			value = snapshot;
		}
	}

	internal CadExactBuildState DeepClone()
	{
		CadExactBuildState clone = new();
		clone.Restore(Snapshot);
		return clone;
	}
}

public static class CadBuildCompatibility
{
	public const uint NativeAbiVersion = 8;
	public const string BuilderVersion = "runner-sew-2.collector-sew-3.collector-branch-solver-1.transactional-publish-1";
	public const string OcctVersion = "8.0.0";
	public const int SewingPolicyVersion = 3;
}

public static class CadGeometryDependencyHash
{
	public static string Runner(ManifoldProject project, CadRunner runner)
	{
		ArgumentNullException.ThrowIfNull(project);
		ArgumentNullException.ThrowIfNull(runner);
		StringBuilder value = Begin("runner");
		Append(value, runner.Id);
		Append(value, runner.StartMateId);
		AppendGeometryGraph(value, runner.Graph);
		CadMate mate = project.Mates.FirstOrDefault(item => item.Id == runner.StartMateId);
		if (mate != null)
		{
			Append(value, mate.Id);
			Append(value, mate.PartId);
			Append(value, mate.Topology?.TopologyId ?? 0);
			Append(value, mate.Topology?.Kind.ToString());
			Append(value, mate.LocalFrame);
			Append(value, mate.RadiusMillimetres);
			CadPart part = project.Parts.FirstOrDefault(item => item.Id == mate.PartId);
			if (part != null)
			{
				Append(value, part.Transform);
			}
		}
		RunnerEndpointConstraint? endpoint = project.GetEndpointConstraint(runner);
		if (endpoint.HasValue)
		{
			RunnerEndpointConstraint constraint = endpoint.Value;
			Append(value, "collector-endpoint");
			Append(value, constraint.CollectorSystemId);
			Append(value, constraint.InletId);
			Append(value, constraint.TerminalBezierNodeId);
			Append(value, constraint.BezierEndFrame);
			Append(value, constraint.TerminalFrame);
			Append(value, constraint.EndHandleLength);
			Append(value, constraint.ClockingTransitionNodeId);
			Append(value, constraint.ClockingTransitionLength);
		}
		return Finish(value);
	}

	public static string Collector(ManifoldProject project, CadCollectorSystem system)
	{
		ArgumentNullException.ThrowIfNull(project);
		ArgumentNullException.ThrowIfNull(system);
		StringBuilder value = Begin("collector");
		Append(value, system.Id);
		Append(value, system.OutletFrame);
		Append(value, system.BranchEndHandleLength);
		foreach (CadCollectorInlet inlet in system.Inlets.OrderBy(item => item.Id))
		{
			Append(value, inlet.Id);
			Append(value, inlet.Binding?.RunnerId);
			Append(value, inlet.Binding?.TerminalBezierNodeId);
			Append(value, inlet.Binding?.ClockingTransitionNodeId);
			Append(value, inlet.LocalFrame);
			Append(value, inlet.MergeStation);
			Append(value, inlet.BranchStartHandleLength);
			Append(value, inlet.BranchOuterRadiusMillimetres);
			Append(value, inlet.BranchPath?.SolverVersion);
			Append(value, inlet.BranchPath?.IsFeasible);
			foreach (CadCollectorBranchSpan span in inlet.BranchPath?.Spans
				?? Enumerable.Empty<CadCollectorBranchSpan>())
			{
				Append(value, span.Control1Local);
				Append(value, span.Control2Local);
				Append(value, span.EndLocal);
			}
			Append(value, inlet.ClockingTransitionLength);
			CadRunner runner = project.Runners.FirstOrDefault(
				item => item.Id == inlet.Binding?.RunnerId
			);
			Append(value, runner == null ? "missing" : Runner(project, runner));
		}
		return Finish(value);
	}

	private static void AppendGeometryGraph(StringBuilder value, RunnerGraph graph)
	{
		Append(value, "geometry-graph-v2");
		RunnerNode[] outputs = graph.Nodes.Where(node =>
			node.DefinitionId == RunnerNodes.RunnerOutput).ToArray();
		HashSet<Guid> reachable = outputs.Length == 1
			? CollectUpstreamNodes(graph, outputs[0].Id)
			: graph.Nodes.Select(node => node.Id).ToHashSet();

		foreach (RunnerNode node in graph.Nodes
			.Where(node => reachable.Contains(node.Id))
			.OrderBy(node => node.Id))
		{
			Append(value, node.Id);
			Append(value, node.DefinitionId);
			foreach ((string name, string propertyValue) in node.Properties
				.OrderBy(property => property.Key, StringComparer.Ordinal))
			{
				Append(value, name);
				Append(value, propertyValue);
			}
		}

		foreach (RunnerConnection connection in graph.Connections
			.Where(connection => reachable.Contains(connection.OutputNodeId)
				&& reachable.Contains(connection.InputNodeId))
			.OrderBy(connection => connection.InputNodeId)
			.ThenBy(connection => connection.InputPort, StringComparer.Ordinal)
			.ThenBy(connection => connection.OutputNodeId)
			.ThenBy(connection => connection.OutputPort, StringComparer.Ordinal))
		{
			Append(value, connection.OutputNodeId);
			Append(value, connection.OutputPort);
			Append(value, connection.InputNodeId);
			Append(value, connection.InputPort);
		}
	}

	private static HashSet<Guid> CollectUpstreamNodes(RunnerGraph graph, Guid outputNodeId)
	{
		HashSet<Guid> result = new() { outputNodeId };
		Stack<Guid> pending = new();
		pending.Push(outputNodeId);
		while (pending.Count > 0)
		{
			Guid inputNodeId = pending.Pop();
			foreach (RunnerConnection connection in graph.Connections.Where(connection =>
				connection.InputNodeId == inputNodeId))
			{
				if (result.Add(connection.OutputNodeId))
				{
					pending.Push(connection.OutputNodeId);
				}
			}
		}
		return result;
	}

	private static StringBuilder Begin(string kind)
	{
		return new StringBuilder()
			.Append("kind=").Append(kind)
			.Append(";abi=").Append(CadBuildCompatibility.NativeAbiVersion)
			.Append(";builder=").Append(CadBuildCompatibility.BuilderVersion)
			.Append(";occt=").Append(CadBuildCompatibility.OcctVersion)
			.Append(";sewing=").Append(CadBuildCompatibility.SewingPolicyVersion);
	}

	private static void Append(StringBuilder target, object value)
	{
		target.Append(';');
		switch (value)
		{
			case null:
				target.Append("null");
				break;
			case CadPoint3 point:
				AppendDouble(target, point.X);
				target.Append(',');
				AppendDouble(target, point.Y);
				target.Append(',');
				AppendDouble(target, point.Z);
				break;
			case CadQuaternion quaternion:
				AppendDouble(target, quaternion.X);
				target.Append(',');
				AppendDouble(target, quaternion.Y);
				target.Append(',');
				AppendDouble(target, quaternion.Z);
				target.Append(',');
				AppendDouble(target, quaternion.W);
				break;
			case CadTransform transform:
				Append(target, transform.Translation);
				Append(target, transform.Rotation);
				break;
			case CadFrame frame:
				Append(target, frame.Origin);
				AppendCanonicalDirection(target, frame.Tangent);
				AppendCanonicalDirection(target, frame.Normal);
				break;
			case PipeProfile profile:
				AppendDouble(target, profile.OuterDiameterMillimetres);
				target.Append(',');
				AppendDouble(target, profile.WallThicknessMillimetres);
				break;
			case IFormattable formattable:
				target.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
				break;
			default:
				target.Append(value);
				break;
		}
	}

	private static void AppendDouble(StringBuilder target, double value)
	{
		// Treat signed zero identically so serialization or arithmetic does not
		// invalidate an otherwise identical dependency key.
		target.Append((value == 0 ? 0 : value).ToString("R", CultureInfo.InvariantCulture));
	}

	private static void AppendCanonicalDirection(StringBuilder target, CadPoint3 value)
	{
		// CadFrame deliberately normalizes and orthogonalizes its axes. Recreating
		// a frame while loading JSON can therefore move the final few binary digits.
		// Twelve decimal places remain far below OCCT's modelling tolerance while
		// making the dependency identity stable across that canonicalization.
		target.Append(';');
		AppendCanonicalDirectionComponent(target, value.X);
		target.Append(',');
		AppendCanonicalDirectionComponent(target, value.Y);
		target.Append(',');
		AppendCanonicalDirectionComponent(target, value.Z);
	}

	private static void AppendCanonicalDirectionComponent(StringBuilder target, double value)
	{
		double canonical = Math.Round(value, 12, MidpointRounding.ToEven);
		AppendDouble(target, canonical);
	}

	private static string Finish(StringBuilder value)
	{
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
	}
}
