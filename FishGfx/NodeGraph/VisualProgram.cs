using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace FishGfx.NodeGraph;

public sealed class VisualProgram
{
	private readonly List<VisualFunction> functions = new List<VisualFunction>();

	public Guid Id { get; }
	public string Name { get; set; }
	public IReadOnlyList<VisualFunction> Functions { get; }
	public VisualNodeCatalog Catalog { get; }

	public VisualProgram(string name, VisualNodeCatalog catalog, Guid? id = null)
	{
		Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
		Id = id ?? Guid.NewGuid();
		Name = string.IsNullOrWhiteSpace(name) ? "VisualProgram" : name.Trim();
		Functions = functions.AsReadOnly();
	}

	public static VisualProgram CreateDefault(VisualNodeCatalog catalog, string name = "VisualProgram")
	{
		VisualProgram program = new VisualProgram(name, catalog);

		program.AddFunction("Main", VisualValueType.None, true);

		return program;
	}

	public VisualFunction AddFunction(
		string name,
		VisualValueType returnType = VisualValueType.None,
		bool isEntryPoint = false
	)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException("A function name is required.", nameof(name));
		}

		string trimmedName = name.Trim();

		if (isEntryPoint && !string.Equals(trimmedName, "Main", StringComparison.Ordinal))
		{
			throw new ArgumentException("The entry-point function must be named 'Main'.", nameof(name));
		}

		if (!Enum.IsDefined(returnType)
			|| isEntryPoint && returnType != VisualValueType.None)
		{
			throw new ArgumentException("The function return type is invalid.", nameof(returnType));
		}

		if (functions.Any(function => string.Equals(function.Name, trimmedName, StringComparison.Ordinal)))
		{
			throw new InvalidOperationException($"A function named '{trimmedName}' already exists.");
		}

		if (isEntryPoint && functions.Any(function => function.IsEntryPoint))
		{
			throw new InvalidOperationException("The program already has an entry point.");
		}

		VisualFunction function = new VisualFunction(
			this,
			trimmedName,
			returnType,
			isEntryPoint
		);

		functions.Add(function);

		return function;
	}

	internal VisualFunction AddFunction(
		Guid id,
		string name,
		VisualValueType returnType,
		bool isEntryPoint,
		bool addEntry
	)
	{
		VisualFunction function = new VisualFunction(
			this,
			name,
			returnType,
			isEntryPoint,
			id,
			addEntry
		);

		functions.Add(function);

		return function;
	}

	public bool RemoveFunction(VisualFunction function)
	{
		if (function == null)
		{
			throw new ArgumentNullException(nameof(function));
		}

		return !function.IsEntryPoint && functions.Remove(function);
	}

	public VisualFunction GetFunction(Guid id)
	{
		return functions.Single(function => function.Id == id);
	}
}

public sealed class VisualFunction
{
	private readonly List<VisualVariableSymbol> symbols = new List<VisualVariableSymbol>();

	public Guid Id { get; }
	public string Name { get; set; }
	public VisualValueType ReturnType { get; }
	public bool IsEntryPoint { get; }
	public IReadOnlyList<VisualVariableSymbol> Symbols { get; }
	public VisualGraph Graph { get; }
	public VisualProgramViewState View { get; set; } = new VisualProgramViewState(new Vector2(120, 80), 1);
	public VisualProgram Program { get; }

	internal VisualFunction(
		VisualProgram program,
		string name,
		VisualValueType returnType,
		bool isEntryPoint,
		Guid? id = null,
		bool addEntry = true
	)
	{
		Program = program;
		Id = id ?? Guid.NewGuid();
		Name = name;
		ReturnType = returnType;
		IsEntryPoint = isEntryPoint;
		Symbols = symbols.AsReadOnly();
		Graph = new VisualGraph(this, program.Catalog);

		if (addEntry)
		{
			Graph.AddSingleNode(CoreVisualNodes.Entry, new Vector2(40, 500));
		}
	}

	public VisualVariableSymbol AddVariable(
		string name,
		VisualValueType type,
		bool isParameter = false,
		Guid? scopeNodeId = null
	)
	{
		if (!Enum.IsDefined(type) || type == VisualValueType.None)
		{
			throw new ArgumentException("A variable requires a supported value type.", nameof(type));
		}

		if (isParameter && (IsEntryPoint || scopeNodeId.HasValue)
			|| !isParameter && !scopeNodeId.HasValue)
		{
			throw new ArgumentException(
				"Parameters belong to non-entry functions; local variables require a scope node.",
				nameof(scopeNodeId)
			);
		}

		if (scopeNodeId.HasValue && Graph.Nodes.All(node => node.Id != scopeNodeId.Value))
		{
			throw new ArgumentException("The variable scope must belong to this function.", nameof(scopeNodeId));
		}

		VisualVariableSymbol symbol = new VisualVariableSymbol(
			UniqueSymbolName(name),
			type,
			isParameter,
			scopeNodeId
		);

		symbols.Add(symbol);

		return symbol;
	}

	internal void AddSymbol(VisualVariableSymbol symbol)
	{
		symbols.Add(symbol);
	}

	public bool RemoveVariable(VisualVariableSymbol symbol)
	{
		return symbol != null && !symbol.IsParameter && symbols.Remove(symbol);
	}

	public bool TryGetSymbol(Guid id, out VisualVariableSymbol symbol)
	{
		symbol = symbols.FirstOrDefault(candidate => candidate.Id == id);

		return symbol != null;
	}

	private string UniqueSymbolName(string requested)
	{
		string baseName = string.IsNullOrWhiteSpace(requested) ? "value" : requested.Trim();
		string candidate = baseName;
		int suffix = 2;

		while (symbols.Any(symbol => string.Equals(symbol.Name, candidate, StringComparison.Ordinal)))
		{
			candidate = baseName + suffix;
			suffix++;
		}

		return candidate;
	}
}
