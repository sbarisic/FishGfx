using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using FishGfx.NodeGraph;
using Xunit;

namespace FishGfx.Tests;

public class VisualProgramTests
{
	[Fact]
	public void CoreCatalogSeparatesExecutionAndValueConnections()
	{
		VisualProgram program = VisualProgram.CreateDefault(VisualNodeCatalog.CreateCore());
		VisualGraph graph = program.Functions[0].Graph;
		VisualNode entry = graph.Nodes.Single();
		VisualNode text = graph.AddNode(CoreVisualNodes.TextLiteral, Vector2.Zero);
		VisualNode write = graph.AddNode(CoreVisualNodes.ConsoleWriteLine, Vector2.Zero);

		Assert.False(graph.TryConnect(text.GetOutput("result"), write.GetInput("in"), out _));
		Assert.True(graph.TryConnect(entry.GetOutput("next"), write.GetInput("in"), out _));
		Assert.True(graph.TryConnect(text.GetOutput("result"), write.GetInput("value"), out _));
		Assert.Equal(2, graph.Connections.Count);
	}

	[Fact]
	public void GeneratorEmitsReadableConsoleProgramAndSourceMap()
	{
		VisualProgram program = HelloWorld();
		CSharpGenerationResult result = new CSharpProgramGenerator().Generate(program);

		Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
		Assert.Contains("private static void Main()", result.Source);
		Assert.Contains("global::System.Console.WriteLine(\"Hello from nodes\");", result.Source);
		Assert.DoesNotContain("switch", result.Source, StringComparison.Ordinal);
		Assert.DoesNotContain("\t", result.Source, StringComparison.Ordinal);
		Assert.Equal(
			program.Functions[0].Graph.Nodes.Count,
			result.SourceMap.Spans.Select(span => span.NodeId).Distinct().Count()
		);
	}

	[Fact]
	public void StructuredBranchUsesPairedMergeAndRejectsLooseValueInputs()
	{
		VisualProgram program = VisualProgram.CreateDefault(VisualNodeCatalog.CreateCore());
		VisualGraph graph = program.Functions[0].Graph;
		VisualNode entry = graph.Nodes.Single();
		VisualNode branch = graph.AddNode(CoreVisualNodes.If, new Vector2(300, 500));
		VisualNode merge = graph.GetNode(branch.PairedNodeId.Value);
		VisualNode condition = graph.AddNode(CoreVisualNodes.BooleanLiteral, new Vector2(50, 300));
		VisualNode thenText = graph.AddNode(CoreVisualNodes.TextLiteral, new Vector2(400, 700));
		VisualNode elseText = graph.AddNode(CoreVisualNodes.TextLiteral, new Vector2(400, 100));
		VisualNode thenWrite = graph.AddNode(CoreVisualNodes.ConsoleWriteLine, new Vector2(700, 700));
		VisualNode elseWrite = graph.AddNode(CoreVisualNodes.ConsoleWriteLine, new Vector2(700, 100));

		condition.Properties["value"] = "true";
		thenText.Properties["value"] = "yes";
		elseText.Properties["value"] = "no";
		Connect(graph, entry, "next", branch, "in");
		Connect(graph, condition, "result", branch, "condition");
		Connect(graph, branch, "then", thenWrite, "in");
		Connect(graph, thenText, "result", thenWrite, "value");
		Connect(graph, thenWrite, "next", merge, "then");
		Connect(graph, branch, "else", elseWrite, "in");
		Connect(graph, elseText, "result", elseWrite, "value");
		Connect(graph, elseWrite, "next", merge, "else");

		CSharpGenerationResult result = new CSharpProgramGenerator().Generate(program);

		Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
		Assert.Contains("if (true)", result.Source);
		Assert.Contains("else", result.Source);
		Assert.Contains("global::System.Console.WriteLine(\"yes\");", result.Source);
		Assert.Contains("global::System.Console.WriteLine(\"no\");", result.Source);
	}

