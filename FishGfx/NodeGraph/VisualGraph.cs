using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace FishGfx.NodeGraph;

public sealed class VisualGraph
{
	private readonly List<VisualNode> nodes = new List<VisualNode>();
	private readonly List<VisualConnection> connections = new List<VisualConnection>();
	private readonly VisualNodeCatalog catalog;

	public IReadOnlyList<VisualNode> Nodes { get; }
	public IReadOnlyList<VisualConnection> Connections { get; }
	public VisualFunction Function { get; }

	internal VisualGraph(VisualFunction function, VisualNodeCatalog catalog)
	{
		Function = function;
		this.catalog = catalog;
		Nodes = nodes.AsReadOnly();
		Connections = connections.AsReadOnly();
	}

	public VisualNode AddNode(string definitionId, Vector2 position)
	{
		VisualNode node = AddSingleNode(definitionId, position);

		if (definitionId == CoreVisualNodes.If)
		{
			Pair(node, AddSingleNode(CoreVisualNodes.Merge, position + new Vector2(420, 0)));
		}
		else if (definitionId == CoreVisualNodes.While
			|| definitionId == CoreVisualNodes.Repeat
			|| definitionId == CoreVisualNodes.ForEach)
		{
			Pair(node, AddSingleNode(CoreVisualNodes.LoopEnd, position + new Vector2(420, -120)));
		}

		ConfigureDynamicNode(node);

		return node;
	}

	public VisualNode AddForEachNode(VisualValueType listType, Vector2 position)
	{
		if (!VisualValueTypes.IsList(listType))
		{
			throw new ArgumentException("A list type is required.", nameof(listType));
		}

		VisualNode node = AddNode(CoreVisualNodes.ForEach, position);
		VisualVariableSymbol oldSymbol = SymbolFor(node);

		if (oldSymbol != null)
		{
			Function.RemoveVariable(oldSymbol);
		}

		node.RemoveValuePorts();
		VisualVariableSymbol symbol = Function.AddVariable(
			"item",
			VisualValueTypes.ElementType(listType),
			false,
			node.Id
		);

		node.Properties["type"] = listType.ToString();
		node.Properties["symbol"] = symbol.Id.ToString("D");
		AddValueInput(node, "list", listType);

		return node;
	}

	public VisualNode AddVariableNode(
		string definitionId,
		VisualVariableSymbol symbol,
		Vector2 position
	)
	{
		if (definitionId != CoreVisualNodes.VariableGet
			&& definitionId != CoreVisualNodes.VariableSet
			&& definitionId != CoreVisualNodes.VariableIncrement)
		{
			throw new ArgumentException("The definition is not a variable reference node.", nameof(definitionId));
		}

		if (symbol == null || !Function.Symbols.Contains(symbol))
		{
			throw new ArgumentException("The variable must belong to this function.", nameof(symbol));
		}

		VisualNode node = AddNode(definitionId, position);

		SetVariable(node, symbol);

		return node;
	}

	public void SetVariable(VisualNode node, VisualVariableSymbol symbol)
	{
		if (node == null || !nodes.Contains(node))
		{
			throw new ArgumentException("The node must belong to this graph.", nameof(node));
		}

		if (symbol == null || !Function.Symbols.Contains(symbol))
		{
			throw new ArgumentException("The variable must belong to this function.", nameof(symbol));
		}

		if (node.DefinitionId != CoreVisualNodes.VariableGet
			&& node.DefinitionId != CoreVisualNodes.VariableSet
			&& node.DefinitionId != CoreVisualNodes.VariableIncrement)
		{
			throw new ArgumentException("The node is not a variable reference node.", nameof(node));
		}

		if (node.DefinitionId == CoreVisualNodes.VariableIncrement
			&& symbol.Type != VisualValueType.Integer)
		{
			throw new InvalidOperationException("Only integer variables can use the increment node.");
		}

		connections.RemoveAll(connection =>
			connection.Input.Node == node && connection.Input.Kind == VisualPortKind.Value
			|| connection.Output.Node == node && connection.Output.Kind == VisualPortKind.Value
		);
		node.RemoveValuePorts();
		node.Properties["symbol"] = symbol.Id.ToString("D");

		if (node.DefinitionId == CoreVisualNodes.VariableGet)
		{
			AddValueOutput(node, "result", symbol.Type);
		}
		else if (node.DefinitionId == CoreVisualNodes.VariableSet)
		{
			AddValueInput(node, "value", symbol.Type);
		}
		else if (node.DefinitionId == CoreVisualNodes.VariableIncrement)
		{
			AddValueInput(node, "amount", VisualValueType.Integer, true, "1");
		}
	}

