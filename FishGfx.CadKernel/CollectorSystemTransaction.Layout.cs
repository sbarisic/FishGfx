namespace FishGfx.Cad;

public sealed partial class CollectorSystemTransaction
{
	private static void ResolveCollectorBranchPaths(
		CadCollectorSystem system,
		IReadOnlyDictionary<Guid, RunnerEvaluationResult> authoritativeEvaluations
	)
	{
		foreach (CadCollectorInlet inlet in system.Inlets)
		{
			if (inlet.Binding != null
				&& authoritativeEvaluations?.TryGetValue(
					inlet.Binding.RunnerId,
					out RunnerEvaluationResult evaluation
				) == true
				&& evaluation.Success)
			{
				inlet.BranchOuterRadiusMillimetres = evaluation.Chain.ActiveProfile
					.ApproximateOuterRadiusMillimetres;
			}
		}
		system.RecalculateBranchPaths();
	}

	private void SeedTerminalHandles(
		CadCollectorSystem system,
		IReadOnlyDictionary<Guid, RunnerEvaluationResult> authoritativeEvaluations,
		bool ensureCurvatureClearance = false
	)
	{
		if (authoritativeEvaluations == null)
		{
			return;
		}
		for (int attempt = 0; ensureCurvatureClearance && attempt < 12; attempt++)
		{
			bool hasInsufficientClearance = false;
			foreach (CadCollectorInlet inlet in system.Inlets)
			{
				if (!TryGetTerminalFrames(
					system,
					inlet,
					authoritativeEvaluations,
					out CadFrame entryFrame,
					out CadFrame target,
					out RunnerSectionProfile profile
				))
				{
					continue;
				}
				var handleSeed = SeedBezierHandles(entryFrame, target);
				double minimumRadius = handleSeed.MinimumRadius;
				if (minimumRadius < profile.ApproximateOuterRadiusMillimetres * 1.5)
				{
					hasInsufficientClearance = true;
					break;
				}
			}
			if (!hasInsufficientClearance)
			{
				break;
			}
			system.OutletFrame = new CadFrame(
				system.OutletFrame.Origin + system.OutletFrame.Tangent * 25,
				system.OutletFrame.Tangent,
				system.OutletFrame.Normal
			);
		}
		foreach (CadCollectorInlet inlet in system.Inlets)
		{
			if (!TryGetTerminalFrames(
				system,
				inlet,
				authoritativeEvaluations,
				out CadFrame entryFrame,
				out CadFrame target,
				out _
			))
			{
				continue;
			}
			var handleSeed = SeedBezierHandles(entryFrame, target);
			double startHandle = handleSeed.Start;
			double endHandle = handleSeed.End;
			RunnerNode terminal = stagedGraphs[inlet.Binding.RunnerId].Nodes.Single(
				node => node.Id == inlet.Binding.TerminalBezierNodeId);
			terminal.Properties["startHandleLength"] =
				FormattableString.Invariant($"{startHandle:R}");
			terminal.Properties["endHandleLength"] =
				FormattableString.Invariant($"{endHandle:R}");
		}
	}

	private static bool TryGetTerminalFrames(
		CadCollectorSystem system,
		CadCollectorInlet inlet,
		IReadOnlyDictionary<Guid, RunnerEvaluationResult> authoritativeEvaluations,
		out CadFrame entry,
		out CadFrame target,
		out RunnerSectionProfile profile
	)
	{
		CadCollectorBinding binding = inlet.Binding;
		if (!authoritativeEvaluations.TryGetValue(
				binding.RunnerId,
				out RunnerEvaluationResult evaluation
			)
			|| !evaluation.Success)
		{
			entry = default;
			target = default;
			profile = null;
			return false;
		}
		target = system.GetWorldInletFrame(inlet);
		if (binding.ClockingTransitionNodeId.HasValue)
		{
			target = new CadFrame(
				target.Origin - target.Tangent * inlet.ClockingTransitionLength,
				target.Tangent,
				target.Normal
			);
		}
		RunnerFeature existingTerminal = evaluation.Chain.Features.LastOrDefault(
			feature => feature.NodeId == binding.TerminalBezierNodeId);
		entry = existingTerminal == null
			? evaluation.Chain.EndFrame
			: existingTerminal.EntryFrame;
		profile = evaluation.Chain.ActiveProfile;
		return true;
	}

