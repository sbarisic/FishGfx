using System.Globalization;

namespace FishGfx.Cad;

public sealed record RunnerFeatureSpec(
	Guid NodeId,
	RunnerFeatureKind Kind,
	double LengthMillimetres,
	double RadiusMillimetres,
	double SweepRadians,
	double RotationRadians,
	double StartHandleLengthMillimetres,
	CadPoint3 Control2Local,
	CadPoint3 EndLocal,
	RunnerSectionProfile OutputProfile,
	CadFrame? ConstrainedEndFrame = null,
	double EndHandleLengthMillimetres = 0
);

public sealed class RunnerGraphPlan
{
	public Guid RunnerId { get; internal set; }
	public Guid OutputNodeId { get; internal set; }
	public long EditRevision { get; internal set; }
	public CadGenerationStamp GenerationStamp { get; internal set; }
	public CadFrame StartFrame { get; internal set; }
	public RunnerSectionProfile StartProfile { get; internal set; }
	public IReadOnlyList<RunnerFeatureSpec> Features { get; internal set; } = Array.Empty<RunnerFeatureSpec>();
	public IReadOnlyList<CadDiagnostic> Diagnostics { get; internal set; } = Array.Empty<CadDiagnostic>();
	public bool Success => StartProfile != null && Features.Count > 0
		&& Diagnostics.All(diagnostic => diagnostic.Severity != CadDiagnosticSeverity.Error);
}

public static class RunnerGraphPlanner
{
	public static RunnerGraphPlan Plan(
		CadRunner runner,
		IReadOnlyDictionary<Guid, CadMate> mates,
		IReadOnlyDictionary<Guid, CadPart> parts,
		RunnerEndpointConstraint? endpointConstraint = null
	)
	{
		ArgumentNullException.ThrowIfNull(runner);
		ArgumentNullException.ThrowIfNull(mates);
		ArgumentNullException.ThrowIfNull(parts);
		return new Planner(runner, mates, parts, endpointConstraint).Run();
	}

	private sealed class Planner
	{
		private readonly CadRunner runner;
		private readonly RunnerGraph graph;
		private readonly IReadOnlyDictionary<Guid, CadMate> mates;
		private readonly IReadOnlyDictionary<Guid, CadPart> parts;
		private readonly RunnerEndpointConstraint? endpointConstraint;
		private readonly List<CadDiagnostic> diagnostics = new();

		internal Planner(
			CadRunner runner,
			IReadOnlyDictionary<Guid, CadMate> mates,
			IReadOnlyDictionary<Guid, CadPart> parts,
			RunnerEndpointConstraint? endpointConstraint
		)
		{
			this.runner = runner;
			graph = runner.Graph;
			this.mates = mates;
			this.parts = parts;
			this.endpointConstraint = endpointConstraint;
		}

		internal RunnerGraphPlan Run()
		{
			RunnerNode[] outputs = graph.Nodes
				.Where(node => node.DefinitionId == RunnerNodes.RunnerOutput)
				.ToArray();
			if (!graph.TryValidate(out string graphError))
			{
				Error("RUN005", graphError, null);
			}
			if (outputs.Length != 1)
			{
				Error("RUN001", "A runner graph requires exactly one Runner Output node.", null);
			}
			if (endpointConstraint.HasValue
				&& graph.Nodes.All(node => node.Id != endpointConstraint.Value.TerminalBezierNodeId
					|| node.DefinitionId != RunnerNodes.CubicBezier))
			{
				Error("COL101", "The collector binding references a missing terminal Bézier node.", null);
			}
			if (endpointConstraint.HasValue
				&& !CollectorSystemTransaction.ValidateTerminalPath(
					graph,
					new CadCollectorBinding
					{
						RunnerId = runner.Id,
						TerminalBezierNodeId = endpointConstraint.Value.TerminalBezierNodeId,
						ClockingTransitionNodeId =
							endpointConstraint.Value.ClockingTransitionNodeId,
					},
					"bound runner",
					out string terminalError
				))
			{
				Error(
					"COL102",
					terminalError,
					endpointConstraint.Value.TerminalBezierNodeId
				);
			}

			PlanState state = outputs.Length == 1 && diagnostics.Count == 0
				? BuildChain(InputNode(outputs[0], "runner"))
				: null;
			if (state != null && state.Specifications.Count == 0)
			{
				Error("RUN040", "Runner Output requires at least one construction feature.", outputs[0].Id);
			}

			return new RunnerGraphPlan
			{
				RunnerId = runner.Id,
				OutputNodeId = outputs.Length == 1 ? outputs[0].Id : Guid.Empty,
				EditRevision = runner.EditRevision,
				GenerationStamp = endpointConstraint?.Stamp
					?? new CadGenerationStamp(CadGenerationOwnerKind.Runner, runner.Id, runner.EditRevision),
				StartFrame = state?.StartFrame ?? default,
				StartProfile = state?.StartProfile,
				Features = state == null
					? Array.Empty<RunnerFeatureSpec>()
					: state.Specifications.AsReadOnly(),
				Diagnostics = diagnostics.AsReadOnly(),
			};
		}

