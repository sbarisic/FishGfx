using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FishGfx.Voxels;

public sealed class VoxelModelSet
{
	private readonly VoxelModel[] models;
	private readonly ReadOnlyCollection<VoxelModel> readOnlyModels;

	public VoxelModelSet(params VoxelModel[] models)
	{
		if (models == null)
		{
			throw new ArgumentNullException(nameof(models));
		}

		if (models.Length == 0)
		{
			throw new ArgumentException("A voxel model set must contain at least one model.", nameof(models));
		}

		this.models = (VoxelModel[])models.Clone();

		for (int i = 0; i < this.models.Length; i++)
		{
			if (this.models[i] == null)
			{
				throw new ArgumentException("Voxel model sets cannot contain null models.", nameof(models));
			}
		}

		readOnlyModels = Array.AsReadOnly(this.models);
	}

	public IReadOnlyList<VoxelModel> Models => readOnlyModels;

	public VoxelModel Select(int worldX, int worldY, int worldZ)
	{
		if (models.Length == 1)
		{
			return models[0];
		}

		unchecked
		{
			int hash = worldX * 73856093 ^ worldY * 19349663 ^ worldZ * 83492791;
			int index = (hash & int.MaxValue) % models.Length;
			return models[index];
		}
	}
}
