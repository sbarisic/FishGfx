namespace FishGfx.NodeEditor;

internal static class Program
{
	private static int Main(string[] args)
	{
		if (args.Length >= 2 && string.Equals(args[0], "--execute", System.StringComparison.OrdinalIgnoreCase))
		{
			return HeadlessRunner.Execute(args[1]);
		}

		new NodeEditorApplication(args).Run();

		return 0;
	}
}
