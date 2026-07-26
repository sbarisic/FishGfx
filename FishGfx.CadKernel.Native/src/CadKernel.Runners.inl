// Versioned C ABI entry points for exact runner generation and lifecycle operations.

fgcad_status fgcad_document_build_runner(
	fgcad_document* document,
	const char* runner_id,
	const char* runner_name,
	const fgcad_runner_feature* features,
	size_t feature_count
)
{
	return guarded([&]()
	{
		if (document == nullptr || features == nullptr || feature_count == 0)
		{
			throw std::invalid_argument("Runner features cannot be empty.");
		}

		auto trace_start = std::chrono::steady_clock::now();
		auto trace_last = std::chrono::steady_clock::now();
		std::vector<std::pair<std::string, long long>> stage_timings;
		auto trace = [&](const char* label)
		{
			auto now = std::chrono::steady_clock::now();
			long long elapsed = std::chrono::duration_cast<std::chrono::microseconds>(
				now - trace_last).count();
			stage_timings.emplace_back(label, elapsed);
			if (std::getenv("FGCAD_TRACE_STAGES") != nullptr)
			{
				append_native_log(
					"runner-build-stage",
					std::string(runner_id == nullptr ? "<null>" : runner_id)
					+ "; " + label + "Us=" + std::to_string(elapsed));
			}
			trace_last = now;
		};

		using profile_wires = runner_boundary_record;

		auto frame_axes = [](const fgcad_frame& frame)
		{
			return gp_Ax2(point(frame.origin), unit(frame.tangent), unit(frame.normal));
		};

		auto wire_area = [](const TopoDS_Wire& wire)
		{
			BRepBuilderAPI_MakeFace face(wire, true);
			if (!face.IsDone() || !BRepCheck_Analyzer(face.Face(), true).IsValid()) return 0.0;
			GProp_GProps properties;
			BRepGProp::SurfaceProperties(face.Face(), properties);
			return std::abs(properties.Mass());
		};

		auto normalize_wire = [](const TopoDS_Wire& wire, const fgcad_frame& frame)
		{
			gp_Vec u(frame.normal.x, frame.normal.y, frame.normal.z);
			gp_Vec tangent(frame.tangent.x, frame.tangent.y, frame.tangent.z);
			gp_Vec v = tangent.Crossed(u);
			gp_Pnt origin = point(frame.origin);
			std::vector<std::pair<double, double>> samples;
			for (BRepTools_WireExplorer explorer(wire); explorer.More(); explorer.Next())
			{
				BRepAdaptor_Curve curve(explorer.Current());
				double first = curve.FirstParameter();
				double last = curve.LastParameter();
				int steps = curve.GetType() == GeomAbs_Line ? 1 : 12;
				for (int index = 0; index < steps; ++index)
				{
					double parameter = first + (last - first) * static_cast<double>(index) / steps;
					gp_Vec relative(origin, curve.Value(parameter));
				samples.emplace_back(relative.Dot(u), relative.Dot(v));
				}
			}
			if (samples.size() < 3) return wire;
			double signed_area = 0;
			for (size_t index = 0; index < samples.size(); ++index)
			{
				const auto& current = samples[index];
				const auto& next = samples[(index + 1) % samples.size()];
				signed_area += current.first * next.second - next.first * current.second;
			}
			return signed_area < 0 ? TopoDS::Wire(wire.Reversed()) : wire;
		};

		auto outward_offset = [&](const TopoDS_Wire& inner, double wall)
		{
			if (!(wall > 0) || !std::isfinite(wall))
			{
				throw std::invalid_argument("Profile wall thickness must be positive and finite.");
			}

			double inner_area = wire_area(inner);
			TopoDS_Wire best;
			double best_area = inner_area;
			for (double sign : { 1.0, -1.0 })
			{
				BRepOffsetAPI_MakeOffset offset(inner, GeomAbs_Arc, false);
				offset.Perform(sign * wall);
				if (!offset.IsDone()) continue;
				std::vector<TopoDS_Wire> candidates;
				for (TopExp_Explorer explorer(offset.Shape(), TopAbs_WIRE); explorer.More(); explorer.Next())
				{
					candidates.push_back(TopoDS::Wire(explorer.Current()));
				}
				if (candidates.size() != 1) continue;
				double area = wire_area(candidates[0]);
				if (area > best_area)
				{
					best_area = area;
					best = candidates[0];
				}
			}
			if (best.IsNull())
			{
				throw std::runtime_error("The mate profile could not produce one valid outward wall offset.");
			}
			return best;
		};

		auto mate_wire = [&](const fgcad_runner_profile& profile, const fgcad_frame& frame)
		{
			std::string mate_id_value(profile.mate_id);
			auto selector = document->selectors.find(mate_id_value);
			if (selector == document->selectors.end())
			{
				throw std::out_of_range("The runner's exact mate-profile selector was not found.");
			}
			part_record& part = find_part(*document, selector->second.part_id);
			auto topology = std::find_if(part.topology.begin(), part.topology.end(), [&](const topology_record& item)
			{
				return item.info.id == selector->second.topology_id;
			});
			if (topology == part.topology.end())
			{
				throw std::out_of_range("The exact mate-profile topology was not found.");
			}

			TopoDS_Wire result;
			if (topology->shape.ShapeType() == TopAbs_WIRE)
			{
				result = TopoDS::Wire(topology->shape);
			}
			else if (topology->shape.ShapeType() == TopAbs_EDGE)
			{
				BRepBuilderAPI_MakeWire builder(TopoDS::Edge(topology->shape));
				result = builder.Wire();
			}
			else if (topology->shape.ShapeType() == TopAbs_FACE)
			{
				double nearest = std::numeric_limits<double>::infinity();
				gp_Pnt target = point(frame.origin).Transformed(part.placement.Inverted());
				for (TopExp_Explorer explorer(topology->shape, TopAbs_EDGE); explorer.More(); explorer.Next())
				{
					TopoDS_Edge edge = TopoDS::Edge(explorer.Current());
					BRepAdaptor_Curve curve(edge);
					if (curve.GetType() != GeomAbs_Circle) continue;
					double distance = target.SquareDistance(curve.Circle().Location());
					if (distance < nearest)
					{
						nearest = distance;
						result = BRepBuilderAPI_MakeWire(edge).Wire();
					}
				}
			}
			if (result.IsNull())
			{
				throw std::runtime_error("The selected mate topology has no usable closed profile wire.");
			}
			fgcad_frame source_frame{};
			source_frame.origin = topology->info.center;
			source_frame.tangent = topology->info.axis;
			if (topology->info.kind == FGCAD_TOPOLOGY_CIRCULAR_EDGE)
			{
				source_frame.normal = direction(
					BRepAdaptor_Curve(TopoDS::Edge(topology->shape)).Circle().XAxis().Direction());
			}
			else
			{
				gp_Ax2 axes(
					point(source_frame.origin),
					unit(source_frame.tangent)
				);
				source_frame.normal = direction(axes.XDirection());
			}
			gp_Pnt source_origin = point(source_frame.origin).Transformed(part.placement);
			gp_Dir source_tangent = unit(source_frame.tangent).Transformed(part.placement);
			gp_Dir source_normal = unit(source_frame.normal).Transformed(part.placement);
			gp_Ax3 from(source_origin, source_tangent, source_normal);
			gp_Ax3 to(point(frame.origin), unit(frame.tangent), unit(frame.normal));
			gp_Trsf displacement;
			displacement.SetDisplacement(from, to);
			TopoDS_Shape placed_wire = result.Moved(TopLoc_Location(part.placement));
			return TopoDS::Wire(placed_wire.Moved(TopLoc_Location(displacement)));
		};

		auto profile_at = [&](const fgcad_runner_profile& profile, const fgcad_frame& frame)
		{
			profile_wires result;
			if (profile.kind == FGCAD_PROFILE_CIRCULAR)
			{
				double outer_radius = profile.outer_diameter * 0.5;
				double inner_radius = outer_radius - profile.wall_thickness;
				if (!(outer_radius > 0) || !(profile.wall_thickness > 0) || !(inner_radius > 0))
				{
					throw std::invalid_argument("The circular pipe profile is invalid.");
				}
				gp_Ax2 axes = frame_axes(frame);
				result.outer = BRepBuilderAPI_MakeWire(BRepBuilderAPI_MakeEdge(gp_Circ(axes, outer_radius))).Wire();
				result.inner = BRepBuilderAPI_MakeWire(BRepBuilderAPI_MakeEdge(gp_Circ(axes, inner_radius))).Wire();
			}
			else if (profile.kind == FGCAD_PROFILE_MATE)
			{
				result.inner = mate_wire(profile, frame);
				result.outer = outward_offset(result.inner, profile.wall_thickness);
			}
			else
			{
				throw std::invalid_argument("Unknown runner profile kind.");
			}
			result.inner = normalize_wire(result.inner, frame);
			result.outer = normalize_wire(result.outer, frame);
			return result;
		};

		auto annular_face = [](const profile_wires& profile)
		{
			// OCCT's generated FirstShape/LastShape wires preserve the surface's
			// local orientation, which is not guaranteed to match the normalized
			// input profile (notably for a reverse-facing mate).  Select the hole
			// orientation by validating the planar face instead of assuming that
			// the inner wire must always be reversed.
			for (bool reverse_inner : { false, true })
			{
				BRepBuilderAPI_MakeFace builder(profile.outer);
				TopoDS_Wire inner = reverse_inner
					? TopoDS::Wire(profile.inner.Reversed())
					: profile.inner;
				builder.Add(inner);
				if (builder.IsDone()
					&& BRepCheck_Analyzer(builder.Face(), true).IsValid())
				{
					return builder.Face();
				}
			}
			throw std::runtime_error("A hollow runner profile face could not be built.");
		};

		auto feature_edge = [](const fgcad_runner_feature& feature) -> TopoDS_Edge
		{
			if (feature.kind == FGCAD_FEATURE_STRAIGHT)
			{
				return BRepBuilderAPI_MakeEdge(point(feature.entry_frame.origin), point(feature.exit_frame.origin)).Edge();
			}
			if (feature.kind == FGCAD_FEATURE_BEND)
			{
				gp_Pnt start = point(feature.entry_frame.origin);
				gp_Pnt center = point(feature.center);
				gp_Vec radius(center, start);
				gp_Dir axis = gp_Dir(radius).Crossed(unit(feature.entry_frame.tangent));
				gp_Trsf half_rotation;
				half_rotation.SetRotation(gp_Ax1(center, axis), feature.sweep_radians * 0.5);
				gp_Pnt middle = start.Transformed(half_rotation);
				Handle(Geom_TrimmedCurve) arc = GC_MakeArcOfCircle(
					start, middle, point(feature.exit_frame.origin));
				return BRepBuilderAPI_MakeEdge(arc).Edge();
			}
			if (feature.kind == FGCAD_FEATURE_CUBIC_BEZIER)
			{
				return BRepBuilderAPI_MakeEdge(make_bezier(
					feature.entry_frame,
					feature.control1,
					feature.control2,
					feature.exit_frame.origin
				)).Edge();
			}
			throw std::invalid_argument("A profile transition does not have a sweep edge.");
		};

		using generated_section = runner_section_record;

		auto append_faces = [](const TopoDS_Shape& shape, std::vector<TopoDS_Face>& faces)
		{
			if (shape.ShapeType() == TopAbs_FACE)
			{
				faces.push_back(TopoDS::Face(shape));
				return;
			}
			for (TopExp_Explorer explorer(shape, TopAbs_FACE); explorer.More(); explorer.Next())
			{
				faces.push_back(TopoDS::Face(explorer.Current()));
			}
		};

		auto same_profile = [](const fgcad_runner_profile& left, const fgcad_runner_profile& right)
		{
			return left.kind == right.kind
				&& std::strcmp(left.mate_id, right.mate_id) == 0
				&& left.outer_diameter == right.outer_diameter
				&& left.wall_thickness == right.wall_thickness;
		};
		auto section_key = [&](size_t first, size_t last)
		{
			std::ostringstream key;
			key << "abi=8;builder=runner-sew-2.collector-sew-3.collector-branch-solver-1.transactional-publish-1;occt=8.0.0;"
				<< "sewing=2;sourceRevision="
				<< document->source_geometry_revision << ';'
				<< std::hexfloat << (last - first) << '|';
			auto append_point = [&](const fgcad_point3& value)
			{
				key << value.x << ',' << value.y << ',' << value.z << ';';
			};
			auto append_frame = [&](const fgcad_frame& value)
			{
				append_point(value.origin);
				append_point(value.tangent);
				append_point(value.normal);
			};
			auto append_profile = [&](const fgcad_runner_profile& value)
			{
				key << static_cast<int>(value.kind) << ':';
				key.write(value.mate_id, sizeof(value.mate_id));
				key << ':' << value.outer_diameter << ':' << value.wall_thickness
					<< ':' << value.equivalent_radius << ';';
			};
			for (size_t index = first; index < last; ++index)
			{
				const fgcad_runner_feature& feature = features[index];
				key << static_cast<int>(feature.kind) << ':';
				key.write(feature.source_node_id, sizeof(feature.source_node_id));
				key << ';';
				append_frame(feature.entry_frame);
				append_frame(feature.exit_frame);
				append_profile(feature.input_profile);
				append_profile(feature.output_profile);
				append_point(feature.center);
				append_point(feature.control1);
				append_point(feature.control2);
				key << feature.length << ':' << feature.radius << ':'
					<< feature.sweep_radians << ':' << feature.rotation_radians << '|';
			}
			return key.str();
		};

		auto require_wire = [](const TopoDS_Shape& shape, const char* description)
		{
			if (shape.ShapeType() == TopAbs_WIRE)
			{
				return TopoDS::Wire(shape);
			}
			if (shape.ShapeType() == TopAbs_EDGE)
			{
				BRepBuilderAPI_MakeWire builder(TopoDS::Edge(shape));
				if (builder.IsDone())
				{
					return builder.Wire();
				}
			}
			throw std::runtime_error(std::string(description) + " is not a usable profile wire.");
		};

		auto surface_boundary_at = [](const TopoDS_Shape& surface,
			const fgcad_frame& frame,
			const char* description)
		{
			ShapeAnalysis_FreeBounds free_bounds(surface, false, true, false);
			TopoDS_Wire selected;
			double selected_gap = std::numeric_limits<double>::infinity();
			gp_Pnt origin = point(frame.origin);
			gp_Vec tangent(frame.tangent.x, frame.tangent.y, frame.tangent.z);
			tangent.Normalize();
			for (TopExp_Explorer explorer(
				free_bounds.GetClosedWires(),
				TopAbs_WIRE);
				explorer.More();
				explorer.Next())
			{
				TopoDS_Wire wire = TopoDS::Wire(explorer.Current());
				double maximum_plane_gap = 0;
				bool sampled = false;
				for (BRepTools_WireExplorer wire_explorer(wire);
					wire_explorer.More();
					wire_explorer.Next())
				{
					BRepAdaptor_Curve curve(wire_explorer.Current());
					double first_parameter = curve.FirstParameter();
					double last_parameter = curve.LastParameter();
					for (double parameter : {
						first_parameter,
						0.5 * (first_parameter + last_parameter),
						last_parameter })
					{
						gp_Vec relative(origin, curve.Value(parameter));
						maximum_plane_gap = std::max(
							maximum_plane_gap,
							std::abs(relative.Dot(tangent)));
						sampled = true;
					}
				}
				if (sampled && maximum_plane_gap < selected_gap)
				{
					selected = wire;
					selected_gap = maximum_plane_gap;
				}
			}
			double allowed_gap = std::max(Precision::Confusion() * 10.0, 1.0e-6);
			if (selected.IsNull() || selected_gap > allowed_gap)
			{
				throw std::runtime_error(
					std::string(description)
					+ " could not be recovered from the generated surface; measuredGap="
					+ std::to_string(selected_gap)
					+ ".");
			}
			return selected;
		};

		auto make_surface_compound = [](const TopoDS_Shape& outer, const TopoDS_Shape& inner)
		{
			BRep_Builder builder;
			TopoDS_Compound compound;
			builder.MakeCompound(compound);
			for (TopExp_Explorer explorer(outer, TopAbs_FACE); explorer.More(); explorer.Next())
			{
				builder.Add(compound, explorer.Current());
			}
			for (TopExp_Explorer explorer(inner, TopAbs_FACE); explorer.More(); explorer.Next())
			{
				builder.Add(compound, explorer.Current().Reversed());
			}
			return TopoDS_Shape(compound);
		};

		auto make_sweep = [&](size_t first, size_t last, const profile_wires& entry_boundary)
		{
			BRepBuilderAPI_MakeWire wire;
			std::vector<TopoDS_Edge> edges;
			edges.reserve(last - first);
			const fgcad_runner_profile& profile = features[first].input_profile;
			bool contains_bezier = false;

			for (size_t index = first; index < last; ++index)
			{
				const fgcad_runner_feature& feature = features[index];
				if (feature.kind == FGCAD_FEATURE_LOFT_TRANSITION
					|| feature.kind == FGCAD_FEATURE_CLOCKING_TRANSITION
					|| !same_profile(profile, feature.input_profile)
					|| !same_profile(profile, feature.output_profile))
				{
					throw std::invalid_argument("A constant-profile sweep group contains incompatible features.");
				}
				contains_bezier = contains_bezier || feature.kind == FGCAD_FEATURE_CUBIC_BEZIER;
				edges.push_back(feature_edge(feature));
				wire.Add(edges.back());
			}

			if (!wire.IsDone()) throw std::runtime_error("The grouped runner spine could not be built.");
			auto build_pipe = [&](const TopoDS_Wire& profile_wire)
			{
				return contains_bezier
					? BRepOffsetAPI_MakePipe(
						wire.Wire(),
						profile_wire,
						GeomFill_IsDiscreteTrihedron,
						true)
					: BRepOffsetAPI_MakePipe(wire.Wire(), profile_wire);
			};
			BRepOffsetAPI_MakePipe outer_pipe = build_pipe(entry_boundary.outer);
			BRepOffsetAPI_MakePipe inner_pipe = build_pipe(entry_boundary.inner);
			if (!outer_pipe.IsDone() || !inner_pipe.IsDone())
			{
				throw std::runtime_error("Open CASCADE could not sweep the runner wall surfaces.");
			}
			trace("groupedSweep");

			generated_section section;
			section.shape = make_surface_compound(outer_pipe.Shape(), inner_pipe.Shape());
			section.entry_boundary = {
				surface_boundary_at(
					inner_pipe.Shape(),
					features[first].entry_frame,
					"The sweep's inner entry boundary"),
				surface_boundary_at(
					outer_pipe.Shape(),
					features[first].entry_frame,
					"The sweep's outer entry boundary")
			};
			section.exit_boundary = {
				surface_boundary_at(
					inner_pipe.Shape(),
					features[last - 1].exit_frame,
					"The sweep's inner exit boundary"),
				surface_boundary_at(
					outer_pipe.Shape(),
					features[last - 1].exit_frame,
					"The sweep's outer exit boundary")
			};
			struct spine_curve
			{
				Handle(Geom_Curve) curve;
				double first{};
				double last{};
			};
			std::vector<spine_curve> spine_curves;
			spine_curves.reserve(edges.size());
			for (const TopoDS_Edge& edge : edges)
			{
				spine_curve value;
				value.curve = BRep_Tool::Curve(edge, value.first, value.last);
				spine_curves.push_back(std::move(value));
			}
			for (size_t index = first; index < last; ++index)
			{
				runner_source source;
				source.id = features[index].source_node_id;
				source.feature = features[index];
				auto append_pipe_history = [&](BRepOffsetAPI_MakePipe& pipe)
				{
					const TopoDS_Edge& edge = edges[index - first];
					const NCollection_List<TopoDS_Shape>& generated = pipe.Generated(edge);
					for (NCollection_List<TopoDS_Shape>::Iterator iterator(generated);
						iterator.More();
						iterator.Next())
					{
						append_faces(iterator.Value(), source.faces);
					}
					const NCollection_List<TopoDS_Shape>& modified = pipe.Modified(edge);
					for (NCollection_List<TopoDS_Shape>::Iterator iterator(modified);
						iterator.More();
						iterator.Next())
					{
						append_faces(iterator.Value(), source.faces);
					}
				};
				append_pipe_history(outer_pipe);
				append_pipe_history(inner_pipe);
				section.sources.push_back(std::move(source));
			}
			std::vector<TopoDS_Face> sweep_faces = shape_faces(section.shape);
			auto face_is_claimed = [&](const TopoDS_Face& face)
			{
				return std::any_of(
					section.sources.begin(),
					section.sources.end(),
					[&](const runner_source& source)
					{
						return std::any_of(
							source.faces.begin(),
							source.faces.end(),
							[&](const TopoDS_Face& source_face)
							{
								return source_face.IsSame(face);
							});
					});
			};
			auto distance_to_spine_edge = [&](const gp_Pnt& value, size_t edge_index)
			{
				const spine_curve& spine = spine_curves[edge_index];
				if (spine.curve.IsNull())
				{
					return std::numeric_limits<double>::infinity();
				}
				GeomAPI_ProjectPointOnCurve projection(
					value,
					spine.curve,
					spine.first,
					spine.last);
				return projection.NbPoints() > 0
					? projection.LowerDistance() * projection.LowerDistance()
					: std::numeric_limits<double>::infinity();
			};
			auto append_face = [](runner_source& source, const TopoDS_Face& face)
			{
				if (std::none_of(
					source.faces.begin(),
					source.faces.end(),
					[&](const TopoDS_Face& existing)
					{
						return existing.IsSame(face);
					}))
				{
					source.faces.push_back(face);
				}
			};

			std::vector<gp_Pnt> sweep_face_centers;
			sweep_face_centers.reserve(sweep_faces.size());
			for (const TopoDS_Face& face : sweep_faces)
			{
				sweep_face_centers.push_back(face_representative_point(face));
			}

			for (size_t face_index = 0; face_index < sweep_faces.size(); ++face_index)
			{
				const TopoDS_Face& face = sweep_faces[face_index];
				if (face_is_claimed(face))
				{
					continue;
				}
				const gp_Pnt& center = sweep_face_centers[face_index];
				std::vector<double> distances;
				distances.reserve(edges.size());
				double best = std::numeric_limits<double>::infinity();
				for (size_t edge_index = 0; edge_index < edges.size(); ++edge_index)
				{
					double distance = distance_to_spine_edge(center, edge_index);
					distances.push_back(distance);
					best = std::min(best, distance);
				}
				double tie_tolerance = std::max(
					Precision::SquareConfusion(),
					best * 1.0e-9);
				for (size_t source_index = 0; source_index < section.sources.size(); ++source_index)
				{
					if (distances[source_index] <= best + tie_tolerance)
					{
						append_face(section.sources[source_index], face);
					}
				}
			}

			for (size_t source_index = 0; source_index < section.sources.size(); ++source_index)
			{
				if (!section.sources[source_index].faces.empty())
				{
					continue;
				}
				double best = std::numeric_limits<double>::infinity();
				const TopoDS_Face* nearest = nullptr;
				for (size_t face_index = 0; face_index < sweep_faces.size(); ++face_index)
				{
					double distance = distance_to_spine_edge(
						sweep_face_centers[face_index],
						source_index);
					if (distance < best)
					{
						best = distance;
						nearest = &sweep_faces[face_index];
					}
				}
				if (nearest != nullptr)
				{
					append_face(section.sources[source_index], *nearest);
				}
			}
			trace("sweepProvenance");
			return section;
		};

		auto make_loft = [&](const fgcad_runner_feature& feature,
			const profile_wires& entry_boundary)
		{
			profile_wires output = profile_at(feature.output_profile, feature.exit_frame);
			trace("loftProfiles");
			BRepOffsetAPI_ThruSections outer(false, false);
			outer.CheckCompatibility(true);
			outer.AddWire(entry_boundary.outer);
			outer.AddWire(output.outer);
			outer.Build();
			BRepOffsetAPI_ThruSections inner(false, false);
			inner.CheckCompatibility(true);
			inner.AddWire(entry_boundary.inner);
			inner.AddWire(output.inner);
			inner.Build();
			if (!outer.IsDone() || !inner.IsDone())
			{
				throw std::runtime_error(
					"Open CASCADE could not loft the profile-transition surfaces.");
			}
			trace("loftSurfaces");
			generated_section section;
			section.shape = make_surface_compound(outer.Shape(), inner.Shape());
			trace("loftCompound");
			TopoDS_Wire inner_entry = surface_boundary_at(
				inner.Shape(),
				feature.entry_frame,
				"The loft's inner entry boundary");
			trace("loftInnerEntry");
			TopoDS_Wire outer_entry = surface_boundary_at(
				outer.Shape(),
				feature.entry_frame,
				"The loft's outer entry boundary");
			trace("loftOuterEntry");
			TopoDS_Wire inner_exit = surface_boundary_at(
				inner.Shape(),
				feature.exit_frame,
				"The loft's inner exit boundary");
			trace("loftInnerExit");
			TopoDS_Wire outer_exit = surface_boundary_at(
				outer.Shape(),
				feature.exit_frame,
				"The loft's outer exit boundary");
			trace("loftOuterExit");
			section.entry_boundary = {
				inner_entry,
				outer_entry
			};
			section.exit_boundary = {
				inner_exit,
				outer_exit
			};
			std::vector<TopoDS_Face> loft_faces = shape_faces(section.shape);
			trace("loftFaces");
			section.sources.push_back({
				feature.source_node_id,
				feature,
				std::move(loft_faces)
			});
			return section;
		};

		runner_record replacement;
		replacement.id = require_text(runner_id, "runner_id");
		replacement.name = require_text(runner_name, "runner_name");
		replacement.geometry_key = section_key(0, feature_count);
		if (!document->staged_runner_id.empty()
			&& document->staged_runner_id != replacement.id)
		{
			throw std::invalid_argument(
				"The runner build does not match the active native publication transaction.");
		}
		auto store_replacement = [&](runner_record&& value)
		{
			if (!document->staged_collector_id.empty())
			{
				document->staged_runners[value.id] = std::move(value);
				return;
			}
			document->runners[value.id] = std::move(value);
			if (!document->staged_runner_id.empty())
			{
				document->staged_runner_published = true;
			}
		};
		fgcad_build_metrics metrics{};
		auto previous_metrics = document->build_metrics.find(replacement.id);
		metrics.revision = previous_metrics == document->build_metrics.end()
			? 1
			: previous_metrics->second.revision + 1;
		auto log_timing = [&]()
		{
			auto trace_end = std::chrono::steady_clock::now();
			metrics.total_microseconds = static_cast<uint64_t>(
				std::chrono::duration_cast<std::chrono::microseconds>(
					trace_end - trace_start).count());
			capture_topology_metrics(replacement.shape, metrics);
			document->build_metrics[replacement.id] = metrics;
			std::ostringstream timing;
			timing << "runner=" << replacement.id
				<< "; features=" << feature_count
				<< "; totalUs="
				<< std::chrono::duration_cast<std::chrono::microseconds>(
					trace_end - trace_start).count();
			for (const auto& stage : stage_timings)
			{
				timing << "; " << stage.first << "Us=" << stage.second;
			}
			append_native_log("runner-build-perf", timing.str());
		};
		const runner_record* previous_cache = nullptr;
		auto previous_cache_iterator = document->runner_build_cache.find(replacement.id);
		if (previous_cache_iterator != document->runner_build_cache.end())
		{
			previous_cache = &previous_cache_iterator->second;
		}
		if (previous_cache != nullptr
			&& previous_cache->geometry_key == replacement.geometry_key)
		{
			replacement = *previous_cache;
			replacement.name = require_text(runner_name, "runner_name");
			metrics.cache_flags |= FGCAD_CACHE_RUNNER_SOLID;
			trace("runnerCacheHit");
			document->runner_build_cache[replacement.id] = replacement;
			log_timing();
			store_replacement(std::move(replacement));
			return FGCAD_STATUS_OK;
		}
		size_t section_index = 0;
		auto reuse_section = [&](const std::string& key, generated_section& section)
		{
			if (previous_cache == nullptr
				|| section_index >= previous_cache->sections.size()
				|| previous_cache->sections[section_index].key != key)
			{
				return false;
			}
			section = previous_cache->sections[section_index];
			metrics.cache_flags |= FGCAD_CACHE_RUNNER_SECTION;
			trace("sectionCacheHit");
			return true;
		};
		profile_wires current_boundary = profile_at(
			features[0].input_profile,
			features[0].entry_frame);
		replacement.start_boundary = current_boundary;
		auto append_section = [&](generated_section&& section)
		{
			if (section.shape.IsNull())
			{
				throw std::runtime_error(
					"A generated runner wall-surface section failed topological validation.");
			}
			if (!section.validated)
			{
				if (!BRepCheck_Analyzer(section.shape, false).IsValid())
				{
					throw std::runtime_error(
						"A generated runner wall-surface section failed topological validation.");
				}
				section.validated = true;
				++metrics.validation_count;
				trace("sectionValidation");
			}
			if (replacement.sections.empty())
			{
				// The cap must reuse the generated surface's actual FirstShape
				// boundary.  The input profile wire may be geometrically equal but
				// have different TShapes after OCCT loft compatibility processing.
				replacement.start_boundary = section.entry_boundary;
			}
			current_boundary = section.exit_boundary;
			replacement.sections.push_back(section);
			for (runner_source& source : section.sources)
			{
				replacement.sources.push_back(std::move(source));
			}
		};

		for (size_t index = 0; index < feature_count;)
		{
			if (features[index].kind == FGCAD_FEATURE_LOFT_TRANSITION
				|| features[index].kind == FGCAD_FEATURE_CLOCKING_TRANSITION)
			{
				std::string key = section_key(index, index + 1);
				generated_section section;
				if (!reuse_section(key, section))
				{
					auto loft_start = std::chrono::steady_clock::now();
					section.key = key;
					section = make_loft(features[index], current_boundary);
					section.key = key;
					metrics.loft_microseconds += static_cast<uint64_t>(
						std::chrono::duration_cast<std::chrono::microseconds>(
							std::chrono::steady_clock::now() - loft_start).count());
					++metrics.loft_count;
				}
				append_section(std::move(section));
				++section_index;
				++index;
				continue;
			}

			size_t end = index + 1;
			while (end < feature_count
				&& features[end].kind != FGCAD_FEATURE_LOFT_TRANSITION
				&& features[end].kind != FGCAD_FEATURE_CLOCKING_TRANSITION
				&& same_profile(features[index].input_profile, features[end].input_profile))
			{
				++end;
			}
			std::string key = section_key(index, end);
			generated_section section;
			if (!reuse_section(key, section))
			{
				auto sweep_start = std::chrono::steady_clock::now();
				section = make_sweep(index, end, current_boundary);
				section.key = key;
				metrics.sweep_microseconds += static_cast<uint64_t>(
					std::chrono::duration_cast<std::chrono::microseconds>(
						std::chrono::steady_clock::now() - sweep_start).count());
				++metrics.sweep_count;
			}
			append_section(std::move(section));
			++section_index;
			index = end;
		}

		if (replacement.sections.empty())
		{
			throw std::runtime_error("The runner produced no wall-surface sections.");
		}
		replacement.end_boundary = current_boundary;
		TopoDS_Face start_cap = TopoDS::Face(
			annular_face(replacement.start_boundary).Reversed());
		TopoDS_Face end_cap = annular_face(replacement.end_boundary);

		double maximum_source_tolerance = Precision::Confusion();
		auto include_tolerances = [&](const TopoDS_Shape& shape)
		{
			for (TopExp_Explorer explorer(shape, TopAbs_EDGE); explorer.More(); explorer.Next())
			{
				maximum_source_tolerance = std::max(
					maximum_source_tolerance,
					BRep_Tool::Tolerance(TopoDS::Edge(explorer.Current())));
			}
			for (TopExp_Explorer explorer(shape, TopAbs_VERTEX); explorer.More(); explorer.Next())
			{
				maximum_source_tolerance = std::max(
					maximum_source_tolerance,
					BRep_Tool::Tolerance(TopoDS::Vertex(explorer.Current())));
			}
		};
		Bnd_Box runner_bounds;
		for (const generated_section& section : replacement.sections)
		{
			include_tolerances(section.shape);
			BRepBndLib::Add(section.shape, runner_bounds, false);
		}
		include_tolerances(start_cap);
		include_tolerances(end_cap);
		BRepBndLib::Add(start_cap, runner_bounds, false);
		BRepBndLib::Add(end_cap, runner_bounds, false);
		double local_scale = 1.0;
		if (!runner_bounds.IsVoid())
		{
			double minimum_x = 0;
			double minimum_y = 0;
			double minimum_z = 0;
			double maximum_x = 0;
			double maximum_y = 0;
			double maximum_z = 0;
			runner_bounds.Get(
				minimum_x,
				minimum_y,
				minimum_z,
				maximum_x,
				maximum_y,
				maximum_z);
			local_scale = std::max(
				1.0,
				gp_Pnt(minimum_x, minimum_y, minimum_z).Distance(
					gp_Pnt(maximum_x, maximum_y, maximum_z)));
		}
		double minimum_wall = std::numeric_limits<double>::infinity();
		for (size_t index = 0; index < feature_count; ++index)
		{
			minimum_wall = std::min({
				minimum_wall,
				features[index].input_profile.wall_thickness,
				features[index].output_profile.wall_thickness
			});
		}
		if (!std::isfinite(minimum_wall) || !(minimum_wall > 0))
		{
			throw std::runtime_error("The runner wall thickness is unavailable for sewing validation.");
		}
		double selected_tolerance = std::max({
			Precision::Confusion(),
			maximum_source_tolerance * 2.0,
			local_scale * 1.0e-10
		});
		metrics.selected_tolerance = selected_tolerance;
		double tolerance_ceiling = std::min(
			minimum_wall * 0.05,
			std::max(1.0e-4, local_scale * 2.0e-5));
		if (selected_tolerance > tolerance_ceiling)
		{
			throw std::runtime_error(
				"The runner boundaries require a sewing tolerance that exceeds the wall-scale safety limit; "
				"selectedTolerance=" + std::to_string(selected_tolerance)
					+ "; sourceTolerance=" + std::to_string(maximum_source_tolerance)
					+ "; localScale=" + std::to_string(local_scale)
					+ "; minimumWall=" + std::to_string(minimum_wall)
					+ "; ceiling=" + std::to_string(tolerance_ceiling) + ".");
		}

		// Adjacent sections are constructed from the preceding section's actual
		// boundary wires.  Cutting those already-shared edges makes OCCT register
		// the same modification twice (and asserts in debug builds), so runner
		// assembly deliberately uses sewing without edge cutting.
		auto sewing_start = std::chrono::steady_clock::now();
		BRepBuilderAPI_Sewing sewing(selected_tolerance, true, true, false, false);
		for (const generated_section& section : replacement.sections)
		{
			sewing.Add(section.shape);
		}
		sewing.Add(start_cap);
		sewing.Add(end_cap);
		sewing.Perform();
		metrics.sewing_microseconds += static_cast<uint64_t>(
			std::chrono::duration_cast<std::chrono::microseconds>(
				std::chrono::steady_clock::now() - sewing_start).count());
		++metrics.sew_count;
		trace("runnerSew");
		if (sewing.NbFreeEdges() != 0 || sewing.NbMultipleEdges() != 0)
		{
			throw std::runtime_error(
				"Runner sewing did not produce a manifold closed shell: freeEdges="
				+ std::to_string(sewing.NbFreeEdges())
				+ "; multipleEdges="
				+ std::to_string(sewing.NbMultipleEdges())
				+ ".");
		}
		TopoDS_Shape sewed = sewing.SewedShape();
		if (sewed.IsNull())
		{
			throw std::runtime_error("Runner sewing produced no shape.");
		}
		TopoDS_Shell shell;
		size_t shell_count = 0;
		if (sewed.ShapeType() == TopAbs_SHELL)
		{
			shell = TopoDS::Shell(sewed);
			shell_count = 1;
		}
		else
		{
			for (TopExp_Explorer explorer(sewed, TopAbs_SHELL); explorer.More(); explorer.Next())
			{
				shell = TopoDS::Shell(explorer.Current());
				++shell_count;
			}
		}
		if (shell_count != 1 || shell.IsNull() || !BRep_Tool::IsClosed(shell))
		{
			throw std::runtime_error(
				"Runner sewing must produce exactly one closed shell; shells="
				+ std::to_string(shell_count)
				+ ".");
		}
		BRepBuilderAPI_MakeSolid solid_builder(shell);
		if (!solid_builder.IsDone())
		{
			throw std::runtime_error("The sewn runner shell could not be converted into a solid.");
		}
		TopoDS_Solid oriented_solid = solid_builder.Solid();
		if (!BRepLib::OrientClosedSolid(oriented_solid))
		{
			throw std::runtime_error("The sewn runner shell could not be consistently oriented.");
		}
		TopoDS_Shape result = oriented_solid;
		bool geometric_controls = document->staged_collector_id.empty();
		if (geometric_controls)
		{
			BRepClass3d_SolidClassifier infinite_classifier(result);
			infinite_classifier.PerformInfinitePoint(selected_tolerance);
			++metrics.classification_count;
			if (infinite_classifier.State() == TopAbs_IN)
			{
				result.Reverse();
			}
		}
		auto validation_start = std::chrono::steady_clock::now();
		// Collector-member runners are still staged at this point.  Their complete
		// assembled system receives the authoritative geometric validation before
		// publication, so doing the same expensive curve-on-surface checks here is
		// redundant.  Standalone runners retain the full geometric check.
		if (geometric_controls)
		{
			BRepCheck_Analyzer final_analyzer(result, true, true);
			if (!final_analyzer.IsValid())
			{
				std::ostringstream statuses;
				for (TopAbs_ShapeEnum type : {
					TopAbs_SOLID,
					TopAbs_SHELL,
					TopAbs_FACE,
					TopAbs_WIRE,
					TopAbs_EDGE })
				{
					for (TopExp_Explorer explorer(result, type);
						explorer.More();
						explorer.Next())
					{
						Handle(BRepCheck_Result) check = final_analyzer.Result(
							explorer.Current());
						if (check.IsNull())
						{
							continue;
						}
						for (BRepCheck_Status status : check->Status())
						{
							if (status != BRepCheck_NoError)
							{
								statuses << static_cast<int>(type) << ':'
									<< static_cast<int>(status) << ',';
							}
						}
					}
				}
				throw std::runtime_error(
					"The complete sewn runner failed exact B-rep validation; statuses="
						+ statuses.str() + ".");
			}
			GProp_GProps result_properties;
			BRepGProp::VolumeProperties(result, result_properties);
			if (!(std::abs(result_properties.Mass()) > Precision::Confusion()))
			{
				throw std::runtime_error(
					"The complete sewn runner has no positive wall volume.");
			}
		}
		trace(geometric_controls ? "runnerGeometricValidation" : "runnerStagedValidation");
		metrics.validation_microseconds += static_cast<uint64_t>(
			std::chrono::duration_cast<std::chrono::microseconds>(
				std::chrono::steady_clock::now() - validation_start).count());
		++metrics.validation_count;

		auto mapped_face = [&](const TopoDS_Face& face)
		{
			if (!sewing.IsModifiedSubShape(face))
			{
				return face;
			}
			TopoDS_Shape modified = sewing.ModifiedSubShape(face);
			return modified.ShapeType() == TopAbs_FACE
				? TopoDS::Face(modified)
				: face;
		};
		replacement.start_cap = mapped_face(start_cap);
		replacement.end_cap = mapped_face(end_cap);
		trace("capHistory");
		if (geometric_controls)
		{
			for (runner_source& source : replacement.sources)
			{
				for (TopoDS_Face& face : source.faces)
				{
					face = mapped_face(face);
				}
			}
		}
		trace("sourceHistory");
		if (!replacement.sources.empty())
		{
			replacement.sources.front().faces.push_back(replacement.start_cap);
			replacement.sources.back().faces.push_back(replacement.end_cap);
		}
		remap_sources_to_result_surfaces(result, replacement.sources);
		trace("resultSurfaceRemap");
		trace("finalValidation");

		replacement.shape = result;
		for (runner_source& source : replacement.sources)
		{
			source.kind = FGCAD_SOURCE_RUNNER_NODE;
			source.owner_id = replacement.id;
		}
		document->runner_build_cache[replacement.id] = replacement;
		log_timing();
		store_replacement(std::move(replacement));
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_begin_runner_build(
	fgcad_document* document,
	const char* runner_id)
{
	return guarded([&]()
	{
		if (document == nullptr) throw std::invalid_argument("The document cannot be null.");
		std::string id = require_text(runner_id, "runner_id");
		if (!document->staged_runner_id.empty()
			|| !document->staged_collector_id.empty())
		{
			throw std::invalid_argument("Another exact publication transaction is already active.");
		}
		document->staged_runner_id = id;
		document->staged_previous_runner = runner_record{};
		document->staged_previous_runner_exists = false;
		document->staged_runner_published = false;
		auto previous = document->runners.find(id);
		if (previous != document->runners.end())
		{
			document->staged_previous_runner = previous->second;
			document->staged_previous_runner_exists = true;
		}
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_commit_runner_build(
	fgcad_document* document,
	const char* runner_id)
{
	return guarded([&]()
	{
		if (document == nullptr) throw std::invalid_argument("The document cannot be null.");
		std::string id = require_text(runner_id, "runner_id");
		if (document->staged_runner_id != id
			|| !document->staged_runner_published)
		{
			throw std::invalid_argument(
				"The runner publication commit does not match a completed active transaction.");
		}
		document->staged_runner_id.clear();
		document->staged_previous_runner = runner_record{};
		document->staged_previous_runner_exists = false;
		document->staged_runner_published = false;
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_abort_runner_build(
	fgcad_document* document,
	const char* runner_id)
{
	return guarded([&]()
	{
		if (document == nullptr) throw std::invalid_argument("The document cannot be null.");
		std::string id = require_text(runner_id, "runner_id");
		if (!document->staged_runner_id.empty()
			&& document->staged_runner_id != id)
		{
			throw std::invalid_argument(
				"The runner publication abort does not match the active transaction.");
		}
		if (document->staged_runner_id == id && document->staged_runner_published)
		{
			if (document->staged_previous_runner_exists)
			{
				document->runners[id] = document->staged_previous_runner;
			}
			else
			{
				document->runners.erase(id);
			}
		}
		document->staged_runner_id.clear();
		document->staged_previous_runner = runner_record{};
		document->staged_previous_runner_exists = false;
		document->staged_runner_published = false;
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_remove_runner(fgcad_document* document, const char* runner_id)
{
	return guarded([&]()
	{
		if (document == nullptr) throw std::invalid_argument("The document cannot be null.");
		std::string id = require_text(runner_id, "runner_id");
		if (document->staged_runner_id == id)
		{
			document->staged_runner_id.clear();
			document->staged_previous_runner = runner_record{};
			document->staged_previous_runner_exists = false;
			document->staged_runner_published = false;
		}
		document->staged_runners.erase(id);
		document->runners.erase(id);
		document->runner_build_cache.erase(id);
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_rename_runner(
	fgcad_document* document,
	const char* runner_id,
	const char* runner_name
)
{
	return guarded([&]()
	{
		if (document == nullptr) throw std::invalid_argument("The document cannot be null.");
		std::string id = require_text(runner_id, "runner_id");
		std::string name = require_text(runner_name, "runner_name");
		auto found = document->runners.find(id);
		if (found != document->runners.end())
		{
			found->second.name = std::move(name);
		}
		return FGCAD_STATUS_OK;
	});
}