	public VisualNode AddListNode(string definitionId, VisualValueType listType, Vector2 position)
	{
		if (!VisualValueTypes.IsList(listType))
		{
			throw new ArgumentException("A list type is required.", nameof(listType));
		}

		if (definitionId != CoreVisualNodes.ListCreate
			&& definitionId != CoreVisualNodes.ListAdd
			&& definitionId != CoreVisualNodes.ListGet
			&& definitionId != CoreVisualNodes.ListCount)
		{
			throw new ArgumentException("The definition is not a list node.", nameof(definitionId));
		}

		VisualNode node = AddNode(definitionId, position);

		connections.RemoveAll(connection =>
			connection.Input.Node == node && connection.Input.Kind == VisualPortKind.Value
			|| connection.Output.Node == node && connection.Output.Kind == VisualPortKind.Value
		);
		node.RemoveValuePorts();
		node.Properties["type"] = listType.ToString();

		if (definitionId == CoreVisualNodes.ListCreate)
		{
			AddValueOutput(node, "result", listType);
		}
		else
		{
			AddValueInput(node, "list", listType);

			if (definitionId == CoreVisualNodes.ListAdd)
			{
				AddValueInput(node, "value", VisualValueTypes.ElementType(listType));
			}
			else if (definitionId == CoreVisualNodes.ListGet)
			{
				AddValueInput(node, "index", VisualValueType.Integer);
				AddValueOutput(node, "result", VisualValueTypes.ElementType(listType));
			}
			else
			{
				AddValueOutput(node, "result", VisualValueType.Integer);
			}
		}

		return node;
	}

	public VisualNode AddFunctionCall(VisualFunction target, Vector2 position)
	{
		if (target == null
			|| !ReferenceEquals(target.Program, Function.Program)
			|| target.IsEntryPoint)
		{
			throw new ArgumentException("The target must be a non-entry function from this program.", nameof(target));
		}

		VisualNode node = AddNode(CoreVisualNodes.FunctionCall, position);

		node.Properties["function"] = target.Id.ToString("D");
		RefreshFunctionCall(node, target);

		return node;
	}

	public void RefreshFunctionCall(VisualNode node, VisualFunction target)
	{
		if (node == null
			|| !nodes.Contains(node)
			|| node.DefinitionId != CoreVisualNodes.FunctionCall)
		{
			throw new ArgumentException("A function-call node from this graph is required.", nameof(node));
		}

		if (target == null || !ReferenceEquals(target.Program, Function.Program))
		{
			throw new ArgumentException("The target function must belong to this program.", nameof(target));
		}

		if (target.IsEntryPoint)
		{
			throw new ArgumentException("The entry point cannot be called as a visual function.", nameof(target));
		}

		Dictionary<string, VisualConnection> inputs = node.Inputs
			.Where(port => port.Kind == VisualPortKind.Value)
			.Select(port => TryGetInputConnection(port, out VisualConnection connection) ? connection : null)
			.Where(connection => connection != null)
			.ToDictionary(connection => connection.Input.Name, StringComparer.Ordinal);
		VisualConnection[] outputs = node.Outputs
			.Where(port => port.Kind == VisualPortKind.Value)
			.SelectMany(port => TryGetOutputConnections(port))
			.ToArray();

		connections.RemoveAll(connection =>
			connection.Input.Node == node && connection.Input.Kind == VisualPortKind.Value
			|| connection.Output.Node == node && connection.Output.Kind == VisualPortKind.Value
		);
		node.RemoveValuePorts();
		node.Properties["function"] = target.Id.ToString("D");

		foreach (VisualVariableSymbol parameter in target.Symbols.Where(symbol => symbol.IsParameter))
		{
			AddValueInput(node, parameter.Id.ToString("N"), parameter.Type);

			if (inputs.TryGetValue(parameter.Id.ToString("N"), out VisualConnection previous)
				&& VisualValueTypes.CanAssign(previous.Output.Type, parameter.Type))
			{
				connections.Add(new VisualConnection(previous.Output, node.GetInput(parameter.Id.ToString("N"))));
			}
		}

		if (target.ReturnType != VisualValueType.None)
		{
			AddValueOutput(node, "result", target.ReturnType);

			foreach (VisualConnection previous in outputs.Where(connection => VisualValueTypes.CanAssign(target.ReturnType, connection.Input.Type)))
			{
				connections.Add(new VisualConnection(node.GetOutput("result"), previous.Input));
			}
		}
	}

