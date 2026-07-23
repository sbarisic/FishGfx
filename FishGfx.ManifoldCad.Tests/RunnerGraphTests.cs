using FishGfx.Cad;
using Xunit;

namespace FishGfx.ManifoldCad.Tests;

public sealed class RunnerGraphTests
{
	[Fact]
	public void DefaultGraphEvaluatesExactDeterministicLength()
	{
		(ManifoldProject project, _, CadRunner runner) = CreateProject();
		RunnerEvaluationResult result = project.EvaluateRunner(runner);

		Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
		Assert.Equal(200 + 50 * Math.PI / 4, result.LengthMillimetres, 9);
		Assert.Equal(3, result.Chain.Features.Count);
		Assert.Equal(RunnerFeatureKind.Bend, result.Chain.Features[1].Kind);
		Assert.Equal(result.Chain.Features[0].ExitFrame.Origin, result.Chain.Features[1].EntryFrame.Origin);
		Assert.DoesNotContain(runner.Graph.Nodes, node => node.DefinitionId == "cad.mate-reference");
	}

	[Fact]
	public void ConnectionsAndSplicesAreTransactional()
	{
		RunnerGraph graph = new();
		RunnerNode first = graph.AddNode(RunnerNodes.StartRunner);
		RunnerNode second = graph.AddNode(RunnerNodes.Straight);
		Assert.True(graph.TryConnect(first.Id, "runner", second.Id, "runner", out RunnerConnection original, out _));
		Assert.False(graph.TryConnect(second.Id, "runner", first.Id, "profile", out _, out _));
		Assert.Equal(original, Assert.Single(graph.Connections));

		Assert.True(graph.TrySpliceConnection(original.Id, RunnerNodes.Bend, 20, 30,
			out RunnerNode bend, out string error), error);
		Assert.Equal(3, graph.Nodes.Count);
		Assert.Equal(2, graph.Connections.Count);
		Assert.Contains(graph.Connections, item => item.InputNodeId == bend.Id);
		Assert.Contains(graph.Connections, item => item.OutputNodeId == bend.Id);

		RunnerConnection beforeFailure = graph.Connections[0];
		Assert.False(graph.TrySpliceConnection(beforeFailure.Id, RunnerNodes.CircularPipe, 0, 0,
			out _, out _));
		Assert.Equal(3, graph.Nodes.Count);
		Assert.Equal(2, graph.Connections.Count);

		RunnerGraph cyclic = new();
		RunnerNode cycleA = cyclic.AddNode(RunnerNodes.Straight);
		RunnerNode cycleB = cyclic.AddNode(RunnerNodes.Straight);
		Assert.True(cyclic.TryConnect(cycleA.Id, "runner", cycleB.Id, "runner", out RunnerConnection retained, out _));
		Assert.False(cyclic.TryConnect(cycleB.Id, "runner", cycleA.Id, "runner", out _, out string cycleError));
		Assert.Contains("acyclic", cycleError, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(retained, Assert.Single(cyclic.Connections));
	}

	[Fact]
	public void ValidationReportsResponsibleNode()
	{
		(ManifoldProject project, _, CadRunner runner) = CreateProject();
		RunnerNode bend = runner.Graph.Nodes.Single(node => node.DefinitionId == RunnerNodes.Bend);
		bend.Properties["radius"] = "10";
		RunnerEvaluationResult result = project.EvaluateRunner(runner);

		Assert.False(result.Success);
		Assert.Contains(result.Diagnostics, item => item.NodeId == bend.Id && item.Code == "RUN041");
	}

	[Fact]
	public void ExactlyOneTerminalOutputIsRequired()
	{
		(ManifoldProject project, _, CadRunner runner) = CreateProject();
		runner.Graph.AddNode(RunnerNodes.RunnerOutput);
		RunnerEvaluationResult result = project.EvaluateRunner(runner);
		Assert.False(result.Success);
		Assert.Contains(result.Diagnostics, item => item.Code == "RUN001");
	}

	[Fact]
	public void UnknownFutureNodeRoundTripsDisabledAndUnchanged()
	{
		RunnerGraph graph = new();
		RunnerNode node = graph.AddNode("cad.future-transition", 12.5, -7);
		node.Properties["mode"] = "conical";
		string json = RunnerGraphJson.Serialize(graph);
		RunnerGraphLoadResult loaded = RunnerGraphJson.Deserialize(json);

		Assert.True(loaded.Success);
		RunnerNode restored = Assert.Single(loaded.Graph.Nodes);
		Assert.Equal(node.Id, restored.Id);
		Assert.False(restored.IsKnown);
		Assert.Equal("conical", restored.Properties["mode"]);
	}

	[Fact]
	public void DeserializationRejectsCyclesBeforeEvaluation()
	{
		Guid graphId = Guid.NewGuid();
		Guid first = Guid.NewGuid();
		Guid second = Guid.NewGuid();
		string json = $$"""
		{
		  "schema": "fishgfx.runner-graph", "version": 1, "id": "{{graphId}}",
		  "nodes": [
		    { "id": "{{first}}", "definitionId": "cad.straight", "x": 0, "y": 0, "properties": { "length": "10" } },
		    { "id": "{{second}}", "definitionId": "cad.straight", "x": 1, "y": 0, "properties": { "length": "10" } }
		  ],
		  "connections": [
		    { "id": "{{Guid.NewGuid()}}", "outputNodeId": "{{first}}", "outputPort": "runner", "inputNodeId": "{{second}}", "inputPort": "runner" },
		    { "id": "{{Guid.NewGuid()}}", "outputNodeId": "{{second}}", "outputPort": "runner", "inputNodeId": "{{first}}", "inputPort": "runner" }
		  ]
		}
		""";

		RunnerGraphLoadResult loaded = RunnerGraphJson.Deserialize(json);
		Assert.False(loaded.Success);
		Assert.Contains(loaded.Errors, error => error.Contains("acyclic", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void StartMateMayOwnOnlyOneRunner()
	{
		(ManifoldProject project, CadMate mate, _) = CreateProject();
		Assert.Throws<InvalidOperationException>(() => project.AddRunner(mate.Id));
	}

	[Fact]
	public void ClosedProfileRunnerUsesRepeatableProfileTransitionFeatures()
	{
		ManifoldProject project = new();
		CadPart part = project.AddPart("Port flange");
		CadMate mate = project.AddMate(part.Id, "Port 1");
		mate.Rebind(new CadTopologyRef(part.Id, 11, CadTopologyKind.ClosedProfile),
			new CadFrame(CadPoint3.Zero, new CadPoint3(0, 0, 1), new CadPoint3(1, 0, 0)), 28);
		CadRunner runner = project.AddRunner(mate.Id);
		RunnerNode lastStraight = runner.Graph.Nodes.Last(node => node.DefinitionId == RunnerNodes.Straight);
		RunnerNode output = runner.Graph.Nodes.Single(node => node.DefinitionId == RunnerNodes.RunnerOutput);
		RunnerConnection tail = runner.Graph.Connections.Single(connection => connection.OutputNodeId == lastStraight.Id
			&& connection.InputNodeId == output.Id);
		Assert.True(runner.Graph.TrySpliceConnection(tail.Id, RunnerNodes.LoftTransition, 1300, 220,
			out RunnerNode secondLoft, out string spliceError), spliceError);
		RunnerNode secondProfile = runner.Graph.AddNode(RunnerNodes.CircularPipe, 1250, 40);
		secondProfile.Properties["outerDiameter"] = "50";
		Assert.True(runner.Graph.TryConnect(secondProfile.Id, "profile", secondLoft.Id, "targetProfile",
			out _, out string connectError), connectError);

		RunnerEvaluationResult evaluation = project.EvaluateRunner(runner);
		Assert.True(evaluation.Success, string.Join(Environment.NewLine, evaluation.Diagnostics.Select(item => item.Message)));
		Assert.Equal(2, evaluation.Chain.Features.Count(feature => feature.Kind == RunnerFeatureKind.LoftTransition));
		RunnerFeature transition = evaluation.Chain.Features.Last(feature => feature.Kind == RunnerFeatureKind.LoftTransition);
		Assert.Equal(RunnerProfileKind.CircularPipe, transition.InputProfile.Kind);
		Assert.Equal(50, transition.OutputProfile.CircularProfile.Value.OuterDiameterMillimetres);
	}

	[Fact]
	public void LoftRotationPreservesRightHandedFrameAndRotatesProfileAxis()
	{
		ManifoldProject project = new();
		CadPart part = project.AddPart("Port flange");
		CadMate mate = project.AddMate(part.Id, "Port 1");
		mate.Rebind(new CadTopologyRef(part.Id, 12, CadTopologyKind.ClosedProfile),
			new CadFrame(CadPoint3.Zero, new CadPoint3(0, 0, 1), new CadPoint3(1, 0, 0)), 28);
		CadRunner runner = project.AddRunner(mate.Id);
		RunnerNode loft = runner.Graph.Nodes.First(node => node.DefinitionId == RunnerNodes.LoftTransition);
		loft.Properties["rotation"] = "90";

		RunnerEvaluationResult evaluation = project.EvaluateRunner(runner);
		Assert.True(evaluation.Success, string.Join(Environment.NewLine, evaluation.Diagnostics.Select(item => item.Message)));
		RunnerFeature transition = evaluation.Chain.Features.First();
		CadFrame exit = transition.ExitFrame;
		Assert.InRange(CadPoint3.Dot(exit.Normal, new CadPoint3(0, 1, 0)), 0.999999, 1.000001);
		Assert.InRange(CadPoint3.Dot(CadPoint3.Cross(exit.Normal, exit.Binormal), exit.Tangent),
			0.999999, 1.000001);
	}

	internal static (ManifoldProject Project, CadMate Mate, CadRunner Runner) CreateProject()
	{
		ManifoldProject project = new();
		CadPart part = project.AddPart("Fixture flange");
		CadMate mate = project.AddMate(part.Id, "Cylinder 1");
		mate.Rebind(
			new CadTopologyRef(part.Id, 7, CadTopologyKind.CircularEdge),
			new CadFrame(CadPoint3.Zero, new CadPoint3(1, 0, 0), new CadPoint3(0, 1, 0)),
			21.2
		);
		CadRunner runner = project.AddRunner(mate.Id);
		return (project, mate, runner);
	}
}
