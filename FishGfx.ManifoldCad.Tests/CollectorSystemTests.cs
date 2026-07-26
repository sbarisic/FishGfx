using System.Text.Json.Nodes;
using FishGfx.Cad;
using Xunit;

namespace FishGfx.ManifoldCad.Tests;

public sealed class CollectorSystemTests
{
	[Fact]
	public void CircularPresetDistributesFourInletsOnOneSymmetricRing()
	{
		(ManifoldProject project, IReadOnlyList<CadRunner> runners) = CreateFourRunnerProject();

		Assert.True(project.TryCreateCollectorSystem(
			runners.Select(runner => runner.Id),
			CollectorLayoutPreset.Radial,
			"Circular four into one",
			out CadCollectorSystem system,
			out string error
		), error);

		double[] radii = system.Inlets.Select(inlet =>
			Math.Sqrt(
				inlet.LocalFrame.Origin.Y * inlet.LocalFrame.Origin.Y
				+ inlet.LocalFrame.Origin.Z * inlet.LocalFrame.Origin.Z
			)).ToArray();
		Assert.All(radii, radius => Assert.Equal(radii[0], radius, 9));
		Assert.All(
			system.Inlets,
			inlet => Assert.Equal(system.Inlets[0].LocalFrame.Origin.X, inlet.LocalFrame.Origin.X, 9)
		);
		Assert.All(system.Inlets, inlet => Assert.Equal(0.5, inlet.MergeStation, 9));
		Assert.Equal(0, system.Inlets.Sum(inlet => inlet.LocalFrame.Origin.Y), 9);
		Assert.Equal(0, system.Inlets.Sum(inlet => inlet.LocalFrame.Origin.Z), 9);
		Assert.All(system.Inlets, inlet => Assert.True(
			CadPoint3.Dot(
				inlet.LocalFrame.Tangent,
				(-inlet.LocalFrame.Origin).Normalized()
			) > 1 - 1e-9
		));
	}