		private PlanState BuildChain(RunnerNode node)
		{
			if (node == null)
			{
				return null;
			}
			if (node.DefinitionId == RunnerNodes.StartRunner)
			{
				return Start(node);
			}

			PlanState state = BuildChain(InputNode(node, "runner"));
			if (state == null)
			{
				return null;
			}
			switch (node.DefinitionId)
			{
				case RunnerNodes.Straight:
					double? straightLength = Number(node, "length", value => value > 0, "must be greater than zero");
					if (straightLength.HasValue)
					{
						state.Specifications.Add(new RunnerFeatureSpec(
							node.Id, RunnerFeatureKind.Straight, straightLength.Value, 0, 0, 0, 0,
							CadPoint3.Zero, CadPoint3.Zero, state.ActiveProfile));
					}
					break;
				case RunnerNodes.Bend:
					AddBend(state, node);
					break;
				case RunnerNodes.LoftTransition:
					AddLoft(state, node);
					break;
				case RunnerNodes.CubicBezier:
					AddBezier(state, node);
					break;
				case RunnerNodes.ClockingTransition:
					AddClockingTransition(state, node);
					break;
				default:
					Error("RUN004", $"Node '{node.DefinitionId}' cannot construct the terminal runner chain.", node.Id);
					return null;
			}
			return state;
		}

