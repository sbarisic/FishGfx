using System;
using System.Collections.Generic;

namespace FishGfx.Graphics;

public readonly record struct OpenGlVersion : IComparable<OpenGlVersion>
{
	public OpenGlVersion(int major, int minor)
	{
		if (major < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(major));
		}

		if (minor < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(minor));
		}

		Major = major;
		Minor = minor;
	}

	public int Major { get; }

	public int Minor { get; }

	public int CompareTo(OpenGlVersion other)
	{
		return Major != other.Major
			? Major.CompareTo(other.Major)
			: Minor.CompareTo(other.Minor);
	}

	public override string ToString()
	{
		return $"{Major}.{Minor}";
	}

	public static bool operator >=(OpenGlVersion left, OpenGlVersion right)
	{
		return left.CompareTo(right) >= 0;
	}

	public static bool operator <=(OpenGlVersion left, OpenGlVersion right)
	{
		return left.CompareTo(right) <= 0;
	}

	public static bool operator >(OpenGlVersion left, OpenGlVersion right)
	{
		return left.CompareTo(right) > 0;
	}

	public static bool operator <(OpenGlVersion left, OpenGlVersion right)
	{
		return left.CompareTo(right) < 0;
	}
}

public sealed class GraphicsCapabilities
{
	private readonly HashSet<string> extensionSet;

	internal GraphicsCapabilities(
		OpenGlVersion version,
		string renderer,
		IReadOnlyList<string> extensions,
		int maximumTexture2DSize,
		int maximumCubeTextureSize,
		int maximumSamples,
		int maximumColorAttachments,
		float maximumAnisotropy
	)
	{
		ArgumentNullException.ThrowIfNull(extensions);

		string[] extensionCopy = new string[extensions.Count];

		for (int index = 0; index < extensions.Count; index++)
		{
			extensionCopy[index] = extensions[index];
		}

		Version = version;
		Renderer = renderer ?? string.Empty;
		Extensions = Array.AsReadOnly(extensionCopy);
		MaximumTexture2DSize = maximumTexture2DSize;
		MaximumCubeTextureSize = maximumCubeTextureSize;
		MaximumSamples = Math.Max(1, maximumSamples);
		MaximumColorAttachments = maximumColorAttachments;
		MaximumAnisotropy = Math.Max(1, maximumAnisotropy);
		extensionSet = new HashSet<string>(extensionCopy, StringComparer.Ordinal);
	}

	public OpenGlVersion Version { get; }

	public string Renderer { get; }

	public IReadOnlyList<string> Extensions { get; }

	public bool SupportsDirectStateAccess => Version >= new OpenGlVersion(4, 5);

	public bool SupportsProgramUniforms => Version >= new OpenGlVersion(4, 1);

	public bool SupportsVertexAttributeBinding => Version >= new OpenGlVersion(4, 3);

	public bool SupportsCopyImage => Version >= new OpenGlVersion(4, 3)
		|| SupportsExtension("GL_ARB_copy_image");

	public bool SupportsAnisotropy => SupportsExtension("GL_EXT_texture_filter_anisotropic")
		|| SupportsExtension("GL_ARB_texture_filter_anisotropic");

	public int MaximumTexture2DSize { get; }

	public int MaximumCubeTextureSize { get; }

	public int MaximumSamples { get; }

	public int MaximumColorAttachments { get; }

	public float MaximumAnisotropy { get; }

	public bool SupportsExtension(string extension)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(extension);

		return extensionSet.Contains(extension);
	}
}
