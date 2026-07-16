namespace FishGfx.NodeEditor;

internal static class Program
{
	private static int Main(string[] args)
	{
		if (args.Length >= 2 && string.Equals(args[0], "--execute", System.StringComparison.OrdinalIgnoreCase))
		{
			return HeadlessRunner.Execute(args[1]);
		}

		if (args.Length >= 2 && string.Equals(args[0], "--build", System.StringComparison.OrdinalIgnoreCase))
		{
			return HeadlessRunner.BuildVisual(args[1]);
		}

		if (args.Length >= 2 && string.Equals(args[0], "--run", System.StringComparison.OrdinalIgnoreCase))
		{
			return HeadlessRunner.RunVisual(args[1]);
		}

		if (System.Array.Exists(args, argument => string.Equals(argument, "--legacy", System.StringComparison.OrdinalIgnoreCase))
			|| IsLegacyDocument(args))
		{
			new NodeEditorApplication(args).Run();
			return 0;
		}

		new VisualNodeEditorApplication(args).Run();

		return 0;
	}

	private static bool IsLegacyDocument(string[] args)
	{
		string path = System.Array.Find(args, argument => !argument.StartsWith("--", System.StringComparison.Ordinal));

		if (path == null || !System.IO.File.Exists(path))
		{
			return false;
		}

		try
		{
			using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(
				System.IO.File.ReadAllText(path)
			);

			return !document.RootElement.TryGetProperty("schema", out _)
				&& document.RootElement.TryGetProperty("version", out System.Text.Json.JsonElement version)
				&& version.GetInt32() == FishGfx.NodeGraph.NodeGraphJson.CurrentVersion;
		}
		catch (System.Exception exception) when (
			exception is System.Text.Json.JsonException
			|| exception is System.InvalidOperationException
			|| exception is System.FormatException
			|| exception is System.IO.IOException
			|| exception is System.UnauthorizedAccessException
		)
		{
			return false;
		}
	}
}
