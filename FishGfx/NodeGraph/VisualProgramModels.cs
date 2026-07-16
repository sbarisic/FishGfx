using System;
using System.Collections.Generic;
using System.Numerics;

namespace FishGfx.NodeGraph;

public sealed class VisualPort
{
	public string Name { get; }
	public string Label { get; }
	public VisualPortKind Kind { get; }
	public VisualPortDirection Direction { get; }
	public VisualValueType Type { get; internal set; }
	public bool Optional { get; }
	public string DefaultValue { get; set; }
	public VisualNode Node { get; internal set; }

	internal VisualPort(VisualPortDefinition definition)
		: this(
			definition.Name,
			definition.Label,
			definition.Kind,
			definition.Direction,
			definition.Type,
			definition.Optional,
			definition.DefaultValue
		)
	{
	}

	internal VisualPort(
		string name,
		string label,
		VisualPortKind kind,
		VisualPortDirection direction,
		VisualValueType type,
		bool optional,
		string defaultValue
	)
	{
		Name = name;
		Label = label;
		Kind = kind;
		Direction = direction;
		Type = type;
		Optional = optional;
		DefaultValue = defaultValue;
	}
}

public sealed class VisualNode
{
	private readonly List<VisualPort> inputs = new List<VisualPort>();
	private readonly List<VisualPort> outputs = new List<VisualPort>();
	private readonly Dictionary<string, string> properties =
		new Dictionary<string, string>(StringComparer.Ordinal);

	public Guid Id { get; }
	public string DefinitionId { get; }
	public string Title { get; internal set; }
	public VisualNodeRole Role { get; internal set; }
	public Vector2 Position { get; set; }
	public float Width { get; set; } = 240;
	public Guid? PairedNodeId { get; internal set; }
	public bool IsMissingDefinition { get; internal set; }
	public IReadOnlyList<VisualPort> Inputs { get; }
	public IReadOnlyList<VisualPort> Outputs { get; }
	public IDictionary<string, string> Properties => properties;

	internal VisualNode(VisualNodeDefinition definition, Vector2 position, Guid? id = null)
	{
		Id = id ?? Guid.NewGuid();
		DefinitionId = definition.Id;
		Title = definition.Title;
		Role = definition.Role;
		Position = position;
		Inputs = inputs.AsReadOnly();
		Outputs = outputs.AsReadOnly();

		foreach (VisualPortDefinition port in definition.Ports)
		{
			AddPort(new VisualPort(port));
		}

		foreach (KeyValuePair<string, string> property in definition.DefaultProperties)
		{
			properties.Add(property.Key, property.Value);
		}
	}

	internal VisualNode(
		Guid id,
		string definitionId,
		string title,
		VisualNodeRole role,
		Vector2 position
	)
	{
		Id = id;
		DefinitionId = definitionId;
		Title = title;
		Role = role;
		Position = position;
		Inputs = inputs.AsReadOnly();
		Outputs = outputs.AsReadOnly();
	}

	public VisualPort GetInput(string name)
	{
		return GetPort(inputs, name, "input");
	}

	public VisualPort GetOutput(string name)
	{
		return GetPort(outputs, name, "output");
	}

	public bool TryGetInput(string name, out VisualPort port)
	{
		return TryGetPort(inputs, name, out port);
	}

	public bool TryGetOutput(string name, out VisualPort port)
	{
		return TryGetPort(outputs, name, out port);
	}

	internal void AddPort(VisualPort port)
	{
		port.Node = this;

		if (port.Direction == VisualPortDirection.Input)
		{
			inputs.Add(port);
		}
		else
		{
			outputs.Add(port);
		}
	}

	internal void RemoveValuePorts()
	{
		inputs.RemoveAll(port => port.Kind == VisualPortKind.Value);
		outputs.RemoveAll(port => port.Kind == VisualPortKind.Value);
	}

	private VisualPort GetPort(IReadOnlyList<VisualPort> ports, string name, string kind)
	{
		if (TryGetPort(ports, name, out VisualPort port))
		{
			return port;
		}

		throw new KeyNotFoundException($"Node '{Id}' has no {kind} port named '{name}'.");
	}

	private static bool TryGetPort(IReadOnlyList<VisualPort> ports, string name, out VisualPort port)
	{
		foreach (VisualPort candidate in ports)
		{
			if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
			{
				port = candidate;
				return true;
			}
		}

		port = null;

		return false;
	}
}

public sealed class VisualConnection
{
	public Guid Id { get; }
	public VisualPort Output { get; }
	public VisualPort Input { get; }
	public VisualPortKind Kind => Output.Kind;

	internal VisualConnection(VisualPort output, VisualPort input, Guid? id = null)
	{
		Id = id ?? Guid.NewGuid();
		Output = output;
		Input = input;
	}
}

public sealed class VisualVariableSymbol
{
	public Guid Id { get; }
	public string Name { get; set; }
	public VisualValueType Type { get; }
	public bool IsParameter { get; }
	public Guid? ScopeNodeId { get; }

	public VisualVariableSymbol(
		string name,
		VisualValueType type,
		bool isParameter = false,
		Guid? scopeNodeId = null,
		Guid? id = null
	)
	{
		Id = id ?? Guid.NewGuid();
		Name = name;
		Type = type;
		IsParameter = isParameter;
		ScopeNodeId = scopeNodeId;
	}
}

public readonly struct VisualProgramViewState
{
	public Vector2 Pan { get; }
	public float Zoom { get; }

	public VisualProgramViewState(Vector2 pan, float zoom)
	{
		Pan = pan;
		Zoom = zoom;
	}
}
