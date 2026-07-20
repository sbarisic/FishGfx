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
		VoxelVertex[] alphaShadow,
		ref int alphaShadowIndex,
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
				material,
				triangle,
				center / 3,
				opaque,
				ref opaqueIndex,
				cutout,
				ref cutoutIndex,
				alphaShadow,
				ref alphaShadowIndex,
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
					material,
					triangle,
					center / 3,
					opaque,
					ref opaqueIndex,
					cutout,
					ref cutoutIndex,
					alphaShadow,
					ref alphaShadowIndex,
					transparent,
					ref transparentIndex
				);
			}
		}
	}

	private static void AppendCustomTriangle(
		VoxelMaterial material,
		ReadOnlySpan<VoxelVertex> triangle,
		Vector3 center,
		VoxelVertex[] opaque,
		ref int opaqueIndex,
		VoxelVertex[] cutout,
		ref int cutoutIndex,
		VoxelVertex[] alphaShadow,
		ref int alphaShadowIndex,
		VoxelTransparentFace[] transparent,
		ref int transparentIndex
	)
	{
		switch (material.RenderMode)
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

				if (material.ShadowCasterMode == VoxelShadowCasterMode.AlphaTest)
				{
					Span<VoxelVertex> destination = alphaShadow.AsSpan(
						alphaShadowIndex,
						triangle.Length
					);
					triangle.CopyTo(destination);

					for (int index = 0; index < destination.Length; index++)
					{
						destination[index].WaveParameters.X = material.ShadowAlphaCutoff;
					}

					alphaShadowIndex += triangle.Length;
				}
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(material));
		}
	}

	private static Color Multiply(Color left, Color right)
	{
		Color linearLeft = ColorSpace.SrgbToLinearColor(left);
		Color linearRight = ColorSpace.SrgbToLinearColor(right);
		return new Color(
			(byte)(linearLeft.R * linearRight.R / 255),
			(byte)(linearLeft.G * linearRight.G / 255),
			(byte)(linearLeft.B * linearRight.B / 255),
			(byte)(left.A * right.A / 255)
		);
	}

}