	internal VisualNode AddSingleNode(string definitionId, Vector2 position, Guid? id = null)
	{
		VisualNode node = new VisualNode(catalog.Get(definitionId), position, id);

		nodes.Add(node);

		return node;
	}

	internal void AddDeserializedNode(VisualNode node)
	{
		nodes.Add(node);
	}

	public bool RemoveNode(VisualNode node)
	{
		if (node == null)
		{
			throw new ArgumentNullException(nameof(node));
		}

		if (!nodes.Remove(node))
		{
			return false;
		}

		connections.RemoveAll(connection => connection.Input.Node == node || connection.Output.Node == node);
		RemoveOwnedSymbols(node);

		if (node.PairedNodeId.HasValue)
		{
			VisualNode pair = nodes.FirstOrDefault(candidate => candidate.Id == node.PairedNodeId.Value);

			if (pair != null)
			{
				RemoveOwnedSymbols(pair);
				nodes.Remove(pair);
				connections.RemoveAll(connection => connection.Input.Node == pair || connection.Output.Node == pair);
			}
		}

		return true;
	}

	public bool TryConnect(VisualPort output, VisualPort input, out VisualConnection connection)
	{
		connection = null;

		if (output == null)
		{
			throw new ArgumentNullException(nameof(output));
		}

		if (input == null)
		{
			throw new ArgumentNullException(nameof(input));
		}

		if (!nodes.Contains(output.Node)
			|| !nodes.Contains(input.Node)
			|| output.Direction != VisualPortDirection.Output
			|| input.Direction != VisualPortDirection.Input
			|| output.Kind != input.Kind)
		{
			return false;
		}

		if (output.Kind == VisualPortKind.Value && !VisualValueTypes.CanAssign(output.Type, input.Type))
		{
			return false;
		}

		if (TryGetInputConnection(input, out VisualConnection existingInput))
		{
			connections.Remove(existingInput);
		}

		if (output.Kind == VisualPortKind.Execution
			&& TryGetOutputConnections(output).FirstOrDefault() is VisualConnection existingOutput)
		{
			connections.Remove(existingOutput);
		}

		connection = new VisualConnection(output, input);
		connections.Add(connection);

		return true;
	}

	internal void AddDeserializedConnection(VisualConnection connection)
	{
		connections.Add(connection);
	}

	public bool Disconnect(VisualConnection connection)
	{
		return connection != null && connections.Remove(connection);
	}

	public bool TryGetInputConnection(VisualPort input, out VisualConnection connection)
	{
		connection = connections.FirstOrDefault(candidate => candidate.Input == input);

		return connection != null;
	}

	public IEnumerable<VisualConnection> TryGetOutputConnections(VisualPort output)
	{
		return connections.Where(candidate => candidate.Output == output);
	}

	public VisualNode GetNode(Guid id)
	{
		return nodes.Single(node => node.Id == id);
	}

	public IReadOnlyList<VisualNode> DuplicateNodes(
		IEnumerable<Guid> nodeIds,
		Vector2 offset
	)
	{
		return ImportNodes(Function.Graph, nodeIds, offset);
	}

