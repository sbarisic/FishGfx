using System;
using System.Collections.Generic;
using System.Linq;

namespace FishGfx.NodeGraph;

public sealed partial class VisualProgramValidator
{
	private void ValidateValueAvailability(VisualFunction function)
	{
		VisualNode entry = function.Graph.Nodes.FirstOrDefault(node => node.Role == VisualNodeRole.Entry);

		if (entry == null)
		{
			return;
		}

		Dictionary<Guid, ExecutionLocation> locations = BuildExecutionLocations(function, entry);
		Dictionary<Guid, VisualNode> declarations = function.Graph.Nodes
			.Where(node => node.DefinitionId == CoreVisualNodes.VariableDeclare)
			.Where(node => TryPropertyGuid(node, "symbol", out _))
			.GroupBy(node => Guid.Parse(node.Properties["symbol"]))
			.ToDictionary(group => group.Key, group => group.First());

		foreach (VisualNode node in function.Graph.Nodes)
		{
			if (node.DefinitionId == CoreVisualNodes.VariableGet
				|| node.DefinitionId == CoreVisualNodes.VariableSet
				|| node.DefinitionId == CoreVisualNodes.VariableIncrement)
			{
				ValidateVariableUse(function, node, locations, declarations);
			}

			if (node.DefinitionId == CoreVisualNodes.ConsoleReadLine
				|| node.DefinitionId == CoreVisualNodes.FunctionCall)
			{
				ValidateStatefulResult(function, node, locations);
			}
		}
	}

	private void ValidateVariableUse(
		VisualFunction function,
		VisualNode node,
		IReadOnlyDictionary<Guid, ExecutionLocation> locations,
		IReadOnlyDictionary<Guid, VisualNode> declarations
	)
	{
		if (!TryPropertyGuid(node, "symbol", out Guid symbolId)
			|| !function.TryGetSymbol(symbolId, out VisualVariableSymbol symbol)
			|| symbol.IsParameter)
		{
			return;
		}

		IReadOnlyList<VisualNode> consumers = node.DefinitionId == CoreVisualNodes.VariableGet
			? FindValueConsumers(function.Graph, node)
			: new[] { node };

		foreach (VisualNode consumer in consumers)
		{
			if (!locations.TryGetValue(consumer.Id, out ExecutionLocation consumerLocation))
			{
				continue;
			}

			bool available;

			if (symbol.ScopeNodeId.HasValue
				&& function.Graph.Nodes.FirstOrDefault(candidate => candidate.Id == symbol.ScopeNodeId.Value)
					is VisualNode scope
				&& scope.DefinitionId == CoreVisualNodes.ForEach)
			{
				available = consumerLocation.Scopes.Any(candidate =>
					candidate.OwnerId == scope.Id && candidate.Region == "body"
				);
			}
			else
			{
				available = declarations.TryGetValue(symbol.Id, out VisualNode declaration)
					&& locations.TryGetValue(declaration.Id, out ExecutionLocation declarationLocation)
					&& Dominates(declarationLocation, consumerLocation);
			}

			if (!available)
			{
				Add(
					"VPG085",
					$"Variable '{symbol.Name}' is used before it is available in this execution path.",
					function,
					node
				);
				return;
			}
		}
	}

	private void ValidateStatefulResult(
		VisualFunction function,
		VisualNode producer,
		IReadOnlyDictionary<Guid, ExecutionLocation> locations
	)
	{
		IReadOnlyList<VisualNode> consumers = FindValueConsumers(function.Graph, producer);

		if (consumers.Count == 0)
		{
			return;
		}

		foreach (VisualNode consumer in consumers)
		{
			if (!locations.TryGetValue(consumer.Id, out ExecutionLocation consumerLocation))
			{
				continue;
			}

			if (!locations.TryGetValue(producer.Id, out ExecutionLocation producerLocation)
				|| !Dominates(producerLocation, consumerLocation))
			{
				Add(
					"VPG086",
					$"'{producer.Title}' must execute before every statement that uses its result.",
					function,
					producer
				);
				return;
			}
		}
	}

	private static IReadOnlyList<VisualNode> FindValueConsumers(VisualGraph graph, VisualNode source)
	{
		List<VisualNode> consumers = new List<VisualNode>();
		Stack<VisualNode> pending = new Stack<VisualNode>();
		HashSet<Guid> visited = new HashSet<Guid>();

		PushConnectedValueNodes(graph, source, pending);

		while (pending.Count > 0)
		{
			VisualNode node = pending.Pop();

			if (!visited.Add(node.Id))
			{
				continue;
			}

			if (node.Role != VisualNodeRole.Expression)
			{
				consumers.Add(node);
				continue;
			}

			PushConnectedValueNodes(graph, node, pending);
		}

		return consumers;
	}

	private static void PushConnectedValueNodes(
		VisualGraph graph,
		VisualNode source,
		Stack<VisualNode> pending
	)
	{
		foreach (VisualPort output in source.Outputs.Where(port => port.Kind == VisualPortKind.Value))
		{
			foreach (VisualConnection connection in graph.TryGetOutputConnections(output))
			{
				pending.Push(connection.Input.Node);
			}
		}
	}

	private static Dictionary<Guid, ExecutionLocation> BuildExecutionLocations(
		VisualFunction function,
		VisualNode entry
	)
	{
		Dictionary<Guid, ExecutionLocation> locations = new Dictionary<Guid, ExecutionLocation>();

		LocateBlock(
			function.Graph,
			Next(function.Graph, entry, "next"),
			null,
			Array.Empty<ExecutionScope>(),
			locations
		);

		return locations;
	}

