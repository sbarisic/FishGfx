using System;
using System.Collections.Generic;

namespace FishGfx.NodeGraph;

public static class CoreVisualNodes
{
	private static readonly HashSet<string> GeneratedDefinitions = new HashSet<string>(StringComparer.Ordinal)
	{
		Entry,
		Merge,
		LoopEnd,
		If,
		While,
		Repeat,
		ForEach,
		Break,
		Continue,
		Return,
		BooleanLiteral,
		IntegerLiteral,
		NumberLiteral,
		TextLiteral,
		IntegerAdd,
		NumberAdd,
		IntegerSubtract,
		NumberSubtract,
		IntegerMultiply,
		NumberMultiply,
		IntegerDivide,
		NumberDivide,
		IntegerModulo,
		NumberLess,
		NumberEqual,
		TextEqual,
		And,
		Or,
		Not,
		TextConcat,
		ConsoleWriteLine,
		ConsoleReadLine,
		ParseInteger,
		ParseNumber,
		ToText,
		VariableDeclare,
		VariableGet,
		VariableSet,
		VariableIncrement,
		ListCreate,
		ListAdd,
		ListGet,
		ListCount,
		FunctionCall,
		Comment,
	};

	public const string Entry = "flow.entry";
	public const string Merge = "flow.merge";
	public const string LoopEnd = "flow.loopEnd";
	public const string If = "flow.if";
	public const string While = "flow.while";
	public const string Repeat = "flow.repeat";
	public const string ForEach = "flow.forEach";
	public const string Break = "flow.break";
	public const string Continue = "flow.continue";
	public const string Return = "flow.return";
	public const string BooleanLiteral = "value.boolean";
	public const string IntegerLiteral = "value.integer";
	public const string NumberLiteral = "value.number";
	public const string TextLiteral = "value.text";
	public const string IntegerAdd = "math.integer.add";
	public const string NumberAdd = "math.number.add";
	public const string IntegerSubtract = "math.integer.subtract";
	public const string NumberSubtract = "math.number.subtract";
	public const string IntegerMultiply = "math.integer.multiply";
	public const string NumberMultiply = "math.number.multiply";
	public const string IntegerDivide = "math.integer.divide";
	public const string NumberDivide = "math.number.divide";
	public const string IntegerModulo = "math.integer.modulo";
	public const string NumberLess = "logic.number.less";
	public const string NumberEqual = "logic.number.equal";
	public const string TextEqual = "logic.text.equal";
	public const string And = "logic.and";
	public const string Or = "logic.or";
	public const string Not = "logic.not";
	public const string TextConcat = "text.concat";
	public const string ConsoleWriteLine = "console.writeLine";
	public const string ConsoleReadLine = "console.readLine";
	public const string ParseInteger = "convert.parseInteger";
	public const string ParseNumber = "convert.parseNumber";
	public const string ToText = "convert.toText";
	public const string VariableDeclare = "variable.declare";
	public const string VariableGet = "variable.get";
	public const string VariableSet = "variable.set";
	public const string VariableIncrement = "variable.increment";
	public const string ListCreate = "list.create";
	public const string ListAdd = "list.add";
	public const string ListGet = "list.get";
	public const string ListCount = "list.count";
	public const string FunctionCall = "function.call";
	public const string Comment = "program.comment";

	internal static bool SupportsGeneration(string definitionId)
	{
		return definitionId != null && GeneratedDefinitions.Contains(definitionId);
	}

	public static void Register(VisualNodeCatalog catalog)
	{
		if (catalog == null)
		{
			throw new ArgumentNullException(nameof(catalog));
		}

		RegisterFlow(catalog);
		RegisterValues(catalog);
		RegisterMath(catalog);
		RegisterLogic(catalog);
		RegisterConsole(catalog);
		RegisterVariables(catalog);
		RegisterLists(catalog);
	}

