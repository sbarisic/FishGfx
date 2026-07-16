using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FishGfx.NodeGraph;

public sealed class GeneratedNodeSpan
{
	public Guid FunctionId { get; }
	public Guid NodeId { get; }
	public int StartLine { get; }
	public int StartColumn { get; }
	public int EndLine { get; }
	public int EndColumn { get; }

	internal GeneratedNodeSpan(
		Guid functionId,
		Guid nodeId,
		int startLine,
		int startColumn,
		int endLine,
		int endColumn
	)
	{
		FunctionId = functionId;
		NodeId = nodeId;
		StartLine = startLine;
		StartColumn = startColumn;
		EndLine = endLine;
		EndColumn = endColumn;
	}
}

public sealed class GeneratedSourceMap
{
	private readonly List<GeneratedNodeSpan> spans;

	public IReadOnlyList<GeneratedNodeSpan> Spans { get; }

	internal GeneratedSourceMap(List<GeneratedNodeSpan> spans)
	{
		this.spans = spans;
		Spans = spans.AsReadOnly();
	}

	public GeneratedNodeSpan Find(int line, int column = 1)
	{
		return spans
			.Where(span => PositionAtOrAfter(line, column, span.StartLine, span.StartColumn)
				&& PositionAtOrBefore(line, column, span.EndLine, span.EndColumn))
			.OrderBy(span => span.EndLine - span.StartLine)
			.ThenBy(span => span.EndColumn - span.StartColumn)
			.FirstOrDefault();
	}

	private static bool PositionAtOrAfter(int line, int column, int targetLine, int targetColumn)
	{
		return line > targetLine || line == targetLine && column >= targetColumn;
	}

	private static bool PositionAtOrBefore(int line, int column, int targetLine, int targetColumn)
	{
		return line < targetLine || line == targetLine && column <= targetColumn;
	}
}

public sealed class CSharpGenerationResult
{
	public bool Success { get; internal set; }
	public string Source { get; internal set; } = "";
	public GeneratedSourceMap SourceMap { get; internal set; } =
		new GeneratedSourceMap(new List<GeneratedNodeSpan>());
	public IReadOnlyList<VisualProgramDiagnostic> Diagnostics { get; internal set; } =
		Array.Empty<VisualProgramDiagnostic>();
}

public interface IVisualProgramCodeGenerator
{
	CSharpGenerationResult Generate(VisualProgram program);
}

public sealed class CSharpProgramGenerator : IVisualProgramCodeGenerator
{
	private CodeWriter writer;
	private VisualFunction function;
	private readonly HashSet<Guid> pendingExpressionNodes = new HashSet<Guid>();

	public CSharpGenerationResult Generate(VisualProgram program)
	{
		if (program == null)
		{
			throw new ArgumentNullException(nameof(program));
		}

		VisualProgramValidationResult validation = new VisualProgramValidator().Validate(program);

		if (!validation.Success)
		{
			return new CSharpGenerationResult
			{
				Success = false,
				Diagnostics = validation.Diagnostics,
			};
		}

		writer = new CodeWriter();
		writer.Line($"internal static class {program.Name}");
		writer.OpenBlock();

		VisualFunction[] orderedFunctions = program.Functions
			.OrderByDescending(candidate => candidate.IsEntryPoint)
			.ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
			.ToArray();

		for (int index = 0; index < orderedFunctions.Length; index++)
		{
			EmitFunction(orderedFunctions[index]);

			if (index < orderedFunctions.Length - 1)
			{
				writer.Line();
			}
		}

		writer.CloseBlock();

		return new CSharpGenerationResult
		{
			Success = true,
			Source = writer.ToString(),
			SourceMap = new GeneratedSourceMap(writer.Spans),
			Diagnostics = validation.Diagnostics,
		};
	}