		private PlanState Start(RunnerNode node)
		{
			if (!mates.TryGetValue(runner.StartMateId, out CadMate mate) || !mate.IsResolved)
			{
				Error("RUN011", "The runner start mate is missing or unresolved.", node.Id);
				return null;
			}
			if (!parts.TryGetValue(mate.PartId, out CadPart part))
			{
				Error("RUN012", $"Mate '{mate.Name}' has no owning part.", node.Id);
				return null;
			}

			RunnerSectionProfile profile;
			RunnerNode profileNode = OptionalInputNode(node, "profile");
			if (profileNode != null)
			{
				PipeProfile? circular = CircularProfile(profileNode);
				if (!circular.HasValue)
				{
					return null;
				}
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
			return new PlanState(
				mate.LocalFrame.Value.Transformed(part.Transform),
				profile
			);
		}

		private void AddBend(PlanState state, RunnerNode node)
		{
			double? radius = Number(node, "radius", value => value > 0, "must be greater than zero");
			double? angle = Number(node, "angle", value => value > 0 && value <= 180,
				"must be in the range (0, 180] degrees");
			double? rotation = Number(node, "rotation", _ => true, "must be finite");
			if (!radius.HasValue || !angle.HasValue || !rotation.HasValue)
			{
				return;
			}
			if (radius.Value <= state.ActiveProfile.ApproximateOuterRadiusMillimetres)
			{
				Error("RUN041", "Centreline bend radius must exceed the active profile's outer radius.", node.Id);
				return;
			}
			state.Specifications.Add(new RunnerFeatureSpec(
				node.Id, RunnerFeatureKind.Bend, 0, radius.Value,
				angle.Value * Math.PI / 180, rotation.Value * Math.PI / 180, 0,
				CadPoint3.Zero, CadPoint3.Zero, state.ActiveProfile));
		}

		private void AddLoft(PlanState state, RunnerNode node)
		{
			double? length = Number(node, "length", value => value > 0, "must be greater than zero");
			double? rotation = Number(node, "rotation", _ => true, "must be finite");
			RunnerNode profileNode = InputNode(node, "targetProfile");
			PipeProfile? circular = profileNode == null ? null : CircularProfile(profileNode);
			if (!length.HasValue || !rotation.HasValue || !circular.HasValue)
			{
				return;
			}
			RunnerSectionProfile output = RunnerSectionProfile.FromCircular(circular.Value);
			state.Specifications.Add(new RunnerFeatureSpec(
				node.Id, RunnerFeatureKind.LoftTransition, length.Value, 0, 0,
				rotation.Value * Math.PI / 180, 0, CadPoint3.Zero, CadPoint3.Zero, output));
			state.ActiveProfile = output;
		}

		private void AddBezier(PlanState state, RunnerNode node)
		{
			double? handle = Number(node, "startHandleLength", value => value > 0, "must be greater than zero");
			double? c2t = Number(node, "control2T", _ => true, "must be finite");
			double? c2u = Number(node, "control2U", _ => true, "must be finite");
			double? c2v = Number(node, "control2V", _ => true, "must be finite");
			double? et = Number(node, "endT", _ => true, "must be finite");
			double? eu = Number(node, "endU", _ => true, "must be finite");
			double? ev = Number(node, "endV", _ => true, "must be finite");
			if (!handle.HasValue || !c2t.HasValue || !c2u.HasValue || !c2v.HasValue
				|| !et.HasValue || !eu.HasValue || !ev.HasValue)
			{
				return;
			}
			CadFrame? constrainedFrame = null;
			double endHandleLength = 0;
			if (endpointConstraint.HasValue && endpointConstraint.Value.TerminalBezierNodeId == node.Id)
			{
				double? constrainedHandle = Number(
					node,
					"endHandleLength",
					value => value > 0,
					"must be greater than zero"
				);
				if (!constrainedHandle.HasValue)
				{
					return;
				}
				constrainedFrame = endpointConstraint.Value.BezierEndFrame;
				endHandleLength = endpointConstraint.Value.EndHandleLength > 0
					? endpointConstraint.Value.EndHandleLength
					: constrainedHandle.Value;
			}
			state.Specifications.Add(new RunnerFeatureSpec(
				node.Id, RunnerFeatureKind.CubicBezier, 0, 0, 0, 0, handle.Value,
				new CadPoint3(c2t.Value, c2u.Value, c2v.Value),
				new CadPoint3(et.Value, eu.Value, ev.Value),
				state.ActiveProfile,
				constrainedFrame,
				endHandleLength));
		}

		private void AddClockingTransition(PlanState state, RunnerNode node)
		{
			double? length = Number(node, "length", value => value > 0, "must be greater than zero");
			double? rotation = Number(node, "rotation", _ => true, "must be finite");
			if (!length.HasValue || !rotation.HasValue)
			{
				return;
			}
			if (endpointConstraint.HasValue
				&& endpointConstraint.Value.ClockingTransitionNodeId == node.Id)
			{
				length = endpointConstraint.Value.ClockingTransitionLength;
			}
			CadFrame? constrainedFrame = endpointConstraint.HasValue
				&& endpointConstraint.Value.ClockingTransitionNodeId == node.Id
				? endpointConstraint.Value.TerminalFrame
				: null;
			state.Specifications.Add(new RunnerFeatureSpec(
				node.Id,
				RunnerFeatureKind.ClockingTransition,
				length.Value,
				0,
				0,
				rotation.Value * Math.PI / 180,
				0,
				CadPoint3.Zero,
				CadPoint3.Zero,
				state.ActiveProfile,
				constrainedFrame
			));
		}

		private PipeProfile? CircularProfile(RunnerNode node)
		{
			if (node.DefinitionId != RunnerNodes.CircularPipe)
			{
				Error("RUN006", "The profile input must come from a Circular Pipe node.", node.Id);
				return null;
			}
			double? outer = Number(node, "outerDiameter", value => value > 0, "must be greater than zero");
			double? wall = Number(node, "wallThickness", value => value > 0, "must be greater than zero");
			if (!outer.HasValue || !wall.HasValue)
			{
				return null;
			}
			if (wall.Value >= outer.Value * 0.5)
			{
				Error("RUN032", "Wall thickness must be less than half the outer diameter.", node.Id);
				return null;
			}
			return new PipeProfile(outer.Value, wall.Value);
		}

		private RunnerNode InputNode(RunnerNode node, string port)
		{
			RunnerNode result = OptionalInputNode(node, port);
			if (result == null)
			{
				Error("RUN020", $"Input '{port}' is not connected.", node.Id);
			}
			return result;
		}

		private RunnerNode OptionalInputNode(RunnerNode node, string port)
		{
			RunnerConnection connection = graph.Connections.SingleOrDefault(candidate =>
				candidate.InputNodeId == node.Id
				&& string.Equals(candidate.InputPort, port, StringComparison.Ordinal));
			return connection == null
				? null
				: graph.Nodes.SingleOrDefault(candidate => candidate.Id == connection.OutputNodeId);
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

		private void Error(string code, string message, Guid? nodeId)
		{
			if (!diagnostics.Any(item => item.Code == code && item.NodeId == nodeId && item.Message == message))
			{
				diagnostics.Add(new CadDiagnostic(code, message, CadDiagnosticSeverity.Error, nodeId));
			}
		}
	}

	private sealed class PlanState
	{
		internal PlanState(CadFrame startFrame, RunnerSectionProfile startProfile)
		{
			StartFrame = startFrame;
			StartProfile = startProfile;
			ActiveProfile = startProfile;
		}

		internal CadFrame StartFrame { get; }
		internal RunnerSectionProfile StartProfile { get; }
		internal RunnerSectionProfile ActiveProfile { get; set; }
		internal List<RunnerFeatureSpec> Specifications { get; } = new();
	}
}