	private static void RegisterFlow(VisualNodeCatalog catalog)
	{
		catalog.Register(Node(Entry, "Entry", "Flow", VisualNodeRole.Entry, OutFlow("next"), hidden: true));
		catalog.Register(
			new VisualNodeDefinition(
				Merge,
				"Merge",
				"Flow",
				VisualNodeRole.Merge,
				new[]
				{
					InFlow("then"),
					InFlow("else"),
					OutFlow("next"),
				},
				hidden: true
			)
		);
		catalog.Register(Node(LoopEnd, "Loop End", "Flow", VisualNodeRole.LoopEnd, InFlow("body"), hidden: true));
		catalog.Register(Node(If, "If / Else", "Flow", VisualNodeRole.Branch, InFlow("in"), InValue("condition", VisualValueType.Boolean), OutFlow("then"), OutFlow("else")));
		catalog.Register(Node(While, "While", "Flow", VisualNodeRole.Loop, InFlow("in"), InValue("condition", VisualValueType.Boolean), OutFlow("body"), OutFlow("completed")));
		catalog.Register(Node(Repeat, "Repeat", "Flow", VisualNodeRole.Loop, InFlow("in"), InValue("count", VisualValueType.Integer), OutFlow("body"), OutFlow("completed")));
		catalog.Register(Node(ForEach, "For Each", "Flow", VisualNodeRole.Loop, InFlow("in"), OutFlow("body"), OutFlow("completed"), Property("type", VisualValueType.IntegerList.ToString()), Property("symbol", "")));
		catalog.Register(Node(Break, "Break", "Flow", VisualNodeRole.Statement, InFlow("in")));
		catalog.Register(Node(Continue, "Continue", "Flow", VisualNodeRole.Statement, InFlow("in")));
		catalog.Register(Node(Return, "Return", "Functions", VisualNodeRole.Statement, InFlow("in")));
		catalog.Register(Node(Comment, "Comment", "Program", VisualNodeRole.Statement, InFlow("in"), OutFlow("next"), Property("text", "comment")));
	}

	private static void RegisterValues(VisualNodeCatalog catalog)
	{
		catalog.Register(Expression(BooleanLiteral, "Boolean", "Values", VisualValueType.Boolean, "false"));
		catalog.Register(Expression(IntegerLiteral, "Integer", "Values", VisualValueType.Integer, "0"));
		catalog.Register(Expression(NumberLiteral, "Number", "Values", VisualValueType.Number, "0"));
		catalog.Register(Expression(TextLiteral, "Text", "Values", VisualValueType.Text, ""));
	}

	private static void RegisterMath(VisualNodeCatalog catalog)
	{
		RegisterBinary(catalog, IntegerAdd, "Add Integers", VisualValueType.Integer);
		RegisterBinary(catalog, NumberAdd, "Add Numbers", VisualValueType.Number);
		RegisterBinary(catalog, IntegerSubtract, "Subtract Integers", VisualValueType.Integer);
		RegisterBinary(catalog, NumberSubtract, "Subtract Numbers", VisualValueType.Number);
		RegisterBinary(catalog, IntegerMultiply, "Multiply Integers", VisualValueType.Integer);
		RegisterBinary(catalog, NumberMultiply, "Multiply Numbers", VisualValueType.Number);
		RegisterBinary(catalog, IntegerDivide, "Divide Integers", VisualValueType.Integer);
		RegisterBinary(catalog, NumberDivide, "Divide Numbers", VisualValueType.Number);
		RegisterBinary(catalog, IntegerModulo, "Integer Remainder", VisualValueType.Integer);
	}

	private static void RegisterLogic(VisualNodeCatalog catalog)
	{
		catalog.Register(Node(NumberLess, "Less Than", "Logic", VisualNodeRole.Expression, InValue("left", VisualValueType.Number), InValue("right", VisualValueType.Number), OutValue("result", VisualValueType.Boolean)));
		catalog.Register(Node(NumberEqual, "Numbers Equal", "Logic", VisualNodeRole.Expression, InValue("left", VisualValueType.Number), InValue("right", VisualValueType.Number), OutValue("result", VisualValueType.Boolean)));
		catalog.Register(Node(TextEqual, "Text Equal", "Logic", VisualNodeRole.Expression, InValue("left", VisualValueType.Text), InValue("right", VisualValueType.Text), OutValue("result", VisualValueType.Boolean)));
		catalog.Register(Node(And, "And", "Logic", VisualNodeRole.Expression, InValue("left", VisualValueType.Boolean), InValue("right", VisualValueType.Boolean), OutValue("result", VisualValueType.Boolean)));
		catalog.Register(Node(Or, "Or", "Logic", VisualNodeRole.Expression, InValue("left", VisualValueType.Boolean), InValue("right", VisualValueType.Boolean), OutValue("result", VisualValueType.Boolean)));
		catalog.Register(Node(Not, "Not", "Logic", VisualNodeRole.Expression, InValue("value", VisualValueType.Boolean), OutValue("result", VisualValueType.Boolean)));
		catalog.Register(Node(TextConcat, "Join Text", "Text", VisualNodeRole.Expression, InValue("left", VisualValueType.Text, true, ""), InValue("right", VisualValueType.Text, true, ""), OutValue("result", VisualValueType.Text)));
	}

	private static void RegisterConsole(VisualNodeCatalog catalog)
	{
		catalog.Register(Node(ConsoleWriteLine, "Write Line", "Console", VisualNodeRole.Statement, InFlow("in"), InValue("value", VisualValueType.Text, true, ""), OutFlow("next")));
		catalog.Register(Node(ConsoleReadLine, "Read Line", "Console", VisualNodeRole.Statement, InFlow("in"), OutFlow("next"), OutValue("result", VisualValueType.Text)));
		catalog.Register(Node(ParseInteger, "Parse Integer", "Conversion", VisualNodeRole.Expression, InValue("text", VisualValueType.Text), OutValue("result", VisualValueType.Integer)));
		catalog.Register(Node(ParseNumber, "Parse Number", "Conversion", VisualNodeRole.Expression, InValue("text", VisualValueType.Text), OutValue("result", VisualValueType.Number)));
		catalog.Register(Node(ToText, "Number To Text", "Conversion", VisualNodeRole.Expression, InValue("value", VisualValueType.Number), OutValue("result", VisualValueType.Text)));
	}

