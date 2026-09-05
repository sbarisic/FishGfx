using System;
using System.Collections.Generic;

namespace FishGfx.Voxels;

public sealed class VoxelLightingOptions
{
	private int updateBudget = 65_536;
	private double timeBudget = double.PositiveInfinity;
	public double TimeBudgetMilliseconds
	{
		get => timeBudget;
		set { if (double.IsNaN(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value)); timeBudget = value; }
	}

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