	private static (double Start, double End, double MinimumRadius) SeedBezierHandles(
		CadFrame entry,
		CadFrame target
	)
	{
		double chordLength = (target.Origin - entry.Origin).Length;
		if (!double.IsFinite(chordLength) || chordLength <= 1e-9)
		{
			return (1, 1, 0);
		}

		double bestStart = chordLength / 3;
		double bestEnd = chordLength / 3;
		double bestMinimumRadius = -1;
		for (int startStep = 2; startStep <= 8; startStep++)
		{
			double startHandle = chordLength * startStep / 10;
			for (int endStep = 2; endStep <= 8; endStep++)
			{
				double endHandle = chordLength * endStep / 10;
				double minimumRadius = SampleMinimumBezierRadius(
					entry,
					target,
					startHandle,
					endHandle
				);
				if (minimumRadius > bestMinimumRadius)
				{
					bestMinimumRadius = minimumRadius;
					bestStart = startHandle;
					bestEnd = endHandle;
				}
			}
		}
		double clampedStart = Math.Clamp(bestStart, 1, 160);
		double clampedEnd = Math.Clamp(bestEnd, 1, 160);
		return (
			clampedStart,
			clampedEnd,
			SampleMinimumBezierRadius(entry, target, clampedStart, clampedEnd)
		);
	}

	private static double SampleMinimumBezierRadius(
		CadFrame entry,
		CadFrame target,
		double startHandle,
		double endHandle
	)
	{
		CadPoint3 p0 = entry.Origin;
		CadPoint3 p1 = p0 + entry.Tangent * startHandle;
		CadPoint3 p3 = target.Origin;
		CadPoint3 p2 = p3 - target.Tangent * endHandle;
		double minimumRadius = double.PositiveInfinity;
		const int sampleCount = 64;
		for (int index = 0; index <= sampleCount; index++)
		{
			double t = index / (double)sampleCount;
			double oneMinusT = 1 - t;
			CadPoint3 derivative = 3 * (
				oneMinusT * oneMinusT * (p1 - p0)
				+ 2 * oneMinusT * t * (p2 - p1)
				+ t * t * (p3 - p2)
			);
			CadPoint3 secondDerivative = 6 * (
				oneMinusT * (p2 - 2 * p1 + p0)
				+ t * (p3 - 2 * p2 + p1)
			);
			double speed = derivative.Length;
			if (!double.IsFinite(speed) || speed <= 1e-9)
			{
				return -1;
			}
			double cross = CadPoint3.Cross(derivative, secondDerivative).Length;
			if (cross > 1e-12)
			{
				minimumRadius = Math.Min(
					minimumRadius,
					speed * speed * speed / cross
				);
			}
		}
		return minimumRadius;
	}

