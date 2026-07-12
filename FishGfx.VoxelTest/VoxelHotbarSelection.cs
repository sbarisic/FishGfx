using System;
using System.Collections.Generic;

namespace FishGfx.VoxelTest
{
	internal sealed class VoxelHotbarSelection
	{
		internal const int VisibleSlots = 9;
		private readonly IReadOnlyList<VoxelTestMaterialEntry> entries;

		internal VoxelHotbarSelection(IReadOnlyList<VoxelTestMaterialEntry> entries)
		{
			this.entries = entries ?? throw new ArgumentNullException(nameof(entries));

			if (entries.Count == 0)
				throw new ArgumentException("The voxel hotbar requires at least one material.", nameof(entries));
		}

		internal int SelectedIndex { get; private set; }
		internal VoxelTestMaterialEntry Selected => entries[SelectedIndex];
		internal int WindowStart => Math.Clamp(
			SelectedIndex - VisibleSlots / 2,
			0,
			Math.Max(0, entries.Count - VisibleSlots)
		);
		internal int VisibleCount => Math.Min(VisibleSlots, entries.Count);

		internal VoxelTestMaterialEntry GetVisible(int slot)
		{
			if (slot < 0 || slot >= VisibleCount)
				throw new ArgumentOutOfRangeException(nameof(slot));

			return entries[WindowStart + slot];
		}

		internal bool IsSelectedSlot(int slot) => WindowStart + slot == SelectedIndex;

		internal void Move(int offset)
		{
			int next = (SelectedIndex + offset) % entries.Count;

			if (next < 0)
				next += entries.Count;

			SelectedIndex = next;
		}

		internal void SelectVisibleSlot(int slot)
		{
			if (slot < 0 || slot >= VisibleCount)
				throw new ArgumentOutOfRangeException(nameof(slot));

			SelectedIndex = WindowStart + slot;
		}
	}
}
