namespace FishGfx.Cad;

public sealed partial class CollectorSystemTransaction
{
	private void SeedTerminalHandles(
		CadCollectorSystem system,
		IReadOnlyDictionary<Guid, RunnerEvaluationResult> authoritativeEvaluations
	)
	{
		if (authoritativeEvaluations == null)
		{
			return;
		}
		foreach (CadCollectorInlet inlet in system.Inlets)
		{
			CadCollectorBinding binding = inlet.Binding;
			if (!authoritativeEvaluations.TryGetValue(
					binding.RunnerId,
					out RunnerEvaluationResult evaluation
				)
				|| !evaluation.Success)
			{
				continue;
			}
			CadFrame target = system.GetWorldInletFrame(inlet);
			if (binding.ClockingTransitionNodeId.HasValue)
			{
				target = new CadFrame(
					target.Origin - target.Tangent * inlet.ClockingTransitionLength,
					target.Tangent,
					target.Normal
				);
			}
			double chordLength = (target.Origin - evaluation.Chain.EndFrame.Origin).Length;
			double handleLength = Math.Clamp(chordLength / 3, 35, 180);
			RunnerNode terminal = stagedGraphs[binding.RunnerId].Nodes.Single(
				node => node.Id == binding.TerminalBezierNodeId);
			terminal.Properties["startHandleLength"] =
				FormattableString.Invariant($"{handleLength:R}");
			terminal.Properties["endHandleLength"] =
				FormattableString.Invariant($"{handleLength:R}");
		}
	}

	private CadFrame SeedOutletFrame(
		IReadOnlyList<Guid> runnerIds,
		IReadOnlyDictionary<Guid, RunnerEvaluationResult> authoritativeEvaluations
	)
	{
		List<CadFrame> startFrames = new();
		bool allAuthoritative = authoritativeEvaluations != null;
		foreach (Guid runnerId in runnerIds)
		{
			CadRunner runner = project.Runners.Single(item => item.Id == runnerId);
			if (authoritativeEvaluations != null
				&& authoritativeEvaluations.TryGetValue(
					runnerId,
					out RunnerEvaluationResult evaluation
				)
				&& evaluation.Success
				&& evaluation.GenerationStamp.OwnerKind == CadGenerationOwnerKind.Runner
				&& evaluation.GenerationStamp.OwnerId == runnerId
				&& evaluation.GenerationStamp.Revision == runner.EditRevision)
			{
				startFrames.Add(evaluation.Chain.EndFrame);
				continue;
			}
			allAuthoritative = false;
			CadMate mate = project.Mates.Single(item => item.Id == runner.StartMateId);
			CadPart part = project.Parts.Single(item => item.Id == mate.PartId);
			startFrames.Add(mate.LocalFrame.Value.Transformed(part.Transform));
		}
		CadPoint3 origin = CadPoint3.Zero;
		CadPoint3 tangent = CadPoint3.Zero;
		CadPoint3 normal = CadPoint3.Zero;
		foreach (CadFrame frame in startFrames)
		{
			origin += frame.Origin;
			tangent += frame.Tangent;
			normal += frame.Normal;
		}
		origin /= startFrames.Count;
		if (tangent.LengthSquared <= 1e-12)
		{
			tangent = startFrames[0].Tangent;
		}
		if (normal.LengthSquared <= 1e-12)
		{
			normal = startFrames[0].Normal;
		}
		CadFrame orientation = new(CadPoint3.Zero, tangent, normal);
		return new CadFrame(
			origin + orientation.Tangent * 600
				+ orientation.Normal * (allAuthoritative ? 0 : 100),
			orientation.Tangent,
			orientation.Normal
		);
	}

	private static void ApplyPreset(CadCollectorSystem system, CollectorLayoutPreset preset)
	{
		int count = system.Inlets.Count;
		double inletX = -(
			system.OutletStubLength
			+ system.MergeLength
			+ system.OverlapLength
			+ 60
		);
		for (int index = 0; index < count; index++)
		{
			double centered = index - (count - 1) * 0.5;
			CadPoint3 origin;
			CadPoint3 tangent;
			CadPoint3 normal;
			switch (preset)
			{
				case CollectorLayoutPreset.Radial:
					double angle = 2 * Math.PI * index / count;
					origin = new CadPoint3(
						inletX,
						70 * Math.Cos(angle),
						70 * Math.Sin(angle)
					);
					tangent = new CadPoint3(0.75, -Math.Cos(angle), -Math.Sin(angle));
					normal = new CadPoint3(0, -Math.Sin(angle), Math.Cos(angle));
					break;
				case CollectorLayoutPreset.Staggered:
					origin = new CadPoint3(
						inletX + (index % 2) * 25,
						centered * 55,
						(index % 2 == 0 ? -1 : 1) * 30
					);
					tangent = new CadPoint3(
						1,
						-centered * 0.12,
						index % 2 == 0 ? 0.25 : -0.25
					);
					normal = new CadPoint3(0, 0, 1);
					break;
				default:
					origin = new CadPoint3(inletX, centered * 55, 0);
					tangent = new CadPoint3(1, -centered * 0.12, 0);
					normal = new CadPoint3(0, 0, 1);
					break;
			}
			system.Inlets[index].LocalFrame = new CadFrame(origin, tangent, normal);
			system.Inlets[index].MergeStation = (index + 1d) / (count + 1d);
		}
	}

	private bool IsRunnerBound(Guid runnerId)
	{
		return stagedCollectors.SelectMany(system => system.Inlets)
			.Any(inlet => inlet.Binding?.RunnerId == runnerId);
	}