	private CadFrame[] ResolveRunnerEndFrames(
		IReadOnlyList<Guid> runnerIds,
		IReadOnlyDictionary<Guid, RunnerEvaluationResult> authoritativeEvaluations
	)
	{
		List<CadFrame> endFrames = new();
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
				endFrames.Add(evaluation.Chain.EndFrame);
				continue;
			}
			CadMate mate = project.Mates.Single(item => item.Id == runner.StartMateId);
			CadPart part = project.Parts.Single(item => item.Id == mate.PartId);
			endFrames.Add(mate.LocalFrame.Value.Transformed(part.Transform));
		}
		return endFrames.ToArray();
	}

	private static CadFrame SeedOutletFrame(
		IReadOnlyList<CadFrame> runnerEndFrames,
		CadCollectorSystem system
	)
	{
		CadPoint3 origin = CadPoint3.Zero;
		CadPoint3 tangent = CadPoint3.Zero;
		CadPoint3 normal = CadPoint3.Zero;
		foreach (CadFrame frame in runnerEndFrames)
		{
			origin += frame.Origin;
			tangent += frame.Tangent;
			normal += frame.Normal;
		}
		origin /= runnerEndFrames.Count;
		if (tangent.LengthSquared <= 1e-12)
		{
			tangent = runnerEndFrames[0].Tangent;
		}
		if (normal.LengthSquared <= 1e-12)
		{
			normal = runnerEndFrames[0].Normal;
		}
		CadFrame orientation = new(CadPoint3.Zero, tangent, normal);
		double furthestAxialOffset = runnerEndFrames.Max(frame =>
			CadPoint3.Dot(frame.Origin - origin, orientation.Tangent));
		double radialExtent = runnerEndFrames.Max(frame =>
		{
			CadPoint3 offset = frame.Origin - origin;
			CadPoint3 radial = offset
				- CadPoint3.Dot(offset, orientation.Tangent) * orientation.Tangent;
			return radial.Length;
		});
		double mergeLead = Math.Max(
			30 + system.BranchEndHandleLength * 1.5,
			Math.Max(
				radialExtent * 0.85,
				30 + system.OutletProfile.OuterDiameterMillimetres
			)
		);
		return new CadFrame(
			origin + orientation.Tangent * (furthestAxialOffset + mergeLead),
			orientation.Tangent,
			orientation.Normal
		);
	}

	private static void ApplyInitialLayout(
		CadCollectorSystem system,
		CollectorLayoutPreset preset,
		IReadOnlyList<CadFrame> runnerEndFrames
	)
	{
		const double runnerConnectionLength = 30;
		for (int index = 0; index < system.Inlets.Count; index++)
		{
			CadFrame source = runnerEndFrames[index];
			CadFrame connected = new(
				source.Origin + source.Tangent * runnerConnectionLength,
				source.Tangent,
				source.Normal
			);
			system.Inlets[index].LocalFrame = connected.RelativeTo(system.OutletFrame);
			system.Inlets[index].MergeStation = system.Inlets.Count == 2
				? 0.5
				: (index + 1d) / (system.Inlets.Count + 1d);
		}

		if (preset != CollectorLayoutPreset.Row)
		{
			ApplyPreset(system, preset);
		}
	}

	private static void ApplyPreset(CadCollectorSystem system, CollectorLayoutPreset preset)
	{
		int count = system.Inlets.Count;
		double minimumInletX = system.BranchEndHandleLength * 1.5;
		double circularRingRadius = CircularRingRadius(system, count);
		double[] circularAngles = Enumerable.Range(0, count)
			.Select(index => 2 * Math.PI * index / count)
			.OrderBy(angle => Math.Cos(angle))
			.ThenBy(angle => Math.Sin(angle))
			.ToArray();
		double inletX = -Math.Max(
			minimumInletX,
			preset == CollectorLayoutPreset.Radial
				? circularRingRadius * 0.75
				: 0
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
					double angle = circularAngles[index];
					origin = new CadPoint3(
						inletX,
						circularRingRadius * Math.Cos(angle),
						circularRingRadius * Math.Sin(angle)
					);
					tangent = -origin;
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
			system.Inlets[index].MergeStation = preset == CollectorLayoutPreset.Radial
				? 0.5
				: count == 2
				? 0.5
				: (index + 1d) / (count + 1d);
			if (preset == CollectorLayoutPreset.Radial)
			{
				double chordLength = origin.Length;
				system.Inlets[index].BranchStartHandleLength = Math.Clamp(
					chordLength / 3,
					10,
					Math.Max(10, chordLength * 0.45)
				);
			}
		}
	}

	private static double CircularRingRadius(CadCollectorSystem system, int inletCount)
	{
		double nominalDiameter = system.OutletProfile.OuterDiameterMillimetres;
		double minimumCentreSpacing = nominalDiameter + Math.Max(
			8,
			system.OutletProfile.WallThicknessMillimetres * 4
		);
		double spacingRadius = inletCount <= 1
			? 0
			: minimumCentreSpacing / (2 * Math.Sin(Math.PI / inletCount));
		return Math.Max(nominalDiameter, spacingRadius);
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
		if (!double.IsFinite(system.BranchEndHandleLength)
			|| system.BranchEndHandleLength <= 0)
		{
			error = "The collector branch end handle must be finite and positive.";
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
				|| !double.IsFinite(inlet.BranchOuterRadiusMillimetres)
				|| inlet.BranchOuterRadiusMillimetres <= 0
				|| !double.IsFinite(inlet.ClockingTransitionLength)
				|| inlet.ClockingTransitionLength <= 0)
			{
				error = $"Collector inlet '{inlet.Name}' has invalid dimensions or merge station.";
				return false;
			}
			CadFrame worldInlet;
			try
			{
				worldInlet = system.GetWorldInletFrame(inlet);
			}
			catch (ArgumentException exception)
			{
				error = $"Collector inlet '{inlet.Name}' has an invalid frame: {exception.Message}";
				return false;
			}
			if (!CadCollectorBranchSolver.ValidatePath(
				inlet.BranchPath,
				system.OutletFrame,
				worldInlet,
				out string pathError
			))
			{
				error = $"Collector inlet '{inlet.Name}' has an invalid branch path: {pathError}";
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
