using System;

namespace FishGfx.SmokeTest;

internal static class Program
{
	private static void Main(string[] args)
	{
		if (Array.Exists(
			args,
			argument => string.Equals(
				argument,
				"--windowing-auto",
				StringComparison.OrdinalIgnoreCase
			)
		))
		{
			WindowingSmokeTest.Run();

			return;
		}

		new PrimitiveGallery(args).Run();
	}
}
