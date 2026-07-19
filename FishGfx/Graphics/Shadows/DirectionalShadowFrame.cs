using System;
using System.Numerics;

namespace FishGfx.Graphics.Shadows;

public readonly struct DirectionalShadowFrame
{
	public const int MaximumCascades = 4;

	private readonly Snapshot snapshot;

	internal DirectionalShadowFrame(Snapshot snapshot)
	{
		this.snapshot = snapshot;
	}

	public bool Enabled => snapshot != null && snapshot.Strength > 0.01f;

	public int CascadeCount => Enabled ? snapshot.CascadeCount : 0;

	public float Strength => Enabled ? snapshot.Strength : 0;

	public IDisposable Bind(ShaderProgram shader, uint firstTextureUnit = 1)
	{
		ArgumentNullException.ThrowIfNull(shader);

		if (!Enabled)
		{
			shader.SetUniform("uShadowEnabled", 0);

			return EmptyScope.Instance;
		}

		if (firstTextureUnit > uint.MaxValue - (uint)snapshot.CascadeCount)
		{
			throw new ArgumentOutOfRangeException(nameof(firstTextureUnit));
		}

		IDisposable[] bindings = new IDisposable[snapshot.CascadeCount];
		int[] textureUnits = new int[snapshot.CascadeCount];

		try
		{
			for (int index = 0; index < snapshot.CascadeCount; index++)
			{
				uint unit = firstTextureUnit + (uint)index;
				bindings[index] = snapshot.DepthTextures[index].Bind(unit);
				textureUnits[index] = checked((int)unit);
			}

			shader.SetUniform("uShadowEnabled", 1);
			shader.SetUniform("uShadowCascadeCount", snapshot.CascadeCount);
			shader.SetUniform("uShadowStrength", snapshot.Strength);
			shader.SetUniform("uShadowBlendFraction", snapshot.BlendFraction);
			shader.SetUniform("uShadowFilterRadius", snapshot.Filter == DirectionalShadowFilter.Pcf5x5 ? 2 : 1);
			shader.SetUniform("uShadowMatrices", snapshot.Matrices);
			shader.SetUniform("uShadowSplits", snapshot.Splits);
			shader.SetUniform("uShadowDepthRanges", snapshot.DepthRanges);
			shader.SetUniform("uShadowMapDepthRanges", snapshot.MapDepthRanges);
			shader.SetUniform("uShadowWorldTexelSizes", snapshot.WorldTexelSizes);
			shader.SetUniform("uShadowMaps", textureUnits);

			return new BindingScope(bindings);
		}
		catch
		{
			DisposeBindings(bindings);

			throw;
		}
	}

	internal sealed class Snapshot
	{
		public required int CascadeCount { get; init; }

		public float Strength { get; set; }

		public float BlendFraction { get; set; }

		public DirectionalShadowFilter Filter { get; set; }

		public required Texture[] DepthTextures { get; init; }

		public required Matrix4x4[] Matrices { get; init; }

		public required float[] Splits { get; init; }

		public required float[] DepthRanges { get; init; }

		public required float[] MapDepthRanges { get; init; }

		public required float[] WorldTexelSizes { get; init; }
	}

	private static void DisposeBindings(IDisposable[] bindings)
	{
		for (int index = bindings.Length - 1; index >= 0; index--)
		{
			bindings[index]?.Dispose();
		}
	}

	private sealed class BindingScope : IDisposable
	{
		private IDisposable[] bindings;

		internal BindingScope(IDisposable[] bindings)
		{
			this.bindings = bindings;
		}

		public void Dispose()
		{
			IDisposable[] current = System.Threading.Interlocked.Exchange(ref bindings, null);

			if (current != null)
			{
				DisposeBindings(current);
			}
		}
	}

	private sealed class EmptyScope : IDisposable
	{
		internal static EmptyScope Instance { get; } = new();

		public void Dispose()
		{
		}
	}
}
