using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using FishGfx.NodeGraph;

namespace FishGfx.NodeEditor;

internal sealed class VisualEditorSession : IDisposable
{
	private readonly VisualEditHistory history = new VisualEditHistory();
	private readonly DotNetProgramRunner runner = new DotNetProgramRunner();
	private CancellationTokenSource executionCancellation;
	private Task<DotNetProgramRunResult> execution;
	private Guid[] clipboardNodeIds = Array.Empty<Guid>();
	private Guid clipboardFunctionId;
	private string clipboardProgram;

	internal VisualNodeCatalog Catalog { get; } = VisualNodeCatalog.CreateCore();
	internal VisualProgram Program { get; private set; }
	internal VisualFunction CurrentFunction { get; private set; }
	internal VisualProgramValidationResult Validation { get; private set; }
	internal CSharpGenerationResult Generation { get; private set; }
	internal string Output { get; private set; } = "Ready.";
	internal bool IsRunning => execution != null;
	internal bool CanUndo => history.CanUndo;
	internal bool CanRedo => history.CanRedo;

	internal VisualEditorSession()
	{
		ResetToSample();
	}

	internal string CaptureMutation()
	{
		return VisualProgramJson.Serialize(Program);
	}

	internal void CommitMutation(string before)
	{
		SynchronizeEditableProperties();
		string after = VisualProgramJson.Serialize(Program);

		if (!string.Equals(before, after, StringComparison.Ordinal))
		{
			history.Record(before, after);
			Refresh();
		}
	}

	internal VisualNode AddNode(VisualNodeDefinition definition, Vector2 position)
	{
		string before = CaptureMutation();
		VisualNode node;

		if (definition.Id == CoreVisualNodes.FunctionCall)
		{
			VisualFunction target = Program.Functions.FirstOrDefault(function =>
				function != CurrentFunction && !function.IsEntryPoint
			);

			node = target == null
				? CurrentFunction.Graph.AddNode(definition.Id, position)
				: CurrentFunction.Graph.AddFunctionCall(target, position);
		}
		else
		{
			node = CurrentFunction.Graph.AddNode(definition.Id, position);
		}

		CommitMutation(before);

		return node;
	}

	internal VisualFunction AddFunction(VisualProgramViewState currentView)
	{
		CurrentFunction.View = currentView;
		string before = CaptureMutation();
		string name = UniqueFunctionName();
		VisualFunction function = Program.AddFunction(name);

		CurrentFunction = function;
		CommitMutation(before);

		return function;
	}

	internal VisualVariableSymbol AddParameter()
	{
		if (CurrentFunction.IsEntryPoint)
		{
			Output = "Main does not expose visual parameters.";
			return null;
		}

		string before = CaptureMutation();
		VisualVariableSymbol parameter = CurrentFunction.AddVariable(
			"parameter",
			VisualValueType.Integer,
			true
		);

		foreach (VisualFunction function in Program.Functions)
		{
			foreach (VisualNode call in function.Graph.Nodes.Where(node =>
				node.DefinitionId == CoreVisualNodes.FunctionCall
				&& node.Properties.TryGetValue("function", out string target)
				&& Guid.TryParse(target, out Guid targetId)
				&& targetId == CurrentFunction.Id
			).ToArray())
			{
				function.Graph.RefreshFunctionCall(call, CurrentFunction);
			}
		}

		CommitMutation(before);

		return parameter;
	}

	internal bool TryConnect(VisualPort output, VisualPort input, out VisualConnection connection)
	{
		string before = CaptureMutation();
		bool connected = CurrentFunction.Graph.TryConnect(output, input, out connection);

		if (connected)
		{
			CommitMutation(before);
		}

		return connected;
	}

	internal void Disconnect(VisualConnection connection)
	{
		string before = CaptureMutation();

		if (CurrentFunction.Graph.Disconnect(connection))
		{
			CommitMutation(before);
		}
	}

