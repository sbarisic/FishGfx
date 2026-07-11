using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace FishGfx.NodeGraph
{
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
	public sealed class NodeFunctionAttribute : Attribute
	{
		public string DisplayName { get; }
		public string Category { get; set; }

		public NodeFunctionAttribute(string displayName = null)
		{
			DisplayName = displayName;
		}
	}

	[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
	public sealed class NodeBodyAttribute : Attribute { }

	public sealed class NodeParameterDescriptor
	{
		public ParameterInfo Parameter { get; }
		public string Name => Parameter.Name ?? "input";
		public Type Type => Parameter.ParameterType;
		public bool IsBody { get; }

		internal NodeParameterDescriptor(ParameterInfo parameter)
		{
			Parameter = parameter;
			IsBody = parameter.IsDefined(typeof(NodeBodyAttribute), false);
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
		public MethodInfo Method { get; }
		public string Title { get; }
		public string MenuLabel { get; }
		public string Group { get; }
		public IReadOnlyList<NodeParameterDescriptor> Parameters { get; }
		public IReadOnlyList<NodeOutputDescriptor> Outputs { get; }

		internal NodeFunctionDescriptor(MethodInfo method, string title, bool overloaded)
		{
			Method = method;
			Title = title;
			string category = method.GetCustomAttribute<NodeFunctionAttribute>().Category;
			Group = string.IsNullOrWhiteSpace(category) ? Method.DeclaringType?.Name ?? "Functions" : category.Trim();
			Parameters = method.GetParameters().Select(p => new NodeParameterDescriptor(p)).ToArray();
			Outputs = CreateOutputs(method);
			MenuLabel = overloaded
				? $"{title}({string.Join(", ", Parameters.Select(p => NodeValueConverter.TypeName(p.Type)))})"
				: title;
		}

		private static IReadOnlyList<NodeOutputDescriptor> CreateOutputs(MethodInfo method)
		{
			if (method.ReturnType == typeof(void))
				return Array.Empty<NodeOutputDescriptor>();
			List<Type> tupleTypes = new List<Type>();
			FlattenTupleTypes(method.ReturnType, tupleTypes);

			if (tupleTypes.Count == 0)
				return new[] { new NodeOutputDescriptor("return", method.ReturnType) };
			IList<string> names = method
				.ReturnParameter.GetCustomAttribute<TupleElementNamesAttribute>()
				?.TransformNames;
			return tupleTypes
				.Select(
					(type, index) =>
						new NodeOutputDescriptor(
							names != null && index < names.Count && !string.IsNullOrWhiteSpace(names[index])
								? names[index]
								: $"Item{index + 1}",
							type,
							index
						)
				)
				.ToArray();
		}

		private static void FlattenTupleTypes(Type type, List<Type> result)
		{
			if (!type.IsGenericType || !type.FullName.StartsWith("System.ValueTuple`", StringComparison.Ordinal))
				return;
			Type[] args = type.GetGenericArguments();
			int normal = args.Length == 8 ? 7 : args.Length;

			for (int i = 0; i < normal; i++)
				result.Add(args[i]);
			if (args.Length == 8)
				FlattenTupleTypes(args[7], result);
		}
	}

	public sealed class NodeFunctionRegistry
	{
		private readonly HashSet<Type> registered = new HashSet<Type>();
		private readonly List<NodeFunctionDescriptor> functions = new List<NodeFunctionDescriptor>();
		public IReadOnlyList<NodeFunctionDescriptor> Functions => functions;

		public IReadOnlyList<NodeFunctionDescriptor> Register(Type staticClass)
		{
			if (staticClass == null)
				throw new ArgumentNullException(nameof(staticClass));
			if (!staticClass.IsAbstract || !staticClass.IsSealed)
				throw new ArgumentException(
					"Registered node function types must be static classes.",
					nameof(staticClass)
				);
			if (staticClass.ContainsGenericParameters)
				throw new ArgumentException("Open generic classes cannot be registered.", nameof(staticClass));
			if (registered.Contains(staticClass))
				throw new InvalidOperationException($"{staticClass.FullName} is already registered.");

			MethodInfo[] methods = staticClass
				.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
				.Where(m => m.IsDefined(typeof(NodeFunctionAttribute), false))
				.ToArray();
			foreach (MethodInfo method in methods)
				Validate(method);
			registered.Add(staticClass);
			Dictionary<string, int> titleCounts = methods.GroupBy(TitleOf).ToDictionary(g => g.Key, g => g.Count());
			NodeFunctionDescriptor[] added = methods
				.Select(m => new NodeFunctionDescriptor(m, TitleOf(m), titleCounts[TitleOf(m)] > 1))
				.OrderBy(d => d.MenuLabel, StringComparer.Ordinal)
				.ToArray();
			functions.AddRange(added);
			return added;
		}

		private static string TitleOf(MethodInfo method) =>
			method.GetCustomAttribute<NodeFunctionAttribute>().DisplayName ?? method.Name;

		private static void Validate(MethodInfo method)
		{
			if (method.IsGenericMethodDefinition || method.ContainsGenericParameters)
				throw new NotSupportedException($"Generic node method {method.Name} is not supported.");
			if (typeof(Task).IsAssignableFrom(method.ReturnType))
				throw new NotSupportedException($"Async node method {method.Name} is not supported.");
			foreach (ParameterInfo parameter in method.GetParameters())
			{
				if (parameter.ParameterType.IsByRef || parameter.IsOut)
					throw new NotSupportedException($"ref/out parameter {parameter.Name} is not supported.");
				if (
					parameter.IsDefined(typeof(NodeBodyAttribute), false)
					&& !NodeValueConverter.IsSupported(parameter.ParameterType)
				)
					throw new NotSupportedException(
						$"[NodeBody] parameter {parameter.Name} has unsupported type {parameter.ParameterType}."
					);
			}
		}
	}
}
