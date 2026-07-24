using FishGfx.Cad;
using Xunit;

namespace FishGfx.ManifoldCad.Tests;

public sealed class CollectorSystemTests
{
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
	public void VersionThreePersistenceRetainsFramesBindingsAndStableIds()
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
}
