using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace FishGfx.NodeGraph;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class NodeFunctionAttribute : Attribute
{
	public string Id { get; }
	public string Title { get; set; }
	public string Category { get; set; }

	public NodeFunctionAttribute(string id)
	{
		Id = id;
	}
}

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class NodeInlineAttribute : Attribute
{
}

public sealed class NodeParameterDescriptor
{
	public ParameterInfo Parameter { get; }
	public string Name => Parameter.Name ?? "input";
	public Type Type => Parameter.ParameterType;
	public bool IsInline { get; }

	internal NodeParameterDescriptor(ParameterInfo parameter)
	{
		Parameter = parameter;
		IsInline = parameter.IsDefined(typeof(NodeInlineAttribute), false);
	}
}

public sealed class NodeOutputDescriptor
{
	public string Name { get; }
	public Type Type { get; }
	internal int TupleIndex { get; }

	internal NodeOutputDescriptor(string name, Type type, int tupleIndex = -1)
	{
		Name = name;
		Type = type;
		TupleIndex = tupleIndex;
	}
}

public sealed class NodeFunctionDescriptor
{
	public string Id { get; }
	public MethodInfo Method { get; }
	public string Title { get; }
	public string MenuLabel { get; }
	public string Category { get; }
	public IReadOnlyList<NodeParameterDescriptor> Parameters { get; }
	public IReadOnlyList<NodeOutputDescriptor> Outputs { get; }

	internal NodeFunctionDescriptor(MethodInfo method, bool overloaded)
	{
		NodeFunctionAttribute attribute = method.GetCustomAttribute<NodeFunctionAttribute>();

		Id = attribute.Id;
		Method = method;
		Title = string.IsNullOrWhiteSpace(attribute.Title) ? method.Name : attribute.Title.Trim();
		Category = string.IsNullOrWhiteSpace(attribute.Category)
			? method.DeclaringType?.Name ?? "Functions"
			: attribute.Category.Trim();
		Parameters = Array.AsReadOnly(
			method
				.GetParameters()
				.Select(parameter => new NodeParameterDescriptor(parameter))
				.ToArray()
		);
		Outputs = CreateOutputs(method);
		MenuLabel = overloaded
			? $"{Title}({string.Join(", ", Parameters.Select(parameter => NodeValueConverter.TypeName(parameter.Type)))})"
			: Title;
	}

	private static IReadOnlyList<NodeOutputDescriptor> CreateOutputs(MethodInfo method)
	{
		if (method.ReturnType == typeof(void))
		{
			return Array.Empty<NodeOutputDescriptor>();
		}

		List<Type> tupleTypes = new List<Type>();
		FlattenTupleTypes(method.ReturnType, tupleTypes);

		if (tupleTypes.Count == 0)
		{
			return Array.AsReadOnly(
				new[]
				{
					new NodeOutputDescriptor("result", method.ReturnType),
				}
			);
		}

		IList<string> names = method
			.ReturnParameter
			.GetCustomAttribute<TupleElementNamesAttribute>()
			?.TransformNames;

		return Array.AsReadOnly(
			tupleTypes
				.Select((type, index) => new NodeOutputDescriptor(OutputName(names, index), type, index))
				.ToArray()
		);
	}

	private static string OutputName(IList<string> names, int index)
	{
		if (names != null && index < names.Count && !string.IsNullOrWhiteSpace(names[index]))
		{
			return names[index];
		}

		return $"item{index + 1}";
	}

	private static void FlattenTupleTypes(Type type, List<Type> result)
	{
		if (!type.IsGenericType
			|| type.FullName?.StartsWith("System.ValueTuple`", StringComparison.Ordinal) != true)
		{
			return;
		}

		Type[] arguments = type.GetGenericArguments();
		int normalArgumentCount = arguments.Length == 8 ? 7 : arguments.Length;

		for (int index = 0; index < normalArgumentCount; index++)
		{
			result.Add(arguments[index]);
		}

		if (arguments.Length == 8)
		{
			FlattenTupleTypes(arguments[7], result);
		}
	}
}

public sealed class NodeFunctionRegistry
{
	private readonly HashSet<Type> registeredTypes = new HashSet<Type>();
	private readonly List<NodeFunctionDescriptor> functions = new List<NodeFunctionDescriptor>();
	private readonly Dictionary<string, NodeFunctionDescriptor> functionsById =
		new Dictionary<string, NodeFunctionDescriptor>(StringComparer.Ordinal);

	public IReadOnlyList<NodeFunctionDescriptor> Functions { get; }

	public NodeFunctionRegistry()
	{
		Functions = functions.AsReadOnly();
	}

