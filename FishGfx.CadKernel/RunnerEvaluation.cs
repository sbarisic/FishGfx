using System.Globalization;

namespace FishGfx.Cad;

public enum RunnerFeatureKind
{
	Straight,
	Bend,
	LoftTransition,
	CubicBezier,
	ClockingTransition,
}

public enum RunnerPathPointKind
{
	Start,
	Control1,
	Control2,
	End,
}

public readonly record struct CadPathPointRef(Guid RunnerId, Guid NodeId, RunnerPathPointKind PointKind);

public enum RunnerProfileKind
{
	MateProfile,
	CircularPipe,
}

public readonly record struct PipeProfile(double OuterDiameterMillimetres, double WallThicknessMillimetres)
{
	public double OuterRadiusMillimetres => OuterDiameterMillimetres * 0.5;
	public double InnerRadiusMillimetres => OuterRadiusMillimetres - WallThicknessMillimetres;
}

public sealed record RunnerSectionProfile(
	RunnerProfileKind Kind,
	Guid? MateId,
	CadTopologyKind MateTopologyKind,
	double MateEquivalentRadiusMillimetres,
	double WallThicknessMillimetres,
	PipeProfile? CircularProfile
)
{
	public static RunnerSectionProfile FromMate(CadMate mate, double wallThickness)
	{
		return new RunnerSectionProfile(
			RunnerProfileKind.MateProfile,
			mate.Id,
			mate.Topology?.Kind ?? CadTopologyKind.Unknown,
			mate.RadiusMillimetres,
			wallThickness,
			null
		);
	}

	public static RunnerSectionProfile FromCircular(PipeProfile profile)
	{
		return new RunnerSectionProfile(
			RunnerProfileKind.CircularPipe,
			null,
			CadTopologyKind.Unknown,
			0,
			profile.WallThicknessMillimetres,
			profile
		);
	}

	public double ApproximateOuterRadiusMillimetres => Kind == RunnerProfileKind.CircularPipe
		? CircularProfile.Value.OuterRadiusMillimetres
		: MateEquivalentRadiusMillimetres + WallThicknessMillimetres;
}

public sealed record RunnerFeature(
	Guid NodeId,
	RunnerFeatureKind Kind,
	CadFrame EntryFrame,
	CadFrame ExitFrame,
	RunnerSectionProfile InputProfile,
	RunnerSectionProfile OutputProfile,
	double LengthMillimetres,
	CadPoint3 Center,
	double RadiusMillimetres,
	double SweepRadians,
	double RotationRadians,
	CadPoint3 Control1 = default,
	CadPoint3 Control2 = default
);

public sealed class RunnerFeatureChain
{
	public RunnerFeatureChain(
		Guid startMateId,
		CadFrame endFrame,
		RunnerSectionProfile activeProfile,
		IReadOnlyList<RunnerFeature> features
	)
	{
		StartMateId = startMateId;
		EndFrame = endFrame;
		ActiveProfile = activeProfile;
		Features = features;
		LengthMillimetres = features.Sum(feature => feature.LengthMillimetres);
	}

	public Guid StartMateId { get; }
	public CadFrame EndFrame { get; }
	public RunnerSectionProfile ActiveProfile { get; }
	public IReadOnlyList<RunnerFeature> Features { get; }
	public double LengthMillimetres { get; }

	public RunnerFeatureChain Append(RunnerFeature feature)
	{
		RunnerFeature[] result = new RunnerFeature[Features.Count + 1];
		for (int index = 0; index < Features.Count; index++)
		{
			result[index] = Features[index];
		}
		result[^1] = feature;
		return new RunnerFeatureChain(StartMateId, feature.ExitFrame, feature.OutputProfile, Array.AsReadOnly(result));
	}
}

