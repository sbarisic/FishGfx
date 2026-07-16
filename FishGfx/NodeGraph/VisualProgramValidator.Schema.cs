using System;
using System.Collections.Generic;
using System.Linq;

namespace FishGfx.NodeGraph;

public sealed partial class VisualProgramValidator
{
	private void ValidateNodeSchema(
		VisualFunction function,
		VisualNode node,
		VisualNodeDefinition definition
	)
	{
		if (node.Role != definition.Role)
		{
			Add("VPG034", $"'{node.Title}' has an invalid node role.", function, node);
		}

		if (node.Inputs.Concat(node.Outputs).Any(port =>
			string.IsNullOrWhiteSpace(port.Name)
				|| !Enum.IsDefined(port.Kind)
				|| !Enum.IsDefined(port.Direction)
				|| !Enum.IsDefined(port.Type)
				|| port.Kind == VisualPortKind.Execution && port.Type != VisualValueType.None
				|| port.Kind == VisualPortKind.Value && port.Type == VisualValueType.None
				|| (port.Optional || port.DefaultValue != null)
				&& (port.Kind != VisualPortKind.Value || port.Direction != VisualPortDirection.Input)
		))
		{
			Add("VPG035", $"'{node.Title}' contains an invalid port.", function, node);
			return;
		}

		IReadOnlyList<PortShape> expected = ExpectedPorts(function, node, definition);

		if (expected == null)
		{
			return;
		}

		PortShape[] actual = node.Inputs
			.Concat(node.Outputs)
			.Select(port => new PortShape(port))
			.OrderBy(port => port.Direction)
			.ThenBy(port => port.Name, StringComparer.Ordinal)
			.ToArray();
		PortShape[] orderedExpected = expected
			.OrderBy(port => port.Direction)
			.ThenBy(port => port.Name, StringComparer.Ordinal)
			.ToArray();

		if (!actual.SequenceEqual(orderedExpected))
		{
			Add("VPG036", $"'{node.Title}' ports do not match its node definition.", function, node);
		}
	}

	private static IReadOnlyList<PortShape> ExpectedPorts(
		VisualFunction function,
		VisualNode node,
		VisualNodeDefinition definition
	)
	{
		List<PortShape> ports = definition.Ports.Select(port => new PortShape(port)).ToList();

		switch (node.DefinitionId)
		{
			case CoreVisualNodes.VariableDeclare:
				return SymbolPorts(function, node, ports, "value", VisualPortDirection.Input, true);
			case CoreVisualNodes.VariableGet:
				return SymbolPorts(function, node, ports, "result", VisualPortDirection.Output, false);
			case CoreVisualNodes.VariableSet:
				return SymbolPorts(function, node, ports, "value", VisualPortDirection.Input, false);
			case CoreVisualNodes.ListCreate:
				return ListPorts(node, ports, addList: false, addValue: false, addIndex: false, addResult: true);
			case CoreVisualNodes.ListAdd:
				return ListPorts(node, ports, addList: true, addValue: true, addIndex: false, addResult: false);
			case CoreVisualNodes.ListGet:
				return ListPorts(node, ports, addList: true, addValue: false, addIndex: true, addResult: true);
			case CoreVisualNodes.ListCount:
				return ListPorts(node, ports, addList: true, addValue: false, addIndex: false, addResult: false);
			case CoreVisualNodes.ForEach:
				return ListPorts(node, ports, addList: true, addValue: false, addIndex: false, addResult: false);
			case CoreVisualNodes.Return:
				if (function.ReturnType != VisualValueType.None)
				{
					ports.Add(PortShape.ValueInput("value", function.ReturnType));
				}

				return ports;
			case CoreVisualNodes.FunctionCall:
				if (!TryPropertyGuid(node, "function", out Guid functionId)
					|| function.Program.Functions.FirstOrDefault(candidate => candidate.Id == functionId)
						is not VisualFunction target
					|| target.IsEntryPoint)
				{
					return null;
				}

				foreach (VisualVariableSymbol parameter in target.Symbols.Where(symbol => symbol.IsParameter))
				{
					ports.Add(PortShape.ValueInput(parameter.Id.ToString("N"), parameter.Type));
				}

				if (target.ReturnType != VisualValueType.None)
				{
					ports.Add(PortShape.ValueOutput("result", target.ReturnType));
				}

				return ports;
			default:
				return ports;
		}
	}