	public IReadOnlyList<NodeFunctionDescriptor> Register(Type staticClass)
	{
		ValidateClass(staticClass);

		MethodInfo[] methods = staticClass
			.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
			.Where(method => method.IsDefined(typeof(NodeFunctionAttribute), false))
			.ToArray();

		foreach (MethodInfo method in methods)
		{
			ValidateMethod(method);
		}

		ValidateIds(methods);

		Dictionary<string, int> titleCounts = methods
			.GroupBy(TitleOf, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
		NodeFunctionDescriptor[] added = methods
			.Select(method => new NodeFunctionDescriptor(method, titleCounts[TitleOf(method)] > 1))
			.OrderBy(descriptor => descriptor.MenuLabel, StringComparer.Ordinal)
			.ThenBy(descriptor => descriptor.Id, StringComparer.Ordinal)
			.ToArray();

		registeredTypes.Add(staticClass);

		foreach (NodeFunctionDescriptor descriptor in added)
		{
			functions.Add(descriptor);
			functionsById.Add(descriptor.Id, descriptor);
		}

		return added;
	}

	public bool TryGet(string id, out NodeFunctionDescriptor descriptor)
	{
		if (id == null)
		{
			descriptor = null;
			return false;
		}

		return functionsById.TryGetValue(id, out descriptor);
	}

	public NodeFunctionDescriptor Get(string id)
	{
		if (!TryGet(id, out NodeFunctionDescriptor descriptor))
		{
			throw new KeyNotFoundException($"No node function is registered with id '{id}'.");
		}

		return descriptor;
	}

	private void ValidateClass(Type staticClass)
	{
		if (staticClass == null)
		{
			throw new ArgumentNullException(nameof(staticClass));
		}

		if (!staticClass.IsAbstract || !staticClass.IsSealed)
		{
			throw new ArgumentException("Registered node function types must be static classes.", nameof(staticClass));
		}

		if (staticClass.ContainsGenericParameters)
		{
			throw new ArgumentException("Open generic classes cannot be registered.", nameof(staticClass));
		}

		if (registeredTypes.Contains(staticClass))
		{
			throw new InvalidOperationException($"{staticClass.FullName} is already registered.");
		}
	}

	private void ValidateIds(IEnumerable<MethodInfo> methods)
	{
		HashSet<string> pending = new HashSet<string>(StringComparer.Ordinal);

		foreach (MethodInfo method in methods)
		{
			string id = method.GetCustomAttribute<NodeFunctionAttribute>().Id;

			if (string.IsNullOrWhiteSpace(id) || id != id.Trim())
			{
				throw new ArgumentException($"Node function {method.Name} must declare a non-empty, trimmed stable id.");
			}

			if (functionsById.ContainsKey(id) || !pending.Add(id))
			{
				throw new InvalidOperationException($"Node function id '{id}' is already registered.");
			}
		}
	}

	private static string TitleOf(MethodInfo method)
	{
		NodeFunctionAttribute attribute = method.GetCustomAttribute<NodeFunctionAttribute>();

		return string.IsNullOrWhiteSpace(attribute.Title) ? method.Name : attribute.Title.Trim();
	}

	private static void ValidateMethod(MethodInfo method)
	{
		if (method.IsGenericMethodDefinition || method.ContainsGenericParameters)
		{
			throw new NotSupportedException($"Generic node method {method.Name} is not supported.");
		}

		if (typeof(Task).IsAssignableFrom(method.ReturnType))
		{
			throw new NotSupportedException($"Async node method {method.Name} is not supported.");
		}

		foreach (ParameterInfo parameter in method.GetParameters())
		{
			if (parameter.ParameterType.IsByRef || parameter.IsOut)
			{
				throw new NotSupportedException($"ref/out parameter {parameter.Name} is not supported.");
			}

			bool isInline = parameter.IsDefined(typeof(NodeInlineAttribute), false);

			if (isInline && !NodeValueConverter.IsSupported(parameter.ParameterType))
			{
				throw new NotSupportedException(
					$"[NodeInline] parameter {parameter.Name} has unsupported type {parameter.ParameterType}."
				);
			}
		}

		NodeFunctionDescriptor descriptor = new NodeFunctionDescriptor(method, false);
		IEnumerable<string> duplicateOutputs = descriptor.Outputs
			.GroupBy(output => output.Name, StringComparer.Ordinal)
			.Where(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
			.Select(group => group.Key);

		if (duplicateOutputs.Any())
		{
			throw new NotSupportedException($"Node function {method.Name} has empty or duplicate output names.");
		}
	}
}
