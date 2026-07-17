using System.Numerics;
using FishGfx.FishUI;
using FishGfx.Graphics;
using Xunit;

namespace FishGfx.Tests;

public sealed class FishUIIntegrationTests
{
	[Fact]
	public void TopLeftCoordinatesConvertToFishGfxCoordinates()
	{
		Assert.Equal(new Vector2(12, 80), FishUIConversions.ToFishGfxPoint(new Vector2(12, 20), 100));
		Assert.Equal(
			new Vector2(12, 50),
			FishUIConversions.ToFishGfxRectanglePosition(new Vector2(12, 20), new Vector2(30, 30), 100)
		);
	}

	[Fact]
	public void ScissorRectangleUsesIndependentFramebufferScale()
	{
		(Vector2 position, Vector2 size) = FishUIConversions.ToFramebufferRectangle(
			new Vector2(10.25f, 20.25f),
			new Vector2(30.5f, 10.5f),
			100,
			new Vector2(1.5f, 2)
		);

		Assert.Equal(new Vector2(15, 138), position);
		Assert.Equal(new Vector2(47, 22), size);
	}

	[Fact]
	public void AtlasUvConversionAccountsForTopLeftSourceCoordinates()
	{
		(Vector2 minimum, Vector2 maximum) = FishUIConversions.ToAtlasUv(
			new Vector2(32, 16),
			new Vector2(16, 32),
			64,
			64
		);

		Assert.Equal(new Vector2(0.5f, 0.25f), minimum);
		Assert.Equal(new Vector2(0.75f, 0.75f), maximum);
	}

	[Fact]
	public void FishColorConversionPreservesAllChannels()
	{
		Color converted = FishUIConversions.ToFishGfxColor(new global::FishUI.FishColor(1, 2, 3, 4));
		Assert.Equal(new Color(1, 2, 3, 4), converted);
	}

	[Fact]
	public void RootedFileSystemResolvesRelativePathsFromConfiguredRoot()
	{
		string root = Path.Combine(Path.GetTempPath(), $"FishGfx-FishUI-{Guid.NewGuid():N}");
		Directory.CreateDirectory(root);
		Directory.CreateDirectory(Path.Combine(root, "data"));
		try
		{
			RootedFishUIFileSystem fileSystem = new RootedFishUIFileSystem(root);
			fileSystem.WriteAllText(Path.Combine("data", "theme.yaml"), "theme: test");

			Assert.True(fileSystem.Exists(Path.Combine("data", "theme.yaml")));
			Assert.Equal("theme: test", fileSystem.ReadAllText(Path.Combine("data", "theme.yaml")));
			Assert.StartsWith(Path.GetFullPath(root), fileSystem.GetFullPath("data"), StringComparison.OrdinalIgnoreCase);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void InputStateQueuesAndResetsKeyboardTransitions()
	{
		FishUIInputState state = new FishUIInputState { Enabled = true };
		state.BeginFrame();
		state.OnKey(Key.A, true, false);

		Assert.True(state.IsKeyDown(global::FishUI.FishKey.A));
		Assert.True(state.IsKeyPressed(global::FishUI.FishKey.A));
		Assert.Equal(global::FishUI.FishKey.A, state.GetKeyPressed());
		Assert.Equal(global::FishUI.FishKey.None, state.GetKeyPressed());

		state.BeginFrame();
		Assert.True(state.IsKeyDown(global::FishUI.FishKey.A));
		Assert.False(state.IsKeyPressed(global::FishUI.FishKey.A));
		state.OnKey(Key.A, false, false);
		Assert.True(state.IsKeyReleased(global::FishUI.FishKey.A));
		Assert.False(state.IsKeyDown(global::FishUI.FishKey.A));
	}

	[Fact]
	public void InputStateQueuesCharactersMouseAndWheel()
	{
		FishUIInputState state = new FishUIInputState { Enabled = true };
		state.BeginFrame();
		state.OnCharacter('x');
		state.OnScroll(2.5f);
		state.OnMouseButton(MouseButton.Left, true, false);

		Assert.Equal('x', state.GetCharPressed());
		Assert.Equal(0, state.GetCharPressed());
		Assert.Equal(2.5f, state.MouseWheel);
		Assert.True(state.IsMouseDown(global::FishUI.FishMouseButton.Left));
		Assert.True(state.IsMousePressed(global::FishUI.FishMouseButton.Left));

		state.BeginFrame();
		state.OnMouseButton(MouseButton.Left, false, false);
		Assert.True(state.IsMouseReleased(global::FishUI.FishMouseButton.Left));
	}

	[Fact]
	public void DisabledInputTracksHeldStateWithoutExposingInteractions()
	{
		FishUIInputState state = new FishUIInputState();
		state.OnKey(Key.W, true, false);
		state.OnMouseButton(MouseButton.Right, true, false);
		state.OnCharacter('x');
		state.OnScroll(1);

		Assert.False(state.IsKeyDown(global::FishUI.FishKey.W));
		Assert.False(state.IsMouseDown(global::FishUI.FishMouseButton.Right));
		Assert.Equal(0, state.GetCharPressed());
		Assert.Equal(0, state.MouseWheel);

		state.Enabled = true;
		Assert.True(state.IsKeyDown(global::FishUI.FishKey.W));
		Assert.True(state.IsMouseDown(global::FishUI.FishMouseButton.Right));
		Assert.False(state.IsKeyPressed(global::FishUI.FishKey.W));
		Assert.False(state.IsMousePressed(global::FishUI.FishMouseButton.Right));
	}

	[Theory]
	[InlineData(Key.Alpha1, global::FishUI.FishKey.One)]
	[InlineData(Key.Tab, global::FishUI.FishKey.Tab)]
	[InlineData(Key.NumpadEnter, global::FishUI.FishKey.KpEnter)]
	public void KeyboardMappingUsesSharedGlfwValues(Key key, global::FishUI.FishKey expected)
	{
		Assert.True(FishUIInputAdapter.TryMapKey(key, out global::FishUI.FishKey actual));
		Assert.Equal(expected, actual);
	}
}