	private static IReadOnlyList<PortShape> SymbolPorts(
		VisualFunction function,
		VisualNode node,
		List<PortShape> ports,
		string name,
		VisualPortDirection direction,
		bool optional
	)
	{
		if (!TryPropertyGuid(node, "symbol", out Guid symbolId)
			|| !function.TryGetSymbol(symbolId, out VisualVariableSymbol symbol))
		{
			return null;
		}

		ports.Add(
			direction == VisualPortDirection.Input
				? PortShape.ValueInput(name, symbol.Type, optional)
				: PortShape.ValueOutput(name, symbol.Type)
		);

		return ports;
	}

	private static IReadOnlyList<PortShape> ListPorts(
		VisualNode node,
		List<PortShape> ports,
		bool addList,
		bool addValue,
		bool addIndex,
		bool addResult
	)
	{
		if (!node.Properties.TryGetValue("type", out string text)
			|| !Enum.TryParse(text, out VisualValueType listType)
			|| !VisualValueTypes.IsList(listType))
		{
			return null;
		}

		ports.RemoveAll(port => port.Kind == VisualPortKind.Value);

		if (addList)
		{
			ports.Add(PortShape.ValueInput("list", listType));
		}

		if (addValue)
		{
			ports.Add(PortShape.ValueInput("value", VisualValueTypes.ElementType(listType)));
		}

		if (addIndex)
		{
			ports.Add(PortShape.ValueInput("index", VisualValueType.Integer));
		}

		if (addResult)
		{
			VisualValueType resultType = node.DefinitionId == CoreVisualNodes.ListCreate
				? listType
				: VisualValueTypes.ElementType(listType);

			ports.Add(PortShape.ValueOutput("result", resultType));
		}

		if (node.DefinitionId == CoreVisualNodes.ListCount)
		{
			ports.Add(PortShape.ValueOutput("result", VisualValueType.Integer));
		}

		return ports;
	}

	private readonly struct PortShape : IEquatable<PortShape>
	{
		internal string Name { get; }
		internal VisualPortKind Kind { get; }
		internal VisualPortDirection Direction { get; }
		internal VisualValueType Type { get; }
		internal bool Optional { get; }

		internal PortShape(VisualPortDefinition port)
			: this(port.Name, port.Kind, port.Direction, port.Type, port.Optional)
		{
		}

		internal PortShape(VisualPort port)
			: this(port.Name, port.Kind, port.Direction, port.Type, port.Optional)
		{
		}

		private PortShape(
			string name,
			VisualPortKind kind,
			VisualPortDirection direction,
			VisualValueType type,
			bool optional
		)
		{
			Name = name;
			Kind = kind;
			Direction = direction;
			Type = type;
			Optional = optional;
		}

		internal static PortShape ValueInput(
			string name,
			VisualValueType type,
			bool optional = false
		)
		{
			return new PortShape(
				name,
				VisualPortKind.Value,
				VisualPortDirection.Input,
				type,
				optional
			);
		}

		internal static PortShape ValueOutput(string name, VisualValueType type)
		{
			return new PortShape(
				name,
				VisualPortKind.Value,
				VisualPortDirection.Output,
				type,
				false
			);
		}

		public bool Equals(PortShape other)
		{
			return string.Equals(Name, other.Name, StringComparison.Ordinal)
				&& Kind == other.Kind
				&& Direction == other.Direction
				&& Type == other.Type
				&& Optional == other.Optional;
		}

		public override bool Equals(object value)
		{
			return value is PortShape other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(Name, Kind, Direction, Type, Optional);
		}
	}
}
