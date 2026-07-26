using FishGfx.Cad;

namespace FishGfx.ManifoldCad;

internal sealed partial class ManifoldCadApplication
{
	private sealed record ExactArtifactSnapshot(long Revision, string DependencyHash);

	private sealed record ExactProjectSnapshot(
		long ProjectEpoch,
		IReadOnlyDictionary<Guid, ExactArtifactSnapshot> Runners,
		IReadOnlyDictionary<Guid, ExactArtifactSnapshot> Collectors
	);

	private ExactProjectSnapshot CaptureExactProjectSnapshot()
	{
		return new ExactProjectSnapshot(
			Interlocked.Read(ref projectEpoch),
			project.Runners.ToDictionary(
				runner => runner.Id,
				runner => new ExactArtifactSnapshot(
					runner.EditRevision,
					CadGeometryDependencyHash.Runner(project, runner)
				)
			),
			project.CollectorSystems.ToDictionary(
				system => system.Id,
				system => new ExactArtifactSnapshot(
					system.GenerationRevision,
					CadGeometryDependencyHash.Collector(project, system)
				)
			)
		);
	}

	private void RequestExactRebuild()
	{
		if (project.Runners.Count == 0)
		{
			ui.SetStatus("There are no runners to rebuild.");
			return;
		}

		ExactProjectSnapshot snapshot = CaptureExactProjectSnapshot();
		QueueExactBuilds(snapshot, true);
		ui.SetStatus("Exact rebuild requested; edits continue to use the fast preview path.");
	}

	private void EnsureExactSnapshotCurrent()
	{
		ExactProjectSnapshot snapshot = CaptureExactProjectSnapshot();
		QueueExactBuilds(snapshot, false);
		WaitForRegeneration();
		ValidateExactSnapshot(snapshot);
	}

	private void QueueExactBuilds(ExactProjectSnapshot snapshot, bool force)
	{
		HashSet<Guid> queuedCollectors = new();
		foreach (CadRunner runner in project.Runners)
		{
			CadCollectorSystem collector = project.CollectorSystems.FirstOrDefault(system =>
				system.Inlets.Any(inlet => inlet.Binding?.RunnerId == runner.Id));
			if (collector != null)
			{
				if (!queuedCollectors.Add(collector.Id))
				{
					continue;
				}
				ExactArtifactSnapshot expected = snapshot.Collectors[collector.Id];
				CadExactBuildSnapshot current = collector.ExactBuild.Snapshot;
				if (force || !IsPublished(current, expected))
				{
					QueueCollectorRegeneration(collector, true);
				}
				continue;
			}

			ExactArtifactSnapshot runnerExpected = snapshot.Runners[runner.Id];
			if (force || !IsPublished(runner.ExactBuild.Snapshot, runnerExpected))
			{
				QueueRunnerRegeneration(runner, true);
			}
		}
	}

	private void ValidateExactSnapshot(ExactProjectSnapshot snapshot)
	{
		if (snapshot.ProjectEpoch != Interlocked.Read(ref projectEpoch))
		{
			throw new InvalidOperationException(
				"The project changed while its exact save/export snapshot was being built."
			);
		}

		foreach (CadRunner runner in project.Runners)
		{
			if (!snapshot.Runners.TryGetValue(runner.Id, out ExactArtifactSnapshot expected)
				|| runner.EditRevision != expected.Revision
				|| !string.Equals(
					CadGeometryDependencyHash.Runner(project, runner),
					expected.DependencyHash,
					StringComparison.OrdinalIgnoreCase
				)
				|| !IsPublished(runner.ExactBuild.Snapshot, expected))
			{
				throw new InvalidOperationException(
					$"Runner '{runner.Name}' does not have current exact geometry."
				);
			}
		}

		foreach (CadCollectorSystem system in project.CollectorSystems)
		{
			if (!snapshot.Collectors.TryGetValue(system.Id, out ExactArtifactSnapshot expected)
				|| system.GenerationRevision != expected.Revision
				|| !string.Equals(
					CadGeometryDependencyHash.Collector(project, system),
					expected.DependencyHash,
					StringComparison.OrdinalIgnoreCase
				)
				|| !IsPublished(system.ExactBuild.Snapshot, expected))
			{
				throw new InvalidOperationException(
					$"Collector '{system.Name}' does not have current exact geometry."
				);
			}
		}
	}

	private static bool IsPublished(
		CadExactBuildSnapshot current,
		ExactArtifactSnapshot expected
	)
	{
		return current.Status == CadExactBuildStatus.Current
			&& current.PublishedRevision == expected.Revision
			&& string.Equals(
				current.PublishedDependencyHash,
				expected.DependencyHash,
				StringComparison.OrdinalIgnoreCase
			);
	}
}