	public IReadOnlyList<VisualNode> ImportNodes(
		VisualGraph sourceGraph,
		IEnumerable<Guid> nodeIds,
		Vector2 offset
	)
	{
		if (sourceGraph == null)
		{
			throw new ArgumentNullException(nameof(sourceGraph));
		}

		HashSet<Guid> selected = new HashSet<Guid>(nodeIds ?? Array.Empty<Guid>());
		VisualNode[] sourceNodes = sourceGraph.nodes.ToArray();
		VisualConnection[] sourceConnections = sourceGraph.connections.ToArray();

		selected.RemoveWhere(id => sourceNodes.All(node => node.Id != id || node.Role == VisualNodeRole.Entry));

		foreach (VisualNode node in sourceNodes.Where(node => selected.Contains(node.Id)))
		{
			if (node.PairedNodeId.HasValue)
			{
				selected.Add(node.PairedNodeId.Value);
			}
		}

		Dictionary<Guid, VisualNode> nodeMap = new Dictionary<Guid, VisualNode>();

		foreach (VisualNode source in sourceNodes.Where(node => selected.Contains(node.Id)))
		{
			VisualNode copy = CloneNode(source, offset);

			nodes.Add(copy);
			nodeMap.Add(source.Id, copy);
		}

		Dictionary<Guid, VisualVariableSymbol> symbolMap = DuplicateOwnedSymbols(
			sourceGraph,
			selected,
			nodeMap
		);

		foreach (VisualNode copy in nodeMap.Values)
		{
			if (copy.Properties.TryGetValue("symbol", out string text)
				&& Guid.TryParse(text, out Guid symbolId)
				&& symbolMap.TryGetValue(symbolId, out VisualVariableSymbol copiedSymbol))
			{
				copy.Properties["symbol"] = copiedSymbol.Id.ToString("D");

				if (copy.DefinitionId == CoreVisualNodes.VariableDeclare)
				{
					copy.Properties["name"] = copiedSymbol.Name;
				}
			}
		}

		foreach (KeyValuePair<Guid, VisualNode> pair in nodeMap)
		{
			VisualNode source = sourceNodes.Single(node => node.Id == pair.Key);

			if (source.PairedNodeId.HasValue
				&& nodeMap.TryGetValue(source.PairedNodeId.Value, out VisualNode copiedPair))
			{
				pair.Value.PairedNodeId = copiedPair.Id;
			}
		}

		foreach (VisualConnection source in sourceConnections)
		{
			if (!nodeMap.TryGetValue(source.Output.Node.Id, out VisualNode outputNode)
				|| !nodeMap.TryGetValue(source.Input.Node.Id, out VisualNode inputNode))
			{
				continue;
			}

			connections.Add(
				new VisualConnection(
					outputNode.GetOutput(source.Output.Name),
					inputNode.GetInput(source.Input.Name)
				)
			);
		}

		return nodeMap.Values.ToArray();
	}

	private static void Pair(VisualNode control, VisualNode boundary)
	{
		control.PairedNodeId = boundary.Id;
		boundary.PairedNodeId = control.Id;
	}

	private Dictionary<Guid, VisualVariableSymbol> DuplicateOwnedSymbols(
		VisualGraph sourceGraph,
		HashSet<Guid> selected,
		IReadOnlyDictionary<Guid, VisualNode> nodeMap
	)
	{
		Dictionary<Guid, VisualVariableSymbol> map = new Dictionary<Guid, VisualVariableSymbol>();

		foreach (VisualVariableSymbol source in sourceGraph.Function.Symbols.Where(symbol =>
			symbol.ScopeNodeId.HasValue && selected.Contains(symbol.ScopeNodeId.Value)
		).ToArray())
		{
			if (!nodeMap.TryGetValue(source.ScopeNodeId.Value, out VisualNode copiedScope))
			{
				continue;
			}

			VisualVariableSymbol copy = Function.AddVariable(
				source.Name + "Copy",
				source.Type,
				false,
				copiedScope.Id
			);

			map.Add(source.Id, copy);
		}

		return map;
	}

	private static VisualNode CloneNode(
		VisualNode source,
		Vector2 offset
	)
	{
		VisualNode copy = new VisualNode(
			Guid.NewGuid(),
			source.DefinitionId,
			source.Title,
			source.Role,
			source.Position + offset
		)
		{
			Width = source.Width,
			IsMissingDefinition = source.IsMissingDefinition,
		};

		foreach (VisualPort port in source.Inputs.Concat(source.Outputs))
		{
			copy.AddPort(
				new VisualPort(
					port.Name,
					port.Label,
					port.Kind,
					port.Direction,
					port.Type,
					port.Optional,
					port.DefaultValue
				)
			);
		}

		foreach (KeyValuePair<string, string> property in source.Properties)
		{
			copy.Properties.Add(property.Key, property.Value);
		}

		return copy;
	}

