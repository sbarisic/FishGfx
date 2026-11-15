using System;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics;

public unsafe sealed partial class Texture
{
    /// <summary>Uploads independently cooked BC7/BC5 blocks. Edge mips may be smaller than a block.</summary>
    public void WriteCompressed(ReadOnlySpan<byte> data, TextureRegion region, int mipLevel = 0)
    {
        EnsureCurrentOwner();
        if (Format is not (TextureFormat.BC7Unorm or TextureFormat.BC5Unorm) || Descriptor.Dimension != TextureDimension.Texture2D)
            throw new InvalidOperationException("Compressed writes require BC7 or BC5 2D storage.");
        if ((Usage & TextureUsageFlags.TransferDestination) == 0) throw new InvalidOperationException("Texture is not an upload destination.");
        if (mipLevel < 0 || mipLevel >= MipLevels) throw new ArgumentOutOfRangeException(nameof(mipLevel));
        var (width, height) = GetMipSize(mipLevel);
        if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0
            || region.Width > width - region.X || region.Height > height - region.Y
            || region.X % 4 != 0 || region.Y % 4 != 0
            || region.Width % 4 != 0 && region.X + region.Width != width
            || region.Height % 4 != 0 && region.Y + region.Height != height)
            throw new ArgumentException("Compressed upload must cover aligned blocks or a mip edge.", nameof(region));
        if (data.Length != checked(((region.Width + 3) / 4) * ((region.Height + 3) / 4) * 16))
            throw new ArgumentException("Incorrect compressed payload length.", nameof(data));
        fixed (byte* bytes = data)
        {
            nint address = (nint)bytes;
            uint length = (uint)data.Length;
            if (Internal_OpenGL.Is45OrAbove)
                Internal_OpenGL.GL.CompressedTextureSubImage2D(Handle, mipLevel, region.X, region.Y,
                    (uint)region.Width, (uint)region.Height, (GLEnum)internalFormat, length, (void*)address);
            else
                WithBound(() => Internal_OpenGL.GL.CompressedTexSubImage2D(target, mipLevel, region.X, region.Y,
                    (uint)region.Width, (uint)region.Height, (GLEnum)internalFormat, length, (void*)address));
        }
    }
}
