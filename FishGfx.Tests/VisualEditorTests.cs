using System;
using System.Linq;
using System.Numerics;
using FishGfx.NodeEditor;
using FishGfx.NodeGraph;
using Xunit;

namespace FishGfx.Tests;

public class VisualEditorTests
{
	[Fact]
	public void SessionStartsWithRunnableVisualProgramAndSupportsUndoRedo()
	{
		using VisualEditorSession session = new VisualEditorSession();
		int initialCount = session.CurrentFunction.Graph.Nodes.Count;
		VisualNodeDefinition comment = session.Catalog.Get(CoreVisualNodes.Comment);

		session.AddNode(comment, new Vector2(900, 200));

		Assert.Equal(initialCount + 1, session.CurrentFunction.Graph.Nodes.Count);
		Assert.True(session.CanUndo);
		Assert.True(session.Undo());
		Assert.Equal(initialCount, session.CurrentFunction.Graph.Nodes.Count);
		Assert.True(session.Redo());
		Assert.Equal(initialCount + 1, session.CurrentFunction.Graph.Nodes.Count);
		Assert.True(session.Generation.Success);
	}

	[Fact]
	public void DuplicatingControlNodeKeepsItsBoundaryPairAndInternalConnections()
	{
		VisualProgram program = VisualProgram.CreateDefault(VisualNodeCatalog.CreateCore());
		VisualGraph graph = program.Functions[0].Graph;
		VisualNode branch = graph.AddNode(CoreVisualNodes.If, Vector2.Zero);
		VisualNode merge = graph.GetNode(branch.PairedNodeId.Value);
		VisualNode comment = graph.AddNode(CoreVisualNodes.Comment, new Vector2(200, 0));

		Assert.True(graph.TryConnect(branch.GetOutput("then"), comment.GetInput("in"), out _));
		Assert.True(graph.TryConnect(comment.GetOutput("next"), merge.GetInput("then"), out _));

		var copies = graph.DuplicateNodes(new[] { branch.Id, comment.Id }, new Vector2(50, 50));
		VisualNode copiedBranch = copies.Single(node => node.DefinitionId == CoreVisualNodes.If);
		VisualNode copiedMerge = copies.Single(node => node.DefinitionId == CoreVisualNodes.Merge);

		Assert.Equal(copiedMerge.Id, copiedBranch.PairedNodeId);
		Assert.Equal(copiedBranch.Id, copiedMerge.PairedNodeId);
		Assert.Equal(2, graph.Connections.Count(connection => copies.Contains(connection.Input.Node) && copies.Contains(connection.Output.Node)));
	}

	[Fact]
	public void VisualContextMenuFiltersCoreCatalogAndPreservesKeyboardNavigation()
	{
		VisualNodeCatalog catalog = VisualNodeCatalog.CreateCore();
		VisualContextMenu menu = new VisualContextMenu(catalog.Definitions);

		menu.Open(new Vector2(400, 500), Vector2.Zero, 1280, 720);
		menu.Append("write");

		Assert.True(menu.IsOpen);
		Assert.Contains(menu.CurrentNodes, node => node.Id == CoreVisualNodes.ConsoleWriteLine);
		menu.MoveNode(1);
		Assert.NotNull(menu.Activate());
		menu.Escape();
		Assert.True(menu.IsOpen);
		menu.Escape();
		Assert.False(menu.IsOpen);
	}

	[Fact]
	public void ClipboardSurvivesDeletingTheOriginalNodes()
	{
		using VisualEditorSession session = new VisualEditorSession();
		VisualNode comment = session.AddNode(
			session.Catalog.Get(CoreVisualNodes.Comment),
			new Vector2(900, 200)
		);

		session.Copy(new[] { comment.Id });
		session.RemoveNodes(new[] { comment.Id });
		var pasted = session.Paste(new Vector2(40, -40));

		Assert.Single(pasted);
		Assert.Equal(CoreVisualNodes.Comment, pasted[0].DefinitionId);
		Assert.Contains(session.CurrentFunction.Graph.Nodes, node => node.Id == pasted[0].Id);
	}

	[Fact]
	public void DuplicatedDeclarationsReceiveUniqueSymbolsAndNames()
	{
		VisualProgram program = VisualProgram.CreateDefault(VisualNodeCatalog.CreateCore());
		VisualGraph graph = program.Functions[0].Graph;
		VisualNode declaration = graph.AddNode(CoreVisualNodes.VariableDeclare, Vector2.Zero);

		VisualNode copy = Assert.Single(graph.DuplicateNodes(new[] { declaration.Id }, new Vector2(40, 40)));
		Guid copiedSymbolId = Guid.Parse(copy.Properties["symbol"]);
		VisualVariableSymbol copiedSymbol = program.Functions[0].Symbols.Single(symbol => symbol.Id == copiedSymbolId);

		Assert.Equal(copiedSymbol.Name, copy.Properties["name"]);
		Assert.NotEqual(declaration.Properties["name"], copy.Properties["name"]);
		Assert.True(new VisualProgramValidator().Validate(program).Success);
	}

	[Fact]
	public void RemovingLoopThroughBoundaryAlsoRemovesItsScopedItemSymbol()
	{
		VisualProgram program = VisualProgram.CreateDefault(VisualNodeCatalog.CreateCore());
		VisualFunction main = program.Functions[0];
		VisualNode loop = main.Graph.AddForEachNode(VisualValueType.IntegerList, Vector2.Zero);
		Guid symbolId = Guid.Parse(loop.Properties["symbol"]);
		VisualNode boundary = main.Graph.GetNode(loop.PairedNodeId.Value);

		Assert.True(main.Graph.RemoveNode(boundary));
		Assert.DoesNotContain(main.Symbols, symbol => symbol.Id == symbolId);
		Assert.DoesNotContain(main.Graph.Nodes, node => node.Id == loop.Id || node.Id == boundary.Id);
	}
}
