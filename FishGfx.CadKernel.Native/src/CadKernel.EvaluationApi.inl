// Versioned C ABI entry points for curve and ordered-feature evaluation.

uint32_t fgcad_api_version(void)
{
	return 5;
}

const char* fgcad_last_error(void)
{
	return last_error.c_str();
}

fgcad_status fgcad_evaluate_cubic_bezier(
	const fgcad_frame* entry_frame,
	const fgcad_point3* control1,
	const fgcad_point3* control2,
	const fgcad_point3* end,
	double outer_radius,
	fgcad_bezier_evaluation* evaluation
)
{
	return guarded([&]()
	{
		if (entry_frame == nullptr || control1 == nullptr || control2 == nullptr
			|| end == nullptr || evaluation == nullptr)
		{
			throw std::invalid_argument("Cubic Bezier evaluation arguments cannot be null.");
		}
		*evaluation = evaluate_cubic_bezier_internal(
			*entry_frame, *control1, *control2, *end, outer_radius);
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_evaluate_runner_features(
	const fgcad_frame* start_frame,
	const fgcad_runner_profile* start_profile,
	const fgcad_runner_feature_spec* specifications,
	size_t specification_count,
	fgcad_runner_feature* evaluated_features,
	size_t evaluated_capacity,
	size_t* evaluated_count
)
{
	return guarded([&]()
	{
		if (start_frame == nullptr || start_profile == nullptr || specifications == nullptr
			|| evaluated_features == nullptr || evaluated_count == nullptr)
		{
			throw std::invalid_argument("Runner feature evaluation arguments cannot be null.");
		}
		*evaluated_count = 0;
		if (specification_count == 0 || evaluated_capacity < specification_count)
		{
			throw std::invalid_argument("The evaluated feature array has insufficient capacity.");
		}

		fgcad_frame frame = *start_frame;
		fgcad_runner_profile profile = *start_profile;
		std::unique_ptr<BRepBuilderAPI_MakeWire> transported_span;
		fgcad_frame transported_span_entry{};
		for (size_t index = 0; index < specification_count; ++index)
		{
			const fgcad_runner_feature_spec& specification = specifications[index];
			const bool transition = specification.kind == FGCAD_FEATURE_LOFT_TRANSITION
				|| specification.kind == FGCAD_FEATURE_CLOCKING_TRANSITION;
			if (!transition && !transported_span)
			{
				size_t span_end = index;
				bool contains_bezier = false;
				while (span_end < specification_count
					&& specifications[span_end].kind != FGCAD_FEATURE_LOFT_TRANSITION
					&& specifications[span_end].kind != FGCAD_FEATURE_CLOCKING_TRANSITION)
				{
					contains_bezier = contains_bezier
						|| specifications[span_end].kind == FGCAD_FEATURE_CUBIC_BEZIER;
					++span_end;
				}
				if (contains_bezier)
				{
					transported_span = std::make_unique<BRepBuilderAPI_MakeWire>();
					transported_span_entry = frame;
				}
			}
			fgcad_runner_feature feature{};
			feature.kind = specification.kind;
			copy_id(feature.source_node_id, specification.source_node_id);
			feature.entry_frame = frame;
			feature.input_profile = profile;
			feature.output_profile = profile;
			feature.rotation_radians = specification.rotation_radians;

			gp_Pnt origin = point(frame.origin);
			gp_Dir tangent = unit(frame.tangent);
			gp_Dir normal = unit(frame.normal);
			if (specification.kind == FGCAD_FEATURE_STRAIGHT)
			{
				if (!(specification.length > 0) || !std::isfinite(specification.length))
				{
					throw std::invalid_argument("Straight length must be positive and finite.");
				}
				frame.origin = point(origin.Translated(gp_Vec(tangent) * specification.length));
				feature.length = specification.length;
			}
			else if (specification.kind == FGCAD_FEATURE_BEND)
			{
				if (!(specification.radius > 0) || !(specification.sweep_radians > 0)
					|| specification.sweep_radians > pi || !std::isfinite(specification.rotation_radians))
				{
					throw std::invalid_argument("Bend radius, angle, or rotation is invalid.");
				}
				double outer_radius = profile.kind == FGCAD_PROFILE_CIRCULAR
					? profile.outer_diameter * 0.5
					: profile.equivalent_radius + profile.wall_thickness;
				if (!(specification.radius > outer_radius))
				{
					throw std::invalid_argument("Centreline bend radius must exceed the active outer profile radius.");
				}
				gp_Vec radial(normal);
				gp_Trsf plane_rotation;
				plane_rotation.SetRotation(gp_Ax1(origin, tangent), specification.rotation_radians);
				radial.Transform(plane_rotation);
				gp_Pnt center = origin.Translated(radial * specification.radius);
				gp_Vec start_radius(center, origin);
				gp_Dir axis = gp_Dir(start_radius).Crossed(tangent);
				gp_Trsf sweep;
				sweep.SetRotation(gp_Ax1(center, axis), specification.sweep_radians);
				gp_Pnt exit = origin.Transformed(sweep);
				gp_Dir exit_tangent = tangent.Transformed(sweep);
				gp_Dir exit_normal = normal.Transformed(sweep);
				feature.center = point(center);
				feature.radius = specification.radius;
				feature.sweep_radians = specification.sweep_radians;
				feature.length = specification.radius * specification.sweep_radians;
				frame.origin = point(exit);
				frame.tangent = direction(exit_tangent);
				frame.normal = direction(exit_normal);
			}
			else if (specification.kind == FGCAD_FEATURE_LOFT_TRANSITION)
			{
				if (!(specification.length > 0) || !std::isfinite(specification.length)
					|| !std::isfinite(specification.rotation_radians))
				{
					throw std::invalid_argument("Loft length and rotation must be finite and valid.");
				}
				gp_Trsf rotation;
				rotation.SetRotation(gp_Ax1(origin, tangent), specification.rotation_radians);
				frame.origin = point(origin.Translated(gp_Vec(tangent) * specification.length));
				frame.normal = direction(normal.Transformed(rotation));
				profile = specification.output_profile;
				feature.output_profile = profile;
				feature.length = specification.length;
				transported_span.reset();
			}
			else if (specification.kind == FGCAD_FEATURE_CUBIC_BEZIER)
			{
				if (!(specification.start_handle_length > 0)
					|| !std::isfinite(specification.start_handle_length))
				{
					throw std::invalid_argument("Cubic Bezier start handle must be positive and finite.");
				}
				gp_Vec t(tangent);
				gp_Vec u(normal);
				gp_Vec v = t.Crossed(u);
				auto local_point = [&](const fgcad_point3& local)
				{
					return origin.Translated(t * local.x + u * local.y + v * local.z);
				};
				gp_Pnt control1 = origin.Translated(t * specification.start_handle_length);
				gp_Pnt control2;
				gp_Pnt end;
				if (specification.has_constrained_end_frame != 0)
				{
					if (!(specification.end_handle_length > 0)
						|| !std::isfinite(specification.end_handle_length))
					{
						throw std::invalid_argument(
							"Constrained cubic Bezier end handle must be positive and finite.");
					}
					end = point(specification.constrained_end_frame.origin);
					gp_Dir end_tangent = unit(specification.constrained_end_frame.tangent);
					control2 = end.Translated(-gp_Vec(end_tangent) * specification.end_handle_length);
				}
				else
				{
					control2 = local_point(specification.control2_local);
					end = local_point(specification.end_local);
				}
				double outer_radius = profile.kind == FGCAD_PROFILE_CIRCULAR
					? profile.outer_diameter * 0.5
					: profile.equivalent_radius + profile.wall_thickness;
				fgcad_point3 control1_value = point(control1);
				fgcad_point3 control2_value = point(control2);
				fgcad_point3 end_value = point(end);
				fgcad_bezier_evaluation evaluation = evaluate_cubic_bezier_internal(
					frame, control1_value, control2_value, end_value, outer_radius);
				feature.control1 = control1_value;
				feature.control2 = control2_value;
				feature.length = evaluation.length;
				feature.radius = evaluation.minimum_radius;
				frame = evaluation.exit_frame;
			}
			else if (specification.kind == FGCAD_FEATURE_CLOCKING_TRANSITION)
			{
				if (!(specification.length > 0) || !std::isfinite(specification.length))
				{
					throw std::invalid_argument("Clocking-transition length must be positive and finite.");
				}
				double rotation = specification.rotation_radians;
				gp_Pnt exit = origin.Translated(gp_Vec(tangent) * specification.length);
				if (specification.has_constrained_end_frame != 0)
				{
					gp_Pnt target = point(specification.constrained_end_frame.origin);
					gp_Vec displacement(origin, target);
					double axial = displacement.Dot(gp_Vec(tangent));
					gp_Vec lateral = displacement - gp_Vec(tangent) * axial;
					if (std::abs(axial - specification.length) > Precision::Confusion()
						|| lateral.Magnitude() > Precision::Confusion())
					{
						throw std::invalid_argument(
							"Clocking-transition target must lie on its incoming axis.");
					}
					gp_Dir target_tangent = unit(specification.constrained_end_frame.tangent);
					if (gp_Vec(tangent).Dot(gp_Vec(target_tangent)) < 1.0 - Precision::Angular())
					{
						throw std::invalid_argument(
							"Clocking-transition target tangent must match its incoming tangent.");
					}
					gp_Dir target_normal = unit(specification.constrained_end_frame.normal);
					rotation = std::atan2(
						gp_Vec(tangent).Dot(gp_Vec(normal).Crossed(gp_Vec(target_normal))),
						gp_Vec(normal).Dot(gp_Vec(target_normal))
					);
					exit = target;
				}
				gp_Trsf roll;
				roll.SetRotation(gp_Ax1(origin, tangent), rotation);
				frame.origin = point(exit);
				frame.normal = direction(normal.Transformed(roll));
				feature.rotation_radians = rotation;
				feature.length = specification.length;
				transported_span.reset();
			}
			else
			{
				throw std::invalid_argument("Unknown runner feature specification kind.");
			}

			if (transported_span && !transition)
			{
				TopoDS_Edge edge;
				if (specification.kind == FGCAD_FEATURE_STRAIGHT)
				{
					edge = BRepBuilderAPI_MakeEdge(
						point(feature.entry_frame.origin),
						point(frame.origin)
					).Edge();
				}
				else if (specification.kind == FGCAD_FEATURE_BEND)
				{
					gp_Pnt start = point(feature.entry_frame.origin);
					gp_Pnt center = point(feature.center);
					gp_Vec start_radius(center, start);
					gp_Dir axis = gp_Dir(start_radius).Crossed(unit(feature.entry_frame.tangent));
					gp_Trsf half_rotation;
					half_rotation.SetRotation(
						gp_Ax1(center, axis),
						feature.sweep_radians * 0.5
					);
					Handle(Geom_TrimmedCurve) arc = GC_MakeArcOfCircle(
						start,
						start.Transformed(half_rotation),
						point(frame.origin)
					);
					edge = BRepBuilderAPI_MakeEdge(arc).Edge();
				}
				else
				{
					edge = BRepBuilderAPI_MakeEdge(make_bezier(
						feature.entry_frame,
						feature.control1,
						feature.control2,
						frame.origin
					)).Edge();
				}
				transported_span->Add(edge);
				if (!transported_span->IsDone())
				{
					throw std::runtime_error("The transported runner span could not form a G1 wire.");
				}
				frame = transport_frame(
					transported_span->Wire(),
					transported_span_entry,
					point(frame.origin),
					unit(frame.tangent)
				);
			}

			feature.exit_frame = frame;
			evaluated_features[index] = feature;
			*evaluated_count = index + 1;
		}
		return FGCAD_STATUS_OK;
	});
}
