using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.OpenGL;
using GLPixelFormat = Silk.NET.OpenGL.PixelFormat;

namespace FishGfx.Graphics
{
	public enum TextureDimension { Texture2D, Cube, Texture2DMultisample }

	[Flags]
	public enum TextureUsageFlags
	{
		None = 0,
		Sampled = 1 << 0,
		ColorAttachment = 1 << 1,
		DepthStencilAttachment = 1 << 2,
		TransferSource = 1 << 3,
		TransferDestination = 1 << 4,
	}

	public enum TextureFormat
	{
		R8Unorm, RG8Unorm, RGB8Unorm, RGBA8Unorm, SRGB8, SRGB8Alpha8,
		R16Unorm, R16Float, RG16Float, RGB16Float, RGBA16Float,
		R32Float, RG32Float, RGB32Float, RGBA32Float, R11G11B10Float,
		Depth16Unorm, Depth24Unorm, Depth32Float, Depth24Stencil8, Depth32FloatStencil8,
	}

	public enum TextureDataFormat
	{
		R8Unorm, RG8Unorm, RGB8Unorm, RGBA8Unorm, BGR8Unorm, BGRA8Unorm,
		R16Unorm, R16Float, R32Float, RG32Float, RGB32Float, RGBA32Float,
		Depth32Float, Depth24Stencil8,
	}

	public enum TextureWrap { ClampToEdge, Repeat, MirroredRepeat, ClampToBorder }
	public enum TextureFilter { Nearest, Linear, NearestMipmapNearest, LinearMipmapNearest, NearestMipmapLinear, LinearMipmapLinear }
	public enum CubeFace { PositiveX, NegativeX, PositiveY, NegativeY, PositiveZ, NegativeZ }

	public readonly struct TextureSamplingState : IEquatable<TextureSamplingState>
	{
		public TextureSamplingState(TextureFilter minFilter = TextureFilter.Nearest, TextureFilter magFilter = TextureFilter.Nearest,
			TextureWrap wrapU = TextureWrap.ClampToEdge, TextureWrap wrapV = TextureWrap.ClampToEdge,
			TextureWrap wrapW = TextureWrap.ClampToEdge, float anisotropy = 1)
		{
			if (!Enum.IsDefined(minFilter)) throw new ArgumentOutOfRangeException(nameof(minFilter));
			if (magFilter != TextureFilter.Nearest && magFilter != TextureFilter.Linear)
				throw new ArgumentOutOfRangeException(nameof(magFilter), "Magnification only supports nearest or linear filtering.");
			if (!Enum.IsDefined(wrapU) || !Enum.IsDefined(wrapV) || !Enum.IsDefined(wrapW))
				throw new ArgumentOutOfRangeException(nameof(wrapU));
			if (!float.IsFinite(anisotropy) || anisotropy < 1) throw new ArgumentOutOfRangeException(nameof(anisotropy));
			MinFilter = minFilter; MagFilter = magFilter; WrapU = wrapU; WrapV = wrapV; WrapW = wrapW; Anisotropy = anisotropy;
		}
		public TextureFilter MinFilter { get; }
		public TextureFilter MagFilter { get; }
		public TextureWrap WrapU { get; }
		public TextureWrap WrapV { get; }
		public TextureWrap WrapW { get; }
		public float Anisotropy { get; }
		public bool Equals(TextureSamplingState other) => MinFilter == other.MinFilter && MagFilter == other.MagFilter &&
			WrapU == other.WrapU && WrapV == other.WrapV && WrapW == other.WrapW && Anisotropy.Equals(other.Anisotropy);
		public override bool Equals(object obj) => obj is TextureSamplingState other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(MinFilter, MagFilter, WrapU, WrapV, WrapW, Anisotropy);
		public static TextureSamplingState Default => new TextureSamplingState(TextureFilter.Nearest);
	}