public sealed class RunnerEvaluationResult
{
	public Guid RunnerId { get; internal set; }
	public Guid OutputNodeId { get; internal set; }
	public long EditRevision { get; internal set; }
	public CadGenerationStamp GenerationStamp { get; internal set; }
	public RunnerFeatureChain Chain { get; internal set; }
	public IReadOnlyList<CadDiagnostic> Diagnostics { get; internal set; } = Array.Empty<CadDiagnostic>();
	public double LengthMillimetres => Chain?.LengthMillimetres ?? 0;
	public bool LengthIsToleranceControlled => Chain?.Features
		.Any(feature => feature.Kind == RunnerFeatureKind.CubicBezier) == true;
	public bool Success => Chain?.Features.Count > 0
		&& Diagnostics.All(diagnostic => diagnostic.Severity != CadDiagnosticSeverity.Error);
}

public static class RunnerGraphEvaluator
{
	public static RunnerEvaluationResult Evaluate(
		CadRunner runner,
		IReadOnlyDictionary<Guid, CadMate> mates,
		IReadOnlyDictionary<Guid, CadPart> parts
	)
	{
		ArgumentNullException.ThrowIfNull(runner);
		ArgumentNullException.ThrowIfNull(mates);
		ArgumentNullException.ThrowIfNull(parts);
		return new Evaluator(runner, mates, parts).Run();
	}

	private sealed class Evaluator
	{
		private readonly CadRunner runner;
		private readonly RunnerGraph graph;
		private readonly IReadOnlyDictionary<Guid, CadMate> mates;
		private readonly IReadOnlyDictionary<Guid, CadPart> parts;
		private readonly Dictionary<(Guid NodeId, string Port), object> values = new();
		private readonly HashSet<Guid> evaluating = new();
		private readonly List<CadDiagnostic> diagnostics = new();

		internal Evaluator(
			CadRunner runner,
			IReadOnlyDictionary<Guid, CadMate> mates,
			IReadOnlyDictionary<Guid, CadPart> parts
		)
		{
			this.runner = runner;
			graph = runner.Graph;
			this.mates = mates;
			this.parts = parts;
		}

		internal RunnerEvaluationResult Run()
		{
			ValidateSchema();
			RunnerNode[] outputs = graph.Nodes.Where(node => node.DefinitionId == RunnerNodes.RunnerOutput).ToArray();
			if (outputs.Length != 1)
			{
				Error("RUN001", "A runner graph requires exactly one Runner Output node.", null);
			}
			if (diagnostics.Any(diagnostic => diagnostic.Severity == CadDiagnosticSeverity.Error))
			{
				return Result(outputs, null);
			}

			RunnerFeatureChain chain = outputs.Length == 1
				? EvaluateOutput(outputs[0], "runner") as RunnerFeatureChain
				: null;
			if (chain != null && chain.Features.Count == 0)
			{
				Error("RUN040", "Runner Output requires at least one construction feature.", outputs[0].Id);
			}

			return Result(outputs, chain);
		}

		private RunnerEvaluationResult Result(RunnerNode[] outputs, RunnerFeatureChain chain)
		{
			return new RunnerEvaluationResult
			{
				RunnerId = runner.Id,
				OutputNodeId = outputs.Length == 1 ? outputs[0].Id : Guid.Empty,
				EditRevision = runner.EditRevision,
				Chain = chain,
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
					RunnerNodes.StartRunner => EvaluateStart(node),
					RunnerNodes.Straight => EvaluateStraight(node),
					RunnerNodes.Bend => EvaluateBend(node),
					RunnerNodes.CubicBezier => UnsupportedBezier(node),
					RunnerNodes.CircularPipe => EvaluateProfile(node),
					RunnerNodes.LoftTransition => EvaluateLoft(node),
					RunnerNodes.RunnerOutput => EvaluateRunnerOutput(node),
					RunnerNodes.RunnerLength => EvaluateLength(node),
					_ => Unknown(node),
				};
				values[(node.Id, port)] = value;
				return value;
			}
			finally
			{
				evaluating.Remove(node.Id);
			}
		}

