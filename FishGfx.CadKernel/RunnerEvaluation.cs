using System.Globalization;

namespace FishGfx.Cad;

public enum RunnerSegmentKind
{
	Straight,
	Bend,
}

public sealed record RunnerSegment(
	Guid NodeId,
	RunnerSegmentKind Kind,
	CadPoint3 Start,
	CadPoint3 End,
	CadPoint3 StartTangent,
	CadPoint3 EndTangent,
	CadPoint3 Center,
	double RadiusMillimetres,
	double SweepRadians,
	double LengthMillimetres
);

public sealed class RunnerPath
{
	public RunnerPath(CadFrame endFrame, IReadOnlyList<RunnerSegment> segments)
	{
		EndFrame = endFrame;
		Segments = segments;
		LengthMillimetres = segments.Sum(segment => segment.LengthMillimetres);
	}

	public CadFrame EndFrame { get; }

	public IReadOnlyList<RunnerSegment> Segments { get; }

	public double LengthMillimetres { get; }

	public RunnerPath Append(RunnerSegment segment, CadFrame endFrame)
	{
		RunnerSegment[] result = new RunnerSegment[Segments.Count + 1];

		for (int index = 0; index < Segments.Count; index++)
		{
			result[index] = Segments[index];
		}

		result[^1] = segment;
		return new RunnerPath(endFrame, Array.AsReadOnly(result));
	}
}

public readonly record struct PipeProfile(double OuterDiameterMillimetres, double WallThicknessMillimetres)
{
	public double OuterRadiusMillimetres => OuterDiameterMillimetres * 0.5;

	public double InnerRadiusMillimetres => OuterRadiusMillimetres - WallThicknessMillimetres;
}

public sealed record RunnerSolidRequest(RunnerPath Path, PipeProfile Profile);

public sealed class RunnerEvaluationResult
{
	public RunnerPath Path { get; internal set; }

	public PipeProfile? Profile { get; internal set; }

	public RunnerSolidRequest SolidRequest { get; internal set; }

	public double LengthMillimetres => Path?.LengthMillimetres ?? 0;

	public IReadOnlyList<CadDiagnostic> Diagnostics { get; internal set; } = Array.Empty<CadDiagnostic>();

	public bool Success => SolidRequest != null
		&& Diagnostics.All(diagnostic => diagnostic.Severity != CadDiagnosticSeverity.Error);
}

public static class RunnerGraphEvaluator
{
	public static RunnerEvaluationResult Evaluate(
		RunnerGraph graph,
		IReadOnlyDictionary<Guid, CadMate> mates,
		IReadOnlyDictionary<Guid, CadPart> parts
	)
	{
		ArgumentNullException.ThrowIfNull(graph);
		ArgumentNullException.ThrowIfNull(mates);
		ArgumentNullException.ThrowIfNull(parts);

		Evaluator evaluator = new(graph, mates, parts);
		return evaluator.Run();
	}

	private sealed class Evaluator
	{
		private readonly RunnerGraph graph;
		private readonly IReadOnlyDictionary<Guid, CadMate> mates;
		private readonly IReadOnlyDictionary<Guid, CadPart> parts;
		private readonly Dictionary<(Guid NodeId, string Port), object> values = new();
		private readonly HashSet<Guid> evaluating = new();
		private readonly List<CadDiagnostic> diagnostics = new();

		internal Evaluator(
			RunnerGraph graph,
			IReadOnlyDictionary<Guid, CadMate> mates,
			IReadOnlyDictionary<Guid, CadPart> parts
		)
		{
			this.graph = graph;
			this.mates = mates;
			this.parts = parts;
		}

		internal RunnerEvaluationResult Run()
		{
			ValidateSchema();
			RunnerNode[] sweeps = graph.Nodes
				.Where(node => node.DefinitionId == RunnerNodes.SweepPipe)
				.ToArray();

			if (sweeps.Length != 1)
			{
				diagnostics.Add(new CadDiagnostic(
					"RUN001",
					"A runner graph requires exactly one Sweep Pipe node.",
					CadDiagnosticSeverity.Error
				));
			}

			RunnerSolidRequest solid = sweeps.Length == 1
				? EvaluateOutput(sweeps[0], "solid") as RunnerSolidRequest
				: null;

			foreach (RunnerNode length in graph.Nodes.Where(node =>
				node.DefinitionId == RunnerNodes.RunnerLength
			))
			{
				EvaluateOutput(length, "length");
			}

			return new RunnerEvaluationResult
			{
				Path = solid?.Path,
				Profile = solid?.Profile,
				SolidRequest = solid,
				Diagnostics = diagnostics.AsReadOnly(),
			};
		}

