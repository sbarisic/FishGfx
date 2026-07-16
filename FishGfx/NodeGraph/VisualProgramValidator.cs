using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FishGfx.NodeGraph;

public sealed partial class VisualProgramValidator
{
	private readonly List<VisualProgramDiagnostic> diagnostics = new List<VisualProgramDiagnostic>();

	public VisualProgramValidationResult Validate(VisualProgram program)
	{
		if (program == null)
		{
			throw new ArgumentNullException(nameof(program));
		}

		diagnostics.Clear();

		if (!CSharpNames.IsIdentifier(program.Name)
			|| CSharpNames.IsReservedGeneratedName(program.Name))
		{
			Add("VPG001", $"'{program.Name}' is not a valid C# program name.");
		}

		if (program.Functions.Count(function => function.IsEntryPoint) != 1)
		{
			Add("VPG002", "A visual program must contain exactly one entry point.");
		}

		HashSet<string> functionNames = new HashSet<string>(StringComparer.Ordinal);

		foreach (VisualFunction function in program.Functions)
		{
			ValidateFunction(function, functionNames);
		}

		return new VisualProgramValidationResult(new List<VisualProgramDiagnostic>(diagnostics));
	}

	private void ValidateFunction(VisualFunction function, HashSet<string> functionNames)
	{
		if (!CSharpNames.IsIdentifier(function.Name)
			|| CSharpNames.IsReservedGeneratedName(function.Name))
		{
			Add("VPG010", $"'{function.Name}' is not a valid C# function name.", function);
		}
		else if (!functionNames.Add(function.Name))
		{
			Add("VPG011", $"Function name '{function.Name}' is duplicated.", function);
		}

		if (function.IsEntryPoint && function.ReturnType != VisualValueType.None)
		{
			Add("VPG012", "The Main entry point must return void.", function);
		}

		if (!Enum.IsDefined(function.ReturnType))
		{
			Add("VPG015", "The function has an unsupported return type.", function);
		}

		if (function.IsEntryPoint && !string.Equals(function.Name, "Main", StringComparison.Ordinal))
		{
			Add("VPG013", "The entry-point function must be named 'Main'.", function);
		}

		if (!function.IsEntryPoint
			&& string.Equals(function.Name, function.Program.Name, StringComparison.Ordinal))
		{
			Add("VPG014", "A function cannot have the same name as the generated program class.", function);
		}

		ValidateSymbols(function);
		ValidateNodes(function);
		ValidateConnections(function);
		ValidateValueCycles(function);
		ValidateControlFlow(function);
		ValidateValueAvailability(function);

		if (!function.IsEntryPoint
			&& function.ReturnType != VisualValueType.None
			&& !AllPathsReturn(function))
		{
			Add("VPG084", $"Function '{function.Name}' must return a value on every path.", function);
		}
	}

	private void ValidateSymbols(VisualFunction function)
	{
		HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);