	private void EmitFunction(VisualFunction visualFunction)
	{
		function = visualFunction;
		CodePosition functionStart = writer.Position;
		string parameters = string.Join(
			", ",
			function.Symbols
				.Where(symbol => symbol.IsParameter)
				.Select(symbol => $"{VisualValueTypes.CSharpName(symbol.Type)} {symbol.Name}")
		);
		string signature = function.IsEntryPoint
			? "private static void Main()"
			: $"private static {VisualValueTypes.CSharpName(function.ReturnType)} {function.Name}({parameters})";

		writer.Line(signature);
		writer.OpenBlock();

		VisualNode entry = function.Graph.Nodes.Single(node => node.Role == VisualNodeRole.Entry);

		EmitFlow(VisualProgramValidator.Next(function.Graph, entry, "next"), null, null);

		writer.CloseBlock();
		writer.AddSpan(function.Id, entry.Id, functionStart);
	}

	private void EmitFlow(VisualNode node, Guid? stopNodeId, VisualNode owningLoop)
	{
		HashSet<Guid> emitted = new HashSet<Guid>();

		while (node != null && node.Id != stopNodeId && emitted.Add(node.Id))
		{
			CodePosition start = writer.Position;
			pendingExpressionNodes.Clear();

			if (node.Role == VisualNodeRole.Branch)
			{
				EmitIf(node, owningLoop, start);
				writer.AddSpan(function.Id, node.Id, start);
				node = PairedNext(node);
				continue;
			}

			if (node.Role == VisualNodeRole.Loop)
			{
				EmitLoop(node, start);
				writer.AddSpan(function.Id, node.Id, start);
				node = VisualProgramValidator.Next(function.Graph, node, "completed");
				continue;
			}

			if (node.Role == VisualNodeRole.Merge || node.Role == VisualNodeRole.LoopEnd)
			{
				return;
			}

			EmitStatement(node, owningLoop);
			AddExpressionSpans(start);
			writer.AddSpan(function.Id, node.Id, start);

			if (TerminatesFlow(node))
			{
				return;
			}

			node = VisualProgramValidator.Next(function.Graph, node, "next");
		}
	}

	private void EmitIf(VisualNode node, VisualNode owningLoop, CodePosition start)
	{
		string condition = Input(node, "condition").Text;

		writer.Line($"if ({condition})");
		AddExpressionSpans(start);
		pendingExpressionNodes.Clear();
		writer.OpenBlock();
		EmitFlow(VisualProgramValidator.Next(function.Graph, node, "then"), node.PairedNodeId, owningLoop);
		writer.CloseBlock();

		VisualNode elseStart = VisualProgramValidator.Next(function.Graph, node, "else");

		if (elseStart != null && elseStart.Id != node.PairedNodeId)
		{
			writer.Line("else");
			writer.OpenBlock();
			EmitFlow(elseStart, node.PairedNodeId, owningLoop);
			writer.CloseBlock();
		}
	}

	private void EmitLoop(VisualNode node, CodePosition start)
	{
		if (node.DefinitionId == CoreVisualNodes.While)
		{
			writer.Line($"while ({Input(node, "condition").Text})");
		}
		else if (node.DefinitionId == CoreVisualNodes.Repeat)
		{
			string index = GeneratedName("repeat", node);
			writer.Line($"for (int {index} = 0; {index} < {Input(node, "count").Text}; {index}++)");
		}
		else
		{
			VisualVariableSymbol item = Symbol(node);
			writer.Line($"foreach ({VisualValueTypes.CSharpName(item.Type)} {item.Name} in {Input(node, "list").Text})");
		}

		AddExpressionSpans(start);
		pendingExpressionNodes.Clear();
		writer.OpenBlock();
		EmitFlow(VisualProgramValidator.Next(function.Graph, node, "body"), node.PairedNodeId, node);
		writer.CloseBlock();
	}