	private static void LocateBlock(
		VisualGraph graph,
		VisualNode node,
		Guid? stopNodeId,
		IReadOnlyList<ExecutionScope> scopes,
		Dictionary<Guid, ExecutionLocation> locations
	)
	{
		HashSet<Guid> visited = new HashSet<Guid>();
		int index = 0;

		while (node != null && node.Id != stopNodeId && visited.Add(node.Id))
		{
			locations.TryAdd(node.Id, new ExecutionLocation(scopes, index));

			if (node.Role == VisualNodeRole.Branch && node.PairedNodeId.HasValue)
			{
				LocateBlock(
					graph,
					Next(graph, node, "then"),
					node.PairedNodeId,
					AppendScope(scopes, node.Id, "then", index),
					locations
				);
				LocateBlock(
					graph,
					Next(graph, node, "else"),
					node.PairedNodeId,
					AppendScope(scopes, node.Id, "else", index),
					locations
				);
				node = PairedNext(graph, node);
			}
			else if (node.Role == VisualNodeRole.Loop && node.PairedNodeId.HasValue)
			{
				LocateBlock(
					graph,
					Next(graph, node, "body"),
					node.PairedNodeId,
					AppendScope(scopes, node.Id, "body", index),
					locations
				);
				node = Next(graph, node, "completed");
			}
			else
			{
				if (node.DefinitionId == CoreVisualNodes.Return
					|| node.DefinitionId == CoreVisualNodes.Break
					|| node.DefinitionId == CoreVisualNodes.Continue)
				{
					return;
				}

				node = Next(graph, node, "next");
			}

			index++;
		}
	}

	private static IReadOnlyList<ExecutionScope> AppendScope(
		IReadOnlyList<ExecutionScope> scopes,
		Guid ownerId,
		string region,
		int ownerIndex
	)
	{
		ExecutionScope[] nested = new ExecutionScope[scopes.Count + 1];

		for (int index = 0; index < scopes.Count; index++)
		{
			nested[index] = scopes[index];
		}

		nested[^1] = new ExecutionScope(ownerId, region, ownerIndex);

		return nested;
	}

	private static bool Dominates(ExecutionLocation producer, ExecutionLocation consumer)
	{
		if (producer.Scopes.Count > consumer.Scopes.Count)
		{
			return false;
		}

		for (int index = 0; index < producer.Scopes.Count; index++)
		{
			if (!producer.Scopes[index].Equals(consumer.Scopes[index]))
			{
				return false;
			}
		}

		if (producer.Scopes.Count == consumer.Scopes.Count)
		{
			return producer.Index < consumer.Index;
		}

		return producer.Index < consumer.Scopes[producer.Scopes.Count].OwnerIndex;
	}

	private static bool AllPathsReturn(VisualFunction function)
	{
		VisualNode entry = function.Graph.Nodes.FirstOrDefault(node => node.Role == VisualNodeRole.Entry);

		return entry != null
			&& ReturnsFrom(
				function.Graph,
				Next(function.Graph, entry, "next"),
				null,
				new HashSet<Guid>()
			);
	}

	private static bool ReturnsFrom(
		VisualGraph graph,
		VisualNode node,
		Guid? stopNodeId,
		HashSet<Guid> visited
	)
	{
		while (node != null && node.Id != stopNodeId && visited.Add(node.Id))
		{
			if (node.DefinitionId == CoreVisualNodes.Return)
			{
				return true;
			}

			if (node.Role == VisualNodeRole.Branch && node.PairedNodeId.HasValue)
			{
				bool thenReturns = ReturnsFrom(
					graph,
					Next(graph, node, "then"),
					node.PairedNodeId,
					new HashSet<Guid>(visited)
				);
				bool elseReturns = ReturnsFrom(
					graph,
					Next(graph, node, "else"),
					node.PairedNodeId,
					new HashSet<Guid>(visited)
				);

				if (thenReturns && elseReturns)
				{
					return true;
				}

				return ReturnsFrom(
					graph,
					PairedNext(graph, node),
					stopNodeId,
					new HashSet<Guid>(visited)
				);
			}

			if (node.Role == VisualNodeRole.Loop)
			{
				node = Next(graph, node, "completed");
				continue;
			}

			if (node.DefinitionId == CoreVisualNodes.Break
				|| node.DefinitionId == CoreVisualNodes.Continue)
			{
				return false;
			}

			node = Next(graph, node, "next");
		}

		return false;
	}

	private sealed class ExecutionLocation
	{
		internal IReadOnlyList<ExecutionScope> Scopes { get; }
		internal int Index { get; }

		internal ExecutionLocation(IReadOnlyList<ExecutionScope> scopes, int index)
		{
			Scopes = scopes;
			Index = index;
		}
	}

	private readonly struct ExecutionScope : IEquatable<ExecutionScope>
	{
		internal Guid OwnerId { get; }
		internal string Region { get; }
		internal int OwnerIndex { get; }

		internal ExecutionScope(Guid ownerId, string region, int ownerIndex)
		{
			OwnerId = ownerId;
			Region = region;
			OwnerIndex = ownerIndex;
		}

		public bool Equals(ExecutionScope other)
		{
			return OwnerId == other.OwnerId
				&& string.Equals(Region, other.Region, StringComparison.Ordinal)
				&& OwnerIndex == other.OwnerIndex;
		}

		public override bool Equals(object value)
		{
			return value is ExecutionScope other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(OwnerId, Region, OwnerIndex);
		}
	}

}
