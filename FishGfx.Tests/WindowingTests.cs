using System.Numerics;
using FishGfx.Graphics;
using Glfw3;
using Xunit;

namespace FishGfx.Tests;

public sealed class WindowingTests
{
	[Fact]
	public void WindowModesPublishTheThreeSupportedPresentationModes()
	{
		Assert.Equal(
			new[]
			{
				WindowMode.Windowed,
				WindowMode.BorderlessFullscreen,
				WindowMode.ExclusiveFullscreen,
			},
			Enum.GetValues<WindowMode>()
		);
	}

	[Fact]
	public void RenderWindowOptionsRejectInvalidMonitorAndModeValues()
	{
		RenderWindowOptions invalidMonitor = new()
		{
			MonitorIndex = -2,
		};
		RenderWindowOptions invalidMode = new()
		{
			Mode = (WindowMode)99,
		};

		Assert.Throws<ArgumentOutOfRangeException>(
			() => RenderWindow.ValidateOptions(invalidMonitor)
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => RenderWindow.ValidateOptions(invalidMode)
		);
	}

	[Fact]
	public void RenderWindowOptionsRejectInvalidExclusiveVideoModes()
	{
		RenderWindowOptions options = new()
		{
			Mode = WindowMode.ExclusiveFullscreen,
			ExclusiveVideoMode = new MonitorVideoMode(
				1920,
				0,
				60
			),
		};

		Assert.Throws<ArgumentOutOfRangeException>(
			() => RenderWindow.ValidateOptions(options)
		);
	}

	[Fact]
	public void ExclusiveVideoModeRequiresExclusiveStartupMode()
	{
		RenderWindowOptions options = new()
		{
			Mode = WindowMode.BorderlessFullscreen,
			ExclusiveVideoMode = new MonitorVideoMode(1920, 1080, 60),
		};

		Assert.Throws<ArgumentException>(
			() => RenderWindow.ValidateOptions(options)
		);
	}

	[Theory]
	[InlineData(1280, 720, 1280, 720, 1, 1)]
	[InlineData(1280, 720, 2560, 1440, 2, 2)]
	[InlineData(1000, 800, 1500, 1200, 1.5f, 1.5f)]
	[InlineData(0, 0, 0, 0, 1, 1)]
	public void ContentScaleFallbackUsesIndependentFramebufferDimensions(
		int logicalWidth,
		int logicalHeight,
		int framebufferWidth,
		int framebufferHeight,
		float expectedX,
		float expectedY
	)
	{
		Vector2 scale = RenderWindow.CalculateContentScale(
			logicalWidth,
			logicalHeight,
			framebufferWidth,
			framebufferHeight
		);

		Assert.Equal(new Vector2(expectedX, expectedY), scale);
	}

	[Fact]
	public void MonitorVideoModePublishesOnlySelectableDimensionsAndRefreshRate()
	{
		MonitorVideoMode mode = new(2560, 1440, 144);

		Assert.Equal(new Vector2(2560, 1440), mode.Size);
		Assert.Equal(144, mode.RefreshRate);
		Assert.Null(typeof(MonitorVideoMode).GetProperty("RedBits"));
		Assert.Null(typeof(MonitorVideoMode).GetProperty("GreenBits"));
		Assert.Null(typeof(MonitorVideoMode).GetProperty("BlueBits"));
	}

	[Fact]
	public void DisconnectedMonitorFallbackPreservesTheOriginalSelectionIdentity()
	{
		Glfw.Monitor disconnected = new()
		{
			Ptr = new IntPtr(1),
		};
		Glfw.Monitor primary = new()
		{
			Ptr = new IntPtr(2),
		};

		Glfw.Monitor resolved = RenderWindow.ResolveSelectedMonitor(
			disconnected,
			new[] { primary },
			primary
		);

		Assert.Equal(primary, resolved);
		Assert.Equal(new IntPtr(1), disconnected.Ptr);
		Assert.NotEqual(disconnected, resolved);
	}
}