	[Fact]
	public void VariableNodesUseStableSymbolsAndImplicitIntegerToNumberConversion()
	{
		VisualProgram program = VisualProgram.CreateDefault(VisualNodeCatalog.CreateCore());
		VisualFunction main = program.Functions[0];
		VisualGraph graph = main.Graph;
		VisualNode entry = graph.Nodes.Single();
		VisualNode declaration = graph.AddNode(CoreVisualNodes.VariableDeclare, new Vector2(300, 500));
		Guid symbolId = Guid.Parse(declaration.Properties["symbol"]);
		VisualVariableSymbol symbol = main.Symbols.Single(candidate => candidate.Id == symbolId);
		VisualNode get = graph.AddVariableNode(CoreVisualNodes.VariableGet, symbol, new Vector2(500, 300));
		VisualNode convert = graph.AddNode(CoreVisualNodes.ToText, new Vector2(700, 300));
		VisualNode write = graph.AddNode(CoreVisualNodes.ConsoleWriteLine, new Vector2(900, 500));

		declaration.GetInput("value").DefaultValue = "42";
		Connect(graph, entry, "next", declaration, "in");
		Connect(graph, declaration, "next", write, "in");
		Connect(graph, get, "result", convert, "value");
		Connect(graph, convert, "result", write, "value");

		CSharpGenerationResult result = new CSharpProgramGenerator().Generate(program);

		Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
		Assert.Contains("int value = 42;", result.Source);
		Assert.Contains("(value).ToString", result.Source);
	}

	[Fact]
	public void JsonRoundTripPreservesProgramStructureAndGeneratedCode()
	{
		VisualProgram program = HelloWorld();
		string before = new CSharpProgramGenerator().Generate(program).Source;
		string json = VisualProgramJson.Serialize(program);
		VisualProgramLoadResult load = VisualProgramJson.Deserialize(json, VisualNodeCatalog.CreateCore());

		Assert.True(load.Success, string.Join(Environment.NewLine, load.Errors));
		Assert.Equal(program.Id, load.Program.Id);
		Assert.Equal(before, new CSharpProgramGenerator().Generate(load.Program).Source);
		Assert.Equal(json, VisualProgramJson.Serialize(load.Program));
	}

	[Fact]
	public void UserFunctionsUseParametersReturnValuesAndStableCallPorts()
	{
		VisualProgram program = VisualProgram.CreateDefault(VisualNodeCatalog.CreateCore());
		VisualFunction doubleValue = program.AddFunction("DoubleValue", VisualValueType.Integer);
		VisualVariableSymbol parameter = doubleValue.AddVariable("value", VisualValueType.Integer, true);
		VisualNode functionEntry = doubleValue.Graph.Nodes.Single();
		VisualNode get = doubleValue.Graph.AddVariableNode(CoreVisualNodes.VariableGet, parameter, new Vector2(100, 200));
		VisualNode two = doubleValue.Graph.AddNode(CoreVisualNodes.IntegerLiteral, new Vector2(100, 80));
		VisualNode multiply = doubleValue.Graph.AddNode(CoreVisualNodes.IntegerMultiply, new Vector2(400, 180));
		VisualNode returnNode = doubleValue.Graph.AddNode(CoreVisualNodes.Return, new Vector2(700, 300));

		two.Properties["value"] = "2";
		Connect(doubleValue.Graph, functionEntry, "next", returnNode, "in");
		Connect(doubleValue.Graph, get, "result", multiply, "left");
		Connect(doubleValue.Graph, two, "result", multiply, "right");
		Connect(doubleValue.Graph, multiply, "result", returnNode, "value");

		VisualFunction main = program.Functions.Single(function => function.IsEntryPoint);
		VisualNode mainEntry = main.Graph.Nodes.Single();
		VisualNode argument = main.Graph.AddNode(CoreVisualNodes.IntegerLiteral, Vector2.Zero);
		VisualNode call = main.Graph.AddFunctionCall(doubleValue, new Vector2(300, 300));
		VisualNode convert = main.Graph.AddNode(CoreVisualNodes.ToText, new Vector2(600, 180));
		VisualNode write = main.Graph.AddNode(CoreVisualNodes.ConsoleWriteLine, new Vector2(800, 300));

		argument.Properties["value"] = "21";
		Connect(main.Graph, mainEntry, "next", call, "in");
		Connect(main.Graph, call, "next", write, "in");
		Connect(main.Graph, argument, "result", call, parameter.Id.ToString("N"));
		Connect(main.Graph, call, "result", convert, "value");
		Connect(main.Graph, convert, "result", write, "value");

		CSharpGenerationResult result = new CSharpProgramGenerator().Generate(program);

		Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
		Assert.Contains("private static int DoubleValue(int value)", result.Source);
		Assert.Contains("return value * 2;", result.Source);
		Assert.Equal(1, result.Source.Split("DoubleValue(21)", StringSplitOptions.None).Length - 1);
		Assert.Contains("int __fishgfx_call_", result.Source);
	}