	internal static bool ValidateSystem(
		CadCollectorSystem system,
		IReadOnlyDictionary<Guid, RunnerGraph> graphs,
		out string error
	)
	{
		error = null;
		if (system == null
			|| system.Id == Guid.Empty
			|| string.IsNullOrWhiteSpace(system.Name)
			|| System.Text.Encoding.UTF8.GetByteCount(system.Name) > 127
			|| system.GenerationRevision < 0
			|| system.GenerationRevision == long.MaxValue)
		{
			error = "A collector system requires a stable ID, name, and usable generation revision.";
			return false;
		}
		if (system.Inlets == null || system.Inlets.Count < 2)
		{
			error = "A collector system requires at least two inlets.";
			return false;
		}
		if (!double.IsFinite(system.OutletStubLength) || system.OutletStubLength <= 0
			|| !double.IsFinite(system.MergeLength) || system.MergeLength <= 0
			|| !double.IsFinite(system.OverlapLength) || system.OverlapLength <= 0
			|| !double.IsFinite(system.BranchEndHandleLength)
			|| system.BranchEndHandleLength <= 0
			|| !double.IsFinite(system.OutletProfile.OuterDiameterMillimetres)
			|| !double.IsFinite(system.OutletProfile.WallThicknessMillimetres)
			|| system.OutletProfile.OuterDiameterMillimetres <= 0
			|| system.OutletProfile.WallThicknessMillimetres <= 0
			|| system.OutletProfile.WallThicknessMillimetres
				>= system.OutletProfile.OuterRadiusMillimetres)
		{
			error = "Collector dimensions and outlet profile must be finite and positive.";
			return false;
		}
		if (system.Inlets.Any(inlet => inlet == null)
			|| system.Inlets.Any(inlet => inlet.Id == Guid.Empty || string.IsNullOrWhiteSpace(inlet.Name))
			|| system.Inlets.Select(inlet => inlet.Id).Distinct().Count() != system.Inlets.Count)
		{
			error = "Collector inlets require unique stable IDs and names.";
			return false;
		}
		HashSet<Guid> runners = new();
		foreach (CadCollectorInlet inlet in system.Inlets)
		{
			if (!double.IsFinite(inlet.MergeStation)
				|| inlet.MergeStation <= 0
				|| inlet.MergeStation >= 1
				|| !double.IsFinite(inlet.BranchStartHandleLength)
				|| inlet.BranchStartHandleLength <= 0
				|| !double.IsFinite(inlet.ClockingTransitionLength)
				|| inlet.ClockingTransitionLength <= 0)
			{
				error = $"Collector inlet '{inlet.Name}' has invalid dimensions or merge station.";
				return false;
			}
			CadCollectorBinding binding = inlet.Binding;
			if (binding == null
				|| binding.RunnerId == Guid.Empty
				|| binding.TerminalBezierNodeId == Guid.Empty
				|| !runners.Add(binding.RunnerId)
				|| !graphs.TryGetValue(binding.RunnerId, out RunnerGraph graph))
			{
				error = $"Collector inlet '{inlet.Name}' has a missing or duplicate runner binding.";
				return false;
			}
			RunnerNode terminal = graph.Nodes.SingleOrDefault(
				node => node.Id == binding.TerminalBezierNodeId);
			if (terminal == null || terminal.DefinitionId != RunnerNodes.CubicBezier)
			{
				error = $"Collector inlet '{inlet.Name}' references a missing terminal Bézier.";
				return false;
			}
			if (!graph.TryValidate(out error))
			{
				return false;
			}
			if (!ValidateTerminalPath(graph, binding, inlet.Name, out error))
			{
				return false;
			}
		}
		return true;
	}

	internal static bool ValidateTerminalPath(
		RunnerGraph graph,
		CadCollectorBinding binding,
		string inletName,
		out string error
	)
	{
		RunnerNode[] outputs = graph.Nodes.Where(
			node => node.DefinitionId == RunnerNodes.RunnerOutput).ToArray();
		if (outputs.Length != 1)
		{
			error = $"Collector inlet '{inletName}' requires exactly one Runner Output.";
			return false;
		}
		RunnerNode output = outputs[0];

		Guid expectedSourceId = binding.TerminalBezierNodeId;
		if (binding.ClockingTransitionNodeId.HasValue)
		{
			RunnerNode clocking = graph.Nodes.SingleOrDefault(
				node => node.Id == binding.ClockingTransitionNodeId.Value);
			if (clocking == null || clocking.DefinitionId != RunnerNodes.ClockingTransition)
			{
				error = $"Collector inlet '{inletName}' references a missing clocking transition.";
				return false;
			}
			if (!graph.Connections.Any(connection =>
				connection.OutputNodeId == binding.TerminalBezierNodeId
					&& connection.OutputPort == "runner"
					&& connection.InputNodeId == clocking.Id
					&& connection.InputPort == "runner"))
			{
				error = $"Collector inlet '{inletName}' terminal Bézier no longer feeds its clocking transition.";
				return false;
			}
			expectedSourceId = clocking.Id;
		}

		if (!graph.Connections.Any(connection =>
			connection.OutputNodeId == expectedSourceId
				&& connection.OutputPort == "runner"
				&& connection.InputNodeId == output.Id
				&& connection.InputPort == "runner"))
		{
			error = $"Collector inlet '{inletName}' terminal feature no longer feeds Runner Output.";
			return false;
		}

		error = null;
		return true;
	}
}
