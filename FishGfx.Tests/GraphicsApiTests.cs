using System;
using System.Numerics;
using System.Reflection;
using FishGfx.Graphics;
using Xunit;

namespace FishGfx.Tests;

public sealed class GraphicsApiTests
{
	[Theory]
	[InlineData("FishGfx.RenderAPI")]
	[InlineData("FishGfx.GfxFont")]
	[InlineData("FishGfx.Formats.BMFont")]
	[InlineData("FishGfx.Formats.TTFFont")]
	[InlineData("FishGfx.Graphics.Gfx")]
	[InlineData("FishGfx.Graphics.GraphicsObject")]
	[InlineData("FishGfx.Graphics.RenderTexture")]
	[InlineData("FishGfx.Graphics.Renderbuffer")]
	[InlineData("FishGfx.Graphics.ShaderUniforms")]
	public void RemovedCompatibilityTypesAreNotPublished(string typeName)
	{
		Assert.Null(typeof(GraphicsContext).Assembly.GetType(typeName));
	}

	[Fact]
	public void CurrentIsTheOnlyPublicStaticContextEscapeHatch()
	{
		PropertyInfo current = Assert.Single(
			typeof(GraphicsContext).GetProperties(
				BindingFlags.Public
				| BindingFlags.Static
				| BindingFlags.DeclaredOnly
			)
		);

		Assert.Equal(nameof(GraphicsContext.Current), current.Name);
		Assert.NotNull(current.GetMethod);
		Assert.Null(current.SetMethod);
		Assert.Empty(
			typeof(GraphicsContext).GetFields(
				BindingFlags.Public
				| BindingFlags.Static
				| BindingFlags.DeclaredOnly
			)
		);
		Assert.DoesNotContain(
			typeof(GraphicsContext).GetMethods(
				BindingFlags.Public
				| BindingFlags.Static
				| BindingFlags.DeclaredOnly
			),
			method => !method.IsSpecialName
		);
	}

	[Fact]
	public void OpenGlVersionsValidateAndOrderLexicographically()
	{
		OpenGlVersion version40 = new(4, 0);
		OpenGlVersion version45 = new(4, 5);
		OpenGlVersion version50 = new(5, 0);

		Assert.True(version40 < version45);
		Assert.True(version45 < version50);
		Assert.True(version50 >= version45);
		Assert.Equal("4.5", version45.ToString());
		Assert.Throws<ArgumentOutOfRangeException>(() => new OpenGlVersion(0, 0));
		Assert.Throws<ArgumentOutOfRangeException>(() => new OpenGlVersion(4, -1));
	}

	[Fact]
	public void DefaultStencilFacesIncludeCompleteWriteMasks()
	{
		RenderPassDescriptor descriptor = new();
		StencilState stencil = StencilState.Default;

		Assert.Null(descriptor.State.Stencil);
		Assert.Equal(uint.MaxValue, stencil.Front.WriteMask);
		Assert.Equal(uint.MaxValue, stencil.Back.WriteMask);

		stencil = stencil with
		{
			Front = stencil.Front with
			{
				WriteMask = 0x0f,
			},
			Back = stencil.Back with
			{
				WriteMask = 0xf0,
			},
		};

		Assert.Equal(0x0fu, stencil.Front.WriteMask);
		Assert.Equal(0xf0u, stencil.Back.WriteMask);
	}

	[Fact]
	public void RenderPassDescriptorCarriesShaderTimeInSeconds()
	{
		RenderPassDescriptor descriptor = new()
		{
			Time = 12.5f,
		};

		Assert.Equal(12.5f, descriptor.Time);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new RenderPassDescriptor
			{
				Time = float.NaN,
			}
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new RenderPassDescriptor
			{
				Time = float.PositiveInfinity,
			}
		);
	}

	[Fact]
	public void RenderViewRejectsNegativeViewportDimensions()
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new RenderView(
				Matrix4x4.Identity,
				Matrix4x4.Identity,
				Vector3.Zero,
				new Vector2(-1, 100),
				0.1f,
				100
			)
		);
	}

	[Fact]
	public void RenderViewComparersSortWithoutReadingMutableCameraState()
	{
		RenderCommandList commands = new();
		commands.Add(new NoOpCommand());
		RenderQueue queue = new();
		RenderItem near = queue.SubmitOpaque(
			commands,
			Matrix4x4.Identity,
			new Vector3(0, 0, -2)
		);
		RenderItem far = queue.SubmitOpaque(
			commands,
			Matrix4x4.Identity,
			new Vector3(0, 0, -10)
		);
		RenderView view = new(
			Matrix4x4.Identity,
			Matrix4x4.Identity,
			Vector3.Zero,
			new Vector2(800, 600),
			0.1f,
			100
		);

		RenderItem[] opaque = { far, near };
		Array.Sort(opaque, RenderItemComparers.OpaqueFrontToBack(view));
		Assert.Equal(new[] { near, far }, opaque);

		RenderItem[] transparent = { near, far };
		Array.Sort(transparent, RenderItemComparers.TransparentBackToFront(view));
		Assert.Equal(new[] { far, near }, transparent);

		RenderItem stateA = queue.SubmitOpaque(
			commands,
			Matrix4x4.Identity,
			new Vector3(0, 0, -20),
			sortKey: 1
		);
		RenderItem stateB = queue.SubmitOpaque(
			commands,
			Matrix4x4.Identity,
			new Vector3(0, 0, -1),
			sortKey: 2
		);
		RenderItem[] stateFirst = { stateB, stateA };
		Array.Sort(
			stateFirst,
			RenderItemComparers.OpaqueStateThenFrontToBack(view)
		);
		Assert.Equal(new[] { stateA, stateB }, stateFirst);
	}

	private sealed class NoOpCommand : RenderCommand
	{
		public override void Execute(RenderPass pass)
		{
		}
	}
}
