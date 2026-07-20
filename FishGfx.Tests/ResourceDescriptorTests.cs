using System;
using FishGfx.Graphics;
using Xunit;

namespace FishGfx.Tests;

public class ResourceDescriptorTests
{
	[Fact]
	public void BufferDescriptorsRequirePositiveSizeAndKnownBindings()
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new GraphicsBufferDescriptor(0, BufferBindFlags.Vertex)
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new GraphicsBufferDescriptor(4, BufferBindFlags.None)
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new GraphicsBufferDescriptor(
				4,
				(BufferBindFlags)(1 << 20)
			)
		);

		GraphicsBufferDescriptor descriptor = new(
			64,
			BufferBindFlags.Vertex | BufferBindFlags.TransferDestination,
			BufferUsage.Stream
		);

		Assert.Equal(64, descriptor.SizeInBytes);
		Assert.Equal(BufferUsage.Stream, descriptor.Usage);
		Assert.True(descriptor.BindFlags.HasFlag(BufferBindFlags.Vertex));
	}

	[Fact]
	public void TextureDescriptorsValidateDimensionsMipsAndAttachmentFamilies()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new TextureDescriptor(0, 4));
		Assert.Throws<ArgumentException>(
			() => new TextureDescriptor(
				8,
				4,
				dimension: TextureDimension.Cube
			)
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new TextureDescriptor(8, 8, mipLevels: 5)
		);
		Assert.Throws<ArgumentException>(
			() => new TextureDescriptor(
				8,
				8,
				TextureFormat.Depth24Stencil8,
				TextureUsageFlags.ColorAttachment
			)
		);
		Assert.Throws<ArgumentException>(
			() => new TextureDescriptor(
				8,
				8,
				TextureFormat.RGBA8Unorm,
				TextureUsageFlags.DepthStencilAttachment
			)
		);

		TextureDescriptor descriptor = new(
			8,
			8,
			TextureFormat.RGBA16Float,
			TextureUsageFlags.Sampled | TextureUsageFlags.ColorAttachment,
			mipLevels: 4
		);

		Assert.Equal(4, descriptor.MipLevels);
		Assert.Equal(TextureSamplingState.Default, descriptor.Sampling);
	}

	[Fact]
	public void ThreeDimensionalTextureDescriptorPreservesDepthAndLinearSampling()
	{
		TextureDescriptor descriptor = new(
			128,
			96,
			TextureFormat.RGBA8Unorm,
			TextureUsageFlags.Sampled | TextureUsageFlags.TransferDestination,
			TextureDimension.Texture3D,
			sampling: new TextureSamplingState(
				TextureFilter.Linear,
				TextureFilter.Linear
			),
			depth: 128
		);

		Assert.Equal(TextureDimension.Texture3D, descriptor.Dimension);
		Assert.Equal(128, descriptor.Depth);
		Assert.Equal(TextureFilter.Linear, descriptor.Sampling.MinFilter);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new TextureDescriptor(4, 4, dimension: TextureDimension.Texture3D, depth: 0)
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new TextureRegion3D(0, 0, 0, 0, 1, 1)
		);
	}

	[Fact]
	public void TextureArrayDescriptorPreservesLayersAcrossMipLevels()
	{
		TextureDescriptor descriptor = new(
			32,
			32,
			TextureFormat.SRGB8Alpha8,
			TextureUsageFlags.Sampled | TextureUsageFlags.TransferDestination,
			TextureDimension.Texture2DArray,
			mipLevels: 6,
			sampling: new TextureSamplingState(
				TextureFilter.NearestMipmapLinear,
				TextureFilter.Nearest
			),
			arrayLayers: 256
		);

		Assert.Equal(TextureDimension.Texture2DArray, descriptor.Dimension);
		Assert.Equal(256, descriptor.ArrayLayers);
		Assert.Equal(1, descriptor.Depth);
		Assert.Equal(6, descriptor.MipLevels);
		Assert.Equal(TextureFilter.NearestMipmapLinear, descriptor.Sampling.MinFilter);
		Assert.Throws<ArgumentException>(
			() => new TextureDescriptor(32, 32, arrayLayers: 2)
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new TextureDescriptor(
				32,
				32,
				dimension: TextureDimension.Texture2DArray,
				arrayLayers: 0
			)
		);
	}

	[Fact]
	public void TextureArrayRegionsValidateLayerRanges()
	{
		TextureArrayRegion region = new(2, 3, 7, 8, 9, 4);

		Assert.Equal(7, region.FirstLayer);
		Assert.Equal(4, region.LayerCount);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new TextureArrayRegion(0, 0, -1, 1, 1, 1)
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new TextureArrayRegion(0, 0, 0, 1, 1, 0)
		);
	}

	[Fact]
	public void MultisampleDescriptorsRejectTransfersAndMipmaps()
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new TextureDescriptor(
				8,
				8,
				usage: TextureUsageFlags.ColorAttachment,
				dimension: TextureDimension.Texture2DMultisample,
				samples: 1
			)
		);
		Assert.Throws<ArgumentException>(
			() => new TextureDescriptor(
				8,
				8,
				usage: TextureUsageFlags.ColorAttachment,
				dimension: TextureDimension.Texture2DMultisample,
				mipLevels: 2,
				samples: 4
			)
		);
		Assert.Throws<ArgumentException>(
			() => new TextureDescriptor(
				8,
				8,
				usage: TextureUsageFlags.ColorAttachment
					| TextureUsageFlags.TransferSource,
				dimension: TextureDimension.Texture2DMultisample,
				samples: 4
			)
		);

		TextureDescriptor descriptor = new(
			8,
			8,
			usage: TextureUsageFlags.Sampled | TextureUsageFlags.ColorAttachment,
			dimension: TextureDimension.Texture2DMultisample,
			samples: 4
		);

		Assert.Equal(4, descriptor.Samples);
	}

	[Fact]
	public void SamplingRegionsAndSubresourcesRejectInvalidValues()
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new TextureSamplingState(
				TextureFilter.LinearMipmapLinear,
				TextureFilter.LinearMipmapLinear
			)
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new TextureSamplingState(anisotropy: 0)
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new TextureRegion(-1, 0, 1, 1)
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new TextureRegion(0, 0, 0, 1)
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new TextureSubresource(-1)
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new TextureSubresource(0, (CubeFace)100)
		);
	}
}
