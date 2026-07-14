using System;
using System.Collections.Generic;
using System.Numerics;

namespace FishGfx.NodeGraph;

public enum NodePortDirection
{
	Input,
	Output,
}

public enum NodeEvaluationState
{
	NotEvaluated,
	Success,
	Error,
	Skipped,
}

public sealed class NodePort
{
	public Guid Id { get; } = Guid.NewGuid();
	public string Name { get; }
	public Type Type { get; }
	public NodePortDirection Direction { get; }
	public FunctionNode Node { get; internal set; }
	public object Value { get; internal set; }

	internal NodePort(string name, Type type, NodePortDirection direction)
	{
		Name = name;
		Type = type;
		Direction = direction;
	}
}

public sealed class NodeInlineValue
{
	public NodeParameterDescriptor Descriptor { get; }
	public string Name => Descriptor.Name;
	public Type Type => Descriptor.Type;
	public string Text { get; set; }
	public object Value { get; private set; }
	public bool IsValid { get; private set; }

	internal NodeInlineValue(NodeParameterDescriptor descriptor)
	{
		Descriptor = descriptor;

		object initialValue = descriptor.Parameter.HasDefaultValue
			&& descriptor.Parameter.DefaultValue != DBNull.Value
			? descriptor.Parameter.DefaultValue
			: NodeValueConverter.Default(Type);

		Text = NodeValueConverter.Format(initialValue, Type);
		Parse();
	}

	public bool Parse()
	{
		IsValid = NodeValueConverter.TryParse(Text, Type, out object parsed);

		if (IsValid)
		{
			Value = parsed;
		}

		return IsValid;
	}
}

public sealed class FunctionNode
{
	private readonly List<NodePort> inputs = new List<NodePort>();
	private readonly List<NodePort> outputs = new List<NodePort>();
	private readonly List<NodeInlineValue> inlineValues = new List<NodeInlineValue>();

	public Guid Id { get; }
	public NodeFunctionDescriptor Function { get; }
	public string Title => Function.Title;
	public Vector2 Position { get; set; }
	public float Width { get; set; } = 240;
	public IReadOnlyList<NodePort> Inputs { get; }
	public IReadOnlyList<NodePort> Outputs { get; }
	public IReadOnlyList<NodeInlineValue> InlineValues { get; }
	public NodeEvaluationState EvaluationState { get; internal set; }
	public string EvaluationMessage { get; internal set; }

	internal FunctionNode(NodeFunctionDescriptor function, Vector2 position, Guid? id = null)
	{
		Function = function ?? throw new ArgumentNullException(nameof(function));
		Id = id ?? Guid.NewGuid();
		Position = position;
		Inputs = inputs.AsReadOnly();
		Outputs = outputs.AsReadOnly();
		InlineValues = inlineValues.AsReadOnly();

		foreach (NodeParameterDescriptor parameter in function.Parameters)
		{
			if (parameter.IsInline)
			{
				inlineValues.Add(new NodeInlineValue(parameter));
			}
			else
			{
				inputs.Add(CreatePort(parameter.Name, parameter.Type, NodePortDirection.Input));
			}
		}

		foreach (NodeOutputDescriptor output in function.Outputs)
		{
			outputs.Add(CreatePort(output.Name, output.Type, NodePortDirection.Output));
		}
	}

	public NodePort GetInput(string name)
	{
		return GetPort(inputs, name, "input");
	}

	public NodePort GetOutput(string name)
	{
		return GetPort(outputs, name, "output");
	}

	public NodeInlineValue GetInlineValue(string name)
	{
		foreach (NodeInlineValue inlineValue in inlineValues)
		{
			if (string.Equals(inlineValue.Name, name, StringComparison.Ordinal))
			{
				return inlineValue;
			}
		}

		throw new KeyNotFoundException($"Node '{Id}' has no inline value named '{name}'.");
	}

	private NodePort CreatePort(string name, Type type, NodePortDirection direction)
	{
		return new NodePort(name, type, direction)
		{
			Node = this,
		};
	}

	private NodePort GetPort(IEnumerable<NodePort> ports, string name, string kind)
	{
		foreach (NodePort port in ports)
		{
			if (string.Equals(port.Name, name, StringComparison.Ordinal))
			{
				return port;
			}
		}

		throw new KeyNotFoundException($"Node '{Id}' has no {kind} port named '{name}'.");
	}
}

public sealed class NodeConnection
{
	public Guid Id { get; } = Guid.NewGuid();
	public NodePort Output { get; }
	public NodePort Input { get; }

	internal NodeConnection(NodePort output, NodePort input)
	{
		Output = output;
		Input = input;
	}
}

public sealed class NodeEvaluationResult
{
	public int SuccessfulNodeCount { get; internal set; }
	public int FailedNodeCount { get; internal set; }
	public bool Success => FailedNodeCount == 0;
	public string Summary => Success
		? $"Evaluated {SuccessfulNodeCount} nodes"
		: $"{FailedNodeCount} errors, {SuccessfulNodeCount} nodes evaluated";
}