	[Fact]
	public void ForEachUsesPairedLoopBoundaryAndScopedItemSymbol()
	{
		VisualProgram program = VisualProgram.CreateDefault(VisualNodeCatalog.CreateCore());
		VisualFunction main = program.Functions[0];
		VisualGraph graph = main.Graph;
		VisualNode entry = graph.Nodes.Single();
		VisualNode list = graph.AddListNode(CoreVisualNodes.ListCreate, VisualValueType.TextList, new Vector2(50, 200));
		VisualNode loop = graph.AddForEachNode(VisualValueType.TextList, new Vector2(300, 400));
		VisualNode loopEnd = graph.GetNode(loop.PairedNodeId.Value);
		Guid itemId = Guid.Parse(loop.Properties["symbol"]);
		VisualVariableSymbol item = main.Symbols.Single(symbol => symbol.Id == itemId);
		VisualNode getItem = graph.AddVariableNode(CoreVisualNodes.VariableGet, item, new Vector2(500, 180));
		VisualNode write = graph.AddNode(CoreVisualNodes.ConsoleWriteLine, new Vector2(700, 400));

		Connect(graph, entry, "next", loop, "in");
		Connect(graph, list, "result", loop, "list");
		Connect(graph, loop, "body", write, "in");
		Connect(graph, getItem, "result", write, "value");
		Connect(graph, write, "next", loopEnd, "body");

		CSharpGenerationResult result = new CSharpProgramGenerator().Generate(program);

		Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
		Assert.Contains("foreach (string item in new global::System.Collections.Generic.List<string>())", result.Source);
		Assert.Contains("global::System.Console.WriteLine(item);", result.Source);
	}

	[Fact]
	public async Task GeneratedProgramBuildsAndRunsOutOfProcess()
	{
		CSharpGenerationResult generation = new CSharpProgramGenerator().Generate(HelloWorld());
		DotNetProgramRunResult result = await new DotNetProgramRunner().BuildAndRunAsync(
			generation,
			cancellationToken: TestContext.Current.CancellationToken
		);

		Assert.True(result.Success, result.Error + Environment.NewLine + result.Output);
		Assert.Equal("Hello from nodes", result.Output.Trim());
	}

	[Fact]
	public void ValidationRejectsVariablesUsedBeforeTheirDeclaration()
	{
		VisualProgram program = VisualProgram.CreateDefault(VisualNodeCatalog.CreateCore());
		VisualFunction main = program.Functions[0];
		VisualGraph graph = main.Graph;
		VisualNode entry = graph.Nodes.Single();
		VisualNode declaration = graph.AddNode(CoreVisualNodes.VariableDeclare, new Vector2(900, 300));
		Guid symbolId = Guid.Parse(declaration.Properties["symbol"]);
		VisualVariableSymbol symbol = main.Symbols.Single(candidate => candidate.Id == symbolId);
		VisualNode get = graph.AddVariableNode(CoreVisualNodes.VariableGet, symbol, new Vector2(200, 100));
		VisualNode convert = graph.AddNode(CoreVisualNodes.ToText, new Vector2(400, 100));
		VisualNode write = graph.AddNode(CoreVisualNodes.ConsoleWriteLine, new Vector2(600, 300));

		Connect(graph, entry, "next", write, "in");
		Connect(graph, write, "next", declaration, "in");
		Connect(graph, get, "result", convert, "value");
		Connect(graph, convert, "result", write, "value");

		CSharpGenerationResult generation = new CSharpProgramGenerator().Generate(program);

		Assert.False(generation.Success);
		Assert.Contains(
			generation.Diagnostics,
			diagnostic => diagnostic.Code == "VPG085" && diagnostic.NodeId == get.Id
		);
	}

