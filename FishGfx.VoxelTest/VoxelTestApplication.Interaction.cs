using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using FishGfx.Game;
using FishGfx.Graphics;
using FishGfx.Voxels;

namespace FishGfx.VoxelTest;

internal sealed partial class VoxelTestApplication
{
	private static void RenderUnderwaterTint(RenderPass pass)
	{
		pass.FillRectangle(0, 0, Width, Height, UnderwaterTint);
	}

	private void UpdateCamera(
		Camera camera,
		InputManager input,
		float deltaTime,
		VoxelTestChunkStreamer streamer
	)
	{
		camera.MouseMovement = true;
		// RenderWindow reports the previous cursor position minus the current one.
		// Camera.Update expects movement in the current-minus-previous direction.
		camera.Update(-mouseDelta);
		mouseDelta = Vector2.Zero;
		float speed = input.IsKeyDown(Key.LeftShift) ? 80 : 20;
		Vector3 movement = Vector3.Zero;

		if (input.IsKeyDown(Key.W))
		{
			movement += camera.WorldForwardNormal;
		}

		if (input.IsKeyDown(Key.S))
		{
			movement -= camera.WorldForwardNormal;
		}

		if (input.IsKeyDown(Key.D))
		{
			movement += camera.WorldRightNormal;
		}

		if (input.IsKeyDown(Key.A))
		{
			movement -= camera.WorldRightNormal;
		}

		if (input.IsKeyDown(Key.Space))
		{
			movement += Vector3.UnitY;
		}

		if (input.IsKeyDown(Key.LeftControl))
		{
			movement -= Vector3.UnitY;
		}

		if (movement.LengthSquared() > 0)
		{
			camera.Position = streamer.ClampPosition(
				camera.Position + Vector3.Normalize(movement) * speed * deltaTime
			);
		}
	}

	private void ToggleBoundaryBlock(VoxelTestChunkStreamer streamer, VoxelTestMaterialIds materials)
	{
		boundaryBlockEnabled = !boundaryBlockEnabled;
		streamer.SetVoxel(
			VoxelTestWorldGenerator.BoundaryEditX,
			VoxelTestWorldGenerator.BoundaryEditY,
			VoxelTestWorldGenerator.BoundaryEditZ,
			boundaryBlockEnabled ? new VoxelCell(materials.Glass) : VoxelCell.Air
		);
	}

	private static void DestroyTargetedVoxel(
		VoxelTestChunkStreamer streamer,
		VoxelWorld world,
		Camera camera
	)
	{
		if (VoxelRaycast.Cast(world, camera.Position, camera.WorldForwardNormal, EditReach, out VoxelRaycastHit hit))
		{
			streamer.SetVoxel(hit.X, hit.Y, hit.Z, VoxelCell.Air);
		}
	}

	private static void PlaceTargetedVoxel(
		VoxelTestChunkStreamer streamer,
		VoxelWorld world,
		Camera camera,
		ushort materialId
	)
	{
		if (
			VoxelRaycast.Cast(world, camera.Position, camera.WorldForwardNormal, EditReach, out VoxelRaycastHit hit)
			&& hit.HasSurfaceNormal
		)
		{
			streamer.SetVoxel(hit.AdjacentX, hit.AdjacentY, hit.AdjacentZ, new VoxelCell(materialId));
		}
	}

	private static void ForceStaleMeshEdits(VoxelWorld world, VoxelTestMaterialIds materials)
	{
		foreach (VoxelChunk chunk in world.LoadedChunks)
		{
			Vector3 origin = chunk.Coordinate.WorldOrigin;
			int x = (int)origin.X + 8;
			int y = (int)origin.Y + 8;
			int z = (int)origin.Z + 8;
			VoxelCell original = world.GetVoxel(x, y, z);
			VoxelCell temporary = original.IsAir ? new VoxelCell(materials.Stone) : VoxelCell.Air;

			world.SetVoxel(x, y, z, temporary);
			world.SetVoxel(x, y, z, original);
		}
	}

	private static Camera CreateCamera(VoxelTestWorldData worldData)
	{
		Camera camera = new Camera();
		camera.SetPerspective(Width, Height, MathF.PI / 2.2f, 0.1f, 500);
		camera.Position = worldData.ShowcaseCameraPosition;
		camera.LookAt(worldData.ShowcaseTarget);

		return camera;
	}

	private static void ValidateCompatibilityShowcase(
		VoxelWorld world,
		VoxelTestWorldData worldData,
		VoxelTestMaterialIds materials
	)
	{
		for (int index = 0; index < materials.Placeable.Count; index++)
		{
			(int x, int y, int z) = worldData.GetShowcasePosition(index);

			if (world.GetVoxel(x, y, z).MaterialId != materials.Placeable[index].Id)
			{
				throw new InvalidOperationException(
					$"Compatibility showcase material "
						+ $"'{materials.Placeable[index].Name}' is missing."
				);
			}
		}

		for (int index = 0; index < VoxelTestWorldGenerator.OrientationShowcaseCount; index++)
		{
			(int x, int y, int z) = worldData.GetOrientationShowcasePosition(index);
			ushort expectedMaterial = VoxelTestWorldGenerator.GetOrientationShowcaseMaterial(materials, index);

			if (world.GetVoxel(x, y, z).MaterialId != expectedMaterial)
			{
				throw new InvalidOperationException("The voxel texture-orientation showcase is incomplete.");
			}
		}
	}

	private static void ValidateVoxelLighting(
		VoxelLighting lighting,
		VoxelTestWorldData worldData,
		VoxelTestMaterialIds materials
	)
	{
		int glowstoneIndex = materials.Placeable
			.Select((entry, index) => (entry, index))
			.Single(item => item.entry.Id == materials.Glowstone)
			.index;
		(int x, int y, int z) = worldData.GetShowcasePosition(glowstoneIndex);
		VoxelLight emitted = lighting.GetLight(x + 1, y, z);

		if (emitted.Block.Red < 14 || emitted.Block.Green < 11 || emitted.Block.Blue < 7)
		{
			throw new InvalidOperationException("The glowstone showcase did not propagate its configured RGB light.");
		}

		VoxelLight sky = lighting.GetLight(x, y + 2, z);

		if (sky.Sky == 0)
		{
			throw new InvalidOperationException("The sky-exposed showcase column did not receive skylight.");
		}
	}

	private static void UpdateHotbar(InputManager input, VoxelHotbarSelection hotbar)
	{
		for (int slot = 0; slot < VoxelHotbarSelection.VisibleSlots; slot++)
		{
			Key key = (Key)((int)Key.Alpha1 + slot);

			if (input.WasKeyPressed(key) && slot < hotbar.VisibleCount)
			{
				hotbar.SelectVisibleSlot(slot);
			}
		}
	}

	private static void RenderCrosshair(RenderPass pass)
	{
		Color color = new Color(245, 245, 245, 220);

		pass.DrawLine(
			new Vertex2(new Vector2(Width / 2 - 10, Height / 2), color),
			new Vertex2(new Vector2(Width / 2 + 10, Height / 2), color),
			2
		);
		pass.DrawLine(
			new Vertex2(new Vector2(Width / 2, Height / 2 - 10), color),
			new Vertex2(new Vector2(Width / 2, Height / 2 + 10), color),
			2
		);
	}
}