	public readonly struct TextureDescriptor
	{
		private const TextureUsageFlags AllUsage = TextureUsageFlags.Sampled | TextureUsageFlags.ColorAttachment |
			TextureUsageFlags.DepthStencilAttachment | TextureUsageFlags.TransferSource | TextureUsageFlags.TransferDestination;

		public TextureDescriptor(int width, int height, TextureFormat format = TextureFormat.RGBA8Unorm,
			TextureUsageFlags usage = TextureUsageFlags.Sampled | TextureUsageFlags.TransferDestination,
			TextureDimension dimension = TextureDimension.Texture2D, int mipLevels = 1, int samples = 1,
			bool fixedSampleLocations = true, TextureSamplingState sampling = default)
		{
			if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Texture dimensions must be positive.");
			if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
			if (!Enum.IsDefined(dimension)) throw new ArgumentOutOfRangeException(nameof(dimension));
			if (usage == TextureUsageFlags.None || (usage & ~AllUsage) != 0) throw new ArgumentOutOfRangeException(nameof(usage));
			if (dimension == TextureDimension.Cube && width != height) throw new ArgumentException("Cube textures must be square.");
			int maximumMips = 1 + (int)Math.Floor(Math.Log2(Math.Max(width, height)));
			if (mipLevels <= 0 || mipLevels > maximumMips) throw new ArgumentOutOfRangeException(nameof(mipLevels));
			if (dimension == TextureDimension.Texture2DMultisample)
			{
				if (samples < 2) throw new ArgumentOutOfRangeException(nameof(samples), "Multisample textures require at least two samples.");
				if (mipLevels != 1) throw new ArgumentException("Multisample textures have exactly one mip level.");
				if ((usage & (TextureUsageFlags.TransferSource | TextureUsageFlags.TransferDestination)) != 0)
					throw new ArgumentException("Multisample texture transfers use Framebuffer.Blit, not texture transfer flags.");
			}
			else if (samples != 1) throw new ArgumentOutOfRangeException(nameof(samples), "Non-multisampled textures use one sample.");
			bool depth = Texture.IsDepthFormat(format);
			if (depth && (usage & TextureUsageFlags.ColorAttachment) != 0) throw new ArgumentException("Depth formats cannot be color attachments.");
			if (!depth && (usage & TextureUsageFlags.DepthStencilAttachment) != 0) throw new ArgumentException("Color formats cannot be depth attachments.");
			if (sampling.Equals(default(TextureSamplingState))) sampling = TextureSamplingState.Default;
			Width = width; Height = height; Format = format; Usage = usage; Dimension = dimension; MipLevels = mipLevels;
			Samples = samples; FixedSampleLocations = fixedSampleLocations; Sampling = sampling;
		}
		public int Width { get; }
		public int Height { get; }
		public TextureFormat Format { get; }
		public TextureUsageFlags Usage { get; }
		public TextureDimension Dimension { get; }
		public int MipLevels { get; }
		public int Samples { get; }
		public bool FixedSampleLocations { get; }
		public TextureSamplingState Sampling { get; }
	}

	public readonly struct TextureSubresource
	{
		public TextureSubresource(int mipLevel = 0, CubeFace? face = null)
		{
			if (mipLevel < 0) throw new ArgumentOutOfRangeException(nameof(mipLevel));
			if (face.HasValue && !Enum.IsDefined(face.Value)) throw new ArgumentOutOfRangeException(nameof(face));
			MipLevel = mipLevel; Face = face;
		}
		public int MipLevel { get; }
		public CubeFace? Face { get; }
	}

	public readonly struct TextureRegion
	{
		public TextureRegion(int x, int y, int width, int height)
		{
			if (x < 0 || y < 0) throw new ArgumentOutOfRangeException(nameof(x));
			if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
			X = x; Y = y; Width = width; Height = height;
		}
		public int X { get; } public int Y { get; } public int Width { get; } public int Height { get; }
	}

	public readonly struct TextureCopyRegion
	{
		public TextureCopyRegion(TextureRegion region, TextureSubresource subresource = default) { Region = region; Subresource = subresource; }
		public TextureRegion Region { get; }
		public TextureSubresource Subresource { get; }
	}

	public unsafe sealed class Texture : GraphicsObject
	{
		private readonly TextureTarget target;
		private readonly InternalFormat internalFormat;

		internal Texture(TextureDescriptor descriptor)
		{
			GraphicsContext context = GraphicsContext.Current;
			ValidateDescriptor(descriptor);
			ValidateCapabilities(descriptor, context.Capabilities);
			Descriptor = descriptor;
			target = ToTarget(descriptor.Dimension);
			internalFormat = ToInternalFormat(descriptor.Format);
			ID = Internal_OpenGL.Is45OrAbove ? Internal_OpenGL.GL.CreateTexture(target) : Internal_OpenGL.GL.GenTexture();
			Allocate();
			if (!Multisampled) SetSampling(descriptor.Sampling);
		}

