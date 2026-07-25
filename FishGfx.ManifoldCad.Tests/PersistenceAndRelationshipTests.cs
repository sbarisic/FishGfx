using System.IO.Compression;
using FishGfx.Cad;
using Xunit;

namespace FishGfx.ManifoldCad.Tests;

public sealed class PersistenceAndRelationshipTests
{
	[Fact]
	public void VersionOneGraphMigratesMateOwnershipAndTerminalOutput()
	{
		Guid graphId = Guid.NewGuid();
		Guid mateId = Guid.NewGuid();
		Guid mateNode = Guid.NewGuid();
		Guid startNode = Guid.NewGuid();
		Guid profileNode = Guid.NewGuid();
		Guid straightNode = Guid.NewGuid();
		Guid sweepNode = Guid.NewGuid();
		string json = $$"""
		{
		  "schema": "fishgfx.runner-graph",
		  "version": 1,
		  "id": "{{graphId}}",
		  "nodes": [
		    { "id": "{{mateNode}}", "definitionId": "cad.mate-reference", "x": 0, "y": 0,
		      "properties": { "mateId": "{{mateId}}" } },
		    { "id": "{{startNode}}", "definitionId": "cad.start-runner", "x": 1, "y": 0, "properties": {} },
		    { "id": "{{profileNode}}", "definitionId": "cad.circular-pipe", "x": 1, "y": 1,
		      "properties": { "outerDiameter": "42.4", "wallThickness": "2" } },
		    { "id": "{{straightNode}}", "definitionId": "cad.straight", "x": 2, "y": 0,
		      "properties": { "length": "100" } },
		    { "id": "{{sweepNode}}", "definitionId": "cad.sweep-pipe", "x": 3, "y": 0, "properties": {} }
		  ],
		  "connections": [
		    { "id": "{{Guid.NewGuid()}}", "outputNodeId": "{{mateNode}}", "outputPort": "mate",
		      "inputNodeId": "{{startNode}}", "inputPort": "mate" },
		    { "id": "{{Guid.NewGuid()}}", "outputNodeId": "{{startNode}}", "outputPort": "path",
		      "inputNodeId": "{{straightNode}}", "inputPort": "path" },
		    { "id": "{{Guid.NewGuid()}}", "outputNodeId": "{{straightNode}}", "outputPort": "path",
		      "inputNodeId": "{{sweepNode}}", "inputPort": "path" },
		    { "id": "{{Guid.NewGuid()}}", "outputNodeId": "{{profileNode}}", "outputPort": "profile",
		      "inputNodeId": "{{sweepNode}}", "inputPort": "profile" }
		  ]
		}
		""";

		RunnerCollectionLoadResult result = RunnerCollectionJson.Deserialize(json);
		Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
		CadRunner runner = Assert.Single(result.Runners);
		Assert.Equal(mateId, runner.StartMateId);
		Assert.DoesNotContain(runner.Graph.Nodes, node => node.DefinitionId == "cad.mate-reference");
		Assert.Equal(RunnerNodes.RunnerOutput,
			runner.Graph.Nodes.Single(node => node.Id == sweepNode).DefinitionId);
		Assert.Contains(runner.Graph.Connections, connection => connection.OutputNodeId == profileNode
			&& connection.InputNodeId == startNode && connection.InputPort == "profile");
	}