		foreach (VisualVariableSymbol symbol in function.Symbols)
		{
			if (!CSharpNames.IsIdentifier(symbol.Name)
				|| CSharpNames.IsReservedGeneratedName(symbol.Name))
			{
				Add("VPG020", $"'{symbol.Name}' is not a valid variable name.", function);
			}
			else if (!names.Add(symbol.Name))
			{
				Add("VPG021", $"Variable name '{symbol.Name}' is duplicated.", function);
			}

			if (symbol.Type == VisualValueType.None)
			{
				Add("VPG022", $"Variable '{symbol.Name}' cannot have type void.", function);
			}
			else if (!Enum.IsDefined(symbol.Type))
			{
				Add("VPG025", $"Variable '{symbol.Name}' has an unsupported type.", function);
			}

			if (symbol.IsParameter && (function.IsEntryPoint || symbol.ScopeNodeId.HasValue))
			{
				Add("VPG026", $"Parameter '{symbol.Name}' has an invalid scope.", function);
			}
			else if (!symbol.IsParameter && !symbol.ScopeNodeId.HasValue)
			{
				Add("VPG027", $"Variable '{symbol.Name}' is missing its declaration scope.", function);
			}

			if (symbol.ScopeNodeId.HasValue
				&& function.Graph.Nodes.All(node => node.Id != symbol.ScopeNodeId.Value))
			{
				Add("VPG023", $"Variable '{symbol.Name}' references a missing scope node.", function);
			}
			else if (symbol.ScopeNodeId.HasValue)
			{
				VisualNode scope = function.Graph.GetNode(symbol.ScopeNodeId.Value);

				if (scope.DefinitionId != CoreVisualNodes.VariableDeclare
					&& scope.DefinitionId != CoreVisualNodes.ForEach)
				{
					Add("VPG024", $"Variable '{symbol.Name}' references an unsupported scope.", function);
				}
			}
		}
	}

	private void ValidateNodes(VisualFunction function)
	{
		VisualGraph graph = function.Graph;

		if (graph.Nodes.Count(node => node.Role == VisualNodeRole.Entry) != 1)
		{
			Add("VPG030", $"Function '{function.Name}' must have exactly one entry node.", function);
		}

		foreach (VisualNode node in graph.Nodes)
		{
			if (node.IsMissingDefinition
				|| !function.Program.Catalog.TryGet(node.DefinitionId, out VisualNodeDefinition definition))
			{
				Add("VPG031", $"Node definition '{node.DefinitionId}' is unavailable.", function, node);
			}
			else
			{
				if (!CoreVisualNodes.SupportsGeneration(node.DefinitionId))
				{
					Add("VPG037", $"Node definition '{node.DefinitionId}' has no C# generator.", function, node);
				}
				else
				{
					ValidateNodeSchema(function, node, definition);
				}
			}

			ValidatePair(function, node);
			ValidateNodeProperties(function, node);
			ValidateRequiredInputs(function, node);
		}
	}

	private void ValidatePair(VisualFunction function, VisualNode node)
	{
		bool requiresPair = node.Role == VisualNodeRole.Branch
			|| node.Role == VisualNodeRole.Loop
			|| node.Role == VisualNodeRole.Merge
			|| node.Role == VisualNodeRole.LoopEnd;

		if (!requiresPair)
		{
			return;
		}

		VisualNode pair = node.PairedNodeId.HasValue
			? function.Graph.Nodes.FirstOrDefault(candidate => candidate.Id == node.PairedNodeId.Value)
			: null;

		if (pair == null || pair.PairedNodeId != node.Id)
		{
			Add("VPG032", $"'{node.Title}' has a missing or invalid structural pair.", function, node);
			return;
		}

		if (node.Role == VisualNodeRole.Branch && pair.Role != VisualNodeRole.Merge
			|| node.Role == VisualNodeRole.Loop && pair.Role != VisualNodeRole.LoopEnd
			|| node.Role == VisualNodeRole.Merge && pair.Role != VisualNodeRole.Branch
			|| node.Role == VisualNodeRole.LoopEnd && pair.Role != VisualNodeRole.Loop)
		{
			Add("VPG033", $"'{node.Title}' is paired with the wrong boundary node.", function, node);
		}
	}

	private void ValidateNodeProperties(VisualFunction function, VisualNode node)
	{
		if (IsLiteral(node.DefinitionId))
		{
			VisualValueType type = node.Outputs.FirstOrDefault()?.Type ?? VisualValueType.None;
			string value = node.Properties.TryGetValue("value", out string text) ? text : null;

			if (!VisualLiterals.IsValid(value, type))
			{
				Add("VPG040", $"'{value}' is not a valid {VisualValueTypes.DisplayName(type)} literal.", function, node);
			}
		}

		if (UsesSymbol(node.DefinitionId))
		{
			if (!TryPropertyGuid(node, "symbol", out Guid symbolId)
				|| !function.TryGetSymbol(symbolId, out VisualVariableSymbol symbol))
			{
				Add("VPG041", "Select a valid variable for this node.", function, node);
			}
			else
			{
				ValidateSymbolPorts(function, node, symbol);
			}
		}

		if (node.DefinitionId == CoreVisualNodes.FunctionCall)
		{
			if (!TryPropertyGuid(node, "function", out Guid functionId)
				|| function.Program.Functions.All(candidate => candidate.Id != functionId)
				|| function.Program.GetFunction(functionId).IsEntryPoint)
			{
				Add("VPG042", "Select a valid function to call.", function, node);
			}
			else
			{
				VisualFunction target = function.Program.GetFunction(functionId);
				string[] expectedInputs = target.Symbols
					.Where(symbol => symbol.IsParameter)
					.Select(symbol => symbol.Id.ToString("N"))
					.OrderBy(name => name, StringComparer.Ordinal)
					.ToArray();
				string[] actualInputs = node.Inputs
					.Where(port => port.Kind == VisualPortKind.Value)
					.Select(port => port.Name)
					.OrderBy(name => name, StringComparer.Ordinal)
					.ToArray();

				if (!expectedInputs.SequenceEqual(actualInputs)
					|| target.ReturnType == VisualValueType.None && node.Outputs.Any(port => port.Kind == VisualPortKind.Value)
					|| target.ReturnType != VisualValueType.None
					&& node.Outputs.All(port => port.Kind != VisualPortKind.Value || port.Type != target.ReturnType))
				{
					Add("VPG045", "Function-call ports do not match the target signature.", function, node);
				}
			}
		}

		if (node.DefinitionId == CoreVisualNodes.ForEach)
		{
			if (!TryPropertyGuid(node, "symbol", out Guid symbolId)
				|| !function.TryGetSymbol(symbolId, out VisualVariableSymbol symbol)
				|| symbol.ScopeNodeId != node.Id
				|| !node.TryGetInput("list", out VisualPort list)
				|| !VisualValueTypes.IsList(list.Type)
				|| VisualValueTypes.ElementType(list.Type) != symbol.Type)
			{
				Add("VPG044", "The for-each item variable does not match its list type.", function, node);
			}
		}

		if (IsListNode(node.DefinitionId)
			&& (!node.Properties.TryGetValue("type", out string listTypeText)
				|| !Enum.TryParse(listTypeText, out VisualValueType listType)
				|| !VisualValueTypes.IsList(listType)))
		{
			Add("VPG046", "Select a valid list type for this node.", function, node);
		}
	}

	private void ValidateSymbolPorts(
		VisualFunction function,
		VisualNode node,
		VisualVariableSymbol symbol
	)
	{
		foreach (VisualPort port in node.Inputs.Concat(node.Outputs).Where(port => port.Kind == VisualPortKind.Value))
		{
			if (port.Name != "amount" && port.Type != symbol.Type)
			{
				Add("VPG043", $"Variable node type does not match '{symbol.Name}'.", function, node, port.Name);
			}
		}
	}

	private void ValidateRequiredInputs(VisualFunction function, VisualNode node)
	{
		foreach (VisualPort input in node.Inputs.Where(port => port.Kind == VisualPortKind.Value))
		{
			if (function.Graph.TryGetInputConnection(input, out _))
			{
				continue;
			}

			if (!input.Optional)
			{
				Add("VPG050", $"Connect the required '{input.Label}' input.", function, node, input.Name);
			}
			else if (!VisualLiterals.IsValid(input.DefaultValue, input.Type))
			{
				Add("VPG051", $"The inline value for '{input.Label}' is invalid.", function, node, input.Name);
			}
		}
	}

	private void ValidateConnections(VisualFunction function)
	{
		HashSet<VisualPort> inputs = new HashSet<VisualPort>();
		HashSet<VisualPort> executionOutputs = new HashSet<VisualPort>();

		foreach (VisualConnection connection in function.Graph.Connections)
		{
			if (!inputs.Add(connection.Input))
			{
				Add("VPG060", $"Input '{connection.Input.Label}' has multiple connections.", function, connection.Input.Node, connection.Input.Name);
			}

			if (connection.Kind == VisualPortKind.Execution && !executionOutputs.Add(connection.Output))
			{
				Add("VPG061", "Execution outputs can connect to only one next statement.", function, connection.Output.Node, connection.Output.Name);
			}

			if (connection.Output.Kind != connection.Input.Kind
				|| connection.Kind == VisualPortKind.Value
				&& !VisualValueTypes.CanAssign(connection.Output.Type, connection.Input.Type))
			{
				Add("VPG062", "Connection port types are incompatible.", function, connection.Input.Node, connection.Input.Name);
			}
		}
	}

	private void ValidateValueCycles(VisualFunction function)
	{
		Dictionary<Guid, int> states = new Dictionary<Guid, int>();

		foreach (VisualNode node in function.Graph.Nodes)
		{
			VisitValueNode(function, node, states);
		}
	}

	private bool VisitValueNode(VisualFunction function, VisualNode node, Dictionary<Guid, int> states)
	{
		if (states.TryGetValue(node.Id, out int state))
		{
			if (state == 1)
			{
				Add("VPG070", "Value connections contain a cycle.", function, node);
				return true;
			}

			return false;
		}

		states[node.Id] = 1;

		foreach (VisualPort input in node.Inputs.Where(port => port.Kind == VisualPortKind.Value))
		{
			if (function.Graph.TryGetInputConnection(input, out VisualConnection connection)
				&& VisitValueNode(function, connection.Output.Node, states))
			{
				break;
			}
		}

		states[node.Id] = 2;

		return false;
	}

	private void ValidateControlFlow(VisualFunction function)
	{
		VisualNode entry = function.Graph.Nodes.FirstOrDefault(node => node.Role == VisualNodeRole.Entry);

		if (entry == null)
		{
			return;
		}

		HashSet<Guid> active = new HashSet<Guid>();
		HashSet<Guid> reached = new HashSet<Guid>();

		Walk(function, Next(function.Graph, entry, "next"), null, active, reached, null);

		foreach (VisualNode node in function.Graph.Nodes)
		{
			if (node.Role != VisualNodeRole.Expression
				&& node.Role != VisualNodeRole.Entry
				&& !reached.Contains(node.Id))
			{
				Add("VPG081", $"'{node.Title}' is not connected to the entry flow.", function, node, severity: VisualDiagnosticSeverity.Warning);
			}
		}
	}

	private void Walk(
		VisualFunction function,
		VisualNode node,
		Guid? stopNodeId,
		HashSet<Guid> active,
		HashSet<Guid> reached,
		VisualNode owningLoop
	)
	{
		while (node != null && node.Id != stopNodeId)
		{
			if (!active.Add(node.Id))
			{
				Add("VPG080", "Execution flow contains an unsupported cycle.", function, node);
				return;
			}

			reached.Add(node.Id);

			if (node.Role == VisualNodeRole.Merge || node.Role == VisualNodeRole.LoopEnd)
			{
				Add("VPG082", "Execution entered a structural boundary from the wrong region.", function, node);
				active.Remove(node.Id);
				return;
			}

			if (node.Role == VisualNodeRole.Branch)
			{
				WalkBranch(function, node, active, reached, owningLoop);
				node = PairedNext(function.Graph, node);
			}
			else if (node.Role == VisualNodeRole.Loop)
			{
				WalkLoop(function, node, active, reached);
				node = Next(function.Graph, node, "completed");
			}
			else
			{
				if ((node.DefinitionId == CoreVisualNodes.Break
					|| node.DefinitionId == CoreVisualNodes.Continue)
					&& owningLoop == null)
				{
					Add("VPG083", $"'{node.Title}' can only be used inside a loop.", function, node);
				}

				if (node.DefinitionId == CoreVisualNodes.Return
					|| node.DefinitionId == CoreVisualNodes.Break
					|| node.DefinitionId == CoreVisualNodes.Continue)
				{
					active.Remove(node.Id);
					return;
				}

				node = Next(function.Graph, node, "next");
			}
		}

		if (node != null)
		{
			reached.Add(node.Id);
		}

		active.Clear();
	}

	private void WalkBranch(
		VisualFunction function,
		VisualNode branch,
		HashSet<Guid> active,
		HashSet<Guid> reached,
		VisualNode owningLoop
	)
	{
		if (!branch.PairedNodeId.HasValue)
		{
			return;
		}

		Walk(function, Next(function.Graph, branch, "then"), branch.PairedNodeId, new HashSet<Guid>(active), reached, owningLoop);
		Walk(function, Next(function.Graph, branch, "else"), branch.PairedNodeId, new HashSet<Guid>(active), reached, owningLoop);
	}

	private void WalkLoop(
		VisualFunction function,
		VisualNode loop,
		HashSet<Guid> active,
		HashSet<Guid> reached
	)
	{
		if (loop.PairedNodeId.HasValue)
		{
			Walk(function, Next(function.Graph, loop, "body"), loop.PairedNodeId, new HashSet<Guid>(active), reached, loop);
		}
	}

	internal static VisualNode Next(VisualGraph graph, VisualNode node, string outputName)
	{
		if (node == null || !node.TryGetOutput(outputName, out VisualPort output))
		{
			return null;
		}

		return graph.TryGetOutputConnections(output).FirstOrDefault()?.Input.Node;
	}

	private static VisualNode PairedNext(VisualGraph graph, VisualNode node)
	{
		if (!node.PairedNodeId.HasValue)
		{
			return null;
		}

		VisualNode pair = graph.Nodes.FirstOrDefault(candidate => candidate.Id == node.PairedNodeId.Value);

		return Next(graph, pair, "next");
	}

	private static bool IsLiteral(string definitionId)
	{
		return definitionId == CoreVisualNodes.BooleanLiteral
			|| definitionId == CoreVisualNodes.IntegerLiteral
			|| definitionId == CoreVisualNodes.NumberLiteral
			|| definitionId == CoreVisualNodes.TextLiteral;
	}

	private static bool UsesSymbol(string definitionId)
	{
		return definitionId == CoreVisualNodes.VariableDeclare
			|| definitionId == CoreVisualNodes.VariableGet
			|| definitionId == CoreVisualNodes.VariableSet
			|| definitionId == CoreVisualNodes.VariableIncrement;
	}

	private static bool IsListNode(string definitionId)
	{
		return definitionId == CoreVisualNodes.ListCreate
			|| definitionId == CoreVisualNodes.ListAdd
			|| definitionId == CoreVisualNodes.ListGet
			|| definitionId == CoreVisualNodes.ListCount
			|| definitionId == CoreVisualNodes.ForEach;
	}

	private static bool TryPropertyGuid(VisualNode node, string name, out Guid value)
	{
		value = Guid.Empty;

		return node.Properties.TryGetValue(name, out string text) && Guid.TryParse(text, out value);
	}

	private void Add(
		string code,
		string message,
		VisualFunction function = null,
		VisualNode node = null,
		string portName = null,
		VisualDiagnosticSeverity severity = VisualDiagnosticSeverity.Error
	)
	{
		diagnostics.Add(
			new VisualProgramDiagnostic(
				code,
				message,
				severity,
				function?.Id,
				node?.Id,
				portName
			)
		);
	}
}