	private void ConfigureDynamicNode(VisualNode node)
	{
		if (node.DefinitionId == CoreVisualNodes.VariableDeclare)
		{
			VisualVariableSymbol symbol = Function.AddVariable("value", VisualValueType.Integer, scopeNodeId: node.Id);

			node.Properties["symbol"] = symbol.Id.ToString("D");
			AddValueInput(node, "value", symbol.Type, true, "0");
		}
		else if (node.DefinitionId == CoreVisualNodes.VariableGet)
		{
			ConfigureSymbolNode(node, false, true);
		}
		else if (node.DefinitionId == CoreVisualNodes.VariableSet)
		{
			ConfigureSymbolNode(node, true, false);
		}
		else if (node.DefinitionId == CoreVisualNodes.ListCreate)
		{
			AddValueOutput(node, "result", VisualValueType.IntegerList);
		}
		else if (node.DefinitionId == CoreVisualNodes.ListAdd)
		{
			AddListPorts(node, true, false);
		}
		else if (node.DefinitionId == CoreVisualNodes.ListGet)
		{
			AddListPorts(node, false, true);
		}
		else if (node.DefinitionId == CoreVisualNodes.ListCount)
		{
			AddValueInput(node, "list", VisualValueType.IntegerList);
		}
		else if (node.DefinitionId == CoreVisualNodes.Return && Function.ReturnType != VisualValueType.None)
		{
			AddValueInput(node, "value", Function.ReturnType);
		}
		else if (node.DefinitionId == CoreVisualNodes.ForEach)
		{
			VisualVariableSymbol symbol = Function.AddVariable(
				"item",
				VisualValueType.Integer,
				false,
				node.Id
			);

			node.Properties["symbol"] = symbol.Id.ToString("D");
			AddValueInput(node, "list", VisualValueType.IntegerList);
		}
	}

	private VisualVariableSymbol SymbolFor(VisualNode node)
	{
		return node.Properties.TryGetValue("symbol", out string text)
			&& Guid.TryParse(text, out Guid id)
			&& Function.TryGetSymbol(id, out VisualVariableSymbol symbol)
			? symbol
			: null;
	}

	private void RemoveOwnedSymbols(VisualNode node)
	{
		foreach (VisualVariableSymbol symbol in Function.Symbols.Where(candidate => candidate.ScopeNodeId == node.Id).ToArray())
		{
			Function.RemoveVariable(symbol);
		}
	}

	private void ConfigureSymbolNode(VisualNode node, bool addInput, bool addOutput)
	{
		VisualVariableSymbol symbol = Function.Symbols.FirstOrDefault();
		VisualValueType type = symbol?.Type ?? VisualValueType.Integer;

		node.Properties["symbol"] = symbol?.Id.ToString("D") ?? "";

		if (addInput)
		{
			AddValueInput(node, "value", type);
		}

		if (addOutput)
		{
			AddValueOutput(node, "result", type);
		}
	}

	private static void AddListPorts(VisualNode node, bool addValueInput, bool addResult)
	{
		VisualValueType listType = VisualValueType.IntegerList;

		AddValueInput(node, "list", listType);

		if (addValueInput)
		{
			AddValueInput(node, "value", VisualValueTypes.ElementType(listType));
		}

		if (addResult)
		{
			AddValueOutput(node, "result", VisualValueTypes.ElementType(listType));
		}
	}

	internal static void AddValueInput(
		VisualNode node,
		string name,
		VisualValueType type,
		bool optional = false,
		string defaultValue = null
	)
	{
		node.AddPort(
			new VisualPort(
				name,
				name,
				VisualPortKind.Value,
				VisualPortDirection.Input,
				type,
				optional,
				defaultValue
			)
		);
	}

	internal static void AddValueOutput(VisualNode node, string name, VisualValueType type)
	{
		node.AddPort(
			new VisualPort(
				name,
				name,
				VisualPortKind.Value,
				VisualPortDirection.Output,
				type,
				false,
				null
			)
		);
	}
}