		private object Unknown(RunnerNode node)
		{
			Error("RUN004", $"Node definition '{node.DefinitionId}' is unavailable.", node.Id);
			return null;
		}

		private RunnerFeatureChain EvaluateStart(RunnerNode node)
		{
			if (!mates.TryGetValue(runner.StartMateId, out CadMate mate))
			{
				Error("RUN010", "The runner does not identify an existing start mate.", node.Id);
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

			PipeProfile? circular = OptionalInput<PipeProfile?>(node, "profile");
			RunnerSectionProfile profile;
			if (circular.HasValue)
			{
				profile = RunnerSectionProfile.FromCircular(circular.Value);
			}
			else
			{
				double? wall = Number(node, "wallThickness", value => value > 0, "must be greater than zero");
				if (!wall.HasValue)
				{
					return null;
				}
				profile = RunnerSectionProfile.FromMate(mate, wall.Value);
			}

			return new RunnerFeatureChain(
				mate.Id,
				mate.LocalFrame.Value.Transformed(part.Transform),
				profile,
				Array.Empty<RunnerFeature>()
			);
		}

		private RunnerFeatureChain EvaluateStraight(RunnerNode node)
		{
			RunnerFeatureChain chain = Input<RunnerFeatureChain>(node, "runner");
			double? length = Number(node, "length", value => value > 0, "must be greater than zero");
			if (chain == null || !length.HasValue)
				return null;
			CadFrame start = chain.EndFrame;
			CadFrame end = new(start.Origin + start.Tangent * length.Value, start.Tangent, start.Normal);
			return chain.Append(new RunnerFeature(
				node.Id, RunnerFeatureKind.Straight, start, end, chain.ActiveProfile, chain.ActiveProfile,
				length.Value, CadPoint3.Zero, 0, 0, 0));
		}

		private RunnerFeatureChain EvaluateBend(RunnerNode node)
		{
			RunnerFeatureChain chain = Input<RunnerFeatureChain>(node, "runner");
			double? radius = Number(node, "radius", value => value > 0, "must be greater than zero");
			double? angle = Number(node, "angle", value => value > 0 && value <= 180,
				"must be in the range (0, 180] degrees");
			double? rotation = Number(node, "rotation", _ => true, "must be finite");
			if (chain == null || !radius.HasValue || !angle.HasValue || !rotation.HasValue)
				return null;

			if (radius.Value <= chain.ActiveProfile.ApproximateOuterRadiusMillimetres)
			{
				Error("RUN041", "Centreline bend radius must exceed the active profile's outer radius.", node.Id);
				return null;
			}

			CadFrame start = chain.EndFrame;
			double rotationRadians = rotation.Value * Math.PI / 180;
			double sweepRadians = angle.Value * Math.PI / 180;
			CadPoint3 radial = CadFrame.RotateAround(start.Normal, start.Tangent, rotationRadians).Normalized();
			CadPoint3 center = start.Origin + radial * radius.Value;
			CadPoint3 startRadius = start.Origin - center;
			CadPoint3 axis = CadPoint3.Cross(startRadius, start.Tangent).Normalized();
			CadPoint3 endRadius = CadFrame.RotateAround(startRadius, axis, sweepRadians);
			CadPoint3 endTangent = CadFrame.RotateAround(start.Tangent, axis, sweepRadians).Normalized();
			CadPoint3 transportedNormal = CadFrame.RotateAround(start.Normal, axis, sweepRadians).Normalized();
			CadFrame end = new(center + endRadius, endTangent, transportedNormal);
			return chain.Append(new RunnerFeature(
				node.Id, RunnerFeatureKind.Bend, start, end, chain.ActiveProfile, chain.ActiveProfile,
				radius.Value * sweepRadians, center, radius.Value, sweepRadians, rotationRadians));
		}

		private RunnerFeatureChain EvaluateLoft(RunnerNode node)
		{
			RunnerFeatureChain chain = Input<RunnerFeatureChain>(node, "runner");
			PipeProfile? target = Input<PipeProfile?>(node, "targetProfile");
			double? length = Number(node, "length", value => value > 0, "must be greater than zero");
			double? rotation = Number(node, "rotation", _ => true, "must be finite");
			if (chain == null || !target.HasValue || !length.HasValue || !rotation.HasValue)
				return null;

			CadFrame start = chain.EndFrame;
			double rotationRadians = rotation.Value * Math.PI / 180;
			CadPoint3 endNormal = CadFrame.RotateAround(start.Normal, start.Tangent, rotationRadians);
			CadFrame end = new(start.Origin + start.Tangent * length.Value, start.Tangent, endNormal);
			RunnerSectionProfile output = RunnerSectionProfile.FromCircular(target.Value);
			return chain.Append(new RunnerFeature(
				node.Id, RunnerFeatureKind.LoftTransition, start, end, chain.ActiveProfile, output,
				length.Value, CadPoint3.Zero, 0, 0, rotationRadians));
		}

		private object UnsupportedBezier(RunnerNode node)
		{
			Error("RUN050", "Cubic Bézier evaluation requires CadDocument.EvaluateRunnerAsync.", node.Id);
			return null;
		}

		private PipeProfile? EvaluateProfile(RunnerNode node)
		{
			double? outer = Number(node, "outerDiameter", value => value > 0, "must be greater than zero");
			double? wall = Number(node, "wallThickness", value => value > 0, "must be greater than zero");
			if (!outer.HasValue || !wall.HasValue)
				return null;
			if (wall.Value >= outer.Value * 0.5)
			{
				Error("RUN032", "Wall thickness must be less than half the outer diameter.", node.Id);
				return null;
			}
			return new PipeProfile(outer.Value, wall.Value);
		}

		private RunnerFeatureChain EvaluateRunnerOutput(RunnerNode node) => Input<RunnerFeatureChain>(node, "runner");
		private double? EvaluateLength(RunnerNode node) => Input<RunnerFeatureChain>(node, "runner")?.LengthMillimetres;

		private T Input<T>(RunnerNode node, string port)
		{
			RunnerConnection connection = graph.Connections.SingleOrDefault(candidate =>
				candidate.InputNodeId == node.Id && string.Equals(candidate.InputPort, port, StringComparison.Ordinal));
			if (connection == null)
			{
				Error("RUN020", $"Input '{port}' is not connected.", node.Id);
				return default;
			}
			RunnerNode source = graph.Nodes.Single(candidate => candidate.Id == connection.OutputNodeId);
			object value = EvaluateOutput(source, connection.OutputPort);
			return value is T typed ? typed : default;
		}

		private T OptionalInput<T>(RunnerNode node, string port)
		{
			RunnerConnection connection = graph.Connections.SingleOrDefault(candidate =>
				candidate.InputNodeId == node.Id && string.Equals(candidate.InputPort, port, StringComparison.Ordinal));
			if (connection == null)
				return default;
			RunnerNode source = graph.Nodes.Single(candidate => candidate.Id == connection.OutputNodeId);
			object value = EvaluateOutput(source, connection.OutputPort);
			return value is T typed ? typed : default;
		}

		private double? Number(RunnerNode node, string property, Func<double, bool> predicate, string requirement)
		{
			if (!node.Properties.TryGetValue(property, out string text)
				|| !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
				|| !double.IsFinite(value) || !predicate(value))
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
					Error("RUN003", $"Duplicate node ID '{node.Id}'.", node.Id);
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
				RunnerPortDefinition source = outputDefinition.FindPort(connection.OutputPort, RunnerPortDirection.Output);
				RunnerPortDefinition destination = inputDefinition.FindPort(connection.InputPort, RunnerPortDirection.Input);
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
			if (!diagnostics.Any(existing => existing.Code == code && existing.NodeId == nodeId
				&& existing.Message == message))
			{
				diagnostics.Add(new CadDiagnostic(code, message, CadDiagnosticSeverity.Error, nodeId));
			}
		}
	}
}
