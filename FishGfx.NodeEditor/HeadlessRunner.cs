using System;
using System.IO;
using FishGfx.NodeGraph;

namespace FishGfx.NodeEditor;

internal static class HeadlessRunner
{
	internal static int Execute(string path)
	{
		TextWriter output = Console.Out;

		try
		{
			Console.SetOut(TextWriter.Null);
			NodeFunctionRegistry registry = new NodeFunctionRegistry();
			registry.Register(typeof(SampleNodeFunctions));
			NodeGraphExecutionResult result = NodeGraphJson.LoadAndEvaluateFile(path, registry);

			Console.SetOut(output);
			output.WriteLine(NodeGraphJson.SerializeExecutionResult(result));

			if (result.Errors.Count > 0)
			{
				return 2;
			}

			return result.Success ? 0 : 1;
		}
		catch (Exception ex)
		{
			Console.SetOut(output);
			NodeGraphExecutionResult result = new NodeGraphExecutionResult { Success = false };

			result.Errors.Add(ex.Message);
			output.WriteLine(NodeGraphJson.SerializeExecutionResult(result));

			return 2;
		}
		finally
		{
			Console.SetOut(output);
		}
	}
}