internal static class VisualLiterals
{
	internal static bool IsValid(string text, VisualValueType type)
	{
		if (type == VisualValueType.Text)
		{
			return text != null;
		}

		if (type == VisualValueType.Boolean)
		{
			return bool.TryParse(text, out _);
		}

		if (type == VisualValueType.Integer)
		{
			return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
		}

		if (type == VisualValueType.Number)
		{
			return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
				&& double.IsFinite(number);
		}

		return false;
	}
}

internal static class CSharpNames
{
	private const string GeneratedPrefix = "__fishgfx_";
	private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
	{
		"abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
		"class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
		"enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
		"foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
		"long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
		"private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
		"sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
		"try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
		"void", "volatile", "while",
	};

	internal static bool IsIdentifier(string value)
	{
		if (string.IsNullOrWhiteSpace(value)
			|| Keywords.Contains(value)
			|| !IsIdentifierStart(value[0]))
		{
			return false;
		}

		for (int index = 1; index < value.Length; index++)
		{
			if (!char.IsLetterOrDigit(value[index]) && value[index] != '_')
			{
				return false;
			}
		}

		return true;
	}

	internal static bool IsReservedGeneratedName(string value)
	{
		return value?.StartsWith(GeneratedPrefix, StringComparison.Ordinal) == true;
	}

	private static bool IsIdentifierStart(char value)
	{
		return char.IsLetter(value) || value == '_';
	}
}

