using System;

namespace FishGfx.Graphics;

/// <summary>
/// Identifies a render-queue partition using ordinal, case-sensitive equality.
/// </summary>
public readonly struct RenderQueueBucket : IEquatable<RenderQueueBucket>
{
	public static readonly RenderQueueBucket Opaque = new("Opaque");
	public static readonly RenderQueueBucket Transparent = new("Transparent");

	public RenderQueueBucket(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException(
				"Render queue bucket names cannot be null, empty, or whitespace.",
				nameof(name)
			);
		}

		Name = name;
	}

	public string Name { get; }

	public bool Equals(RenderQueueBucket other)
	{
		return string.Equals(Name, other.Name, StringComparison.Ordinal);
	}

	public override bool Equals(object value)
	{
		return value is RenderQueueBucket other && Equals(other);
	}

	public override int GetHashCode()
	{
		return StringComparer.Ordinal.GetHashCode(Name ?? string.Empty);
	}

	public override string ToString()
	{
		return Name ?? string.Empty;
	}

	public static bool operator ==(RenderQueueBucket left, RenderQueueBucket right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(RenderQueueBucket left, RenderQueueBucket right)
	{
		return !left.Equals(right);
	}
}
