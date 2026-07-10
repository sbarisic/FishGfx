using FishGfx.NodeGraph;
using System;
using System.Numerics;

namespace FishGfx.NodeEditor
{
	internal static class SampleNodeFunctions
	{
		internal static string LastOutput { get; private set; }

		[NodeFunction("Integer", Category = "Values")]
		public static int Integer([NodeBody] int value = 1) => value;

		[NodeFunction("Scalar", Category = "Values")]
		public static float Scalar([NodeBody] float value = 1) => value;

		[NodeFunction("Vector", Category = "Vector")]
		public static Vector2 Vector([NodeBody] float x = 1, [NodeBody] float y = 1) => new Vector2(x, y);

		[NodeFunction("Add", Category = "Math")]
		public static float Add(float a, float b) => a + b;

		[NodeFunction("Multiply", Category = "Vector")]
		public static Vector2 Multiply(Vector2 vector, float scalar) => vector * scalar;

		[NodeFunction("Split Vector", Category = "Vector")]
		public static (float x, float y) Split(Vector2 vector) => (vector.X, vector.Y);

		[NodeFunction("Display", Category = "Output")]
		public static void Display(float value, [NodeBody] string label = "Result") => LastOutput = $"{label}: {value:0.###}";

		[NodeFunction("Console", Category = "Output")]
		public static void Console(float value, [NodeBody] string label = "Result")
		{
			System.Console.WriteLine($"{label}: {value}");
		}

		[NodeFunction("Fail", Category = "Debug")]
		public static float Fail([NodeBody] string message = "Example error") => throw new InvalidOperationException(message);
	}
}