	private static void RegisterVariables(VisualNodeCatalog catalog)
	{
		catalog.Register(Node(VariableDeclare, "Declare Variable", "Variables", VisualNodeRole.Statement, InFlow("in"), OutFlow("next"), Property("name", "value"), Property("type", VisualValueType.Integer.ToString())));
		catalog.Register(Node(VariableGet, "Get Variable", "Variables", VisualNodeRole.Expression, Property("symbol", "")));
		catalog.Register(Node(VariableSet, "Set Variable", "Variables", VisualNodeRole.Statement, InFlow("in"), OutFlow("next"), Property("symbol", "")));
		catalog.Register(Node(VariableIncrement, "Change Integer", "Variables", VisualNodeRole.Statement, InFlow("in"), InValue("amount", VisualValueType.Integer, true, "1"), OutFlow("next"), Property("symbol", "")));
	}

	private static void RegisterLists(VisualNodeCatalog catalog)
	{
		catalog.Register(Node(ListCreate, "Create List", "Lists", VisualNodeRole.Expression, Property("type", VisualValueType.IntegerList.ToString())));
		catalog.Register(Node(ListAdd, "Add To List", "Lists", VisualNodeRole.Statement, InFlow("in"), OutFlow("next"), Property("type", VisualValueType.IntegerList.ToString())));
		catalog.Register(Node(ListGet, "Get List Item", "Lists", VisualNodeRole.Expression, InValue("index", VisualValueType.Integer), Property("type", VisualValueType.IntegerList.ToString())));
		catalog.Register(Node(ListCount, "List Count", "Lists", VisualNodeRole.Expression, OutValue("result", VisualValueType.Integer), Property("type", VisualValueType.IntegerList.ToString())));
		catalog.Register(Node(FunctionCall, "Call Function", "Functions", VisualNodeRole.Statement, InFlow("in"), OutFlow("next"), Property("function", "")));
	}

	private static void RegisterBinary(VisualNodeCatalog catalog, string id, string title, VisualValueType type)
	{
		catalog.Register(Node(id, title, "Math", VisualNodeRole.Expression, InValue("left", type), InValue("right", type), OutValue("result", type)));
	}

	private static VisualNodeDefinition Expression(string id, string title, string category, VisualValueType type, string value)
	{
		return Node(id, title, category, VisualNodeRole.Expression, OutValue("result", type), Property("value", value));
	}

	private static VisualNodeDefinition Node(
		string id,
		string title,
		string category,
		VisualNodeRole role,
		params object[] parts
	)
	{
		List<VisualPortDefinition> ports = new List<VisualPortDefinition>();
		Dictionary<string, string> properties = new Dictionary<string, string>(StringComparer.Ordinal);
		bool hidden = false;

		foreach (object part in parts)
		{
			if (part is VisualPortDefinition port)
			{
				ports.Add(port);
			}
			else if (part is KeyValuePair<string, string> property)
			{
				properties.Add(property.Key, property.Value);
			}
			else if (part is bool isHidden)
			{
				hidden = isHidden;
			}
		}

		return new VisualNodeDefinition(id, title, category, role, ports, properties, hidden);
	}

	private static VisualNodeDefinition Node(
		string id,
		string title,
		string category,
		VisualNodeRole role,
		VisualPortDefinition port,
		bool hidden
	)
	{
		return new VisualNodeDefinition(id, title, category, role, new[] { port }, hidden: hidden);
	}

	private static VisualPortDefinition InFlow(string name)
	{
		return new VisualPortDefinition(name, name, VisualPortKind.Execution, VisualPortDirection.Input);
	}

	private static VisualPortDefinition OutFlow(string name)
	{
		return new VisualPortDefinition(name, name, VisualPortKind.Execution, VisualPortDirection.Output);
	}

	private static VisualPortDefinition InValue(string name, VisualValueType type, bool optional = false, string defaultValue = null)
	{
		return new VisualPortDefinition(name, name, VisualPortKind.Value, VisualPortDirection.Input, type, optional, defaultValue);
	}

	private static VisualPortDefinition OutValue(string name, VisualValueType type)
	{
		return new VisualPortDefinition(name, name, VisualPortKind.Value, VisualPortDirection.Output, type);
	}

	private static KeyValuePair<string, string> Property(string name, string value)
	{
		return new KeyValuePair<string, string>(name, value);
	}
}