		public TextureDescriptor Descriptor { get; }
		public int Width => Descriptor.Width;
		public int Height => Descriptor.Height;
		public int MipLevels => Descriptor.MipLevels;
		public int Multisamples => Multisampled ? Descriptor.Samples : 0;
		public bool Multisampled => Descriptor.Dimension == TextureDimension.Texture2DMultisample;
		public bool IsCubeMap => Descriptor.Dimension == TextureDimension.Cube;
		public TextureFormat Format => Descriptor.Format;
		public TextureUsageFlags Usage => Descriptor.Usage;
		public Vector2 Size => new Vector2(Width, Height);
		public TextureSamplingState Sampling { get; private set; }

		public void SetSampling(TextureSamplingState sampling)
		{
			EnsureCurrentOwner();
			if (Multisampled) throw new InvalidOperationException("Multisample textures do not have sampling state.");
			if (sampling.Equals(default(TextureSamplingState))) sampling = TextureSamplingState.Default;
			else sampling = new TextureSamplingState(sampling.MinFilter, sampling.MagFilter, sampling.WrapU, sampling.WrapV, sampling.WrapW, sampling.Anisotropy);
			GraphicsCapabilities capabilities = GraphicsContext.Current.Capabilities;
			if (sampling.Anisotropy > 1 && !capabilities.SupportsAnisotropy)
				throw new NotSupportedException("Texture anisotropy is not supported by this context.");
			if (sampling.Anisotropy > capabilities.MaximumAnisotropy)
				throw new ArgumentOutOfRangeException(nameof(sampling), $"Anisotropy exceeds the context limit of {capabilities.MaximumAnisotropy}.");
			SetParameter(TextureParameterName.TextureMinFilter, (int)ToGlFilter(sampling.MinFilter));
			SetParameter(TextureParameterName.TextureMagFilter, (int)ToGlFilter(sampling.MagFilter));
			SetParameter(TextureParameterName.TextureWrapS, (int)ToGlWrap(sampling.WrapU));
			SetParameter(TextureParameterName.TextureWrapT, (int)ToGlWrap(sampling.WrapV));
			SetParameter(TextureParameterName.TextureWrapR, (int)ToGlWrap(sampling.WrapW));
			if (capabilities.SupportsAnisotropy) SetParameter((TextureParameterName)0x84FE, sampling.Anisotropy);
			Sampling = sampling;
		}

		public void Write<T>(ReadOnlySpan<T> data, TextureDataFormat dataFormat, TextureSubresource subresource = default) where T : unmanaged
		{
			ValidateSubresource(subresource);
			(int width, int height) = GetMipSize(subresource.MipLevel);
			Write(data, dataFormat, new TextureRegion(0, 0, width, height), subresource);
		}

		public void Write<T>(ReadOnlySpan<T> data, TextureDataFormat dataFormat, TextureRegion region, TextureSubresource subresource = default) where T : unmanaged
		{
			EnsureCurrentOwner();
			if (Multisampled) throw new InvalidOperationException("Multisample textures cannot receive CPU uploads.");
			if ((Usage & TextureUsageFlags.TransferDestination) == 0) throw new InvalidOperationException("The texture is missing TransferDestination usage.");
			ValidateSubresource(subresource);
			ValidateRegion(region, subresource.MipLevel);
			ValidateUploadCompatibility(Format, dataFormat);
			int requiredBytes = checked(checked(region.Width * region.Height) * BytesPerPixel(dataFormat));
			int actualBytes = checked(data.Length * Unsafe.SizeOf<T>());
			if (actualBytes != requiredBytes) throw new ArgumentException($"Upload contains {actualBytes} bytes; {requiredBytes} bytes are required.", nameof(data));
			if (actualBytes == 0) return;
			(GLPixelFormat pixelFormat, PixelType pixelType) = ToPixelFormat(dataFormat);
			Internal_OpenGL.GL.GetInteger(GetPName.UnpackAlignment, out int previousAlignment);
			Internal_OpenGL.GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
			try
			{
				fixed (T* pointer = data)
				{
					if (Internal_OpenGL.Is45OrAbove)
					{
						if (IsCubeMap) Internal_OpenGL.GL.TextureSubImage3D(ID, subresource.MipLevel, region.X, region.Y, (int)subresource.Face.Value,
							region.Width, region.Height, 1, pixelFormat, pixelType, (IntPtr)pointer);
						else Internal_OpenGL.GL.TextureSubImage2D(ID, subresource.MipLevel, region.X, region.Y, region.Width, region.Height, pixelFormat, pixelType, (IntPtr)pointer);
					}
					else
					{
						Internal_OpenGL.GL.GetInteger(BindingQuery(target), out int previous);
						Internal_OpenGL.GL.BindTexture(target, ID);
						try { Internal_OpenGL.GL.TexSubImage2D(IsCubeMap ? ToFaceTarget(subresource.Face.Value) : target,
							subresource.MipLevel, region.X, region.Y, region.Width, region.Height, pixelFormat, pixelType, (IntPtr)pointer); }
						finally { Internal_OpenGL.GL.BindTexture(target, (uint)previous); }
					}
				}
			}
			finally { Internal_OpenGL.GL.PixelStore(PixelStoreParameter.UnpackAlignment, previousAlignment); }
		}