	internal void RemoveNodes(IEnumerable<Guid> ids)
	{
		string before = CaptureMutation();

		foreach (Guid id in ids.Distinct().ToArray())
		{
			VisualNode node = CurrentFunction.Graph.Nodes.FirstOrDefault(candidate => candidate.Id == id);

			if (node != null && node.Role != VisualNodeRole.Entry)
			{
				CurrentFunction.Graph.RemoveNode(node);
			}
		}

		CommitMutation(before);
	}

	internal void Copy(IEnumerable<Guid> ids)
	{
		clipboardFunctionId = CurrentFunction.Id;
		clipboardNodeIds = ids.Distinct().ToArray();
		clipboardProgram = clipboardNodeIds.Length == 0 ? null : VisualProgramJson.Serialize(Program);
		Output = clipboardNodeIds.Length == 0
			? "Nothing selected to copy."
			: $"Copied {clipboardNodeIds.Length} node(s).";
	}

	internal IReadOnlyList<VisualNode> Paste(Vector2 offset)
	{
		if (clipboardFunctionId != CurrentFunction.Id
			|| clipboardNodeIds.Length == 0
			|| clipboardProgram == null)
		{
			Output = "The clipboard does not contain nodes for this function.";
			return Array.Empty<VisualNode>();
		}

		string before = CaptureMutation();
		VisualProgramLoadResult clipboard = VisualProgramJson.Deserialize(clipboardProgram, Catalog);

		if (!clipboard.Success)
		{
			Output = "The copied nodes could not be restored.";
			return Array.Empty<VisualNode>();
		}

		VisualFunction sourceFunction = clipboard.Program.Functions.First(function => function.Id == clipboardFunctionId);
		IReadOnlyList<VisualNode> nodes = CurrentFunction.Graph.ImportNodes(
			sourceFunction.Graph,
			clipboardNodeIds,
			offset
		);

		CommitMutation(before);

		return nodes;
	}

	internal bool Undo()
	{
		return Restore(history.Undo());
	}

	internal bool Redo()
	{
		return Restore(history.Redo());
	}

	internal void SelectFunction(Guid id, VisualProgramViewState previousView)
	{
		CurrentFunction.View = previousView;
		CurrentFunction = Program.Functions.First(function => function.Id == id);
	}

	internal void Save(string path, VisualProgramViewState currentView)
	{
		CurrentFunction.View = currentView;
		VisualProgramJson.SaveFile(path, Program);
		Output = "Saved " + Path.GetFileName(path);
	}

	internal bool TryLoad(string path, out IReadOnlyList<string> errors)
	{
		VisualProgramLoadResult load = VisualProgramJson.LoadFile(path, Catalog);

		if (!load.Success)
		{
			errors = load.Errors;
			return false;
		}

		Stop();
		Program = load.Program;
		CurrentFunction = Program.Functions.First(function => function.IsEntryPoint);
		history.Clear();
		Refresh();
		Output = "Loaded " + Path.GetFileName(path);
		errors = Array.Empty<string>();

		return true;
	}

	internal void Refresh()
	{
		Validation = new VisualProgramValidator().Validate(Program);
		Generation = new CSharpProgramGenerator().Generate(Program);
	}

	internal void SetOutput(string value)
	{
		Output = value ?? "";
	}

	internal void Run()
	{
		if (IsRunning)
		{
			return;
		}

		Refresh();

		if (!Generation.Success)
		{
			Output = "Fix validation errors before running.";
			return;
		}

		executionCancellation = new CancellationTokenSource();
		execution = runner.BuildAndRunAsync(
			Generation,
			null,
			executionCancellation.Token
		);
		Output = "Building and running...";
	}