	[Fact]
	public async Task CircularPresetProducesKernelValidTerminalRunnerCurves()
	{
		(ManifoldProject project, IReadOnlyList<CadRunner> runners) = CreateFourRunnerProject();
		using CadDocument document = await CadDocument.CreateAsync(
			TestContext.Current.CancellationToken);
		Dictionary<Guid, RunnerEvaluationResult> initialEvaluations = new();
		foreach (CadRunner runner in runners)
		{
			initialEvaluations[runner.Id] = await project.EvaluateRunnerAsync(
				document,
				runner,
				TestContext.Current.CancellationToken);
		}

		Assert.True(project.TryCreateCollectorSystem(
			runners.Select(runner => runner.Id),
			CollectorLayoutPreset.Radial,
			"Circular four into one",
			initialEvaluations,
			out _,
			out string createError
		), createError);

		foreach (CadRunner runner in runners)
		{
			RunnerEvaluationResult result = await project.EvaluateRunnerAsync(
				document,
				runner,
				TestContext.Current.CancellationToken);
			Assert.True(result.Success, $"{runner.Name}: {string.Join(
				Environment.NewLine,
				result.Diagnostics.Select(diagnostic => diagnostic.Message)
			)}");
		}
	}

	[Fact]
	public void CircularCollectorDependencySurvivesArchiveFrameCanonicalization()
	{
		(ManifoldProject project, IReadOnlyList<CadRunner> runners) = CreateFourRunnerProject();
		Assert.True(project.TryCreateCollectorSystem(
			runners.Select(runner => runner.Id),
			CollectorLayoutPreset.Radial,
			"Archive round trip",
			out CadCollectorSystem system,
			out string createError
		), createError);

		foreach (CadRunner runner in runners)
		{
			string runnerHash = CadGeometryDependencyHash.Runner(project, runner);
			runner.ExactBuild.Request(runner.EditRevision, runnerHash);
			Assert.True(runner.ExactBuild.TryBegin(runner.EditRevision, runnerHash));
			Assert.True(runner.ExactBuild.TryPublish(runner.EditRevision, runnerHash));
		}
		string collectorHash = CadGeometryDependencyHash.Collector(project, system);
		system.ExactBuild.Request(system.GenerationRevision, collectorHash);
		Assert.True(system.ExactBuild.TryBegin(system.GenerationRevision, collectorHash));
		Assert.True(system.ExactBuild.TryPublish(system.GenerationRevision, collectorHash));

		string path = Path.Combine(
			Path.GetTempPath(),
			$"fishgfx-collector-frame-{Guid.NewGuid():N}.fgcad"
		);
		try
		{
			CadProjectArchive.Save(path, project, new byte[] { 3, 1, 4, 1, 5, 9 });
			CadProjectPackage loaded = CadProjectArchive.Load(path);

			Assert.True(loaded.ExactGeometryFresh);
			CadCollectorSystem loadedSystem = Assert.Single(loaded.Project.CollectorSystems);
			Assert.Equal(
				collectorHash,
				CadGeometryDependencyHash.Collector(loaded.Project, loadedSystem)
			);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public void CreateCollectorSplicesEveryGraphAndCommitsOneSystemRevision()
	{
		(ManifoldProject project, CadRunner first, CadRunner second) = CreateTwoRunnerProject();
		Guid firstGraphId = first.Graph.Id;
		Guid secondGraphId = second.Graph.Id;

		Assert.True(project.TryCreateCollectorSystem(
			new[] { first.Id, second.Id },
			CollectorLayoutPreset.Row,
			"2 into 1",
			out CadCollectorSystem system,
			out string error
		), error);

		Assert.Equal(1, system.GenerationRevision);
		Assert.Same(system, Assert.Single(project.CollectorSystems));
		Assert.Equal(2, system.Inlets.Count);
		Assert.Equal(firstGraphId, first.Graph.Id);
		Assert.Equal(secondGraphId, second.Graph.Id);
		Assert.All(system.Inlets, inlet => Assert.Equal(0.5, inlet.MergeStation));
		CadPoint3 averageInlet = system.Inlets
			.Select(system.GetWorldInletFrame)
			.Select(frame => frame.Origin)
			.Aggregate(CadPoint3.Zero, (sum, point) => sum + point)
			/ system.Inlets.Count;
		Assert.InRange(
			(system.OutletFrame.Origin - averageInlet).Length,
			1,
			200
		);
		foreach (CadCollectorInlet inlet in system.Inlets)
		{
			CadRunner runner = project.Runners.Single(item => item.Id == inlet.Binding.RunnerId);
			RunnerNode terminal = runner.Graph.Nodes.Single(
				node => node.Id == inlet.Binding.TerminalBezierNodeId);
			Assert.Equal(RunnerNodes.CubicBezier, terminal.DefinitionId);
			Assert.Contains(runner.Graph.Connections, connection =>
				connection.OutputNodeId == terminal.Id
				&& runner.Graph.Nodes.Single(node => node.Id == connection.InputNodeId)
					.DefinitionId == RunnerNodes.RunnerOutput);
		}
	}

	[Fact]
	public void BoundRunnerDependencyIncludesEndpointButNotRevisionOnlyChanges()
	{
		(ManifoldProject project, CadRunner first, CadRunner second) = CreateTwoRunnerProject();
		Assert.True(project.TryCreateCollectorSystem(
			new[] { first.Id, second.Id },
			CollectorLayoutPreset.Row,
			"Dependency fixture",
			out CadCollectorSystem system,
			out string createError
		), createError);
		string initialRunnerHash = CadGeometryDependencyHash.Runner(project, first);
		string initialCollectorHash = CadGeometryDependencyHash.Collector(project, system);

		system.CommitEdit();
		Assert.Equal(initialRunnerHash, CadGeometryDependencyHash.Runner(project, first));
		Assert.Equal(initialCollectorHash, CadGeometryDependencyHash.Collector(project, system));

		CadCollectorInlet inlet = system.Inlets.Single(item =>
			item.Binding.RunnerId == first.Id);
		CadFrame frame = inlet.LocalFrame;
		inlet.LocalFrame = new CadFrame(
			frame.Origin + new CadPoint3(0, 2.5, -1.25),
			frame.Tangent,
			frame.Normal
		);
		system.CommitEdit();

		Assert.NotEqual(initialRunnerHash, CadGeometryDependencyHash.Runner(project, first));
		Assert.NotEqual(initialCollectorHash, CadGeometryDependencyHash.Collector(project, system));
	}

	[Fact]
	public void BranchOnlyCollectorDependencyIgnoresLegacyOutletBodyParameters()
	{
		(ManifoldProject project, CadRunner first, CadRunner second) = CreateTwoRunnerProject();
		Assert.True(project.TryCreateCollectorSystem(
			new[] { first.Id, second.Id },
			CollectorLayoutPreset.Row,
			"Branch-only dependency fixture",
			out CadCollectorSystem system,
			out string createError
		), createError);
		string initialHash = CadGeometryDependencyHash.Collector(project, system);

		system.OutletStubLength += 10;
		system.MergeLength += 20;
		system.OverlapLength += 5;
		system.OutletProfile = new PipeProfile(76, 2.5);

		Assert.Equal(initialHash, CadGeometryDependencyHash.Collector(project, system));

		system.BranchEndHandleLength += 1;
		Assert.NotEqual(initialHash, CadGeometryDependencyHash.Collector(project, system));
	}

	[Fact]
	public void MovingOrRotatingOutletPreservesEveryWorldInletConstraint()
	{
		(ManifoldProject project, IReadOnlyList<CadRunner> runners) = CreateFourRunnerProject();
		Assert.True(project.TryCreateCollectorSystem(
			runners.Select(runner => runner.Id),
			CollectorLayoutPreset.Radial,
			"Movable outlet",
			out CadCollectorSystem system,
			out string createError
		), createError);
		Dictionary<Guid, CadFrame> originalWorldFrames = system.Inlets.ToDictionary(
			inlet => inlet.Id,
			system.GetWorldInletFrame
		);
		Dictionary<Guid, CadPoint3[]> originalControls = system.Inlets.ToDictionary(
			inlet => inlet.Id,
			inlet => BranchControlPoints(system, inlet)
		);
		CadQuaternion rotation = CadQuaternion.FromEulerDegrees(new CadPoint3(23, -41, 67));
		CadFrame movedOutlet = new(
			system.OutletFrame.Origin + new CadPoint3(35, -18, 24),
			rotation.Rotate(new CadPoint3(1, 0, 0)),
			rotation.Rotate(new CadPoint3(0, 1, 0))
		);

		system.SetOutletFramePreservingWorldInlets(movedOutlet);

		Assert.Equal(movedOutlet, system.OutletFrame);
		foreach (CadCollectorInlet inlet in system.Inlets)
		{
			CadFrame worldInlet = system.GetWorldInletFrame(inlet);
			AssertFrameEqual(originalWorldFrames[inlet.Id], worldInlet);
			AssertBranchMatchesFrames(system, inlet, worldInlet);
		}
		Assert.Contains(system.Inlets, inlet => !originalControls[inlet.Id]
			.SequenceEqual(BranchControlPoints(system, inlet)));
	}

	[Fact]
	public void FailedProjectTransactionPreservesEveryOriginalGraphAndRevision()
	{
		(ManifoldProject project, CadRunner first, _) = CreateTwoRunnerProject();
		string before = RunnerGraphJson.Serialize(first.Graph);
		CollectorSystemTransaction transaction = CollectorSystemTransaction.Begin(project);

		Assert.False(transaction.TryCreate(
			new[] { first.Id },
			CollectorLayoutPreset.Radial,
			"invalid",
			null,
			out _,
			out _
		));
		Assert.True(transaction.Commit(out string commitError), commitError);
		Assert.Empty(project.CollectorSystems);
		Assert.Equal(before, RunnerGraphJson.Serialize(first.Graph));
		Assert.Equal(0, first.EditRevision);
	}

	[Fact]
	public void BoundTerminalBezierUsesFullNativeEndpointConstraint()
	{
		(ManifoldProject project, CadRunner first, CadRunner second) = CreateTwoRunnerProject();
		Assert.True(project.TryCreateCollectorSystem(
			new[] { first.Id, second.Id },
			CollectorLayoutPreset.Staggered,
			null,
			out CadCollectorSystem system,
			out string error
		), error);
		CadCollectorInlet inlet = system.Inlets.Single(item => item.Binding.RunnerId == first.Id);
		RunnerEndpointConstraint constraint = project.GetEndpointConstraint(first).Value;
		RunnerGraphPlan plan = RunnerGraphPlanner.Plan(
			first,
			project.Mates.ToDictionary(item => item.Id),
			project.Parts.ToDictionary(item => item.Id),
			constraint
		);

		Assert.True(plan.Success, string.Join(Environment.NewLine, plan.Diagnostics.Select(item => item.Message)));
		Assert.Equal(system.GenerationStamp, plan.GenerationStamp);
		RunnerFeatureSpec terminal = plan.Features.Single(
			feature => feature.NodeId == inlet.Binding.TerminalBezierNodeId);
		Assert.Equal(constraint.BezierEndFrame, terminal.ConstrainedEndFrame);
		Assert.Equal(constraint.EndHandleLength, terminal.EndHandleLengthMillimetres);
	}

	[Fact]
	public void VersionFivePersistenceRetainsFramesBindingsPathsAndStableIds()
	{
		(ManifoldProject project, CadRunner first, CadRunner second) = CreateTwoRunnerProject();
		Assert.True(project.TryCreateCollectorSystem(
			new[] { first.Id, second.Id },
			CollectorLayoutPreset.Radial,
			"Radial collector",
			out CadCollectorSystem system,
			out string error
		), error);
		string json = RunnerCollectionJson.Serialize(project);
		RunnerCollectionLoadResult loaded = RunnerCollectionJson.Deserialize(json);

		Assert.True(loaded.Success, string.Join(Environment.NewLine, loaded.Errors));
		CadCollectorSystem restored = Assert.Single(loaded.CollectorSystems);
		Assert.Equal(system.Id, restored.Id);
		Assert.Equal(system.GenerationRevision, restored.GenerationRevision);
		Assert.Equal(system.OutletFrame, restored.OutletFrame);
		Assert.Equal(
			system.Inlets.Select(inlet => inlet.Id),
			restored.Inlets.Select(inlet => inlet.Id));
		Assert.Equal(
			system.Inlets.Select(inlet => inlet.Binding.RunnerId),
			restored.Inlets.Select(inlet => inlet.Binding.RunnerId));
		for (int index = 0; index < system.Inlets.Count; ++index)
		{
			AssertBranchPathEqual(
				system.Inlets[index].BranchPath,
				restored.Inlets[index].BranchPath
			);
		}
	}

	[Fact]
	public void VersionFourPersistenceRecalculatesMissingCollectorBranchPaths()
	{
		(ManifoldProject project, CadRunner first, CadRunner second) = CreateTwoRunnerProject();
		Assert.True(project.TryCreateCollectorSystem(
			new[] { first.Id, second.Id },
			CollectorLayoutPreset.Row,
			"Legacy collector",
			out CadCollectorSystem system,
			out string createError
		), createError);
		JsonObject root = JsonNode.Parse(RunnerCollectionJson.Serialize(project))!.AsObject();
		root["version"] = 4;
		foreach (JsonNode inletNode in root["collectorSystems"]![0]!["inlets"]!.AsArray())
		{
			JsonObject inlet = inletNode!.AsObject();
			inlet.Remove("branchOuterRadiusMillimetres");
			inlet.Remove("branchPath");
		}

		RunnerCollectionLoadResult loaded = RunnerCollectionJson.Deserialize(root.ToJsonString());

		Assert.True(loaded.Success, string.Join(Environment.NewLine, loaded.Errors));
		CadCollectorSystem restored = Assert.Single(loaded.CollectorSystems);
		Assert.Equal(system.Inlets.Count, restored.Inlets.Count);
		foreach (CadCollectorInlet inlet in restored.Inlets)
		{
			Assert.Equal(21.2, inlet.BranchOuterRadiusMillimetres, 9);
			AssertBranchMatchesFrames(restored, inlet, restored.GetWorldInletFrame(inlet));
		}
	}

	[Fact]
	public void ArchiveClearsSavedInletThatDoesNotBelongToActiveCollector()
	{
		(ManifoldProject project, CadRunner first, CadRunner second) = CreateTwoRunnerProject();
		Assert.True(project.TryCreateCollectorSystem(
			new[] { first.Id, second.Id },
			CollectorLayoutPreset.Row,
			null,
			out CadCollectorSystem system,
			out string createError
		), createError);
		project.View.ActiveCollectorSystemId = system.Id;
		project.View.ActiveCollectorInletId = Guid.NewGuid();
		string path = Path.Combine(
			Path.GetTempPath(),
			$"fishgfx-collector-view-{Guid.NewGuid():N}.fgcad");
		try
		{
			CadProjectArchive.Save(path, project, new byte[] { 1 });
			CadProjectPackage loaded = CadProjectArchive.Load(path);

			Assert.Equal(system.Id, loaded.Project.View.ActiveCollectorSystemId);
			Assert.Null(loaded.Project.View.ActiveCollectorInletId);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public void OneRunnerCannotBindToTwoSystems()
	{
		(ManifoldProject project, CadRunner first, CadRunner second) = CreateTwoRunnerProject();
		Assert.True(project.TryCreateCollectorSystem(
			new[] { first.Id, second.Id },
			CollectorLayoutPreset.Row,
			null,
			out _,
			out string error
		), error);
		CollectorSystemTransaction transaction = CollectorSystemTransaction.Begin(project);
		Assert.False(transaction.TryCreate(
			new[] { first.Id, second.Id },
			CollectorLayoutPreset.Row,
			"duplicate",
			null,
			out _,
			out string duplicateError
		));
		Assert.Contains("only one", duplicateError, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void RewiredTerminalBindingInvalidatesTransactionAndNativePlan()
	{
		(ManifoldProject project, CadRunner first, CadRunner second) = CreateTwoRunnerProject();
		Assert.True(project.TryCreateCollectorSystem(
			new[] { first.Id, second.Id },
			CollectorLayoutPreset.Row,
			null,
			out CadCollectorSystem system,
			out string createError
		), createError);
		CadCollectorInlet inlet = system.Inlets.Single(item => item.Binding.RunnerId == first.Id);
		RunnerNode output = first.Graph.Nodes.Single(
			node => node.DefinitionId == RunnerNodes.RunnerOutput);
		RunnerConnection terminalWire = first.Graph.Connections.Single(connection =>
			connection.OutputNodeId == inlet.Binding.TerminalBezierNodeId
				&& connection.InputNodeId == output.Id);
		RunnerConnection terminalInput = first.Graph.Connections.Single(connection =>
			connection.InputNodeId == inlet.Binding.TerminalBezierNodeId);
		Assert.True(first.Graph.RemoveConnection(terminalWire.Id));
		Assert.True(first.Graph.TryConnect(
			terminalInput.OutputNodeId,
			terminalInput.OutputPort,
			output.Id,
			"runner",
			out _,
			out string connectError
		), connectError);

		CollectorSystemTransaction transaction = CollectorSystemTransaction.Begin(project);
		Assert.False(transaction.TryValidate(out string validationError));
		Assert.Contains("terminal", validationError, StringComparison.OrdinalIgnoreCase);

		RunnerGraphPlan plan = RunnerGraphPlanner.Plan(
			first,
			project.Mates.ToDictionary(item => item.Id),
			project.Parts.ToDictionary(item => item.Id),
			project.GetEndpointConstraint(first)
		);
		Assert.False(plan.Success);
		Assert.Contains(plan.Diagnostics, diagnostic => diagnostic.Code == "COL102");
	}

	[Fact]
	public void GenericUpdateCannotRewriteStableInletBindings()
	{
		(ManifoldProject project, CadRunner first, CadRunner second) = CreateTwoRunnerProject();
		Assert.True(project.TryCreateCollectorSystem(
			new[] { first.Id, second.Id },
			CollectorLayoutPreset.Row,
			null,
			out CadCollectorSystem system,
			out string createError
		), createError);
		long revision = system.GenerationRevision;
		Guid inletId = system.Inlets[0].Id;
		CollectorSystemTransaction transaction = CollectorSystemTransaction.Begin(project);

		Assert.False(transaction.TryUpdate(
			system.Id,
			candidate => candidate.Inlets[0].Id = Guid.NewGuid(),
			out string updateError
		));
		Assert.Contains("stable", updateError, StringComparison.OrdinalIgnoreCase);
		Assert.True(transaction.Commit(out string commitError), commitError);
		Assert.Equal(revision, project.CollectorSystems[0].GenerationRevision);
		Assert.Equal(inletId, project.CollectorSystems[0].Inlets[0].Id);

		CollectorSystemTransaction revisionTransaction = CollectorSystemTransaction.Begin(project);
		Assert.False(revisionTransaction.TryUpdate(
			system.Id,
			candidate => candidate.CommitEdit(),
			out string revisionError
		));
		Assert.Contains("revision", revisionError, StringComparison.OrdinalIgnoreCase);
		Assert.True(revisionTransaction.Commit(out string revisionCommitError), revisionCommitError);
		Assert.Equal(revision, project.CollectorSystems[0].GenerationRevision);
	}

	[Fact]
	public void RenamePreservesGenerationRevisionAndStableBindings()
	{
		(ManifoldProject project, CadRunner first, CadRunner second) = CreateTwoRunnerProject();
		Assert.True(project.TryCreateCollectorSystem(
			new[] { first.Id, second.Id },
			CollectorLayoutPreset.Row,
			null,
			out CadCollectorSystem system,
			out string createError
		), createError);
		long revision = system.GenerationRevision;
		var bindings = system.Inlets
			.Select(inlet => (
				inlet.Id,
				inlet.Binding.RunnerId,
				inlet.Binding.TerminalBezierNodeId,
				inlet.Binding.ClockingTransitionNodeId
			))
			.ToArray();

		CollectorSystemTransaction transaction = CollectorSystemTransaction.Begin(project);
		Assert.True(
			transaction.TryRename(system.Id, "Merge: left, right", out string renameError),
			renameError
		);
		Assert.True(transaction.Commit(out string commitError), commitError);

		CadCollectorSystem renamed = Assert.Single(project.CollectorSystems);
		Assert.Equal("Merge: left, right", renamed.Name);
		Assert.Equal(revision, renamed.GenerationRevision);
		Assert.Equal(
			bindings,
			renamed.Inlets.Select(inlet => (
				inlet.Id,
				inlet.Binding.RunnerId,
				inlet.Binding.TerminalBezierNodeId,
				inlet.Binding.ClockingTransitionNodeId
			))
		);
	}

	[Fact]
	public void RemovingMemberPartRemovesDanglingCollectorAndRevisionsSurvivor()
	{
		(ManifoldProject project, CadRunner first, CadRunner second) = CreateTwoRunnerProject();
		Assert.True(project.TryCreateCollectorSystem(
			new[] { first.Id, second.Id },
			CollectorLayoutPreset.Row,
			null,
			out CadCollectorSystem system,
			out string createError
		), createError);
		project.SetActiveCollector(system.Id, system.Inlets[0].Id);
		Guid firstPartId = project.Mates.Single(mate => mate.Id == first.StartMateId).PartId;

		Assert.True(project.RemovePart(firstPartId));

		Assert.Empty(project.CollectorSystems);
		Assert.DoesNotContain(project.Runners, runner => runner.Id == first.Id);
		Assert.Contains(project.Runners, runner => runner.Id == second.Id);
		Assert.Equal(1, second.EditRevision);
		Assert.Null(project.View.ActiveCollectorSystemId);
		Assert.Null(project.View.ActiveCollectorInletId);
	}

	[Fact]
	public async Task DeleteBakesAuthoritativeConstrainedBezierAndDetachesEveryRunner()
	{
		(ManifoldProject project, CadRunner first, CadRunner second) = CreateTwoRunnerProject();
		using CadDocument document = await CadDocument.CreateAsync(
			TestContext.Current.CancellationToken);
		Dictionary<Guid, RunnerEvaluationResult> evaluations = new();
		foreach (CadRunner runner in project.Runners)
		{
			evaluations[runner.Id] = await project.EvaluateRunnerAsync(
				document,
				runner,
				TestContext.Current.CancellationToken
			);
		}
		Assert.True(project.TryCreateCollectorSystem(
			new[] { first.Id, second.Id },
			CollectorLayoutPreset.Row,
			null,
			evaluations,
			out CadCollectorSystem system,
			out string createError
		), createError);
		evaluations.Clear();
		foreach (CadRunner runner in project.Runners)
		{
			RunnerEvaluationResult result = await project.EvaluateRunnerAsync(
				document,
				runner,
				TestContext.Current.CancellationToken
			);
			Assert.True(result.Success, string.Join(
				Environment.NewLine,
				result.Diagnostics.Select(item => item.Message)
			));
			evaluations[runner.Id] = result;
		}

		CadCollectorInlet firstInlet = system.Inlets.Single(
			inlet => inlet.Binding.RunnerId == first.Id);
		RunnerFeature constrained = evaluations[first.Id].Chain.Features.Single(
			feature => feature.NodeId == firstInlet.Binding.TerminalBezierNodeId);
		CadPoint3 expectedEnd = constrained.EntryFrame.InverseTransformPoint(
			constrained.ExitFrame.Origin);

		Assert.True(project.TryDeleteCollectorSystem(
			system.Id,
			evaluations,
			out string deleteError
		), deleteError);
		Assert.Empty(project.CollectorSystems);
		Assert.Equal(1, first.EditRevision);
		Assert.Equal(1, second.EditRevision);
		RunnerNode detachedBezier = first.Graph.Nodes.Single(
			node => node.Id == firstInlet.Binding.TerminalBezierNodeId);
		Assert.Equal(expectedEnd.X, double.Parse(
			detachedBezier.Properties["endT"],
			System.Globalization.CultureInfo.InvariantCulture
		), 9);
		Assert.Equal(expectedEnd.Y, double.Parse(
			detachedBezier.Properties["endU"],
			System.Globalization.CultureInfo.InvariantCulture
		), 9);
		Assert.Equal(expectedEnd.Z, double.Parse(
			detachedBezier.Properties["endV"],
			System.Globalization.CultureInfo.InvariantCulture
		), 9);
	}

	private static void AssertFrameEqual(CadFrame expected, CadFrame actual)
	{
		AssertPointEqual(expected.Origin, actual.Origin);
		AssertPointEqual(expected.Tangent, actual.Tangent);
		AssertPointEqual(expected.Normal, actual.Normal);
	}

	private static void AssertPointEqual(CadPoint3 expected, CadPoint3 actual)
	{
		Assert.Equal(expected.X, actual.X, 9);
		Assert.Equal(expected.Y, actual.Y, 9);
		Assert.Equal(expected.Z, actual.Z, 9);
	}

	private static void AssertBranchMatchesFrames(
		CadCollectorSystem system,
		CadCollectorInlet inlet,
		CadFrame worldInlet
	)
	{
		CadCollectorBranchPath path = Assert.IsType<CadCollectorBranchPath>(inlet.BranchPath);
		Assert.True(path.IsFeasible, path.Diagnostic);
		Assert.True(
			CadCollectorBranchSolver.ValidatePath(
				path,
				system.OutletFrame,
				worldInlet,
				out string validationError
			),
			validationError
		);
		IReadOnlyList<CadCollectorWorldBranchSpan> spans =
			CadCollectorBranchSolver.ToWorldSpans(
				path,
				system.OutletFrame,
				worldInlet.Origin
			);
		Assert.InRange(spans.Count, 1, 2);
		Assert.True((spans[0].Control1 - spans[0].Start).Length > 0);
		Assert.True((spans[^1].End - spans[^1].Control2).Length > 0);
		AssertPointEqual(system.OutletFrame.Origin, spans[^1].End);
	}

	private static CadPoint3[] BranchControlPoints(
		CadCollectorSystem system,
		CadCollectorInlet inlet
	)
	{
		return CadCollectorBranchSolver.ToWorldSpans(
			inlet.BranchPath,
			system.OutletFrame,
			system.GetWorldInletFrame(inlet).Origin
		).SelectMany(span => new[] { span.Control1, span.Control2, span.End }).ToArray();
	}

	private static void AssertBranchPathEqual(
		CadCollectorBranchPath expected,
		CadCollectorBranchPath actual
	)
	{
		Assert.NotNull(expected);
		Assert.NotNull(actual);
		Assert.Equal(expected.SolverVersion, actual.SolverVersion);
		Assert.Equal(expected.OuterRadiusMillimetres, actual.OuterRadiusMillimetres, 9);
		Assert.Equal(expected.IsFeasible, actual.IsFeasible);
		Assert.Equal(expected.MinimumRadiusMillimetres, actual.MinimumRadiusMillimetres, 9);
		Assert.Equal(expected.Diagnostic, actual.Diagnostic);
		Assert.Equal(expected.Spans.Count, actual.Spans.Count);
		for (int index = 0; index < expected.Spans.Count; ++index)
		{
			AssertPointEqual(expected.Spans[index].Control1Local, actual.Spans[index].Control1Local);
			AssertPointEqual(expected.Spans[index].Control2Local, actual.Spans[index].Control2Local);
			AssertPointEqual(expected.Spans[index].EndLocal, actual.Spans[index].EndLocal);
		}
	}

	private static (ManifoldProject Project, CadRunner First, CadRunner Second)
		CreateTwoRunnerProject()
	{
		(ManifoldProject project, _, CadRunner first) = RunnerGraphTests.CreateProject();
		CadPart part = project.AddPart("Second flange");
		CadMate mate = project.AddMate(part.Id, "Cylinder 2");
		mate.Rebind(
			new CadTopologyRef(part.Id, 17, CadTopologyKind.CircularEdge),
			new CadFrame(
				new CadPoint3(0, 60, 0),
				new CadPoint3(1, 0, 0),
				new CadPoint3(0, 1, 0)
			),
			21.2
		);
		CadRunner second = project.AddRunner(mate.Id);
		return (project, first, second);
	}

	private static (ManifoldProject Project, IReadOnlyList<CadRunner> Runners)
		CreateFourRunnerProject()
	{
		(ManifoldProject project, _, CadRunner first) = RunnerGraphTests.CreateProject();
		List<CadRunner> runners = [first];
		for (int index = 1; index < 4; index++)
		{
			CadPart part = project.AddPart($"Flange {index + 1}");
			CadMate mate = project.AddMate(part.Id, $"Cylinder {index + 1}");
			mate.Rebind(
				new CadTopologyRef(part.Id, (ulong)(20 + index), CadTopologyKind.CircularEdge),
				new CadFrame(
					new CadPoint3(0, index * 60, 0),
					new CadPoint3(1, 0, 0),
					new CadPoint3(0, 1, 0)
				),
				21.2
			);
			runners.Add(project.AddRunner(mate.Id));
		}
		return (project, runners);
	}
}
