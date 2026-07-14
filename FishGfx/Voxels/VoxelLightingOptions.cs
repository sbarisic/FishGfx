using System;
using System.Collections.Generic;

namespace FishGfx.Voxels;

public sealed class VoxelLightingOptions
{
	private int updateBudget = 65_536;

	public int UpdateBudget
	{
		get => updateBudget;
		set
		{
			if (value <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(value));
			}

			updateBudget = value;
		}
	}
}