	internal void PollExecution()
	{
		if (execution == null || !execution.IsCompleted)
		{
			return;
		}

		try
		{
			DotNetProgramRunResult result = execution.GetAwaiter().GetResult();
			string combined = result.Output;

			if (!string.IsNullOrWhiteSpace(result.Error))
			{
				combined += (combined.Length == 0 ? "" : Environment.NewLine) + result.Error;
			}

			Output = result.Cancelled
				? "Execution cancelled."
				: string.IsNullOrWhiteSpace(combined)
					? result.Success ? "Program completed successfully." : $"Program exited with code {result.ExitCode}."
					: combined.TrimEnd();
		}
		catch (Exception exception)
		{
			Output = "Execution failed: " + exception.Message;
		}
		finally
		{
			execution = null;
			executionCancellation.Dispose();
			executionCancellation = null;
		}
	}

	internal void Stop()
	{
		executionCancellation?.Cancel();
	}

	public void Dispose()
	{
		Stop();
		executionCancellation?.Dispose();
	}

	private bool Restore(string json)
	{
		if (json == null)
		{
			return false;
		}

		Guid functionId = CurrentFunction.Id;
		VisualProgramLoadResult load = VisualProgramJson.Deserialize(json, Catalog);

		if (!load.Success)
		{
			Output = "History restore failed: " + string.Join(" | ", load.Errors);
			return false;
		}

		Program = load.Program;
		CurrentFunction = Program.Functions.FirstOrDefault(function => function.Id == functionId)
			?? Program.Functions.First(function => function.IsEntryPoint);
		Refresh();

		return true;
	}

	private void ResetToSample()
	{
		Program = VisualProgram.CreateDefault(Catalog, "VisualProgram");
		CurrentFunction = Program.Functions[0];
		VisualGraph graph = CurrentFunction.Graph;
		VisualNode entry = graph.Nodes.Single();
		VisualNode text = graph.AddNode(CoreVisualNodes.TextLiteral, new Vector2(420, 470));
		VisualNode write = graph.AddNode(CoreVisualNodes.ConsoleWriteLine, new Vector2(800, 610));

		text.Properties["value"] = "Hello from FishGfx nodes";
		graph.TryConnect(entry.GetOutput("next"), write.GetInput("in"), out _);
		graph.TryConnect(text.GetOutput("result"), write.GetInput("value"), out _);
		Refresh();
	}

	private string UniqueFunctionName()
	{
		int suffix = 1;
		string name;

		do
		{
			name = "Function" + suffix;
			suffix++;
		}
		while (Program.Functions.Any(function => string.Equals(function.Name, name, StringComparison.Ordinal)));

		return name;
	}

	private void SynchronizeEditableProperties()
	{
		foreach (VisualFunction function in Program.Functions)
		{
			foreach (VisualNode declaration in function.Graph.Nodes.Where(node => node.DefinitionId == CoreVisualNodes.VariableDeclare))
			{
				if (declaration.Properties.TryGetValue("symbol", out string symbolText)
					&& Guid.TryParse(symbolText, out Guid symbolId)
					&& function.TryGetSymbol(symbolId, out VisualVariableSymbol symbol)
					&& declaration.Properties.TryGetValue("name", out string name))
				{
					symbol.Name = name;
				}
			}
		}
	}
}

internal sealed class VisualEditHistory
{
	private readonly Stack<VisualEdit> undo = new Stack<VisualEdit>();
	private readonly Stack<VisualEdit> redo = new Stack<VisualEdit>();

	internal bool CanUndo => undo.Count > 0;
	internal bool CanRedo => redo.Count > 0;

	internal void Record(string before, string after)
	{
		undo.Push(new VisualEdit(before, after));
		redo.Clear();
	}

	internal string Undo()
	{
		if (!undo.TryPop(out VisualEdit edit))
		{
			return null;
		}

		redo.Push(edit);

		return edit.Before;
	}

	internal string Redo()
	{
		if (!redo.TryPop(out VisualEdit edit))
		{
			return null;
		}

		undo.Push(edit);

		return edit.After;
	}

	internal void Clear()
	{
		undo.Clear();
		redo.Clear();
	}

	private readonly struct VisualEdit
	{
		internal string Before { get; }
		internal string After { get; }

		internal VisualEdit(string before, string after)
		{
			Before = before;
			After = after;
		}
	}
}
