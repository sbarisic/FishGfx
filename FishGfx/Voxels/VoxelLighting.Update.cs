using System;
using System.Collections.Generic;

namespace FishGfx.Voxels;

public sealed partial class VoxelLighting
{
	private int UpdateUnchecked(
		int budget,
		ref List<(ChunkCoordinate Coordinate, long Revision)> invalidated
	)
	{
		int processed = 0;

		while (true)
		{
			if (transaction == null && incrementalTransaction == null)
			{
				if (fullRebuildRequested)
				{
					fullRebuildRequested = false;
					if (residents.Count != 0)
					{
						transaction = CreateTransaction();
					}

					ClearPendingIncrementalChanges();
				}
				else if (HasPendingIncrementalChanges())
				{
					incrementalTransaction = CreateIncrementalTransaction();
				}

				if (transaction == null && incrementalTransaction == null)
				{
					break;
				}
			}

			if (incrementalTransaction != null)
			{
				IncrementalStepResult step = ProcessIncrementalStep(
					incrementalTransaction,
					processed < budget,
					ref invalidated
				);
				if (step == IncrementalStepResult.NeedsBudget)
				{
					break;
				}

				if (step == IncrementalStepResult.Consumed)
				{
					processed++;
				}
				else
				{
					incrementalTransaction = null;
				}

				continue;
			}

			if (transaction.Phase == RebuildPhase.Initialize)
			{
				if (processed >= budget)
				{
					break;
				}

				InitializeNextCell(transaction);
				processed++;
				continue;
			}

			if (transaction.Phase == RebuildPhase.SeedDirectSky)
			{
				if (!TryPrepareNextFullDirectCell(transaction))
				{
					transaction.Phase = RebuildPhase.Propagate;
					continue;
				}
				if (processed >= budget)
				{
					break;
				}

				ProcessNextFullDirectCell(transaction);
				processed++;
				continue;
			}

			if (transaction.Phase == RebuildPhase.Propagate)
			{
				if (transaction.Propagation.Count == 0)
				{
					transaction.Phase = RebuildPhase.Compare;
					continue;
				}
				if (processed >= budget)
				{
					break;
				}

				PropagateNextCell(transaction);
				processed++;
				continue;
			}

			if (transaction.Phase == RebuildPhase.Compare)
			{
				if (transaction.CompareChunkIndex >= transaction.Chunks.Count)
				{
					InitializeFullTombstoneComparison(transaction);
					continue;
				}
				if (processed >= budget)
				{
					break;
				}

				CompareNextCell(transaction);
				processed++;
				continue;
			}

			if (transaction.Phase == RebuildPhase.CompareTombstones)
			{
				if (!TryPrepareNextFullTombstoneCell(transaction))
				{
					CommitTransaction(transaction, ref invalidated);
					transaction = null;
					continue;
				}
				if (processed >= budget)
				{
					break;
				}

				CompareNextFullTombstoneCell(transaction);
				processed++;
			}
		}

		return processed;
	}
}