		public void GenerateMipmaps()
		{
			EnsureCurrentOwner();
			if (Multisampled) throw new InvalidOperationException("Multisample textures do not have mipmaps.");
			if (MipLevels < 2) throw new InvalidOperationException("The texture descriptor only allocated one mip level.");
			if (Internal_OpenGL.Is45OrAbove) Internal_OpenGL.GL.GenerateTextureMipmap(ID);
			else WithBound(() => Internal_OpenGL.GL.GenerateMipmap(target));
		}

		public void CopyTo(Texture destination)
		{
			if (destination == null) throw new ArgumentNullException(nameof(destination));
			if (Descriptor.Dimension != destination.Descriptor.Dimension || Width != destination.Width || Height != destination.Height || MipLevels != destination.MipLevels)
				throw new InvalidOperationException("Whole-texture copies require matching dimensions, extents, and mip counts.");
			int faceCount = IsCubeMap ? 6 : 1;
			for (int mipLevel = 0; mipLevel < MipLevels; mipLevel++)
			{
				(int width, int height) = GetMipSize(mipLevel);
				for (int face = 0; face < faceCount; face++)
				{
					TextureSubresource subresource = new TextureSubresource(mipLevel, IsCubeMap ? (CubeFace?)face : null);
					CopyTo(destination, new TextureCopyRegion(new TextureRegion(0, 0, width, height), subresource), subresource);
				}
			}
		}

		public void CopyTo(Texture destination, TextureCopyRegion source, TextureSubresource destinationSubresource,
			int destinationX = 0, int destinationY = 0)
		{
			GraphicsContext context = EnsureCurrentOwner();
			if (destination == null) throw new ArgumentNullException(nameof(destination));
			destination.EnsureOwner(context);
			if (ReferenceEquals(this, destination)) throw new InvalidOperationException("A texture cannot be copied to itself.");
			if (Multisampled || destination.Multisampled) throw new InvalidOperationException("Use Framebuffer.Blit to resolve or copy multisample textures.");
			if ((Usage & TextureUsageFlags.TransferSource) == 0) throw new InvalidOperationException("The source texture is missing TransferSource usage.");
			if ((destination.Usage & TextureUsageFlags.TransferDestination) == 0) throw new InvalidOperationException("The destination texture is missing TransferDestination usage.");
			if (Format != destination.Format) throw new InvalidOperationException("Texture copies require identical formats.");
			ValidateSubresource(source.Subresource); destination.ValidateSubresource(destinationSubresource);
			ValidateRegion(source.Region, source.Subresource.MipLevel);
			TextureRegion destinationRegion = new TextureRegion(destinationX, destinationY, source.Region.Width, source.Region.Height);
			destination.ValidateRegion(destinationRegion, destinationSubresource.MipLevel);

			if (context.Capabilities.SupportsCopyImage)
			{
				Internal_OpenGL.GL.CopyImageSubData(ID, (GLEnum)target, source.Subresource.MipLevel, source.Region.X, source.Region.Y, Layer(source.Subresource),
					destination.ID, (GLEnum)destination.target, destinationSubresource.MipLevel, destinationX, destinationY, destination.Layer(destinationSubresource),
					(uint)source.Region.Width, (uint)source.Region.Height, 1);
				return;
			}
			CopyWithFramebuffers(destination, source, destinationSubresource, destinationX, destinationY);
		}

		public void BindTextureUnit(uint unit = 0)
		{
			EnsureCurrentOwner();
			if (Internal_OpenGL.Is45OrAbove) Internal_OpenGL.GL.BindTextureUnit(unit, ID);
			else { Internal_OpenGL.GL.ActiveTexture(TextureUnit.Texture0 + (int)unit); Internal_OpenGL.GL.BindTexture(target, ID); }
		}

		public void UnbindTextureUnit(uint unit = 0)
		{
			EnsureCurrentOwner();
			if (Internal_OpenGL.Is45OrAbove) Internal_OpenGL.GL.BindTextureUnit(unit, 0);
			else { Internal_OpenGL.GL.ActiveTexture(TextureUnit.Texture0 + (int)unit); Internal_OpenGL.GL.BindTexture(target, 0); }
		}

