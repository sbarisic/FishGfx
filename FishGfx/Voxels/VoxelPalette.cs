using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FishGfx.Voxels;

public readonly struct VoxelWaveSettings : IEquatable<VoxelWaveSettings>
{
	public VoxelWaveSettings(float amplitude, float wavelength, float speed)
	{
		if (!float.IsFinite(amplitude) || amplitude < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(amplitude));
		}

		if (!float.IsFinite(wavelength) || wavelength <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(wavelength));
		}

		if (!float.IsFinite(speed))
		{
			throw new ArgumentOutOfRangeException(nameof(speed));
		}

		Amplitude = amplitude;
		Wavelength = wavelength;
		Speed = speed;
	}

	public float Amplitude { get; }
	public float Wavelength { get; }
	public float Speed { get; }

	public bool Equals(VoxelWaveSettings other)
	{
		return Amplitude == other.Amplitude
			&& Wavelength == other.Wavelength
			&& Speed == other.Speed;
	}

	public override bool Equals(object obj) => obj is VoxelWaveSettings other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Amplitude, Wavelength, Speed);
	public static bool operator ==(VoxelWaveSettings left, VoxelWaveSettings right) => left.Equals(right);
	public static bool operator !=(VoxelWaveSettings left, VoxelWaveSettings right) => !left.Equals(right);

	internal void Validate(string parameterName)
	{
		if (
			!float.IsFinite(Amplitude)
			|| Amplitude < 0
			|| !float.IsFinite(Wavelength)
			|| Wavelength <= 0
			|| !float.IsFinite(Speed)
		)
		{
			throw new ArgumentException("Voxel wave settings are invalid.", parameterName);
		}
	}
}

public sealed class VoxelMaterial
{
	public VoxelMaterial(
		string name,
		VoxelRenderMode renderMode,
		VoxelFaceTiles tiles,
		Color? tint = null,
		bool? occludesFaces = null,
		bool doubleSided = false,
		VoxelModelSet models = null,
		VoxelWaveSettings? wave = null,
		VoxelMaterialLightSettings? light = null
	)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException("Voxel material names cannot be empty.", nameof(name));
		}

		if (wave.HasValue)
		{
			wave.Value.Validate(nameof(wave));
			if (renderMode != VoxelRenderMode.Transparent)
			{
				throw new ArgumentException("Voxel waves require a transparent material.", nameof(wave));
			}

			if (models != null)
			{
				throw new ArgumentException("Voxel waves require standard cube geometry.", nameof(wave));
			}
		}

		Name = name;
		RenderMode = renderMode;
		Tiles = tiles;
		Tint = tint ?? Color.White;
		OccludesFaces = occludesFaces ?? renderMode == VoxelRenderMode.Opaque;
		DoubleSided = doubleSided;
		Models = models;
		Wave = wave;
		Light = light ?? new VoxelMaterialLightSettings(OccludesFaces ? (byte)15 : (byte)0);
	}

	public string Name { get; }
	public VoxelRenderMode RenderMode { get; }
	public VoxelFaceTiles Tiles { get; }
	public Color Tint { get; }
	public bool OccludesFaces { get; }
	public bool DoubleSided { get; }
	public VoxelModelSet Models { get; }
	public VoxelWaveSettings? Wave { get; }
	public VoxelMaterialLightSettings Light { get; }
}

public sealed class VoxelPaletteBuilder
{
	private readonly List<VoxelMaterial> materials = new List<VoxelMaterial> { null };
	private bool built;

	public ushort Add(VoxelMaterial material)
	{
		if (built)
		{
			throw new InvalidOperationException("A voxel palette builder cannot be changed after Build.");
		}

		if (material == null)
		{
			throw new ArgumentNullException(nameof(material));
		}

		if (materials.Count > ushort.MaxValue)
		{
			throw new InvalidOperationException("The voxel palette has reached the ushort material limit.");
		}

		materials.Add(material);
		return (ushort)(materials.Count - 1);
	}

	public VoxelPalette Build()
	{
		if (built)
		{
			throw new InvalidOperationException("Build can only be called once.");
		}

		built = true;
		return new VoxelPalette(materials.ToArray());
	}
}

public sealed class VoxelPalette
{
	private readonly VoxelMaterial[] materials;
	private readonly ReadOnlyCollection<VoxelMaterial> readOnlyMaterials;

	internal VoxelPalette(VoxelMaterial[] materials)
	{
		this.materials = materials;
		readOnlyMaterials = Array.AsReadOnly(materials);
	}

	public int Count => materials.Length;
	public IReadOnlyList<VoxelMaterial> Materials => readOnlyMaterials;
	public VoxelMaterial this[ushort materialId] =>
		materialId == 0
			? null
			: materialId < materials.Length
				? materials[materialId]
				: throw new ArgumentOutOfRangeException(nameof(materialId));

	public bool Contains(ushort materialId) => materialId < materials.Length;
}

public readonly struct VoxelAtlasLayout
{
	public VoxelAtlasLayout(int columns, int rows, int textureWidth, int textureHeight)
	{
		if (columns <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(columns));
		}

		if (rows <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(rows));
		}

		if (textureWidth <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(textureWidth));
		}

		if (textureHeight <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(textureHeight));
		}

		Columns = columns;
		Rows = rows;
		TextureWidth = textureWidth;
		TextureHeight = textureHeight;
	}

	public int Columns { get; }
	public int Rows { get; }
	public int TextureWidth { get; }
	public int TextureHeight { get; }
	public int TileCount => Columns * Rows;
}
