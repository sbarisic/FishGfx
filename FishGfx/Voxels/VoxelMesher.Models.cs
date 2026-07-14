using System;
using System.Numerics;

namespace FishGfx.Voxels;

public static partial class VoxelMesher
{
	private static void AppendCustomModel(
		VoxelModel model,
		VoxelMaterial material,
		Vector3 blockPosition,
		VoxelLightChunkSnapshot lightSnapshot,
		VoxelVertex[] opaque,
		ref int opaqueIndex,
		VoxelVertex[] cutout,
		ref int cutoutIndex,
		VoxelTransparentFace[] transparent,
		ref int transparentIndex,
		ref MeshBoundsBuilder bounds
	)
	{
		VoxelVertex[] source = model.VertexArray;
		Span<VoxelVertex> triangle = stackalloc VoxelVertex[3];

		for (int triangleStart = 0; triangleStart < source.Length; triangleStart += 3)
		{
			Vector3 center = Vector3.Zero;

			for (int i = 0; i < triangle.Length; i++)
			{
				VoxelVertex vertex = source[triangleStart + i];
				Vector3 localPosition = vertex.Position;
				vertex.Position += blockPosition;
				vertex.Color = Multiply(vertex.Color, material.Tint);
				vertex.PackedLightChannels = SampleCustomModelLight(
					lightSnapshot,
					blockPosition,
					localPosition,
					material.Light.Emission
				);
				triangle[i] = vertex;
				center += vertex.Position;
				bounds.Add(vertex.Position);
			}

			AppendCustomTriangle(
				material.RenderMode,
				triangle,
				center / 3,
				opaque,
				ref opaqueIndex,
				cutout,
				ref cutoutIndex,
				transparent,
				ref transparentIndex
			);

			if (material.DoubleSided)
			{
				(triangle[0], triangle[2]) = (triangle[2], triangle[0]);

				for (int i = 0; i < triangle.Length; i++)
				{
					triangle[i].Normal = -triangle[i].Normal;
				}

				AppendCustomTriangle(
					material.RenderMode,
					triangle,
					center / 3,
					opaque,
					ref opaqueIndex,
					cutout,
					ref cutoutIndex,
					transparent,
					ref transparentIndex
				);
			}
		}
	}

	private static void AppendCustomTriangle(
		VoxelRenderMode renderMode,
		ReadOnlySpan<VoxelVertex> triangle,
		Vector3 center,
		VoxelVertex[] opaque,
		ref int opaqueIndex,
		VoxelVertex[] cutout,
		ref int cutoutIndex,
		VoxelTransparentFace[] transparent,
		ref int transparentIndex
	)
	{
		switch (renderMode)
		{
			case VoxelRenderMode.Opaque:
				triangle.CopyTo(opaque.AsSpan(opaqueIndex));
				opaqueIndex += triangle.Length;
				break;
			case VoxelRenderMode.Cutout:
				triangle.CopyTo(cutout.AsSpan(cutoutIndex));
				cutoutIndex += triangle.Length;
				break;
			case VoxelRenderMode.Transparent:
				transparent[transparentIndex++] = new VoxelTransparentFace(
					center,
					triangle.ToArray()
				);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(renderMode));
		}
	}

	private static Color Multiply(Color left, Color right)
	{
		return new Color(
			(byte)(left.R * right.R / 255),
			(byte)(left.G * right.G / 255),
			(byte)(left.B * right.B / 255),
			(byte)(left.A * right.A / 255)
		);
	}

}
