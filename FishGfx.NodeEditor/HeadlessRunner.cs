using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FishGfx.NodeGraph;

namespace FishGfx.NodeEditor;

internal static class HeadlessRunner
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
	};

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

	internal static int BuildVisual(string path)
	{
		VisualProgramLoadResult load = VisualProgramJson.LoadFile(path, VisualNodeCatalog.CreateCore());

		if (!load.Success)
		{
			WriteVisualResult(false, load.Errors, Array.Empty<DotNetBuildDiagnostic>(), "", "");
			return 2;
		}

		CSharpGenerationResult generation = new CSharpProgramGenerator().Generate(load.Program);

		if (!generation.Success)
		{
			WriteVisualResult(
				false,
				generation.Diagnostics.Select(diagnostic => diagnostic.Message),
				Array.Empty<DotNetBuildDiagnostic>(),
				"",
				""
			);

			return 1;
		}

		try
		{
			using DotNetProgramBuildResult build = new DotNetProgramRunner()
				.BuildAsync(generation)
				.GetAwaiter()
				.GetResult();

			WriteVisualResult(build.Success, Array.Empty<string>(), build.Diagnostics, build.Output, build.Error);

			return build.Success ? 0 : 1;
		}
		catch (Exception exception)
		{
			WriteVisualResult(false, new[] { exception.Message }, Array.Empty<DotNetBuildDiagnostic>(), "", "");
			return 2;
		}
	}

	internal static int RunVisual(string path)
	{
		VisualProgramLoadResult load = VisualProgramJson.LoadFile(path, VisualNodeCatalog.CreateCore());

		if (!load.Success)
		{
			WriteVisualResult(false, load.Errors, Array.Empty<DotNetBuildDiagnostic>(), "", "");
			return 2;
		}

		CSharpGenerationResult generation = new CSharpProgramGenerator().Generate(load.Program);

		if (!generation.Success)
		{
			WriteVisualResult(
				false,
				generation.Diagnostics.Select(diagnostic => diagnostic.Message),
				Array.Empty<DotNetBuildDiagnostic>(),
				"",
				""
			);

			return 1;
		}

		DotNetProgramRunResult run = new DotNetProgramRunner()
			.BuildAndRunAsync(generation)
			.GetAwaiter()
			.GetResult();

		WriteVisualResult(run.Success, Array.Empty<string>(), run.Diagnostics, run.Output, run.Error);

		return run.Success ? 0 : 1;
	}

	private static void WriteVisualResult(
		bool success,
		System.Collections.Generic.IEnumerable<string> errors,
		System.Collections.Generic.IEnumerable<DotNetBuildDiagnostic> diagnostics,
		string output,
		string errorOutput
	)
	{
		Console.WriteLine(
			JsonSerializer.Serialize(
				new
				{
					version = VisualProgramJson.CurrentVersion,
					success,
					errors = errors.ToArray(),
					diagnostics = diagnostics.ToArray(),
					output,
					errorOutput,
				},
				JsonOptions
			)
		);
	}
}
