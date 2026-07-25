using System.Numerics;
using FishGfx.Cad;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;

namespace FishGfx.ManifoldCad;

internal sealed partial class CadViewport
{
	internal bool CanPickMateCandidate(CadRect bounds)
	{
		return TryCreateVisibleCandidatePickContext(bounds, out _);
	}

	internal bool TryCapturePickingRayToVisibleCandidate(CadRect bounds)
	{
		if (!TryCreateVisibleCandidatePickContext(bounds, out PickContext context))
		{
			return false;
		}

		pickingRayDebugEnabled = true;
		CaptureDebugPickingRay(context);
		return true;
	}

	private bool TryCreateVisibleCandidatePickContext(CadRect bounds, out PickContext context)
	{
		context = default;
		ConfigureCamera(Math.Max(1, (int)bounds.Width), Math.Max(1, (int)bounds.Height));

		foreach (MateCandidateGlyph candidate in mateCandidates)
		{
			Vector3 screen = camera.WorldToScreen(CandidateDisplayCenter(candidate));

			if (!IsProjectedPointInClip(screen))
			{
				continue;
			}

			Vector2 layoutPoint = FromCameraPoint(bounds, new Vector2(screen.X, screen.Y));
			PickContext candidateContext = CreatePickContext(bounds, layoutPoint);

			if (TryFindMateCandidate(candidateContext, out MateCandidateGlyph selected)
				&& selected.PartId == candidate.PartId
				&& selected.TopologyId == candidate.TopologyId)
			{
				context = candidateContext;
				return true;
			}
		}

		return false;
	}


	private PickContext CreatePickContext(CadRect bounds, Vector2 mouse)
	{
		ConfigureCamera(Math.Max(1, (int)bounds.Width), Math.Max(1, (int)bounds.Height));
		Vector2 local = ToCameraPoint(bounds, mouse);
		PickingRay ray = camera.CreatePickingRay(local);
		CadPickHit? nearestFace = null;
		SceneItem nearestFaceItem = null;

		foreach (SceneItem item in items)
		{
			if (item.Bvh.TryIntersect(ray, out CadPickHit hit)
				&& (!nearestFace.HasValue || hit.Distance < nearestFace.Value.Distance))
			{
				nearestFace = hit;
				nearestFaceItem = item;
			}
		}

		float faceDepth = nearestFace.HasValue
			? camera.WorldToScreen(ray.GetPoint(nearestFace.Value.Distance)).Z
			: float.PositiveInfinity;
		return new PickContext(local, ray, nearestFace, nearestFaceItem, faceDepth);
	}

	private void CaptureDebugPickingRay(PickContext context)
	{
		if (!pickingRayDebugEnabled)
		{
			return;
		}

		float rayLength = context.NearestFace.HasValue
			? context.NearestFace.Value.Distance + Math.Max(distance * 0.25f, 25)
			: Math.Max(distance * 2, 500);
		debugPickingRayStart = orthographic ? context.Ray.Origin : camera.Position;
		debugPickingRayEnd = context.Ray.GetPoint(rayLength);
		debugPickingHit = context.NearestFace.HasValue
			? context.Ray.GetPoint(context.NearestFace.Value.Distance)
			: null;
		hasDebugPickingRay = true;
	}

	private bool TryPickMateGlyph(PickContext context)
	{
		MateGlyph? glyph = mates
			.Select(mate => (Mate: mate, Screen: camera.WorldToScreen(ToVector(mate.Frame.Origin))))
			.Select(item => (
				item.Mate,
				item.Screen,
				Distance: Vector2.Distance(new Vector2(item.Screen.X, item.Screen.Y), context.LocalPoint)
			))
			.Where(item => IsProjectedPointVisible(item.Screen, context.FaceDepth) && item.Distance <= 11)
			.OrderBy(item => item.Distance)
			.ThenBy(item => item.Screen.Z)
			.Select(item => (MateGlyph?)item.Mate)
			.FirstOrDefault();

		if (glyph.HasValue)
		{
			MateGlyph mate = glyph.Value;
			SetSelection(new CadViewportSelection(mate.PartId, null, mate.TopologyId, null, mate.Frame.Origin, mate.Id));
			return true;
		}

		return false;
	}

