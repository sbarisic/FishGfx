using System;
using System.Linq;
using System.Numerics;
using FishGfx.Graphics;
using Xunit;

namespace FishGfx.Tests;

public class GraphicsApiTests
{
	[Fact]
	public void OpenGlVersionsValidateAndOrderLexicographically()
	{
		OpenGLVersion v40 = new(4, 0);
		OpenGLVersion v45 = new(4, 5);
		OpenGLVersion v50 = new(5, 0);

		Assert.True(v40 < v45);
		Assert.True(v45 < v50);
		Assert.True(v50 >= v45);
		Assert.Equal("4.5", v45.ToString());
		Assert.Throws<ArgumentOutOfRangeException>(() => new OpenGLVersion(0, 0));
		Assert.Throws<ArgumentOutOfRangeException>(() => new OpenGLVersion(4, -1));
	}

	[Fact]
	public void DefaultPassStateIncludesCompleteStencilWriteMasks()
	{
		RenderPassDescriptor descriptor = new();

		Assert.Equal(0, descriptor.Time);
		Assert.Equal(uint.MaxValue, descriptor.State.StencilFrontWriteMask);
		Assert.Equal(uint.MaxValue, descriptor.State.StencilBackWriteMask);

		RenderState state = descriptor.State;
		state.StencilWriteMask(0x0f);
		state.StencilWriteMaskSeparate(StencilFace.Back, 0xf0);

		Assert.Equal(0x0fu, state.StencilFrontWriteMask);
		Assert.Equal(0xf0u, state.StencilBackWriteMask);
	}

	[Fact]
	public void RenderPassDescriptorCarriesShaderTimeInSeconds()
	{
		RenderPassDescriptor descriptor = new() { Time = 12.5f };

		Assert.Equal(12.5f, descriptor.Time);
		Assert.Throws<ArgumentOutOfRangeException>(() => descriptor.Time = float.NaN);
		Assert.Throws<ArgumentOutOfRangeException>(() => descriptor.Time = float.PositiveInfinity);
	}

	[Fact]
	public void RenderViewRejectsNegativeViewportDimensions()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new RenderView(
			Matrix4x4.Identity,
			Matrix4x4.Identity,
			Vector3.Zero,
			new Vector2(-1, 100),
			0.1f,
			100
		));
	}

	[Fact]
	public void RenderViewComparersSortWithoutReadingMutableCameraState()
	{
		CommandList commands = new();
		commands.Add(new NoOpCommand());
		DeferredRenderQueue queue = new();
		RenderSubmission near = queue.SubmitOpaque(commands, Matrix4x4.Identity, new Vector3(0, 0, -2));
		RenderSubmission far = queue.SubmitOpaque(commands, Matrix4x4.Identity, new Vector3(0, 0, -10));
		RenderView view = new(
			Matrix4x4.Identity,
			Matrix4x4.Identity,
			Vector3.Zero,
			new Vector2(800, 600),
			0.1f,
			100
		);

		RenderSubmission[] opaque = new[] { far, near };
		Array.Sort(opaque, RenderSubmissionComparers.OpaqueFrontToBack(view));
		Assert.Equal(new[] { near, far }, opaque);

		RenderSubmission[] transparent = new[] { near, far };
		Array.Sort(transparent, RenderSubmissionComparers.TransparentBackToFront(view));
		Assert.Equal(new[] { far, near }, transparent);

		RenderSubmission stateA = queue.SubmitOpaque(commands, Matrix4x4.Identity, new Vector3(0, 0, -20), sortKey: 1);
		RenderSubmission stateB = queue.SubmitOpaque(commands, Matrix4x4.Identity, new Vector3(0, 0, -1), sortKey: 2);
		RenderSubmission[] stateFirst = new[] { stateB, stateA };
		Array.Sort(stateFirst, RenderSubmissionComparers.OpaqueStateThenFrontToBack(view));
		Assert.Equal(new[] { stateA, stateB }, stateFirst);
	}

	private sealed class NoOpCommand : GraphicsCommand
	{
		public override void Execute() { }
	}
}