	[Fact]
	public void SetVariableRejectsInvalidChangesWithoutMutatingTheNode()
	{
		VisualProgram program = VisualProgram.CreateDefault(VisualNodeCatalog.CreateCore());
		VisualFunction main = program.AddFunction("Worker");
		VisualGraph graph = main.Graph;
		VisualNode declaration = graph.AddNode(CoreVisualNodes.VariableDeclare, Vector2.Zero);
		VisualVariableSymbol integer = main.Symbols.Single(symbol =>
			symbol.Id == Guid.Parse(declaration.Properties["symbol"])
		);
		VisualVariableSymbol text = main.AddVariable("text", VisualValueType.Text, true);
		VisualNode increment = graph.AddVariableNode(CoreVisualNodes.VariableIncrement, integer, Vector2.Zero);
		VisualNode literal = graph.AddNode(CoreVisualNodes.IntegerLiteral, Vector2.Zero);

		Connect(graph, literal, "result", increment, "amount");
		string symbolBefore = increment.Properties["symbol"];
		VisualPort amountBefore = increment.GetInput("amount");
		VisualConnection connectionBefore = graph.Connections.Single(connection => connection.Input == amountBefore);

		Assert.Throws<InvalidOperationException>(() => graph.SetVariable(increment, text));
		Assert.Same(amountBefore, increment.GetInput("amount"));
		Assert.Same(connectionBefore, graph.Connections.Single(connection => connection.Input == amountBefore));
		Assert.Equal(symbolBefore, increment.Properties["symbol"]);

		VisualNode write = graph.AddNode(CoreVisualNodes.ConsoleWriteLine, Vector2.Zero);
		int portCount = write.Inputs.Count + write.Outputs.Count;

		Assert.Throws<ArgumentException>(() => graph.SetVariable(write, integer));
		Assert.Equal(portCount, write.Inputs.Count + write.Outputs.Count);
	}