	private void EmitStatement(VisualNode node, VisualNode owningLoop)
	{
			switch (node.DefinitionId)
		{
			case CoreVisualNodes.ConsoleWriteLine:
				writer.Line($"global::System.Console.WriteLine({Input(node, "value").Text});");
				break;
			case CoreVisualNodes.ConsoleReadLine:
				writer.Line($"string {ReadResultName(node)} = global::System.Console.ReadLine() ?? string.Empty;");
				break;
			case CoreVisualNodes.VariableDeclare:
				VisualVariableSymbol declaration = Symbol(node);
				writer.Line($"{VisualValueTypes.CSharpName(declaration.Type)} {declaration.Name} = {Input(node, "value").Text};");
				break;
			case CoreVisualNodes.VariableSet:
				writer.Line($"{Symbol(node).Name} = {Input(node, "value").Text};");
				break;
			case CoreVisualNodes.VariableIncrement:
				writer.Line($"{Symbol(node).Name} += {Input(node, "amount").Text};");
				break;
			case CoreVisualNodes.ListAdd:
				writer.Line($"{Input(node, "list").Text}.Add({Input(node, "value").Text});");
				break;
			case CoreVisualNodes.FunctionCall:
				VisualFunction target = FunctionTarget(node);

				if (target.ReturnType == VisualValueType.None)
				{
					writer.Line(FunctionCall(node) + ";");
				}
				else
				{
					writer.Line($"{VisualValueTypes.CSharpName(target.ReturnType)} {CallResultName(node)} = {FunctionCall(node)};");
				}

				break;
			case CoreVisualNodes.Comment:
				writer.Line("// " + SingleLine(node.Properties.TryGetValue("text", out string text) ? text : ""));
				break;
			case CoreVisualNodes.Return:
				writer.Line(function.ReturnType == VisualValueType.None ? "return;" : $"return {Input(node, "value").Text};");
				break;
			case CoreVisualNodes.Break:
				writer.Line("break;");
				break;
			case CoreVisualNodes.Continue:
				writer.Line("continue;");
				break;
		}
	}

	private ExpressionCode Input(VisualNode node, string portName)
	{
		VisualPort input = node.GetInput(portName);

		if (function.Graph.TryGetInputConnection(input, out VisualConnection connection))
		{
			ExpressionCode expression = Expression(connection.Output.Node);

			if (connection.Output.Type == VisualValueType.Integer && input.Type == VisualValueType.Number)
			{
				return new ExpressionCode(expression.Text, expression.Precedence);
			}

			return expression;
		}

		return new ExpressionCode(Literal(input.DefaultValue, input.Type), 100);
	}

	private ExpressionCode Expression(VisualNode node)
	{
		pendingExpressionNodes.Add(node.Id);

		return node.DefinitionId switch
		{
			CoreVisualNodes.BooleanLiteral => LiteralExpression(node, VisualValueType.Boolean),
			CoreVisualNodes.IntegerLiteral => LiteralExpression(node, VisualValueType.Integer),
			CoreVisualNodes.NumberLiteral => LiteralExpression(node, VisualValueType.Number),
			CoreVisualNodes.TextLiteral => LiteralExpression(node, VisualValueType.Text),
			CoreVisualNodes.IntegerAdd => Binary(node, "+", 60),
			CoreVisualNodes.NumberAdd => Binary(node, "+", 60),
			CoreVisualNodes.IntegerSubtract => Binary(node, "-", 60),
			CoreVisualNodes.NumberSubtract => Binary(node, "-", 60),
			CoreVisualNodes.IntegerMultiply => Binary(node, "*", 70),
			CoreVisualNodes.NumberMultiply => Binary(node, "*", 70),
			CoreVisualNodes.IntegerDivide => Binary(node, "/", 70),
			CoreVisualNodes.NumberDivide => Binary(node, "/", 70),
			CoreVisualNodes.IntegerModulo => Binary(node, "%", 70),
			CoreVisualNodes.NumberLess => Binary(node, "<", 50),
			CoreVisualNodes.NumberEqual => Binary(node, "==", 45),
			CoreVisualNodes.TextEqual => Binary(node, "==", 45),
			CoreVisualNodes.And => Binary(node, "&&", 30),
			CoreVisualNodes.Or => Binary(node, "||", 20),
			CoreVisualNodes.Not => Unary(node, "!"),
			CoreVisualNodes.TextConcat => Binary(node, "+", 60),
			CoreVisualNodes.ConsoleReadLine => new ExpressionCode(ReadResultName(node), 100),
			CoreVisualNodes.ParseInteger => new ExpressionCode($"int.Parse({Input(node, "text").Text}, global::System.Globalization.CultureInfo.InvariantCulture)", 100),
			CoreVisualNodes.ParseNumber => new ExpressionCode($"double.Parse({Input(node, "text").Text}, global::System.Globalization.CultureInfo.InvariantCulture)", 100),
			CoreVisualNodes.ToText => new ExpressionCode($"({Input(node, "value").Text}).ToString(global::System.Globalization.CultureInfo.InvariantCulture)", 100),
			CoreVisualNodes.VariableGet => new ExpressionCode(Symbol(node).Name, 100),
			CoreVisualNodes.ListCreate => new ExpressionCode($"new {VisualValueTypes.CSharpName(node.GetOutput("result").Type)}()", 100),
			CoreVisualNodes.ListGet => new ExpressionCode($"{Parenthesize(Input(node, "list"), 100)}[{Input(node, "index").Text}]", 100),
			CoreVisualNodes.ListCount => new ExpressionCode($"{Parenthesize(Input(node, "list"), 100)}.Count", 100),
			CoreVisualNodes.FunctionCall => new ExpressionCode(CallResultName(node), 100),
			_ => new ExpressionCode(DefaultValue(node.Outputs.FirstOrDefault()?.Type ?? VisualValueType.Text), 100),
		};
	}

