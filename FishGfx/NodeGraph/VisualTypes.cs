using System;

namespace FishGfx.NodeGraph;

public enum VisualValueType
{
	None,
	Boolean,
	Integer,
	Number,
	Text,
	BooleanList,
	IntegerList,
	NumberList,
	TextList,
}

public static class VisualValueTypes
{
	public static bool IsList(VisualValueType type)
	{
		return type == VisualValueType.BooleanList
			|| type == VisualValueType.IntegerList
			|| type == VisualValueType.NumberList
			|| type == VisualValueType.TextList;
	}

	public static VisualValueType ElementType(VisualValueType type)
	{
		return type switch
		{
			VisualValueType.BooleanList => VisualValueType.Boolean,
			VisualValueType.IntegerList => VisualValueType.Integer,
			VisualValueType.NumberList => VisualValueType.Number,
			VisualValueType.TextList => VisualValueType.Text,
			_ => throw new ArgumentException($"{type} is not a list type.", nameof(type)),
		};
	}

	public static VisualValueType ListOf(VisualValueType elementType)
	{
		return elementType switch
		{
			VisualValueType.Boolean => VisualValueType.BooleanList,
			VisualValueType.Integer => VisualValueType.IntegerList,
			VisualValueType.Number => VisualValueType.NumberList,
			VisualValueType.Text => VisualValueType.TextList,
			_ => throw new ArgumentException($"{elementType} cannot be stored in a list.", nameof(elementType)),
		};
	}

	public static bool CanAssign(VisualValueType source, VisualValueType destination)
	{
		return source == destination
			|| source == VisualValueType.Integer && destination == VisualValueType.Number;
	}

	public static string CSharpName(VisualValueType type)
	{
		return type switch
		{
			VisualValueType.None => "void",
			VisualValueType.Boolean => "bool",
			VisualValueType.Integer => "int",
			VisualValueType.Number => "double",
			VisualValueType.Text => "string",
			VisualValueType.BooleanList => "global::System.Collections.Generic.List<bool>",
			VisualValueType.IntegerList => "global::System.Collections.Generic.List<int>",
			VisualValueType.NumberList => "global::System.Collections.Generic.List<double>",
			VisualValueType.TextList => "global::System.Collections.Generic.List<string>",
			_ => throw new ArgumentOutOfRangeException(nameof(type)),
		};
	}

	public static string DisplayName(VisualValueType type)
	{
		return type switch
		{
			VisualValueType.None => "flow",
			VisualValueType.Boolean => "Boolean",
			VisualValueType.Integer => "Integer",
			VisualValueType.Number => "Number",
			VisualValueType.Text => "Text",
			VisualValueType.BooleanList => "Boolean List",
			VisualValueType.IntegerList => "Integer List",
			VisualValueType.NumberList => "Number List",
			VisualValueType.TextList => "Text List",
			_ => type.ToString(),
		};
	}
}

public enum VisualPortKind
{
	Execution,
	Value,
}

public enum VisualPortDirection
{
	Input,
	Output,
}

public enum VisualNodeRole
{
	Entry,
	Statement,
	Expression,
	Branch,
	Loop,
	Merge,
	LoopEnd,
}