	[Fact]
	public void EntryPointsAndGeneratedNamesCannotCollideWithUserCode()
	{
		VisualNodeCatalog catalog = VisualNodeCatalog.CreateCore();
		VisualProgram program = VisualProgram.CreateDefault(catalog, "VisualProgram");

		Assert.Throws<ArgumentException>(() => program.AddFunction("Start", isEntryPoint: true));
		Assert.Throws<InvalidOperationException>(() => program.AddFunction(" Main "));
		Assert.Throws<ArgumentException>(() => program.Functions[0].Graph.AddFunctionCall(program.Functions[0], Vector2.Zero));
		Assert.Throws<ArgumentException>(() => program.Functions[0].AddVariable("parameter", VisualValueType.Integer, true));

		VisualFunction collision = program.AddFunction("VisualProgram");
		VisualVariableSymbol reserved = collision.AddVariable("__fishgfx_call_value", VisualValueType.Integer, true);
		VisualProgramValidationResult validation = new VisualProgramValidator().Validate(program);

		Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Code == "VPG014");
		Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Code == "VPG020");
		Assert.NotNull(reserved);

		program.Functions[0].Name = "Start";
		validation = new VisualProgramValidator().Validate(program);

		Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Code == "VPG013");
	}

	[Fact]
	public void NonVoidFunctionsMustReturnOnEveryPath()
	{
		VisualProgram program = VisualProgram.CreateDefault(VisualNodeCatalog.CreateCore());
		VisualFunction function = program.AddFunction("Value", VisualValueType.Integer);

		VisualProgramValidationResult validation = new VisualProgramValidator().Validate(program);

		Assert.Contains(
			validation.Diagnostics,
			diagnostic => diagnostic.Code == "VPG084" && diagnostic.FunctionId == function.Id
		);
		Assert.False(new CSharpProgramGenerator().Generate(program).Success);
	}

	[Fact]
	public void BranchReturnsAndStatefulResultsRespectExecutionRegions()
	{
		VisualProgram returningProgram = VisualProgram.CreateDefault(VisualNodeCatalog.CreateCore());
		VisualFunction function = returningProgram.AddFunction("Choose", VisualValueType.Integer);
		VisualGraph functionGraph = function.Graph;
		VisualNode functionEntry = functionGraph.Nodes.Single();
		VisualNode branch = functionGraph.AddNode(CoreVisualNodes.If, Vector2.Zero);
		VisualNode condition = functionGraph.AddNode(CoreVisualNodes.BooleanLiteral, Vector2.Zero);
		VisualNode one = functionGraph.AddNode(CoreVisualNodes.IntegerLiteral, Vector2.Zero);
		VisualNode two = functionGraph.AddNode(CoreVisualNodes.IntegerLiteral, Vector2.Zero);
		VisualNode firstReturn = functionGraph.AddNode(CoreVisualNodes.Return, Vector2.Zero);
		VisualNode secondReturn = functionGraph.AddNode(CoreVisualNodes.Return, Vector2.Zero);

		Connect(functionGraph, functionEntry, "next", branch, "in");
		Connect(functionGraph, condition, "result", branch, "condition");
		Connect(functionGraph, branch, "then", firstReturn, "in");
		Connect(functionGraph, one, "result", firstReturn, "value");
		Connect(functionGraph, branch, "else", secondReturn, "in");
		Connect(functionGraph, two, "result", secondReturn, "value");

		Assert.DoesNotContain(
			new VisualProgramValidator().Validate(returningProgram).Diagnostics,
			diagnostic => diagnostic.Code == "VPG084"
		);
		Assert.True(new CSharpProgramGenerator().Generate(returningProgram).Success);

		VisualProgram scopedProgram = VisualProgram.CreateDefault(VisualNodeCatalog.CreateCore());
		VisualGraph graph = scopedProgram.Functions[0].Graph;
		VisualNode entry = graph.Nodes.Single();
		VisualNode scopedBranch = graph.AddNode(CoreVisualNodes.If, Vector2.Zero);
		VisualNode merge = graph.GetNode(scopedBranch.PairedNodeId.Value);
		VisualNode scopedCondition = graph.AddNode(CoreVisualNodes.BooleanLiteral, Vector2.Zero);
		VisualNode read = graph.AddNode(CoreVisualNodes.ConsoleReadLine, Vector2.Zero);
		VisualNode write = graph.AddNode(CoreVisualNodes.ConsoleWriteLine, Vector2.Zero);

		Connect(graph, entry, "next", scopedBranch, "in");
		Connect(graph, scopedCondition, "result", scopedBranch, "condition");
		Connect(graph, scopedBranch, "then", read, "in");
		Connect(graph, read, "next", merge, "then");
		Connect(graph, scopedBranch, "else", merge, "else");
		Connect(graph, merge, "next", write, "in");
		Connect(graph, read, "result", write, "value");

		VisualProgramValidationResult validation = new VisualProgramValidator().Validate(scopedProgram);

		Assert.Contains(
			validation.Diagnostics,
			diagnostic => diagnostic.Code == "VPG086" && diagnostic.NodeId == read.Id
		);
	}

	[Fact]
	public void RegisteredNodesWithoutAGeneratorAreRejected()
	{
		VisualNodeCatalog catalog = VisualNodeCatalog.CreateCore();

		catalog.Register(
			new VisualNodeDefinition(
				"extension.statement",
				"Extension",
				"Tests",
				VisualNodeRole.Statement,
				new[]
				{
					new VisualPortDefinition(
						"in",
						"in",
						VisualPortKind.Execution,
						VisualPortDirection.Input
					)
				}
			)
		);

		VisualProgram program = VisualProgram.CreateDefault(catalog);
		VisualNode node = program.Functions[0].Graph.AddNode("extension.statement", Vector2.Zero);
		CSharpGenerationResult generation = new CSharpProgramGenerator().Generate(program);

		Assert.False(generation.Success);
		Assert.Contains(
			generation.Diagnostics,
			diagnostic => diagnostic.Code == "VPG037" && diagnostic.NodeId == node.Id
		);
	}

	[Fact]
	public async Task IntegerToTextAndControlCharactersGenerateValidCSharp()
	{
		VisualProgram program = VisualProgram.CreateDefault(VisualNodeCatalog.CreateCore());
		VisualGraph graph = program.Functions[0].Graph;
		VisualNode entry = graph.Nodes.Single();
		VisualNode integer = graph.AddNode(CoreVisualNodes.IntegerLiteral, Vector2.Zero);
		VisualNode convert = graph.AddNode(CoreVisualNodes.ToText, Vector2.Zero);
		VisualNode controls = graph.AddNode(CoreVisualNodes.TextLiteral, Vector2.Zero);
		VisualNode join = graph.AddNode(CoreVisualNodes.TextConcat, Vector2.Zero);
		VisualNode write = graph.AddNode(CoreVisualNodes.ConsoleWriteLine, Vector2.Zero);

		integer.Properties["value"] = "1";
		controls.Properties["value"] = "\0\b\f\u2028\u2029";
		Connect(graph, entry, "next", write, "in");
		Connect(graph, integer, "result", convert, "value");
		Connect(graph, convert, "result", join, "left");
		Connect(graph, controls, "result", join, "right");
		Connect(graph, join, "result", write, "value");

		CSharpGenerationResult generation = new CSharpProgramGenerator().Generate(program);

		Assert.True(generation.Success);
		Assert.Contains("(1).ToString", generation.Source);
		Assert.Contains("\\u0000\\u0008\\u000C\\u2028\\u2029", generation.Source);

		using DotNetProgramBuildResult build = await new DotNetProgramRunner().BuildAsync(
			generation,
			TestContext.Current.CancellationToken
		);

		Assert.True(build.Success, string.Join(Environment.NewLine, build.Diagnostics.Select(diagnostic => diagnostic.Message)));
	}

	[Fact]
	public void SourceMapSpansEndOnGeneratedContent()
	{
		VisualProgram program = HelloWorld();
		VisualNode write = program.Functions[0].Graph.Nodes.Single(node =>
			node.DefinitionId == CoreVisualNodes.ConsoleWriteLine
		);
		CSharpGenerationResult generation = new CSharpProgramGenerator().Generate(program);
		GeneratedNodeSpan span = generation.SourceMap.Spans.Single(candidate => candidate.NodeId == write.Id);
		string[] lines = generation.Source.Split(Environment.NewLine);

		Assert.Contains("WriteLine", lines[span.EndLine - 1]);
		Assert.NotEqual(write.Id, generation.SourceMap.Find(span.EndLine + 1, 1)?.NodeId);
	}

	[Fact]
	public async Task ConsoleInputIsCapturedOnceAndForwardedToTheChildProcess()
	{
		VisualProgram program = VisualProgram.CreateDefault(VisualNodeCatalog.CreateCore());
		VisualGraph graph = program.Functions[0].Graph;
		VisualNode entry = graph.Nodes.Single();
		VisualNode read = graph.AddNode(CoreVisualNodes.ConsoleReadLine, new Vector2(300, 300));
		VisualNode write = graph.AddNode(CoreVisualNodes.ConsoleWriteLine, new Vector2(600, 300));

		Connect(graph, entry, "next", read, "in");
		Connect(graph, read, "next", write, "in");
		Connect(graph, read, "result", write, "value");

		CSharpGenerationResult generation = new CSharpProgramGenerator().Generate(program);
		DotNetProgramRunResult result = await new DotNetProgramRunner().BuildAndRunAsync(
			generation,
			"Ada" + Environment.NewLine,
			TestContext.Current.CancellationToken
		);

		Assert.True(result.Success, result.Error);
		Assert.Equal("Ada", result.Output.Trim());
		Assert.Equal(1, generation.Source.Split("Console.ReadLine()", StringSplitOptions.None).Length - 1);
	}

	private static VisualProgram HelloWorld()
	{
		VisualProgram program = VisualProgram.CreateDefault(VisualNodeCatalog.CreateCore());
		VisualGraph graph = program.Functions[0].Graph;
		VisualNode entry = graph.Nodes.Single();
		VisualNode text = graph.AddNode(CoreVisualNodes.TextLiteral, new Vector2(100, 300));
		VisualNode write = graph.AddNode(CoreVisualNodes.ConsoleWriteLine, new Vector2(500, 500));

		text.Properties["value"] = "Hello from nodes";
		Connect(graph, entry, "next", write, "in");
		Connect(graph, text, "result", write, "value");

		return program;
	}

	private static void Connect(
		VisualGraph graph,
		VisualNode outputNode,
		string outputName,
		VisualNode inputNode,
		string inputName
	)
	{
		Assert.True(
			graph.TryConnect(
				outputNode.GetOutput(outputName),
				inputNode.GetInput(inputName),
				out _
			)
		);
	}
}