	private void AddExpressionSpans(CodePosition start)
	{
		foreach (Guid nodeId in pendingExpressionNodes)
		{
			writer.AddSpan(function.Id, nodeId, start);
		}
	}

	private ExpressionCode LiteralExpression(VisualNode node, VisualValueType type)
	{
		return new ExpressionCode(
			Literal(node.Properties.TryGetValue("value", out string value) ? value : "", type),
			100
		);
	}

	private ExpressionCode Binary(VisualNode node, string operation, int precedence)
	{
		ExpressionCode left = Input(node, "left");
		ExpressionCode right = Input(node, "right");
		string text = $"{Parenthesize(left, precedence)} {operation} {Parenthesize(right, precedence + 1)}";

		return new ExpressionCode(text, precedence);
	}

	private ExpressionCode Unary(VisualNode node, string operation)
	{
		return new ExpressionCode(operation + Parenthesize(Input(node, "value"), 80), 80);
	}

	private string FunctionCall(VisualNode node)
	{
		VisualFunction target = FunctionTarget(node);
		string arguments = string.Join(
			", ",
			target.Symbols
				.Where(symbol => symbol.IsParameter)
				.Select(symbol => Input(node, symbol.Id.ToString("N")).Text)
		);

		return $"{target.Name}({arguments})";
	}

	private VisualFunction FunctionTarget(VisualNode node)
	{
		Guid id = Guid.Parse(node.Properties["function"]);

		return function.Program.GetFunction(id);
	}

	private static string CallResultName(VisualNode node)
	{
		return GeneratedName("call", node);
	}

	private static string ReadResultName(VisualNode node)
	{
		return GeneratedName("read", node);
	}

	private static string GeneratedName(string purpose, VisualNode node)
	{
		return $"__fishgfx_{purpose}_{node.Id:N}";
	}

	private VisualVariableSymbol Symbol(VisualNode node)
	{
		Guid id = Guid.Parse(node.Properties["symbol"]);

		return function.Symbols.Single(symbol => symbol.Id == id);
	}

	private VisualNode PairedNext(VisualNode node)
	{
		VisualNode pair = function.Graph.GetNode(node.PairedNodeId.Value);

		return VisualProgramValidator.Next(function.Graph, pair, "next");
	}

	private static bool TerminatesFlow(VisualNode node)
	{
		return node.DefinitionId == CoreVisualNodes.Return
			|| node.DefinitionId == CoreVisualNodes.Break
			|| node.DefinitionId == CoreVisualNodes.Continue;
	}

	private static string Parenthesize(ExpressionCode expression, int requiredPrecedence)
	{
		return expression.Precedence < requiredPrecedence
			? "(" + expression.Text + ")"
			: expression.Text;
	}

