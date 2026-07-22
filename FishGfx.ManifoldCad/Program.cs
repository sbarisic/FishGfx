namespace FishGfx.ManifoldCad;

internal static class Program
{
	[STAThread]
	private static int Main(string[] args)
	{
		try
		{
			using ManifoldCadApplication application = new(args);
			application.Run();
			return 0;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return 1;
		}
	}
}
