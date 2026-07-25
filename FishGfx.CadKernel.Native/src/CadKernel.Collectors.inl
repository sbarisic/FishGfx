// Versioned C ABI entry points for transactional collector generation and lifecycle operations.

fgcad_status fgcad_document_build_collector_system(
	fgcad_document* document,
	const fgcad_collector_system_spec* system,
	const fgcad_collector_inlet* inlets,
	size_t inlet_count
)
{
	return guarded([&]()
	{
		if (document == nullptr || system == nullptr || inlets == nullptr || inlet_count < 2)
		{
			throw std::invalid_argument(
				"A collector system requires a document, system specification, and at least two inlets.");
		}
		if (!(system->outlet_stub_length > 0) || !(system->merge_length > 0)
			|| !(system->overlap_length > 0) || !(system->branch_end_handle_length > 0))
		{
			throw std::invalid_argument("Collector lengths and handles must be positive.");
		}
		if (system->outlet_profile.kind != FGCAD_PROFILE_CIRCULAR)
		{
			throw std::invalid_argument("The collector outlet profile must be circular.");
		}

		auto radii = [](const fgcad_runner_profile& profile)
		{
			double outer = profile.kind == FGCAD_PROFILE_CIRCULAR
				? profile.outer_diameter * 0.5
				: profile.equivalent_radius + profile.wall_thickness;
			double inner = profile.kind == FGCAD_PROFILE_CIRCULAR
				? outer - profile.wall_thickness
				: profile.equivalent_radius;
			if (!(outer > 0) || !(inner > 0) || !(profile.wall_thickness > 0))
			{
				throw std::invalid_argument("A collector circular profile is invalid.");
			}
			return std::pair<double, double>(outer, inner);
		};
		auto disk = [](const fgcad_frame& frame, double radius)
		{
			gp_Ax2 axes(point(frame.origin), unit(frame.tangent), unit(frame.normal));
			TopoDS_Wire wire = BRepBuilderAPI_MakeWire(
				BRepBuilderAPI_MakeEdge(gp_Circ(axes, radius))).Wire();
			BRepBuilderAPI_MakeFace face(wire, true);
			if (!face.IsDone()) throw std::runtime_error("A collector section disk could not be built.");
			return face.Face();
		};
		struct collector_profile_faces
		{
			TopoDS_Face outer;
			TopoDS_Face inner;
		};
		auto wire_area = [](const TopoDS_Wire& wire)
		{
			BRepBuilderAPI_MakeFace face(wire, true);
			if (!face.IsDone()) return 0.0;
			GProp_GProps properties;
			BRepGProp::SurfaceProperties(face.Face(), properties);
			return std::abs(properties.Mass());
		};
		auto outward_offset = [&](const TopoDS_Wire& inner, double wall)
		{
			double inner_area = wire_area(inner);
			TopoDS_Wire best;
			double best_area = inner_area;
			for (double sign : { 1.0, -1.0 })
			{
				BRepOffsetAPI_MakeOffset offset(inner, GeomAbs_Arc, false);
				offset.Perform(sign * wall);
				if (!offset.IsDone()) continue;
				std::vector<TopoDS_Wire> candidates;
				for (TopExp_Explorer explorer(offset.Shape(), TopAbs_WIRE);
					explorer.More(); explorer.Next())
				{
					candidates.push_back(TopoDS::Wire(explorer.Current()));
				}
				if (candidates.size() != 1) continue;
				double area = wire_area(candidates.front());
				if (area > best_area)
				{
					best_area = area;
					best = candidates.front();
				}
			}
			if (best.IsNull())
			{
				throw std::runtime_error(
					"An arbitrary collector inlet could not produce one enclosing wall offset.");
			}
			return best;
		};
		auto mate_wire_at = [&](const fgcad_runner_profile& profile,
			const fgcad_frame& reference,
			const fgcad_frame& target)
		{
			auto selector = document->selectors.find(std::string(profile.mate_id));
			if (selector == document->selectors.end())
			{
				throw std::out_of_range(
					"The arbitrary collector inlet profile selector was not found.");
			}
			part_record& part = find_part(*document, selector->second.part_id);
			auto topology = std::find_if(
				part.topology.begin(),
				part.topology.end(),
				[&](const topology_record& item)
				{
					return item.info.id == selector->second.topology_id;
				});
			if (topology == part.topology.end())
			{
				throw std::out_of_range(
					"The arbitrary collector inlet profile topology was not found.");
			}
			TopoDS_Wire wire;
			if (topology->shape.ShapeType() == TopAbs_WIRE)
			{
				wire = TopoDS::Wire(topology->shape);
			}
			else if (topology->shape.ShapeType() == TopAbs_EDGE)
			{
				wire = BRepBuilderAPI_MakeWire(TopoDS::Edge(topology->shape)).Wire();
			}
			else if (topology->shape.ShapeType() == TopAbs_FACE)
			{
				for (TopExp_Explorer explorer(topology->shape, TopAbs_WIRE);
					explorer.More(); explorer.Next())
				{
					TopoDS_Wire candidate = TopoDS::Wire(explorer.Current());
					if (wire.IsNull() || wire_area(candidate) > wire_area(wire))
					{
						wire = candidate;
					}
				}
			}
			if (wire.IsNull())
			{
				throw std::runtime_error(
					"The arbitrary collector inlet has no usable exact closed wire.");
			}
			wire = TopoDS::Wire(wire.Moved(TopLoc_Location(part.placement)));
			gp_Ax3 from(
				point(reference.origin),
				unit(reference.tangent),
				unit(reference.normal)
			);
			gp_Ax3 to(
				point(target.origin),
				unit(target.tangent),
				unit(target.normal)
			);
			gp_Trsf displacement;
			displacement.SetDisplacement(from, to);
			return TopoDS::Wire(wire.Moved(TopLoc_Location(displacement)));
		};
		auto profile_faces = [&](const fgcad_collector_inlet& inlet)
		{
			collector_profile_faces result;
			if (inlet.profile.kind == FGCAD_PROFILE_CIRCULAR)
			{
				auto profile_radii = radii(inlet.profile);
				result.outer = disk(inlet.frame, profile_radii.first);
				result.inner = disk(inlet.frame, profile_radii.second);
			}
			else
			{
				TopoDS_Wire inner = mate_wire_at(
					inlet.profile,
					inlet.profile_reference_frame,
					inlet.frame
				);
				TopoDS_Wire outer = outward_offset(inner, inlet.profile.wall_thickness);
				BRepBuilderAPI_MakeFace outer_face(outer, true);
				BRepBuilderAPI_MakeFace inner_face(inner, true);
				if (!outer_face.IsDone() || !inner_face.IsDone()
					|| wire_area(outer) <= wire_area(inner)
					|| !BRepCheck_Analyzer(outer_face.Face(), true).IsValid()
					|| !BRepCheck_Analyzer(inner_face.Face(), true).IsValid())
				{
					throw std::runtime_error(
						"The arbitrary collector inlet profile is invalid or collapsed.");
				}
				result.outer = outer_face.Face();
				result.inner = inner_face.Face();
			}
			return result;
		};
		auto annular_face = [&](const collector_profile_faces& profile)
		{
			TopoDS_Wire outer_wire;
			TopoDS_Wire inner_wire;
			TopExp_Explorer outer_explorer(profile.outer, TopAbs_WIRE);
			if (outer_explorer.More())
			{
				outer_wire = TopoDS::Wire(outer_explorer.Current());
			}
			TopExp_Explorer inner_explorer(profile.inner, TopAbs_WIRE);
			if (inner_explorer.More())
			{
				inner_wire = TopoDS::Wire(inner_explorer.Current());
			}
			if (outer_wire.IsNull() || inner_wire.IsNull())
			{
				throw std::runtime_error(
					"A collector profile did not contain complete inner and outer wires.");
			}
			BRepBuilderAPI_MakeFace builder(outer_wire, true);
			builder.Add(TopoDS::Wire(inner_wire.Reversed()));
			if (!builder.IsDone()
				|| !BRepCheck_Analyzer(builder.Face(), true).IsValid())
			{
				throw std::runtime_error(
					"A collector annular wall profile could not be built.");
			}
			GProp_GProps properties;
			BRepGProp::SurfaceProperties(builder.Face(), properties);
			if (std::abs(properties.Mass()) <= 1.0e-8)
			{
				throw std::runtime_error(
					"A collector annular wall profile has no positive area.");
			}
			return builder.Face();
		};
		auto swept_volume = [&](const fgcad_frame& frame,
			const gp_Pnt& control1,
			const gp_Pnt& control2,
			const gp_Pnt& end,
			const TopoDS_Face& section,
			double interface_overlap,
			const gp_Dir& outlet_tangent,
			double outlet_overlap)
		{
			fgcad_point3 c1 = point(control1);
			fgcad_point3 c2 = point(control2);
			fgcad_point3 p3 = point(end);
			Handle(Geom_BezierCurve) curve = make_bezier(frame, c1, c2, p3);
			TopoDS_Edge edge = BRepBuilderAPI_MakeEdge(curve).Edge();
			gp_Vec lead = -gp_Vec(unit(frame.tangent)) * interface_overlap;
			gp_Pnt lead_start = point(frame.origin).Translated(lead);
			BRepBuilderAPI_MakeWire wire_builder;
			wire_builder.Add(BRepBuilderAPI_MakeEdge(lead_start, point(frame.origin)).Edge());
			wire_builder.Add(edge);
			if (outlet_overlap > 0)
			{
				gp_Pnt overlap_end = end.Translated(
					gp_Vec(outlet_tangent) * outlet_overlap);
				wire_builder.Add(
					BRepBuilderAPI_MakeEdge(end, overlap_end).Edge());
			}
			if (!wire_builder.IsDone())
			{
				throw std::runtime_error("A collector branch lead-in could not be built.");
			}
			TopoDS_Wire wire = wire_builder.Wire();
			gp_Trsf section_translation;
			section_translation.SetTranslation(lead);
			TopoDS_Face lead_section = TopoDS::Face(
				section.Moved(TopLoc_Location(section_translation)));
			BRepOffsetAPI_MakePipe pipe(
				wire,
				lead_section,
				GeomFill_IsDiscreteTrihedron,
				true
			);
			if (!pipe.IsDone() || pipe.Shape().IsNull())
			{
				throw std::runtime_error("Open CASCADE could not sweep a collector branch volume.");
			}
			return pipe.Shape();
		};
		auto straight_wall_volume = [&](const fgcad_frame& frame,
			const TopoDS_Face& section,
			double backward_overlap,
			double forward_overlap)
		{
			gp_Vec lead = -gp_Vec(unit(frame.tangent)) * backward_overlap;
			gp_Pnt start = point(frame.origin).Translated(lead);
			gp_Pnt end = point(frame.origin).Translated(
				gp_Vec(unit(frame.tangent)) * forward_overlap);
			TopoDS_Wire wire = BRepBuilderAPI_MakeWire(
				BRepBuilderAPI_MakeEdge(start, end)).Wire();
			gp_Trsf section_translation;
			section_translation.SetTranslation(lead);
			TopoDS_Face start_section = TopoDS::Face(
				section.Moved(TopLoc_Location(section_translation)));
			BRepOffsetAPI_MakePipe pipe(
				wire,
				start_section,
				GeomFill_IsDiscreteTrihedron,
				true
			);
			size_t pipe_solid_count = 0;
			if (pipe.IsDone() && !pipe.Shape().IsNull())
			{
				for (TopExp_Explorer explorer(pipe.Shape(), TopAbs_SOLID);
					explorer.More();
					explorer.Next())
				{
					++pipe_solid_count;
				}
			}
			if (!pipe.IsDone()
				|| pipe.Shape().IsNull()
				|| pipe_solid_count != 1)
			{
				throw std::runtime_error(
					"Open CASCADE could not sweep a short collector interface bridge.");
			}
			return pipe.Shape();
		};
		auto fuse_all = [](const std::vector<TopoDS_Shape>& values,
			bool glue,
			std::vector<runner_source>* sources)
		{
			if (values.empty()) throw std::invalid_argument("A collector fusion cannot be empty.");
			if (values.size() == 1) return values.front();
			NCollection_List<TopoDS_Shape> arguments;
			NCollection_List<TopoDS_Shape> tools;
			arguments.Append(values.front());
			for (size_t index = 1; index < values.size(); ++index)
			{
				tools.Append(values[index]);
			}
			BRepAlgoAPI_Fuse fuse;
			fuse.SetArguments(arguments);
			fuse.SetTools(tools);
			fuse.SetNonDestructive(true);
			fuse.SetRunParallel(true);
			if (glue) fuse.SetGlue(BOPAlgo_GlueFull);
			fuse.SetFuzzyValue(1.0e-3);
			fuse.Build();
			if (!fuse.IsDone() || fuse.Shape().IsNull())
			{
				throw std::runtime_error(
					"Collector multi-argument fusion failed for "
					+ std::to_string(values.size())
					+ " inputs.");
			}
			if (sources != nullptr)
			{
				apply_boolean_history(fuse, *sources);
				size_t result_solid_count = 0;
				for (TopExp_Explorer explorer(
						fuse.Shape(),
						TopAbs_SOLID);
					explorer.More();
					explorer.Next())
				{
					++result_solid_count;
				}
				append_native_log(
					"collector",
					"Multi-argument wall fusion produced "
						+ std::to_string(result_solid_count)
						+ " solid(s) from "
						+ std::to_string(values.size())
						+ " inputs.");
			}
			return fuse.Shape();
		};
		auto fuse_sequential = [](const std::vector<TopoDS_Shape>& values,
			std::vector<runner_source>* sources)
		{
			if (values.empty())
			{
				throw std::invalid_argument(
					"A sequential collector fusion cannot be empty.");
			}
			TopoDS_Shape result = values.front();
			for (size_t index = 1; index < values.size(); ++index)
			{
				BRepAlgoAPI_Fuse fuse(result, values[index]);
				fuse.SetNonDestructive(true);
				fuse.SetRunParallel(true);
				fuse.SetFuzzyValue(1.0e-3);
				fuse.Build();
				if (!fuse.IsDone() || fuse.Shape().IsNull())
				{
					throw std::runtime_error(
						"Sequential collector fusion failed at input "
						+ std::to_string(index + 1)
						+ " of "
						+ std::to_string(values.size())
						+ ".");
				}
				if (sources != nullptr)
				{
					apply_boolean_history(fuse, *sources);
				}
				result = fuse.Shape();
			}
			return result;
		};
		auto solid_count = [](const TopoDS_Shape& shape)
		{
			size_t count = 0;
			for (TopExp_Explorer explorer(shape, TopAbs_SOLID); explorer.More(); explorer.Next()) ++count;
			return count;
		};
		auto try_connected_fusion = [&](const std::vector<TopoDS_Shape>& values,
			std::vector<runner_source>* sources,
			TopoDS_Shape& output)
		{
			auto try_operation = [&](bool sequential)
			{
				std::vector<runner_source> staged_sources;
				std::vector<runner_source>* staged_source_pointer = nullptr;
				if (sources != nullptr)
				{
					staged_sources = *sources;
					staged_source_pointer = &staged_sources;
				}
				try
				{
					TopoDS_Shape candidate = sequential
						? fuse_sequential(values, staged_source_pointer)
						: fuse_all(values, false, staged_source_pointer);
					if (candidate.IsNull()
						|| solid_count(candidate) != 1
						|| !BRepCheck_Analyzer(candidate, true).IsValid())
					{
						return false;
					}
					output = std::move(candidate);
					if (sources != nullptr)
					{
						*sources = std::move(staged_sources);
					}
					return true;
				}
				catch (const std::exception& error)
				{
					append_native_log(
						"collector",
						std::string(sequential
							? "Sequential staged fusion failed: "
							: "Multi-argument staged fusion failed: ")
							+ error.what());
					return false;
				}
			};
			return try_operation(false) || try_operation(true);
		};
		auto same_point = [](const fgcad_point3& left, const fgcad_point3& right)
		{
			return left.x == right.x && left.y == right.y && left.z == right.z;
		};
		auto same_frame = [&](const fgcad_frame& left, const fgcad_frame& right)
		{
			return same_point(left.origin, right.origin)
				&& same_point(left.tangent, right.tangent)
				&& same_point(left.normal, right.normal);
		};
		auto same_profile = [](const fgcad_runner_profile& left,
			const fgcad_runner_profile& right)
		{
			return left.kind == right.kind
				&& std::strcmp(left.mate_id, right.mate_id) == 0
				&& left.outer_diameter == right.outer_diameter
				&& left.wall_thickness == right.wall_thickness
				&& left.equivalent_radius == right.equivalent_radius;
		};
		auto same_system_geometry = [&](const fgcad_collector_system_spec& left,
			const fgcad_collector_system_spec& right)
		{
			return same_frame(left.outlet_frame, right.outlet_frame)
				&& same_profile(left.outlet_profile, right.outlet_profile)
				&& left.outlet_stub_length == right.outlet_stub_length
				&& left.merge_length == right.merge_length
				&& left.overlap_length == right.overlap_length
				&& left.branch_end_handle_length == right.branch_end_handle_length;
		};
		auto same_inlet_geometry = [&](const fgcad_collector_inlet& left,
			const fgcad_collector_inlet& right)
		{
			return std::strcmp(left.inlet_id, right.inlet_id) == 0
				&& same_frame(left.frame, right.frame)
				&& same_frame(left.profile_reference_frame, right.profile_reference_frame)
				&& same_profile(left.profile, right.profile)
				&& left.merge_station == right.merge_station
				&& left.branch_start_handle_length == right.branch_start_handle_length;
		};

		std::string system_id = require_text(system->system_id, "system_id");
		if (document->staged_collector_id.empty())
		{
			throw std::invalid_argument(
				"A collector system must be built inside an active staged generation.");
		}
		if (document->staged_collector_id != system_id
			|| document->staged_generation_revision != system->generation_revision)
		{
			throw std::invalid_argument(
				"The collector build does not match the active native staging generation.");
		}
		collector_record replacement;
		replacement.id = system_id;
		replacement.name = require_text(system->name, "name");
		replacement.generation_revision = system->generation_revision;
		append_native_log(
			"collector",
			"Build started: id="
				+ system_id
				+ "; name="
				+ replacement.name
				+ "; revision="
				+ std::to_string(system->generation_revision)
				+ "; inlets="
				+ std::to_string(inlet_count));
		if (!std::isfinite(system->outlet_stub_length)
			|| !std::isfinite(system->merge_length)
			|| !std::isfinite(system->overlap_length)
			|| !std::isfinite(system->branch_end_handle_length)
			|| !(system->outlet_stub_length > 0)
			|| !(system->merge_length > 0)
			|| !(system->overlap_length > 0)
			|| !(system->branch_end_handle_length > 0))
		{
			throw std::invalid_argument(
				"Collector stub, merge, overlap, and terminal-handle lengths must be finite and positive.");
		}
		replacement.geometry_spec = *system;
		replacement.inlet_specs.assign(inlets, inlets + inlet_count);
		TopoDS_Shape collector_wall;
		std::vector<TopoDS_Shape> collector_wall_components;
		std::vector<TopoDS_Shape> collector_couplers;
		auto previous = document->collectors.find(system_id);
		bool all_profiles_are_circular = std::all_of(
			inlets,
			inlets + inlet_count,
			[](const fgcad_collector_inlet& inlet)
			{
				return inlet.profile.kind == FGCAD_PROFILE_CIRCULAR;
			});
		bool reuse_collector_wall = all_profiles_are_circular
			&& previous != document->collectors.end()
			&& previous->second.has_wall_cache
			&& !previous->second.wall_shape.IsNull()
			&& same_system_geometry(previous->second.geometry_spec, *system)
			&& previous->second.inlet_specs.size() == inlet_count
			&& std::equal(
				previous->second.inlet_specs.begin(),
				previous->second.inlet_specs.end(),
				inlets,
				same_inlet_geometry);
		if (reuse_collector_wall)
		{
			collector_wall = previous->second.wall_shape;
			collector_wall_components = previous->second.wall_component_shapes;
			collector_couplers = previous->second.coupler_shapes;
			replacement.sources = previous->second.collector_sources;
			replacement.runner_ids = previous->second.runner_ids;
			append_native_log(
				"collector",
				"Collector wall cache hit; branch sweeps and wall booleans reused.");
		}
		else
		{

		gp_Pnt outlet_origin = point(system->outlet_frame.origin);
		gp_Dir outlet_tangent = unit(system->outlet_frame.tangent);
		double merge_outer_radius = 0;
		double merge_inner_radius = 0;
		double merge_boolean_overlap = 2.0;
		std::vector<size_t> branch_order(inlet_count);
		for (size_t index = 0; index < inlet_count; ++index)
		{
			branch_order[index] = index;
			auto inlet_radii = radii(inlets[index].profile);
			merge_outer_radius = std::max(
				merge_outer_radius,
				inlet_radii.first);
			merge_inner_radius = std::max(
				merge_inner_radius,
				inlet_radii.second);
			merge_boolean_overlap = std::max(
				merge_boolean_overlap,
				std::max(
					inlets[index].profile.wall_thickness * 2.0,
					inlet_radii.first * 0.5));
		}
		std::stable_sort(
			branch_order.begin(),
			branch_order.end(),
			[&](size_t left, size_t right)
			{
				auto radial_distance = [&](size_t index)
				{
					gp_Vec inlet_to_outlet(
						point(inlets[index].frame.origin),
						outlet_origin);
					double axial = inlet_to_outlet.Dot(gp_Vec(outlet_tangent));
					return (
						inlet_to_outlet
						- gp_Vec(outlet_tangent) * axial
					).SquareMagnitude();
				};
				return radial_distance(left) < radial_distance(right);
			});
		append_native_log(
			"collector",
			"Building a branch-only collector with one shared Bezier endpoint.");
		std::vector<TopoDS_Shape> wall_volumes;
		std::vector<TopoDS_Shape> inner_volumes;
		std::vector<TopoDS_Shape> inlet_bridges;
		runner_source outlet_source;
		outlet_source.id = "outlet";
		outlet_source.kind = FGCAD_SOURCE_COLLECTOR_OUTLET;
		outlet_source.owner_id = system_id;
		outlet_source.feature.entry_frame = system->outlet_frame;
		outlet_source.feature.exit_frame = system->outlet_frame;
		for (size_t order_index = 0; order_index < inlet_count; ++order_index)
		{
			size_t index = branch_order[order_index];
			const fgcad_collector_inlet& inlet = inlets[index];
			if (!(inlet.merge_station > 0 && inlet.merge_station < 1)
				|| !(inlet.branch_start_handle_length > 0)
				|| !std::isfinite(inlet.merge_station)
				|| !std::isfinite(inlet.branch_start_handle_length))
			{
				throw std::invalid_argument(
					"Collector merge stations must lie in (0,1) and branch handles must be positive.");
			}
			auto inlet_radii = radii(inlet.profile);
			collector_profile_faces inlet_faces = profile_faces(inlet);
			double interface_overlap = std::max(
				2.0,
				inlet.profile.wall_thickness * 2.0
			);
			gp_Pnt p0 = point(inlet.frame.origin);
			gp_Dir inlet_tangent = unit(inlet.frame.tangent);
			gp_Pnt p1 = p0.Translated(
				gp_Vec(inlet_tangent) * inlet.branch_start_handle_length);
			gp_Pnt p3 = outlet_origin;
			gp_Pnt p2 = p3.Translated(
				-gp_Vec(outlet_tangent) * system->branch_end_handle_length);
			double outer_radius = inlet_radii.first;
			fgcad_bezier_evaluation evaluation = evaluate_cubic_bezier_internal(
				inlet.frame,
				point(p1),
				point(p2),
				point(p3),
				outer_radius
			);
			(void)evaluation;

			TopoDS_Shape branch_wall = swept_volume(
				inlet.frame,
				p1,
				p2,
				p3,
				annular_face(inlet_faces),
				interface_overlap,
				outlet_tangent,
				merge_boolean_overlap
			);
			TopoDS_Shape branch_inner = swept_volume(
				inlet.frame,
				p1,
				p2,
				p3,
				inlet_faces.inner,
				interface_overlap,
				outlet_tangent,
				merge_boolean_overlap
			);
			TopoDS_Shape inlet_bridge = straight_wall_volume(
				inlet.frame,
				annular_face(inlet_faces),
				interface_overlap,
				std::max(
					interface_overlap * 2.0,
					inlet.branch_start_handle_length)
			);
			append_native_log(
				"collector",
				"Branch swept: index="
					+ std::to_string(index + 1)
					+ "/"
					+ std::to_string(inlet_count)
					+ "; inlet="
					+ require_text(inlet.inlet_id, "inlet_id")
					+ "; runner="
					+ require_text(inlet.runner_id, "runner_id"));
			wall_volumes.push_back(branch_wall);
			inner_volumes.push_back(branch_inner);
			inlet_bridges.push_back(inlet_bridge);
			replacement.runner_ids.push_back(require_text(inlet.runner_id, "runner_id"));

			runner_source inlet_source;
			inlet_source.id = require_text(inlet.inlet_id, "inlet_id");
			inlet_source.feature.kind = FGCAD_FEATURE_CUBIC_BEZIER;
			inlet_source.feature.entry_frame = inlet.frame;
			inlet_source.feature.exit_frame = system->outlet_frame;
			inlet_source.feature.control1 = point(p1);
			inlet_source.feature.control2 = point(p2);
			inlet_source.faces = shape_faces(branch_wall);
			for (const TopoDS_Face& face : shape_faces(inlet_bridge))
			{
				inlet_source.faces.push_back(face);
			}
			inlet_source.kind = FGCAD_SOURCE_COLLECTOR_INLET;
			inlet_source.owner_id = system_id;
			replacement.sources.push_back(std::move(inlet_source));
		}
		gp_Pnt merge_start = outlet_origin.Translated(
			-gp_Vec(outlet_tangent) * merge_boolean_overlap);
		gp_Ax2 merge_axes(
			merge_start,
			outlet_tangent,
			unit(system->outlet_frame.normal));
		TopoDS_Shape merge_outer = BRepPrimAPI_MakeCylinder(
			merge_axes,
			merge_outer_radius,
			merge_boolean_overlap).Shape();
		TopoDS_Shape merge_inner = BRepPrimAPI_MakeCylinder(
			merge_axes,
			merge_inner_radius,
			merge_boolean_overlap).Shape();
		BRepAlgoAPI_Cut merge_wall_cut(merge_outer, merge_inner);
		merge_wall_cut.SetNonDestructive(true);
		merge_wall_cut.SetRunParallel(true);
		merge_wall_cut.SetFuzzyValue(1.0e-3);
		merge_wall_cut.Build();
		if (!merge_wall_cut.IsDone()
			|| merge_wall_cut.Shape().IsNull()
			|| solid_count(merge_wall_cut.Shape()) != 1)
		{
			throw std::runtime_error(
				"The collector merge collar could not be built as one wall solid.");
		}
		wall_volumes.insert(
			wall_volumes.begin(),
			merge_wall_cut.Shape());
		inner_volumes.insert(
			inner_volumes.begin(),
			merge_inner);
		replacement.sources.push_back(std::move(outlet_source));

		append_native_log(
			"collector",
			"Fusing annular wall volumes: count="
				+ std::to_string(wall_volumes.size()));
		collector_wall_components = wall_volumes;
		std::vector<runner_source> raw_collector_sources =
			replacement.sources;
		std::vector<runner_source> validation_sources =
			raw_collector_sources;
		TopoDS_Shape annular_wall_union = fuse_all(
			wall_volumes,
			false,
			&validation_sources);
		if (solid_count(annular_wall_union) != 1
			|| !BRepCheck_Analyzer(annular_wall_union, true).IsValid())
		{
			append_native_log(
				"collector",
				"Multi-argument annular fusion was inconclusive; trying "
				"a validated sequential component fusion.");
			validation_sources = raw_collector_sources;
			annular_wall_union = fuse_sequential(
				wall_volumes,
				&validation_sources);
			if (solid_count(annular_wall_union) != 1
				|| !BRepCheck_Analyzer(annular_wall_union, true).IsValid())
			{
				throw std::runtime_error(
					"The exact annular collector branches did not fuse into one "
					"valid connected solid; repair geometry was not published.");
			}
		}
		collector_wall = annular_wall_union;
		replacement.sources = std::move(raw_collector_sources);
		append_native_log(
			"collector",
			"Fusing inner gas volumes: count="
				+ std::to_string(inner_volumes.size()));
		TopoDS_Shape inner_union = fuse_all(
			inner_volumes,
			false,
			nullptr);
		if (solid_count(inner_union) != 1)
		{
			append_native_log(
				"collector",
				"Multi-argument gas fusion was inconclusive; "
				"trying a validated sequential gas fusion.");
			inner_union = fuse_sequential(inner_volumes, nullptr);
			if (solid_count(inner_union) != 1)
			{
				throw std::runtime_error(
					"The collector gas-flow union is not one connected solid.");
			}
		}
		append_native_log(
			"collector",
			"Collector wall and gas path validated as connected solids.");
		collector_couplers = std::move(inlet_bridges);
		}
		replacement.wall_shape = collector_wall;
		replacement.wall_component_shapes = collector_wall_components;
		replacement.coupler_shapes = collector_couplers;
		replacement.collector_sources = replacement.sources;
		replacement.has_wall_cache = true;
		if (collector_wall_components.size()
			!= replacement.runner_ids.size() + 1)
		{
			throw std::runtime_error(
				"The collector wall component cache is incomplete.");
		}
		std::vector<TopoDS_Shape> assembled_branch_walls;
		assembled_branch_walls.reserve(replacement.runner_ids.size());
		for (size_t index = 0; index < replacement.runner_ids.size(); ++index)
		{
			const std::string& runner_id = replacement.runner_ids[index];
			auto staged_runner = document->staged_runners.find(runner_id);
			auto runner = document->runners.find(runner_id);
			const runner_record* member = staged_runner != document->staged_runners.end()
				? &staged_runner->second
				: runner != document->runners.end() ? &runner->second : nullptr;
			if (member == nullptr || member->shape.IsNull())
			{
				throw std::out_of_range("A collector member runner has no valid exact solid.");
			}
			for (const runner_source& source : member->sources)
			{
				replacement.sources.push_back(source);
			}
			const TopoDS_Shape& branch_wall =
				collector_wall_components[index + 1];
			std::vector<TopoDS_Shape> branch_assembly_inputs{
				branch_wall,
				member->shape
			};
			TopoDS_Shape assembled_branch;
			bool branch_is_connected = try_connected_fusion(
				branch_assembly_inputs,
				&replacement.sources,
				assembled_branch);
			if (!branch_is_connected
				&& index < collector_couplers.size())
			{
				append_native_log(
					"collector",
					"Direct runner/branch assembly was inconclusive for "
						+ runner_id
						+ "; retrying with its short interface bridge.");
				branch_assembly_inputs = {
					branch_wall,
					collector_couplers[index],
					member->shape
				};
				branch_is_connected = try_connected_fusion(
					branch_assembly_inputs,
					&replacement.sources,
					assembled_branch);
			}
			if (!branch_is_connected)
			{
				throw std::runtime_error(
					"A collector branch and its member runner could not be "
					"assembled into one valid wall solid.");
			}
			append_native_log(
				"collector",
				"Runner/branch assembly connected: runner=" + runner_id);
			assembled_branch_walls.push_back(std::move(assembled_branch));
		}
		if (std::any_of(
			document->staged_runners.begin(),
			document->staged_runners.end(),
			[&](const auto& staged)
			{
				return std::find(
					replacement.runner_ids.begin(),
					replacement.runner_ids.end(),
					staged.first) == replacement.runner_ids.end();
			}))
		{
			throw std::runtime_error(
				"The staged generation contains runners outside this collector system.");
		}
		append_native_log(
			"collector",
			"Fusing runner-owned branch assemblies into the merge wall: runners="
				+ std::to_string(replacement.runner_ids.size()));
		std::vector<TopoDS_Shape> system_assembly_inputs{
			collector_wall_components.front()
		};
		system_assembly_inputs.insert(
			system_assembly_inputs.end(),
			assembled_branch_walls.begin(),
			assembled_branch_walls.end());
		TopoDS_Shape fused = collector_wall;
		bool published_as_connected_solid = false;
		if (solid_count(collector_wall) == 1)
		{
			published_as_connected_solid = try_connected_fusion(
				system_assembly_inputs,
				&replacement.sources,
				fused);
			if (!published_as_connected_solid)
			{
				append_native_log(
					"collector",
					"Component assembly was inconclusive; retrying with the "
					"validated collector wall plus runner-owned branches.");
				std::vector<TopoDS_Shape> repaired_system_inputs{
					collector_wall
				};
				repaired_system_inputs.insert(
					repaired_system_inputs.end(),
					assembled_branch_walls.begin(),
					assembled_branch_walls.end());
				published_as_connected_solid = try_connected_fusion(
					repaired_system_inputs,
					&replacement.sources,
					fused);
			}
		}
		if (!published_as_connected_solid)
		{
			throw std::runtime_error(
				"The runner and collector walls did not fuse into one connected solid; "
				"the invalid multi-solid result was not published.");
		}
		gp_Pln outlet_plane(
			point(system->outlet_frame.origin),
			unit(system->outlet_frame.tangent));
		BRepBuilderAPI_MakeFace outlet_plane_face(outlet_plane);
		if (!outlet_plane_face.IsDone())
		{
			throw std::runtime_error(
				"The collector outlet trim plane could not be constructed.");
		}
		gp_Pnt downstream_reference = point(system->outlet_frame.origin).Translated(
			gp_Vec(unit(system->outlet_frame.tangent)));
		TopoDS_Solid upstream_half_space = BRepPrimAPI_MakeHalfSpace(
			outlet_plane_face.Face(),
			downstream_reference).Solid();
		GProp_GProps untrimmed_properties;
		BRepGProp::VolumeProperties(fused, untrimmed_properties);
		BRepAlgoAPI_Common outlet_trim(fused, upstream_half_space);
		outlet_trim.SetNonDestructive(true);
		outlet_trim.SetRunParallel(true);
		outlet_trim.SetFuzzyValue(1.0e-3);
		outlet_trim.Build();
		if (!outlet_trim.IsDone()
			|| outlet_trim.Shape().IsNull()
			|| solid_count(outlet_trim.Shape()) != 1
			|| !BRepCheck_Analyzer(outlet_trim.Shape(), true).IsValid())
		{
			throw std::runtime_error(
				"The connected collector could not be trimmed cleanly at its "
				"shared outlet plane.");
		}
		GProp_GProps trimmed_properties;
		BRepGProp::VolumeProperties(
			outlet_trim.Shape(),
			trimmed_properties);
		double untrimmed_volume = std::abs(untrimmed_properties.Mass());
		double trimmed_volume = std::abs(trimmed_properties.Mass());
		append_native_log(
			"collector",
			"Outlet trim candidate volume: "
				+ std::to_string(untrimmed_volume)
				+ " -> "
				+ std::to_string(trimmed_volume)
				+ " cubic millimetres.");
		if (!(untrimmed_volume > Precision::Confusion())
			|| trimmed_volume < untrimmed_volume * 0.5)
		{
			throw std::runtime_error(
				"The outlet trim discarded most of the runner/collector "
				"assembly instead of only its temporary downstream overlap.");
		}
		apply_boolean_history(outlet_trim, replacement.sources);
		fused = outlet_trim.Shape();
		append_native_log(
			"collector",
			"Temporary downstream Boolean overlap trimmed at the shared outlet plane: "
				+ std::to_string(untrimmed_volume)
				+ " -> "
				+ std::to_string(trimmed_volume)
				+ " cubic millimetres.");
		runner_source* published_outlet = nullptr;
		for (runner_source& source : replacement.sources)
		{
			if (source.kind == FGCAD_SOURCE_COLLECTOR_OUTLET
				&& source.owner_id == system_id)
			{
				published_outlet = &source;
				published_outlet->faces.clear();
				break;
			}
		}
		if (published_outlet == nullptr)
		{
			runner_source source;
			source.id = "outlet";
			source.kind = FGCAD_SOURCE_COLLECTOR_OUTLET;
			source.owner_id = system_id;
			source.feature.entry_frame = system->outlet_frame;
			source.feature.exit_frame = system->outlet_frame;
			replacement.sources.push_back(std::move(source));
			published_outlet = &replacement.sources.back();
		}
		gp_Pnt published_outlet_origin = point(system->outlet_frame.origin);
		gp_Dir published_outlet_tangent = unit(system->outlet_frame.tangent);
		double nearest_outlet_face_distance =
			std::numeric_limits<double>::infinity();
		for (const TopoDS_Face& face : shape_faces(fused))
		{
			GProp_GProps properties;
			BRepGProp::SurfaceProperties(face, properties);
			gp_Vec from_outlet(
				published_outlet_origin,
				properties.CentreOfMass());
			double outlet_distance = std::abs(
				from_outlet.Dot(gp_Vec(published_outlet_tangent)));
			nearest_outlet_face_distance = std::min(
				nearest_outlet_face_distance,
				outlet_distance);
			if (outlet_distance
				<= Precision::Confusion() * 1000.0)
			{
				published_outlet->faces.push_back(face);
			}
		}
		if (published_outlet->faces.empty())
		{
			throw std::runtime_error(
				"The published collector has no identifiable shared outlet opening "
				"(nearest face center "
				+ std::to_string(nearest_outlet_face_distance)
				+ " mm from the outlet plane).");
		}
		replacement.shape = fused;
		for (const std::string& runner_id : replacement.runner_ids)
		{
			auto staged = document->staged_runners.find(runner_id);
			if (staged != document->staged_runners.end())
			{
				document->runners[runner_id] = std::move(staged->second);
			}
		}
		document->staged_runners.clear();
		document->staged_collector_id.clear();
		document->staged_generation_revision = 0;
		document->collectors[system_id] = std::move(replacement);
		append_native_log(
			"collector",
			"Build published successfully: id="
				+ system_id
				+ "; revision="
				+ std::to_string(system->generation_revision));
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_begin_collector_system_build(
	fgcad_document* document,
	const char* system_id,
	uint64_t generation_revision
)
{
	return guarded([&]()
	{
		if (document == nullptr) throw std::invalid_argument("The document cannot be null.");
		std::string id = require_text(system_id, "system_id");
		if (!document->staged_collector_id.empty())
		{
			throw std::invalid_argument("Another collector-system build is already staged.");
		}
		document->staged_collector_id = std::move(id);
		document->staged_generation_revision = generation_revision;
		document->staged_runners.clear();
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_abort_collector_system_build(
	fgcad_document* document,
	const char* system_id,
	uint64_t generation_revision
)
{
	return guarded([&]()
	{
		if (document == nullptr) throw std::invalid_argument("The document cannot be null.");
		std::string id = require_text(system_id, "system_id");
		if (!document->staged_collector_id.empty()
			&& (document->staged_collector_id != id
				|| document->staged_generation_revision != generation_revision))
		{
			throw std::invalid_argument(
				"The collector staging abort does not match the active generation.");
		}
		document->staged_runners.clear();
		document->staged_collector_id.clear();
		document->staged_generation_revision = 0;
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_remove_collector_system(
	fgcad_document* document,
	const char* system_id
)
{
	return guarded([&]()
	{
		if (document == nullptr) throw std::invalid_argument("The document cannot be null.");
		document->collectors.erase(require_text(system_id, "system_id"));
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_rename_collector_system(
	fgcad_document* document,
	const char* system_id,
	const char* name
)
{
	return guarded([&]()
	{
		if (document == nullptr) throw std::invalid_argument("The document cannot be null.");
		auto found = document->collectors.find(require_text(system_id, "system_id"));
		if (found != document->collectors.end())
		{
			found->second.name = require_text(name, "name");
		}
		return FGCAD_STATUS_OK;
	});
}