	private static string Literal(string value, VisualValueType type)
	{
		if (type == VisualValueType.Text)
		{
			return "\"" + EscapeString(value ?? "") + "\"";
		}

		if (type == VisualValueType.Boolean)
		{
			return bool.Parse(value).ToString().ToLowerInvariant();
		}

		if (type == VisualValueType.Integer)
		{
			return int.Parse(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
		}

		if (type == VisualValueType.Number)
		{
			double number = double.Parse(value, CultureInfo.InvariantCulture);
			string text = number.ToString("R", CultureInfo.InvariantCulture);

			return text.IndexOfAny(new[] { '.', 'E', 'e' }) < 0 ? text + ".0" : text;
		}

		return DefaultValue(type);
	}

	private static string DefaultValue(VisualValueType type)
	{
		return type switch
		{
			VisualValueType.Boolean => "false",
			VisualValueType.Integer => "0",
			VisualValueType.Number => "0.0",
			VisualValueType.Text => "string.Empty",
			_ when VisualValueTypes.IsList(type) => $"new {VisualValueTypes.CSharpName(type)}()",
			_ => "default",
		};
	}

	private static string EscapeString(string value)
	{
		StringBuilder escaped = new StringBuilder(value.Length);

		foreach (char character in value)
		{
			switch (character)
			{
				case '\\':
					escaped.Append("\\\\");
					break;
				case '"':
					escaped.Append("\\\"");
					break;
				case '\r':
					escaped.Append("\\r");
					break;
				case '\n':
					escaped.Append("\\n");
					break;
				case '\t':
					escaped.Append("\\t");
					break;
				default:
					if (char.IsControl(character)
						|| char.IsSurrogate(character)
						|| character == '\u2028'
						|| character == '\u2029')
					{
						escaped.Append("\\u");
						escaped.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
					}
					else
					{
						escaped.Append(character);
					}

					break;
			}
		}

		return escaped.ToString();
	}

	private static string SingleLine(string value)
	{
		StringBuilder singleLine = new StringBuilder(value?.Length ?? 0);

		foreach (char character in value ?? "")
		{
			singleLine.Append(
				char.IsControl(character)
					|| char.IsSurrogate(character)
					|| character == '\u2028'
					|| character == '\u2029'
					? ' '
					: character
			);
		}

		return singleLine.ToString();
	}

	private readonly struct ExpressionCode
	{
		internal string Text { get; }
		internal int Precedence { get; }

		internal ExpressionCode(string text, int precedence)
		{
			Text = text;
			Precedence = precedence;
		}
	}
}

internal readonly struct CodePosition
{
	internal int Line { get; }
	internal int Column { get; }

	internal CodePosition(int line, int column)
	{
		Line = line;
		Column = column;
	}
}

internal sealed class CodeWriter
{
	private readonly StringBuilder text = new StringBuilder();
	private int indent;
	private int line = 1;
	private int column = 1;
	private int lastContentLine = 1;
	private int lastContentColumn = 1;

	internal List<GeneratedNodeSpan> Spans { get; } = new List<GeneratedNodeSpan>();
	internal CodePosition Position => new CodePosition(line, column);

	internal void Line(string value = "")
	{
		if (value.Length > 0)
		{
			string indentation = new string(' ', indent * 4);

			text.Append(indentation);
			text.Append(value);
			column += indentation.Length + value.Length;
			lastContentLine = line;
			lastContentColumn = Math.Max(1, column - 1);
		}

		text.AppendLine();
		line++;
		column = 1;
	}

	internal void OpenBlock()
	{
		Line("{");
		indent++;
	}

	internal void CloseBlock()
	{
		indent--;
		Line("}");
	}

	internal void AddSpan(Guid functionId, Guid nodeId, CodePosition start)
	{
		Spans.Add(
			new GeneratedNodeSpan(
				functionId,
				nodeId,
				start.Line,
				start.Column,
				lastContentLine,
				lastContentColumn
			)
		);
	}

	public override string ToString()
	{
		return text.ToString();
	}
}
