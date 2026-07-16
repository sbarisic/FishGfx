using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FishGfx.NodeGraph;

public sealed class VisualPortDefinition
{
	public string Name { get; }
	public string Label { get; }
	public VisualPortKind Kind { get; }
	public VisualPortDirection Direction { get; }
	public VisualValueType Type { get; }
	public bool Optional { get; }
	public string DefaultValue { get; }

	public VisualPortDefinition(
		string name,
		string label,
		VisualPortKind kind,
		VisualPortDirection direction,
		VisualValueType type = VisualValueType.None,
		bool optional = false,
		string defaultValue = null
	)
	{
		if (!Enum.IsDefined(kind)
			|| !Enum.IsDefined(direction)
			|| !Enum.IsDefined(type))
		{
			throw new ArgumentOutOfRangeException(nameof(kind), "Port enum values must be defined.");
		}

		if (string.IsNullOrWhiteSpace(name) || name != name.Trim())
		{
			throw new ArgumentException("A stable, trimmed port name is required.", nameof(name));
		}

		if (kind == VisualPortKind.Execution && type != VisualValueType.None
			|| kind == VisualPortKind.Value && type == VisualValueType.None)
		{
			throw new ArgumentException("The port kind and value type are incompatible.", nameof(type));
		}

		if ((optional || defaultValue != null)
			&& (kind != VisualPortKind.Value || direction != VisualPortDirection.Input))
		{
			throw new ArgumentException("Only value inputs can have inline defaults.", nameof(defaultValue));
		}

		if (!optional && defaultValue != null)
		{
			throw new ArgumentException("A port with an inline default must be optional.", nameof(optional));
		}

		Name = name;
		Label = string.IsNullOrWhiteSpace(label) ? name : label;
		Kind = kind;
		Direction = direction;
		Type = type;
		Optional = optional;
		DefaultValue = defaultValue;
	}
}

public sealed class VisualNodeDefinition
{
	public string Id { get; }
	public string Title { get; }
	public string Category { get; }
	public VisualNodeRole Role { get; }
	public IReadOnlyList<VisualPortDefinition> Ports { get; }
	public IReadOnlyDictionary<string, string> DefaultProperties { get; }
	public bool Hidden { get; }

	public VisualNodeDefinition(
		string id,
		string title,
		string category,
		VisualNodeRole role,
		IEnumerable<VisualPortDefinition> ports,
		IReadOnlyDictionary<string, string> defaultProperties = null,
		bool hidden = false
	)
	{
		if (string.IsNullOrWhiteSpace(id) || id != id.Trim())
		{
			throw new ArgumentException("A stable, trimmed node id is required.", nameof(id));
		}

		if (!Enum.IsDefined(role))
		{
			throw new ArgumentOutOfRangeException(nameof(role));
		}

		Id = id;
		Title = string.IsNullOrWhiteSpace(title) ? id : title.Trim();
		Category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim();
		Role = role;
		VisualPortDefinition[] portArray = (ports ?? Array.Empty<VisualPortDefinition>()).ToArray();

		if (portArray.Any(port => port == null))
		{
			throw new ArgumentException("Node ports cannot contain null entries.", nameof(ports));
		}

		if (portArray.GroupBy(port => (port.Direction, port.Name)).Any(group => group.Count() > 1))
		{
			throw new ArgumentException("Node port names must be unique within each direction.", nameof(ports));
		}

		Ports = Array.AsReadOnly(portArray);
		Dictionary<string, string> properties = defaultProperties == null
			? new Dictionary<string, string>(StringComparer.Ordinal)
			: new Dictionary<string, string>(defaultProperties, StringComparer.Ordinal);

		if (properties.Keys.Any(name => string.IsNullOrWhiteSpace(name) || name != name.Trim()))
		{
			throw new ArgumentException("Default property names must be stable and trimmed.", nameof(defaultProperties));
		}

		DefaultProperties = new ReadOnlyDictionary<string, string>(properties);
		Hidden = hidden;
	}
}

public sealed class VisualNodeCatalog
{
	private readonly Dictionary<string, VisualNodeDefinition> definitions =
		new Dictionary<string, VisualNodeDefinition>(StringComparer.Ordinal);
	private readonly List<VisualNodeDefinition> visibleDefinitions = new List<VisualNodeDefinition>();

	public IReadOnlyList<VisualNodeDefinition> Definitions { get; }

	public VisualNodeCatalog()
	{
		Definitions = visibleDefinitions.AsReadOnly();
	}

	public static VisualNodeCatalog CreateCore()
	{
		VisualNodeCatalog catalog = new VisualNodeCatalog();

		CoreVisualNodes.Register(catalog);

		return catalog;
	}

	public void Register(VisualNodeDefinition definition)
	{
		if (definition == null)
		{
			throw new ArgumentNullException(nameof(definition));
		}

		if (!definitions.TryAdd(definition.Id, definition))
		{
			throw new InvalidOperationException($"Visual node id '{definition.Id}' is already registered.");
		}

		if (!definition.Hidden)
		{
			visibleDefinitions.Add(definition);
			visibleDefinitions.Sort(CompareDefinitions);
		}
	}

	public bool TryGet(string id, out VisualNodeDefinition definition)
	{
		if (id == null)
		{
			definition = null;
			return false;
		}

		return definitions.TryGetValue(id, out definition);
	}

	public VisualNodeDefinition Get(string id)
	{
		if (!TryGet(id, out VisualNodeDefinition definition))
		{
			throw new KeyNotFoundException($"No visual node is registered with id '{id}'.");
		}

		return definition;
	}

	private static int CompareDefinitions(VisualNodeDefinition left, VisualNodeDefinition right)
	{
		int category = string.Compare(left.Category, right.Category, StringComparison.OrdinalIgnoreCase);

		return category != 0
			? category
			: string.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase);
	}
}
