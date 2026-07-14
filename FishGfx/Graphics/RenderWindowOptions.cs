namespace FishGfx.Graphics;

public sealed class RenderWindowOptions
{
	public int Width { get; set; } = 1280;

	public int Height { get; set; } = 720;

	public string Title { get; set; } = "FishGfx";

	public bool Resizable { get; set; }

	public bool CenterWindow { get; set; } = true;

	public OpenGlVersion PreferredVersion { get; set; } = new(4, 6);

	public OpenGlVersion MinimumVersion { get; set; } = new(4, 0);

	public bool RequireExactVersion { get; set; }
}