	private bool TryPickCollectorGlyph(PickContext context)
	{
		CollectorGlyph? glyph = collectorGlyphs
			.Select(item => (Glyph: item, Screen: camera.WorldToScreen(ToVector(item.Frame.Origin))))
			.Select(item => (
				item.Glyph,
				item.Screen,
				Distance: Vector2.Distance(new Vector2(item.Screen.X, item.Screen.Y), context.LocalPoint)
			))
			.Where(item => IsProjectedPointVisible(item.Screen, context.FaceDepth)
				&& item.Distance <= 13)
			.OrderBy(item => item.Distance)
			.ThenBy(item => item.Screen.Z)
			.Select(item => (CollectorGlyph?)item.Glyph)
			.FirstOrDefault();
		if (!glyph.HasValue)
		{
			return false;
		}
		CollectorGlyph value = glyph.Value;
		CadGeometrySourceRef source = value.InletId.HasValue
			? new CadGeometrySourceRef(
				CadGeometrySourceKind.CollectorInlet,
				value.SystemId,
				value.InletId.Value.ToString("D")
			)
			: new CadGeometrySourceRef(
				CadGeometrySourceKind.CollectorOutlet,
				value.SystemId,
				"outlet"
			);
		SetSelection(new CadViewportSelection(
			null,
			null,
			0,
			null,
			value.Frame.Origin,
			null,
			false,
			new[] { source }
		));
		return true;
	}

	private bool TryPickMateCandidate(PickContext context)
	{
		if (TryFindMateCandidate(context, out MateCandidateGlyph candidate))
		{
			SetSelection(new CadViewportSelection(
				candidate.PartId,
				null,
				candidate.TopologyId,
				null,
				candidate.Center,
				null,
				true
			));
			return true;
		}

		return false;
	}

	private void PickGeometry(PickContext context)
	{
		if (TryPickEdge(
			context.LocalPoint,
			context.FaceDepth,
			out SceneItem edgeItem,
			out CadEdgePolyline edge,
			out CadPoint3 edgePoint
		))
		{
			SetSelection(new CadViewportSelection(
				edgeItem.PartId, edgeItem.RunnerId, edge.TopologyId, null, edgePoint, null));
			return;
		}

		SetSelection(context.NearestFace.HasValue
			? new CadViewportSelection(
				context.NearestFaceItem.PartId,
				context.NearestFaceItem.RunnerId,
				context.NearestFace.Value.TopologyId,
				context.NearestFace.Value.SourceNodeId,
				CadPoint3.FromVector3(context.Ray.GetPoint(context.NearestFace.Value.Distance)),
				null,
				false,
				context.NearestFace.Value.Sources
			)
			: default);
	}

	private void SetSelection(CadViewportSelection selection)
	{
		Selection = selection;
		selectedFaceMesh?.Dispose();
		selectedFaceMesh = null;

		if (selection.PartId.HasValue || selection.RunnerId.HasValue)
		{
			SceneItem item = items.FirstOrDefault(candidate => candidate.PartId == selection.PartId
				&& candidate.RunnerId == selection.RunnerId);
			CadFaceRange[] faces = item?.Tessellation.Faces
				.Where(face => face.TopologyId == selection.TopologyId)
				.ToArray();

			if (faces?.Length > 0)
			{
				selectedFaceMesh = graphics.CreateMesh3D(BufferUsage.Dynamic);
				selectedFaceMesh.SetVertices(item.Tessellation.Vertices
					.Select(vertex => new Vector3(vertex.X, vertex.Y, vertex.Z))
					.ToArray());
				selectedFaceMesh.DefaultColor = new Color(255, 205, 55, 190);
				selectedFaceMesh.SetElements(faces
					.SelectMany(face => item.Tessellation.Indices.Skip(face.FirstIndex).Take(face.IndexCount))
					.ToArray());
			}
		}

		SelectionChanged?.Invoke(Selection);
	}

