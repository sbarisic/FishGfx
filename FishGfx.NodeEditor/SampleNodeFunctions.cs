using System;
using System.Numerics;
using FishGfx.NodeGraph;

namespace FishGfx.NodeEditor;

internal static class SampleNodeFunctions
{
	internal static string LastOutput { get; private set; }

	[NodeFunction("value.integer", Title = "Integer", Category = "Values")]
	public static int Integer([NodeInline] int value = 1) => value;

	[NodeFunction("value.scalar", Title = "Scalar", Category = "Values")]
	public static float Scalar([NodeInline] float value = 1) => value;

	[NodeFunction("value.vector2", Title = "Vector", Category = "Vector")]
	public static Vector2 Vector([NodeInline] float x = 1, [NodeInline] float y = 1) => new Vector2(x, y);

	[NodeFunction("math.add", Title = "Add", Category = "Math")]
	public static float Add(float a, float b) => a + b;

	[NodeFunction("vector.multiply", Title = "Multiply", Category = "Vector")]
	public static Vector2 Multiply(Vector2 vector, float scalar) => vector * scalar;

	[NodeFunction("vector.split", Title = "Split Vector", Category = "Vector")]
	public static (float x, float y) Split(Vector2 vector) => (vector.X, vector.Y);

	[NodeFunction("output.display", Title = "Display", Category = "Output")]
	public static void Display(float value, [NodeInline] string label = "Result")
	{
		LastOutput = $"{label}: {value:0.###}";
	}

	[NodeFunction("output.console", Title = "Console", Category = "Output")]
	public static float Console(float value, [NodeInline] string label = "Result")
	{
		System.Console.WriteLine($"{label}: {value}");
		return value;
	}

	[NodeFunction("debug.fail", Title = "Fail", Category = "Debug")]
	public static float Fail([NodeInline] string message = "Example error") =>
		throw new InvalidOperationException(message);
}