		public override void Bind() { EnsureCurrentOwner(); Internal_OpenGL.GL.BindTexture(target, ID); }
		public override void Unbind() => Internal_OpenGL.GL.BindTexture(target, 0);
		public override void GraphicsDispose() => Internal_OpenGL.GL.DeleteTexture(ID);

		private void Allocate()
		{
			if (Multisampled)
			{
				if (Internal_OpenGL.Is45OrAbove) Internal_OpenGL.GL.TextureStorage2DMultisample(ID, Descriptor.Samples, internalFormat, Width, Height, Descriptor.FixedSampleLocations);
				else if (Internal_OpenGL.Is42OrAbove) WithBound(() => Internal_OpenGL.GL.TexStorage2DMultisample(target, Descriptor.Samples, internalFormat, Width, Height, Descriptor.FixedSampleLocations));
				else WithBound(() => Internal_OpenGL.GL.TexImage2DMultisample(target, (uint)Descriptor.Samples, (GLEnum)internalFormat, (uint)Width, (uint)Height, Descriptor.FixedSampleLocations));
				return;
			}
			if (Internal_OpenGL.Is45OrAbove) Internal_OpenGL.GL.TextureStorage2D(ID, MipLevels, internalFormat, Width, Height);
			else if (Internal_OpenGL.Is42OrAbove) WithBound(() => Internal_OpenGL.GL.TexStorage2D(target, MipLevels, internalFormat, Width, Height));
			else WithBound(AllocateMutableStorage);
		}

		private void AllocateMutableStorage()
		{
			(GLPixelFormat format, PixelType type) = AllocationPixelFormat(Format);
			for (int level = 0; level < MipLevels; level++)
			{
				(int width, int height) = GetMipSize(level);
				if (IsCubeMap)
					for (int face = 0; face < 6; face++) Internal_OpenGL.GL.TexImage2D(ToFaceTarget((CubeFace)face), level, internalFormat, (uint)width, (uint)height, 0, format, type, null);
				else Internal_OpenGL.GL.TexImage2D(target, level, internalFormat, (uint)width, (uint)height, 0, format, type, null);
			}
		}