	private bool TryFindMateCandidate(PickContext context, out MateCandidateGlyph selectedCandidate)
	{
		selectedCandidate = default;
		float bestScreenDistance = float.PositiveInfinity;
		float bestRayDistance = float.PositiveInfinity;

		foreach (MateCandidateGlyph candidate in mateCandidates)
		{
			Vector3 center = CandidateDisplayCenter(candidate);
			float radius = CandidateDisplayRadius(candidate);
			Vector3 projectedCenter = camera.WorldToScreen(center);

			if (!IsProjectedPointInClip(projectedCenter))
			{
				continue;
			}

			Vector3 projectedRadiusPoint = camera.WorldToScreen(center + camera.WorldRightNormal * radius);
			float projectedRadius = Vector2.Distance(
				new Vector2(projectedCenter.X, projectedCenter.Y),
				new Vector2(projectedRadiusPoint.X, projectedRadiusPoint.Y)
			);

			if (!float.IsFinite(projectedRadius) || projectedRadius <= 0)
			{
				continue;
			}

			float screenDistance = Vector2.Distance(
				context.LocalPoint,
				new Vector2(projectedCenter.X, projectedCenter.Y)
			);
			float pickRadius = projectedRadius + 4;

			if (screenDistance > pickRadius)
			{
				continue;
			}

			float expandedRadius = radius * pickRadius / projectedRadius;

			if (TryIntersectSphere(context.Ray, center, expandedRadius, out float rayDistance)
				&& rayDistance <= (context.NearestFace?.Distance ?? float.PositiveInfinity)
					+ Math.Max(radius * 0.05f, 0.01f)
				&& (screenDistance < bestScreenDistance
					|| (MathF.Abs(screenDistance - bestScreenDistance) < 0.25f
						&& rayDistance < bestRayDistance)))
			{
				bestScreenDistance = screenDistance;
				bestRayDistance = rayDistance;
				selectedCandidate = candidate;
			}
		}

		return float.IsFinite(bestRayDistance);
	}

	private bool TryPickEdge(
		Vector2 mouse,
		float faceDepth,
		out SceneItem selectedItem,
		out CadEdgePolyline selectedEdge,
		out CadPoint3 selectedPoint
	)
	{
		selectedItem = null;
		selectedEdge = null;
		selectedPoint = default;
		float best = 7;
		float bestDepth = float.PositiveInfinity;

		foreach (SceneItem item in items)
			foreach (CadEdgePolyline edge in item.Tessellation.Edges)
			{
				for (int index = 1; index < edge.Points.Length; index++)
				{
					Vector3 a = camera.WorldToScreen(ToVector(edge.Points[index - 1]));
					Vector3 b = camera.WorldToScreen(ToVector(edge.Points[index]));

					if (!IsProjectedPointInClip(a) || !IsProjectedPointInClip(b))
					{
						continue;
					}

					float amount = ClosestSegmentAmount(
						mouse,
						new Vector2(a.X, a.Y),
						new Vector2(b.X, b.Y)
					);
					Vector2 projected = Vector2.Lerp(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), amount);
					float distance = Vector2.Distance(mouse, projected);
					float depth = float.Lerp(a.Z, b.Z, amount);

					if (depth <= faceDepth + 0.002f
						&& (distance < best || (MathF.Abs(distance - best) < 0.25f && depth < bestDepth)))
					{
						best = distance;
						bestDepth = depth;
						selectedItem = item;
						selectedEdge = edge;
						selectedPoint = edge.Points[index - 1] + (edge.Points[index] - edge.Points[index - 1]) * amount;
					}
				}
			}

		return selectedEdge != null;
	}
}
