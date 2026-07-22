using FishGfx.Cad;
using Xunit;

namespace FishGfx.ManifoldCad.Tests;

public sealed class RunnerGraphTests
{
	[Fact]
	public void DefaultGraphEvaluatesExactDeterministicLength()
	{
		(ManifoldProject project, CadMate _) = CreateProject();
		RunnerEvaluationResult result = project.EvaluateRunner();

		Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
		Assert.Equal(200 + 50 * Math.PI / 4, result.LengthMillimetres, 9);
		Assert.Equal(3, result.Path.Segments.Count);
		Assert.Equal(RunnerSegmentKind.Bend, result.Path.Segments[1].Kind);
		Assert.Equal(result.Path.Segments[0].End, result.Path.Segments[1].Start);
	}

	[Fact]
	public void GraphRejectsCyclesAndIncompatiblePorts()
	{
		RunnerGraph graph = new();
		RunnerNode first = graph.AddNode(RunnerNodes.Straight);
		RunnerNode second = graph.AddNode(RunnerNodes.Straight);
		Assert.True(graph.TryConnect(first.Id, "path", second.Id, "path", out _, out _));
		Assert.False(graph.TryConnect(second.Id, "path", first.Id, "path", out _, out string cycleError));
		Assert.Contains("cyclic", cycleError, StringComparison.OrdinalIgnoreCase);
		RunnerNode profile = graph.AddNode(RunnerNodes.CircularPipe);
		Assert.False(graph.TryConnect(profile.Id, "profile", first.Id, "path", out _, out string typeError));
		Assert.Contains("incompatible", typeError, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ValidationReportsResponsibleNode()
	{
		(ManifoldProject project, CadMate _) = CreateProject();
		RunnerNode bend = project.Graph.Nodes.Single(node => node.DefinitionId == RunnerNodes.Bend);
		bend.Properties["radius"] = "10";
		RunnerEvaluationResult result = project.EvaluateRunner();

		Assert.False(result.Success);
		Assert.Contains(result.Diagnostics, item => item.NodeId == bend.Id && item.Code == "RUN041");
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

	internal static (ManifoldProject Project, CadMate Mate) CreateProject()
	{
		ManifoldProject project = new();
		CadPart part = project.AddPart("Fixture flange");
		CadMate mate = project.AddMate(part.Id, "Cylinder 1");
		mate.Rebind(
			new CadTopologyRef(part.Id, 7, CadTopologyKind.CircularEdge),
			new CadFrame(CadPoint3.Zero, new CadPoint3(1, 0, 0), new CadPoint3(0, 1, 0)),
			21.2
		);
		project.Graph = RunnerGraph.CreateDefault(mate.Id);
		return (project, mate);
	}
}