		private object EvaluateOutput(RunnerNode node, string port)
		{
			if (values.TryGetValue((node.Id, port), out object existing))
			{
				return existing;
			}

			if (!evaluating.Add(node.Id))
			{
				Error("RUN002", "The runner graph contains a cycle.", node.Id);
				return null;
			}

			try
			{
				object value = node.DefinitionId switch
				{
					RunnerNodes.MateReference => EvaluateMate(node),
					RunnerNodes.StartRunner => EvaluateStart(node),
					RunnerNodes.Straight => EvaluateStraight(node),
					RunnerNodes.Bend => EvaluateBend(node),
					RunnerNodes.CircularPipe => EvaluateProfile(node),
					RunnerNodes.SweepPipe => EvaluateSweep(node),
					RunnerNodes.RunnerLength => EvaluateLength(node),
					_ => null,
				};

				values[(node.Id, port)] = value;
				return value;
			}
			finally
			{
				evaluating.Remove(node.Id);
			}
		}

		private CadFrame? EvaluateMate(RunnerNode node)
		{
			if (!node.Properties.TryGetValue("mateId", out string value)
				|| !Guid.TryParse(value, out Guid mateId)
				|| !mates.TryGetValue(mateId, out CadMate mate))
			{
				Error("RUN010", "Mate Reference does not identify an existing mate.", node.Id);
				return null;
			}

			if (!mate.IsResolved)
			{
				Error("RUN011", $"Mate '{mate.Name}' is unresolved.", node.Id);
				return null;
			}

			if (!parts.TryGetValue(mate.PartId, out CadPart part))
			{
				Error("RUN012", $"Mate '{mate.Name}' has no owning part.", node.Id);
				return null;
			}

			return mate.LocalFrame.Value.Transformed(part.Transform);
		}

		private RunnerPath EvaluateStart(RunnerNode node)
		{
			CadFrame? frame = Input<CadFrame?>(node, "mate");

			return frame.HasValue
				? new RunnerPath(frame.Value, Array.Empty<RunnerSegment>())
				: null;
		}

		private RunnerPath EvaluateStraight(RunnerNode node)
		{
			RunnerPath path = Input<RunnerPath>(node, "path");
			double? length = Number(node, "length", value => value > 0, "must be greater than zero");

			if (path == null || !length.HasValue)
			{
				return null;
			}

			CadFrame start = path.EndFrame;
			CadPoint3 end = start.Origin + start.Tangent * length.Value;
			CadFrame endFrame = new(end, start.Tangent, start.Normal);
			RunnerSegment segment = new(
				node.Id,
				RunnerSegmentKind.Straight,
				start.Origin,
				end,
				start.Tangent,
				start.Tangent,
				CadPoint3.Zero,
				0,
				0,
				length.Value
			);

			return path.Append(segment, endFrame);
		}

		private RunnerPath EvaluateBend(RunnerNode node)
		{
			RunnerPath path = Input<RunnerPath>(node, "path");
			double? radius = Number(node, "radius", value => value > 0, "must be greater than zero");
			double? angle = Number(
				node,
				"angle",
				value => value > 0 && value <= 180,
				"must be in the range (0, 180] degrees"
			);
			double? rotation = Number(node, "rotation", _ => true, "must be finite");

			if (path == null || !radius.HasValue || !angle.HasValue || !rotation.HasValue)
			{
				return null;
			}

			CadFrame start = path.EndFrame;
			double rotationRadians = rotation.Value * Math.PI / 180;
			double sweepRadians = angle.Value * Math.PI / 180;
			CadPoint3 radial = CadFrame.RotateAround(start.Normal, start.Tangent, rotationRadians).Normalized();
			CadPoint3 center = start.Origin + radial * radius.Value;
			double cosine = Math.Cos(sweepRadians);
			double sine = Math.Sin(sweepRadians);
			CadPoint3 end = center + (-radial * cosine + start.Tangent * sine) * radius.Value;
			CadPoint3 endTangent = (start.Tangent * cosine + radial * sine).Normalized();
			CadPoint3 endNormal = (center - end).Normalized();
			CadFrame endFrame = new(end, endTangent, endNormal);
			RunnerSegment segment = new(
				node.Id,
				RunnerSegmentKind.Bend,
				start.Origin,
				end,
				start.Tangent,
				endTangent,
				center,
				radius.Value,
				sweepRadians,
				radius.Value * sweepRadians
			);

			return path.Append(segment, endFrame);
		}

