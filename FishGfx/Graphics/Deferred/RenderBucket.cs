using System;

namespace FishGfx.Graphics
{
	/// <summary>
	/// Identifies a deferred render pass. Names use ordinal, case-sensitive equality.
	/// </summary>
	public readonly struct RenderBucket : IEquatable<RenderBucket>
	{
		public static readonly RenderBucket Opaque = new RenderBucket("Opaque");
		public static readonly RenderBucket Transparent = new RenderBucket("Transparent");

		public RenderBucket(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
				throw new ArgumentException("Render bucket names cannot be null, empty, or whitespace.", nameof(name));

			Name = name;
		}

		public string Name { get; }

		public bool Equals(RenderBucket other) => string.Equals(Name, other.Name, StringComparison.Ordinal);
		public override bool Equals(object obj) => obj is RenderBucket other && Equals(other);
		public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Name ?? string.Empty);
		public override string ToString() => Name ?? string.Empty;

		public static bool operator ==(RenderBucket left, RenderBucket right) => left.Equals(right);
		public static bool operator !=(RenderBucket left, RenderBucket right) => !left.Equals(right);
	}
}
