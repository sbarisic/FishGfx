using System.Numerics;
using FishGfx.Cad;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;

namespace FishGfx.ManifoldCad;

internal sealed partial class CadViewport
{
	private const int CollectorPreviewSides = 16;

	private void AddCollectorDraftCurve(CadPoint3[] samples, double outerRadius)
	{
		if (samples == null || samples.Length < 2
			|| !double.IsFinite(outerRadius) || outerRadius <= 0)
		{
			return;
		}
		collectorDraftCurves.Add(samples);
		collectorDraftMeshes.Add(CreateCollectorDraftTube(samples, outerRadius));
	}

	private Mesh3D CreateCollectorDraftTube(
		IReadOnlyList<CadPoint3> samples,
		double radius
	)
	{
		int ringCount = samples.Count;
		Vector3[] positions = new Vector3[ringCount * CollectorPreviewSides];
		Vector3[] normals = new Vector3[positions.Length];
		uint[] indices = new uint[
			(ringCount - 1) * CollectorPreviewSides * 6
		];
		Vector3 carriedNormal = default;
		for (int ring = 0; ring < ringCount; ++ring)
		{
			Vector3 center = samples[ring].ToVector3();
			Vector3 tangent = PreviewTangent(samples, ring);
			carriedNormal = TransportPreviewNormal(tangent, carriedNormal);
			Vector3 binormal = Vector3.Normalize(Vector3.Cross(tangent, carriedNormal));
			for (int side = 0; side < CollectorPreviewSides; ++side)
			{
				float angle = MathF.Tau * side / CollectorPreviewSides;
				Vector3 normal = carriedNormal * MathF.Cos(angle)
					+ binormal * MathF.Sin(angle);
				int vertex = ring * CollectorPreviewSides + side;
				positions[vertex] = center + normal * (float)radius;
				normals[vertex] = normal;
			}
		}
		int element = 0;
		for (int ring = 0; ring < ringCount - 1; ++ring)
		{
			for (int side = 0; side < CollectorPreviewSides; ++side)
			{
				uint current = (uint)(ring * CollectorPreviewSides + side);
				uint next = (uint)(ring * CollectorPreviewSides
					+ (side + 1) % CollectorPreviewSides);
				uint following = current + CollectorPreviewSides;
				uint followingNext = next + CollectorPreviewSides;
				indices[element++] = current;
				indices[element++] = following;
				indices[element++] = followingNext;
				indices[element++] = current;
				indices[element++] = followingNext;
				indices[element++] = next;
			}
		}
		Mesh3D mesh = graphics.CreateMesh3D(BufferUsage.Dynamic);
		mesh.SetVertices(positions);
		mesh.SetNormals(normals);
		mesh.SetElements(indices);
		return mesh;
	}

	private static Vector3 PreviewTangent(
		IReadOnlyList<CadPoint3> samples,
		int index
	)
	{
		Vector3 tangent = index == 0
			? (samples[1] - samples[0]).ToVector3()
			: index == samples.Count - 1
				? (samples[^1] - samples[^2]).ToVector3()
				: (samples[index + 1] - samples[index - 1]).ToVector3();
		if (tangent.LengthSquared() <= 1e-12f)
		{
			return Vector3.UnitX;
		}
		return Vector3.Normalize(tangent);
	}

	private static Vector3 TransportPreviewNormal(
		Vector3 tangent,
		Vector3 previous
	)
	{
		Vector3 normal = previous - tangent * Vector3.Dot(previous, tangent);
		if (normal.LengthSquared() <= 1e-8f)
		{
			Vector3 reference = MathF.Abs(Vector3.Dot(tangent, Vector3.UnitY)) < 0.9f
				? Vector3.UnitY
				: Vector3.UnitZ;
			normal = reference - tangent * Vector3.Dot(reference, tangent);
		}
		return Vector3.Normalize(normal);
	}

	private static CadPoint3[] SampleCubicBezier(RunnerFeature feature)
	{
		const int sampleCount = 25;
		CadPoint3[] samples = new CadPoint3[sampleCount];
		CadPoint3 p0 = feature.EntryFrame.Origin;
		CadPoint3 p1 = feature.Control1;
		CadPoint3 p2 = feature.Control2;
		CadPoint3 p3 = feature.ExitFrame.Origin;
		for (int sample = 0; sample < sampleCount; ++sample)
		{
			double t = sample / (double)(sampleCount - 1);
			double inverse = 1 - t;
			samples[sample] = p0 * (inverse * inverse * inverse)
				+ p1 * (3 * inverse * inverse * t)
				+ p2 * (3 * inverse * t * t)
				+ p3 * (t * t * t);
		}
		return samples;
	}

	private void ClearCollectorDraftMeshes()
	{
		foreach (Mesh3D mesh in collectorDraftMeshes)
		{
			mesh.Dispose();
		}
		collectorDraftMeshes.Clear();
	}
}