		private PipeProfile? EvaluateProfile(RunnerNode node)
		{
			double? outerDiameter = Number(
				node,
				"outerDiameter",
				value => value > 0,
				"must be greater than zero"
			);
			double? wall = Number(
				node,
				"wallThickness",
				value => value > 0,
				"must be greater than zero"
			);

			if (!outerDiameter.HasValue || !wall.HasValue)
			{
				return null;
			}

			if (wall.Value >= outerDiameter.Value * 0.5)
			{
				Error("RUN032", "Wall thickness must be less than half the outer diameter.", node.Id);
				return null;
			}

			return new PipeProfile(outerDiameter.Value, wall.Value);
		}

		private RunnerSolidRequest EvaluateSweep(RunnerNode node)
		{
			RunnerPath path = Input<RunnerPath>(node, "path");
			PipeProfile? profile = Input<PipeProfile?>(node, "profile");

			if (path == null || !profile.HasValue)
			{
				return null;
			}

			if (path.Segments.Count == 0)
			{
				Error("RUN040", "Sweep Pipe requires at least one runner segment.", node.Id);
				return null;
			}

			foreach (RunnerSegment segment in path.Segments.Where(segment =>
				segment.Kind == RunnerSegmentKind.Bend
			))
			{
				if (segment.RadiusMillimetres <= profile.Value.OuterRadiusMillimetres)
				{
					Error(
						"RUN041",
						"Every centreline bend radius must be greater than the pipe outer radius.",
						segment.NodeId
					);
				}
			}

			return diagnostics.Any(diagnostic => diagnostic.Severity == CadDiagnosticSeverity.Error)
				? null
				: new RunnerSolidRequest(path, profile.Value);
		}

		private double? EvaluateLength(RunnerNode node)
		{
			return Input<RunnerPath>(node, "path")?.LengthMillimetres;
		}

		private T Input<T>(RunnerNode node, string port)
		{
			RunnerConnection connection = graph.Connections.SingleOrDefault(candidate =>
				candidate.InputNodeId == node.Id
				&& string.Equals(candidate.InputPort, port, StringComparison.Ordinal)
			);

			if (connection == null)
			{
				Error("RUN020", $"Input '{port}' is not connected.", node.Id);
				return default;
			}

			RunnerNode source = graph.Nodes.Single(candidate => candidate.Id == connection.OutputNodeId);
			object value = EvaluateOutput(source, connection.OutputPort);

			return value is T typed ? typed : default;
		}

		private double? Number(
			RunnerNode node,
			string property,
			Func<double, bool> predicate,
			string requirement
		)
		{
			if (!node.Properties.TryGetValue(property, out string text)
				|| !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
				|| !double.IsFinite(value)
				|| !predicate(value))
			{
				Error("RUN030", $"Property '{property}' {requirement}.", node.Id);
				return null;
			}

			return value;
		}

		private void ValidateSchema()
		{
			HashSet<Guid> nodeIds = new();

			foreach (RunnerNode node in graph.Nodes)
			{
				if (!nodeIds.Add(node.Id))
				{
					Error("RUN003", $"Duplicate node ID '{node.Id}'.", node.Id);
				}

				if (!RunnerNodes.TryGet(node.DefinitionId, out _))
				{
					Error("RUN004", $"Node definition '{node.DefinitionId}' is unavailable.", node.Id);
				}
			}

			HashSet<(Guid NodeId, string Port)> inputs = new();

			foreach (RunnerConnection connection in graph.Connections)
			{
				RunnerNode output = graph.Nodes.FirstOrDefault(node => node.Id == connection.OutputNodeId);
				RunnerNode input = graph.Nodes.FirstOrDefault(node => node.Id == connection.InputNodeId);

				if (output == null || input == null
					|| !RunnerNodes.TryGet(output.DefinitionId, out RunnerNodeDefinition outputDefinition)
					|| !RunnerNodes.TryGet(input.DefinitionId, out RunnerNodeDefinition inputDefinition))
				{
					Error("RUN005", "A connection references an unavailable node.", null);
					continue;
				}

				RunnerPortDefinition source = outputDefinition.FindPort(
					connection.OutputPort,
					RunnerPortDirection.Output
				);
				RunnerPortDefinition destination = inputDefinition.FindPort(
					connection.InputPort,
					RunnerPortDirection.Input
				);

				if (source == null || destination == null || source.Type != destination.Type)
				{
					Error("RUN006", "A connection has missing or incompatible ports.", input.Id);
				}

				if (!inputs.Add((input.Id, connection.InputPort)))
				{
					Error("RUN007", "A node input has more than one connection.", input.Id);
				}
			}
		}

		private void Error(string code, string message, Guid? nodeId)
		{
			if (!diagnostics.Any(existing =>
				existing.Code == code && existing.NodeId == nodeId && existing.Message == message
			))
			{
				diagnostics.Add(new CadDiagnostic(
					code,
					message,
					CadDiagnosticSeverity.Error,
					nodeId
				));
			}
		}
	}
}