	[Fact]
	public void ArchiveContainsVersionedExactDocumentAndReopensWithoutStepSource()
	{
		(ManifoldProject project, CadMate mate, CadRunner runner) = RunnerGraphTests.CreateProject();
		project.Parts[0].SourcePath = @"Z:\missing\flange.step";
		project.Parts[0].Transform = new CadTransform(
			new CadPoint3(10, 20, 30),
			CadQuaternion.FromEulerDegrees(new CadPoint3(0, 45, 0))
		);
		project.View.Orthographic = true;
		byte[] exact = { 1, 4, 9, 16, 25 };
		string path = Temporary(".fgcad");

		try
		{
			CadProjectArchive.Save(path, project, exact);
			using (ZipArchive archive = ZipFile.OpenRead(path))
			{
				Assert.Equal(
					new[] { "graph.json", "manifest.json", "model.xbf", "view.json" },
					archive.Entries.Select(entry => entry.FullName).Order().ToArray()
				);
			}

			CadProjectPackage loaded = CadProjectArchive.Load(path);
			Assert.Equal(exact, loaded.ModelDocument);
			Assert.Equal(project.Parts[0].Id, loaded.Project.Parts[0].Id);
			Assert.Equal(mate.Id, loaded.Project.Mates[0].Id);
			Assert.Equal(runner.Id, loaded.Project.Runners[0].Id);
			Assert.Equal(runner.Graph.Id, loaded.Project.Runners[0].Graph.Id);
			Assert.Equal(mate.Id, loaded.Project.Runners[0].StartMateId);
			Assert.True(loaded.Project.View.Orthographic);
			Assert.True(loaded.Project.EvaluateRunner(loaded.Project.Runners[0]).Success);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public void ReplaceInvalidatesMateButManualRebindPreservesLogicalIdentity()
	{
		(ManifoldProject project, CadMate mate, CadRunner runner) = RunnerGraphTests.CreateProject();
		Guid mateId = mate.Id;
		string name = mate.Name;
		project.ReplacePart(project.Parts[0].Id, "replacement.step");

		Assert.False(mate.IsResolved);
		Assert.False(project.EvaluateRunner(runner).Success);
		mate.Rebind(
			new CadTopologyRef(project.Parts[0].Id, 99, CadTopologyKind.CylindricalFace),
			new CadFrame(new CadPoint3(50, 0, 0), new CadPoint3(0, 0, 1), new CadPoint3(1, 0, 0)),
			21.2
		);
		Assert.Equal(mateId, mate.Id);
		Assert.Equal(name, mate.Name);
		Assert.True(project.EvaluateRunner(runner).Success);
	}

	[Fact]
	public void MovingPartComposesMateFrameAndMovesExactPathStart()
	{
		(ManifoldProject project, CadMate _, CadRunner runner) = RunnerGraphTests.CreateProject();
		CadPoint3 original = project.EvaluateRunner(runner).Chain.Features[0].EntryFrame.Origin;
		project.Parts[0].Transform = new CadTransform(new CadPoint3(25, -5, 8), CadQuaternion.Identity);
		CadPoint3 moved = project.EvaluateRunner(runner).Chain.Features[0].EntryFrame.Origin;

		Assert.Equal(original + new CadPoint3(25, -5, 8), moved);
	}

	[Fact]
	public void RemovingPartSelectsRemainingRunnerWhenActiveRunnerWasRemoved()
	{
		(ManifoldProject project, _, CadRunner removedRunner) = RunnerGraphTests.CreateProject();
		CadPart secondPart = project.AddPart("Second flange");
		CadMate secondMate = project.AddMate(secondPart.Id, "Cylinder 2");
		secondMate.Rebind(
			new CadTopologyRef(secondPart.Id, 8, CadTopologyKind.CircularEdge),
			new CadFrame(CadPoint3.Zero, new CadPoint3(1, 0, 0), new CadPoint3(0, 1, 0)),
			21.2
		);
		CadRunner remainingRunner = project.AddRunner(secondMate.Id);
		Assert.True(project.SetActiveRunner(removedRunner.Id));

		Assert.True(project.RemovePart(project.Parts[0].Id));
		Assert.Equal(remainingRunner.Id, project.ActiveRunner.Id);
		Assert.Equal(remainingRunner.Id, project.View.ActiveRunnerId);
	}

	[Fact]
	public void QuaternionEulerRoundTripPreservesPartOrientation()
	{
		CadQuaternion original = CadQuaternion.FromEulerDegrees(new CadPoint3(27, -43, 16));
		CadQuaternion restored = CadQuaternion.FromEulerDegrees(original.ToEulerDegrees());
		CadPoint3 direction = new CadPoint3(0.2, -0.4, 0.8).Normalized();
		CadPoint3 expected = original.Rotate(direction);
		CadPoint3 actual = restored.Rotate(direction);

		Assert.Equal(expected.X, actual.X, 6);
		Assert.Equal(expected.Y, actual.Y, 6);
		Assert.Equal(expected.Z, actual.Z, 6);
	}

	private static string Temporary(string extension)
	{
		return Path.Combine(Path.GetTempPath(), $"fishgfx-cad-{Guid.NewGuid():N}{extension}");
	}
}