		private void CopyWithFramebuffers(Texture destination, TextureCopyRegion source, TextureSubresource destinationSubresource, int destinationX, int destinationY)
		{
			Internal_OpenGL.GL.GetInteger((GetPName)0x8CAA, out int previousRead);
			Internal_OpenGL.GL.GetInteger((GetPName)0x8CA6, out int previousDraw);
			uint read = Internal_OpenGL.GL.GenFramebuffer(), draw = Internal_OpenGL.GL.GenFramebuffer();
			try
			{
				FramebufferAttachment attachment = IsDepthFormat(Format) ? FramebufferAttachment.DepthAttachment : FramebufferAttachment.ColorAttachment0;
				if (Format == TextureFormat.Depth24Stencil8 || Format == TextureFormat.Depth32FloatStencil8) attachment = FramebufferAttachment.DepthStencilAttachment;
				Internal_OpenGL.GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read);
				Internal_OpenGL.GL.FramebufferTexture2D(FramebufferTarget.ReadFramebuffer, attachment, IsCubeMap ? ToFaceTarget(source.Subresource.Face.Value) : target, ID, source.Subresource.MipLevel);
				Internal_OpenGL.GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, draw);
				Internal_OpenGL.GL.FramebufferTexture2D(FramebufferTarget.DrawFramebuffer, attachment, destination.IsCubeMap ? ToFaceTarget(destinationSubresource.Face.Value) : destination.target, destination.ID, destinationSubresource.MipLevel);
				if (IsDepthFormat(Format))
				{
					Internal_OpenGL.GL.ReadBuffer(ReadBufferMode.None);
					Internal_OpenGL.GL.DrawBuffer(DrawBufferMode.None);
				}
				ClearBufferMask mask = IsDepthFormat(Format) ? ClearBufferMask.DepthBufferBit : ClearBufferMask.ColorBufferBit;
				if (Format == TextureFormat.Depth24Stencil8 || Format == TextureFormat.Depth32FloatStencil8) mask |= ClearBufferMask.StencilBufferBit;
				Internal_OpenGL.GL.BlitFramebuffer(source.Region.X, source.Region.Y, source.Region.X + source.Region.Width, source.Region.Y + source.Region.Height,
					destinationX, destinationY, destinationX + source.Region.Width, destinationY + source.Region.Height, mask, BlitFramebufferFilter.Nearest);
			}
			finally
			{
				Internal_OpenGL.GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, (uint)previousRead);
				Internal_OpenGL.GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)previousDraw);
				Internal_OpenGL.GL.DeleteFramebuffer(read); Internal_OpenGL.GL.DeleteFramebuffer(draw);
			}
		}

		private void ValidateSubresource(TextureSubresource subresource)
		{
			if (subresource.MipLevel < 0 || subresource.MipLevel >= MipLevels) throw new ArgumentOutOfRangeException(nameof(subresource));
			if (IsCubeMap != subresource.Face.HasValue) throw new ArgumentException(IsCubeMap ? "A cube face is required." : "A cube face is only valid for cube textures.", nameof(subresource));
		}
		private void ValidateRegion(TextureRegion region, int mipLevel)
		{
			if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0)
				throw new ArgumentOutOfRangeException(nameof(region));
			(int width, int height) = GetMipSize(mipLevel);
			if (region.X > width - region.Width || region.Y > height - region.Height) throw new ArgumentOutOfRangeException(nameof(region), "The region exceeds the mip bounds.");
		}
		private (int Width, int Height) GetMipSize(int level) => (Math.Max(1, Width >> level), Math.Max(1, Height >> level));
		private int Layer(TextureSubresource subresource) => IsCubeMap ? (int)subresource.Face.Value : 0;

		private void SetParameter(TextureParameterName name, int value)
		{
			if (Internal_OpenGL.Is45OrAbove) Internal_OpenGL.GL.TextureParameter(ID, name, value);
			else WithBound(() => Internal_OpenGL.GL.TexParameter(target, name, value));
		}
		private void SetParameter(TextureParameterName name, float value)
		{
			if (Internal_OpenGL.Is45OrAbove) Internal_OpenGL.GL.TextureParameter(ID, name, value);
			else WithBound(() => Internal_OpenGL.GL.TexParameter(target, name, value));
		}
		private void WithBound(Action action)
		{
			Internal_OpenGL.GL.GetInteger(BindingQuery(target), out int previous);
			Internal_OpenGL.GL.BindTexture(target, ID);
			try { action(); } finally { Internal_OpenGL.GL.BindTexture(target, (uint)previous); }
		}

		internal static bool IsDepthFormat(TextureFormat format) => format is
			TextureFormat.Depth16Unorm or TextureFormat.Depth24Unorm or TextureFormat.Depth32Float or
			TextureFormat.Depth24Stencil8 or TextureFormat.Depth32FloatStencil8;
		private static void ValidateDescriptor(TextureDescriptor descriptor)
		{
			if (descriptor.Width <= 0 || descriptor.Height <= 0) throw new ArgumentOutOfRangeException(nameof(descriptor), "Texture dimensions must be positive.");
			if (!Enum.IsDefined(descriptor.Format) || !Enum.IsDefined(descriptor.Dimension)) throw new ArgumentOutOfRangeException(nameof(descriptor));
			const TextureUsageFlags allUsage = TextureUsageFlags.Sampled | TextureUsageFlags.ColorAttachment |
				TextureUsageFlags.DepthStencilAttachment | TextureUsageFlags.TransferSource | TextureUsageFlags.TransferDestination;
			if (descriptor.Usage == TextureUsageFlags.None || (descriptor.Usage & ~allUsage) != 0) throw new ArgumentOutOfRangeException(nameof(descriptor));
			if (descriptor.Dimension == TextureDimension.Cube && descriptor.Width != descriptor.Height) throw new ArgumentException("Cube textures must be square.", nameof(descriptor));
			int maximumMips = 1 + (int)Math.Floor(Math.Log2(Math.Max(descriptor.Width, descriptor.Height)));
			if (descriptor.MipLevels <= 0 || descriptor.MipLevels > maximumMips) throw new ArgumentOutOfRangeException(nameof(descriptor));
			if (descriptor.Dimension == TextureDimension.Texture2DMultisample)
			{
				if (descriptor.Samples < 2 || descriptor.MipLevels != 1) throw new ArgumentOutOfRangeException(nameof(descriptor));
				if ((descriptor.Usage & (TextureUsageFlags.TransferSource | TextureUsageFlags.TransferDestination)) != 0) throw new ArgumentException("Multisample transfers use Framebuffer.Blit.", nameof(descriptor));
			}
			else if (descriptor.Samples != 1) throw new ArgumentOutOfRangeException(nameof(descriptor));
			bool depth = IsDepthFormat(descriptor.Format);
			if (depth && (descriptor.Usage & TextureUsageFlags.ColorAttachment) != 0) throw new ArgumentException("Depth formats cannot be color attachments.", nameof(descriptor));
			if (!depth && (descriptor.Usage & TextureUsageFlags.DepthStencilAttachment) != 0) throw new ArgumentException("Color formats cannot be depth attachments.", nameof(descriptor));
		}
		private static void ValidateCapabilities(TextureDescriptor descriptor, GraphicsCapabilities capabilities)
		{
			int limit = descriptor.Dimension == TextureDimension.Cube ? capabilities.MaximumCubeTextureSize : capabilities.MaximumTexture2DSize;
			if (descriptor.Width > limit || descriptor.Height > limit) throw new ArgumentOutOfRangeException(nameof(descriptor), $"Texture dimensions exceed the context limit of {limit}.");
			if (descriptor.Samples > capabilities.MaximumSamples) throw new ArgumentOutOfRangeException(nameof(descriptor), $"Sample count exceeds the context limit of {capabilities.MaximumSamples}.");
		}
		private static void ValidateUploadCompatibility(TextureFormat storage, TextureDataFormat data)
		{
			bool storageDepth = IsDepthFormat(storage);
			bool dataDepth = data == TextureDataFormat.Depth32Float || data == TextureDataFormat.Depth24Stencil8;
			if (storageDepth != dataDepth) throw new ArgumentException("Color and depth data formats cannot be mixed.", nameof(data));
			if ((storage == TextureFormat.Depth24Stencil8 || storage == TextureFormat.Depth32FloatStencil8) != (data == TextureDataFormat.Depth24Stencil8))
				throw new ArgumentException("Depth-stencil storage requires depth-stencil upload data.", nameof(data));
		}

		private static TextureTarget ToTarget(TextureDimension dimension) => dimension switch { TextureDimension.Texture2D => TextureTarget.Texture2D, TextureDimension.Cube => TextureTarget.TextureCubeMap, TextureDimension.Texture2DMultisample => TextureTarget.Texture2DMultisample, _ => throw new ArgumentOutOfRangeException(nameof(dimension)) };
		private static TextureTarget ToFaceTarget(CubeFace face) => (TextureTarget)((int)TextureTarget.TextureCubeMapPositiveX + (int)face);
		private static GetPName BindingQuery(TextureTarget value) => value switch { TextureTarget.Texture2D => GetPName.TextureBinding2D, TextureTarget.TextureCubeMap => GetPName.TextureBindingCubeMap, TextureTarget.Texture2DMultisample => (GetPName)0x9104, _ => throw new ArgumentOutOfRangeException(nameof(value)) };
		private static GLEnum ToGlFilter(TextureFilter value) => value switch { TextureFilter.Nearest => GLEnum.Nearest, TextureFilter.Linear => GLEnum.Linear, TextureFilter.NearestMipmapNearest => GLEnum.NearestMipmapNearest, TextureFilter.LinearMipmapNearest => GLEnum.LinearMipmapNearest, TextureFilter.NearestMipmapLinear => GLEnum.NearestMipmapLinear, TextureFilter.LinearMipmapLinear => GLEnum.LinearMipmapLinear, _ => throw new ArgumentOutOfRangeException(nameof(value)) };
		private static GLEnum ToGlWrap(TextureWrap value) => value switch { TextureWrap.ClampToEdge => GLEnum.ClampToEdge, TextureWrap.Repeat => GLEnum.Repeat, TextureWrap.MirroredRepeat => GLEnum.MirroredRepeat, TextureWrap.ClampToBorder => GLEnum.ClampToBorder, _ => throw new ArgumentOutOfRangeException(nameof(value)) };

		private static InternalFormat ToInternalFormat(TextureFormat format) => format switch
		{
			TextureFormat.R8Unorm => InternalFormat.R8, TextureFormat.RG8Unorm => InternalFormat.RG8, TextureFormat.RGB8Unorm => InternalFormat.Rgb8,
			TextureFormat.RGBA8Unorm => InternalFormat.Rgba8, TextureFormat.SRGB8 => InternalFormat.Srgb8, TextureFormat.SRGB8Alpha8 => InternalFormat.Srgb8Alpha8,
			TextureFormat.R16Unorm => InternalFormat.R16, TextureFormat.R16Float => InternalFormat.R16f, TextureFormat.RG16Float => InternalFormat.RG16f,
			TextureFormat.RGB16Float => InternalFormat.Rgb16f, TextureFormat.RGBA16Float => InternalFormat.Rgba16f, TextureFormat.R32Float => InternalFormat.R32f,
			TextureFormat.RG32Float => InternalFormat.RG32f, TextureFormat.RGB32Float => InternalFormat.Rgb32f, TextureFormat.RGBA32Float => InternalFormat.Rgba32f,
			TextureFormat.R11G11B10Float => InternalFormat.R11fG11fB10f, TextureFormat.Depth16Unorm => InternalFormat.DepthComponent16,
			TextureFormat.Depth24Unorm => InternalFormat.DepthComponent24, TextureFormat.Depth32Float => InternalFormat.DepthComponent32f,
			TextureFormat.Depth24Stencil8 => InternalFormat.Depth24Stencil8, TextureFormat.Depth32FloatStencil8 => InternalFormat.Depth32fStencil8,
			_ => throw new ArgumentOutOfRangeException(nameof(format)),
		};

		private static (GLPixelFormat, PixelType) AllocationPixelFormat(TextureFormat format)
		{
			if (format == TextureFormat.Depth24Stencil8) return (GLPixelFormat.DepthStencil, PixelType.UnsignedInt248);
			if (format == TextureFormat.Depth32FloatStencil8) return (GLPixelFormat.DepthStencil, (PixelType)0x8DAD);
			if (IsDepthFormat(format)) return (GLPixelFormat.DepthComponent, PixelType.Float);
			if (format is TextureFormat.R8Unorm or TextureFormat.R16Unorm or TextureFormat.R16Float or TextureFormat.R32Float) return (GLPixelFormat.Red, PixelType.UnsignedByte);
			if (format is TextureFormat.RG8Unorm or TextureFormat.RG16Float or TextureFormat.RG32Float) return (GLPixelFormat.RG, PixelType.UnsignedByte);
			if (format is TextureFormat.RGB8Unorm or TextureFormat.SRGB8 or TextureFormat.RGB16Float or TextureFormat.RGB32Float or TextureFormat.R11G11B10Float) return (GLPixelFormat.Rgb, PixelType.UnsignedByte);
			return (GLPixelFormat.Rgba, PixelType.UnsignedByte);
		}

		private static (GLPixelFormat, PixelType) ToPixelFormat(TextureDataFormat format) => format switch
		{
			TextureDataFormat.R8Unorm => (GLPixelFormat.Red, PixelType.UnsignedByte), TextureDataFormat.RG8Unorm => (GLPixelFormat.RG, PixelType.UnsignedByte),
			TextureDataFormat.RGB8Unorm => (GLPixelFormat.Rgb, PixelType.UnsignedByte), TextureDataFormat.RGBA8Unorm => (GLPixelFormat.Rgba, PixelType.UnsignedByte),
			TextureDataFormat.BGR8Unorm => (GLPixelFormat.Bgr, PixelType.UnsignedByte), TextureDataFormat.BGRA8Unorm => (GLPixelFormat.Bgra, PixelType.UnsignedByte),
			TextureDataFormat.R16Unorm => (GLPixelFormat.Red, PixelType.UnsignedShort), TextureDataFormat.R16Float => (GLPixelFormat.Red, PixelType.HalfFloat),
			TextureDataFormat.R32Float => (GLPixelFormat.Red, PixelType.Float), TextureDataFormat.RG32Float => (GLPixelFormat.RG, PixelType.Float),
			TextureDataFormat.RGB32Float => (GLPixelFormat.Rgb, PixelType.Float), TextureDataFormat.RGBA32Float => (GLPixelFormat.Rgba, PixelType.Float),
			TextureDataFormat.Depth32Float => (GLPixelFormat.DepthComponent, PixelType.Float), TextureDataFormat.Depth24Stencil8 => (GLPixelFormat.DepthStencil, PixelType.UnsignedInt248),
			_ => throw new ArgumentOutOfRangeException(nameof(format)),
		};
		private static int BytesPerPixel(TextureDataFormat format) => format switch
		{
			TextureDataFormat.R8Unorm => 1, TextureDataFormat.RG8Unorm => 2, TextureDataFormat.RGB8Unorm or TextureDataFormat.BGR8Unorm => 3,
			TextureDataFormat.RGBA8Unorm or TextureDataFormat.BGRA8Unorm or TextureDataFormat.R32Float or TextureDataFormat.Depth32Float or TextureDataFormat.Depth24Stencil8 => 4,
			TextureDataFormat.R16Unorm or TextureDataFormat.R16Float => 2, TextureDataFormat.RG32Float => 8, TextureDataFormat.RGB32Float => 12, TextureDataFormat.RGBA32Float => 16,
			_ => throw new ArgumentOutOfRangeException(nameof(format)),
		};
	}
}
