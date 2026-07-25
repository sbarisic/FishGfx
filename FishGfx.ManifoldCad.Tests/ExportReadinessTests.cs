using FishGfx.Cad;
using Xunit;

namespace FishGfx.ManifoldCad.Tests;

public sealed class ExportReadinessTests
{
	[Fact]
	public void ReplacedPartImmediatelyDisablesProjectExport()
	{
		(ManifoldProject project, _, CadRunner runner) = RunnerGraphTests.CreateProject();
		RunnerEvaluationResult evaluation = project.EvaluateRunner(runner);
		Dictionary<Guid, RunnerEvaluationResult> evaluations = new()
		{
			[runner.Id] = evaluation,
		};
		Dictionary<Guid, string> errors = new();

		Assert.True(ManifoldCadApplication.CanExportProject(project, evaluations, errors));
		project.ReplacePart(project.Parts[0].Id, "replacement.step");
		Assert.False(ManifoldCadApplication.CanExportProject(project, evaluations, errors));
	}

	[Fact]
	public async Task CurrentNativeBezierEvaluationAllowsExport()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		(ManifoldProject project, _, CadRunner runner) = RunnerGraphTests.CreateProject();
		RunnerNode lastStraight = runner.Graph.Nodes.Last(node =>
			node.DefinitionId == RunnerNodes.Straight);
		RunnerNode output = runner.Graph.Nodes.Single(node =>
			node.DefinitionId == RunnerNodes.RunnerOutput);
		RunnerConnection tail = runner.Graph.Connections.Single(connection =>
			connection.OutputNodeId == lastStraight.Id
			&& connection.InputNodeId == output.Id);
		Assert.True(runner.Graph.TrySpliceConnection(
			tail.Id,
			RunnerNodes.CubicBezier,
			1300,
			220,
			out _,
			out string error
		), error);
		runner.CommitEdit();

		await using CadDocument document = await CadDocument.CreateAsync(cancellationToken);
		RunnerEvaluationResult evaluation = await project.EvaluateRunnerAsync(
			document,
			runner,
			cancellationToken
		);
		Assert.True(evaluation.Success);
		Assert.False(project.EvaluateRunner(runner).Success);

		Dictionary<Guid, RunnerEvaluationResult> evaluations = new()
		{
			[runner.Id] = evaluation,
		};
		Assert.True(ManifoldCadApplication.CanExportRunners(
			project.Runners,
			evaluations,
			new Dictionary<Guid, string>()
		));

		runner.CommitEdit();
		Assert.False(ManifoldCadApplication.CanExportRunners(
			project.Runners,
			evaluations,
			new Dictionary<Guid, string>()
		));
	}
}
