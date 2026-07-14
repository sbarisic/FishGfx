using System.Numerics;

namespace FishGfx.Graphics;

internal sealed class RenderUniformState
{
	internal const string ModelUniformName = "uModel";
	internal const string ProjectionUniformName = "uProjection";
	internal const string TimeUniformName = "uTime";
	internal const string ViewUniformName = "uView";
	internal const string ViewPositionUniformName = "uViewPosition";

	internal RenderUniformState(RenderView view, float time)
	{
		View = view;
		Model = Matrix4x4.Identity;
		Time = time;
	}

	internal RenderView View { get; set; }

	internal Matrix4x4 Model { get; set; }

	internal Vector2 TextureSize { get; set; }

	internal float AlphaCutoff { get; set; }

	internal float Time { get; set; }

	internal int SampleCount { get; set; }

	internal void Apply(ShaderProgram program)
	{
		program.SetUniform("uNear", View.Near);
		program.SetUniform("uFar", View.Far);
		program.SetUniform(ViewUniformName, View.View);
		program.SetUniform(ProjectionUniformName, View.Projection);
		program.SetUniform(ModelUniformName, Model);
		program.SetUniform("uViewport", View.ViewportSize);
		program.SetUniform("uAlphaCutoff", AlphaCutoff);
		program.SetUniform(TimeUniformName, Time);
		program.SetUniform("uSampleCount", SampleCount);
		program.SetUniform("uTextureSize", TextureSize);
		program.SetUniform("uResolution", View.ViewportSize);
		program.SetUniform(ViewPositionUniformName, View.Position);
	}
}
