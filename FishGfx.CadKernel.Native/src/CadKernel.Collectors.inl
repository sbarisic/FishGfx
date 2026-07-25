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
				GeomFill_IsCorrectedFrenet,
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
			std::vector<runner_source>* sources,
			double fuzzy_value = 1.0e-3,
			bool run_parallel = true)
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
			fuse.SetRunParallel(run_parallel);
			if (glue) fuse.SetGlue(BOPAlgo_GlueShift);
			fuse.SetFuzzyValue(fuzzy_value);
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
			auto preserves_input_extent_and_volume = [&](const TopoDS_Shape& candidate)
			{
				Bnd_Box input_bounds;
				double largest_input_volume = 0;
				for (const TopoDS_Shape& value : values)
				{
					BRepBndLib::Add(value, input_bounds);
					GProp_GProps input_properties;
					BRepGProp::VolumeProperties(value, input_properties);
					largest_input_volume = std::max(
						largest_input_volume,
						std::abs(input_properties.Mass()));
				}
				Bnd_Box candidate_bounds;
				BRepBndLib::Add(candidate, candidate_bounds);
				if (input_bounds.IsVoid() || candidate_bounds.IsVoid())
				{
					return false;
				}
				double input_min_x = 0;
				double input_min_y = 0;
				double input_min_z = 0;
				double input_max_x = 0;
				double input_max_y = 0;
				double input_max_z = 0;
				double candidate_min_x = 0;
				double candidate_min_y = 0;
				double candidate_min_z = 0;
				double candidate_max_x = 0;
				double candidate_max_y = 0;
				double candidate_max_z = 0;
				input_bounds.Get(
					input_min_x,
					input_min_y,
					input_min_z,
					input_max_x,
					input_max_y,
					input_max_z);
				candidate_bounds.Get(
					candidate_min_x,
					candidate_min_y,
					candidate_min_z,
					candidate_max_x,
					candidate_max_y,
					candidate_max_z);
				constexpr double bounds_tolerance = 1.0e-2;
				bool preserves_bounds =
					candidate_min_x <= input_min_x + bounds_tolerance
					&& candidate_min_y <= input_min_y + bounds_tolerance
					&& candidate_min_z <= input_min_z + bounds_tolerance
					&& candidate_max_x >= input_max_x - bounds_tolerance
					&& candidate_max_y >= input_max_y - bounds_tolerance
					&& candidate_max_z >= input_max_z - bounds_tolerance;
				GProp_GProps candidate_properties;
				BRepGProp::VolumeProperties(candidate, candidate_properties);
				double candidate_volume = std::abs(candidate_properties.Mass());
				bool preserves_volume = candidate_volume
					>= largest_input_volume * (1.0 - 1.0e-6);
				append_native_log(
					"collector",
					"Fusion preservation check: candidateVolume="
						+ std::to_string(candidate_volume)
						+ "; largestInputVolume="
						+ std::to_string(largest_input_volume)
						+ "; bounds="
						+ (preserves_bounds ? "preserved" : "lost")
						+ "; volume="
						+ (preserves_volume ? "preserved" : "lost"));
				return preserves_bounds && preserves_volume;
			};
			auto try_operation = [&](int mode)
			{
				bool sequential = mode == 1;
				bool glue = mode == 2;
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
						: fuse_all(
							values,
							glue,
							staged_source_pointer);
					if (candidate.IsNull()
						|| solid_count(candidate) != 1
						|| !BRepCheck_Analyzer(candidate, true).IsValid()
						|| !preserves_input_extent_and_volume(candidate))
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
							: glue
								? "Glue-shift staged fusion failed: "
								: "Multi-argument staged fusion failed: ")
							+ error.what());
					return false;
				}
			};
			return try_operation(0)
				|| try_operation(1)
				|| try_operation(2);
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
		std::vector<TopoDS_Shape> outer_volumes;
		std::vector<TopoDS_Shape> inner_volumes;
		std::vector<TopoDS_Shape> inlet_bridges;
		TopoDS_Face binary_merge_split_face;
		bool has_binary_merge_split = false;
		if (inlet_count == 2)
		{
			gp_Vec inlet_separation(
				point(inlets[0].frame.origin),
				point(inlets[1].frame.origin));
			inlet_separation -= gp_Vec(outlet_tangent)
				* inlet_separation.Dot(gp_Vec(outlet_tangent));
			if (inlet_separation.Magnitude() > Precision::Confusion())
			{
				BRepBuilderAPI_MakeFace split_face_builder(gp_Pln(
					outlet_origin,
					gp_Dir(inlet_separation)));
				if (split_face_builder.IsDone())
				{
					binary_merge_split_face = split_face_builder.Face();
					has_binary_merge_split = true;
				}
			}
		}
		auto clip_to_binary_merge_side = [&](const TopoDS_Shape& shape,
			const gp_Pnt& branch_reference)
		{
			if (!has_binary_merge_split)
			{
				return shape;
			}
			TopoDS_Solid branch_half_space = BRepPrimAPI_MakeHalfSpace(
				binary_merge_split_face,
				branch_reference).Solid();
			BRepAlgoAPI_Common clip(shape, branch_half_space);
			clip.SetNonDestructive(true);
			clip.SetRunParallel(false);
			clip.SetFuzzyValue(1.0e-3);
			clip.Build();
			if (!clip.IsDone()
				|| clip.Shape().IsNull()
				|| solid_count(clip.Shape()) != 1
				|| !BRepCheck_Analyzer(clip.Shape(), true).IsValid())
			{
				throw std::runtime_error(
					"A two-branch collector could not be partitioned at its "
					"merge bisector.");
			}
			return clip.Shape();
		};
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
				0
			);
			TopoDS_Shape branch_outer = swept_volume(
				inlet.frame,
				p1,
				p2,
				p3,
				inlet_faces.outer,
				interface_overlap,
				outlet_tangent,
				0
			);
			TopoDS_Shape branch_inner = swept_volume(
				inlet.frame,
				p1,
				p2,
				p3,
				inlet_faces.inner,
				interface_overlap,
				outlet_tangent,
				0
			);
			branch_wall = clip_to_binary_merge_side(branch_wall, p0);
			branch_outer = clip_to_binary_merge_side(branch_outer, p0);
			branch_inner = clip_to_binary_merge_side(branch_inner, p0);
			if (solid_count(branch_outer) != 1
				|| !BRepCheck_Analyzer(branch_outer, true).IsValid())
			{
				throw std::runtime_error(
					"A collector branch outer sweep is not one valid solid.");
			}
			if (solid_count(branch_inner) != 1
				|| !BRepCheck_Analyzer(branch_inner, true).IsValid())
			{
				throw std::runtime_error(
					"A collector branch inner sweep is not one valid solid.");
			}
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
			outer_volumes.push_back(branch_outer);
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
			inlet_source.faces = shape_faces(branch_outer);
			for (const TopoDS_Face& face : shape_faces(branch_inner))
			{
				inlet_source.faces.push_back(face);
			}
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
		outer_volumes.insert(
			outer_volumes.begin(),
			merge_outer);
		outlet_source.faces = shape_faces(merge_outer);
		for (const TopoDS_Face& face : shape_faces(merge_inner))
		{
			outlet_source.faces.push_back(face);
		}
		replacement.sources.push_back(std::move(outlet_source));

		collector_wall_components = wall_volumes;
		std::vector<runner_source> annular_sources = replacement.sources;
		for (size_t index = 0; index < branch_order.size(); ++index)
		{
			std::string inlet_id = require_text(
				inlets[branch_order[index]].inlet_id,
				"inlet_id");
			auto source = std::find_if(
				annular_sources.begin(),
				annular_sources.end(),
				[&](const runner_source& candidate)
				{
					return candidate.kind == FGCAD_SOURCE_COLLECTOR_INLET
						&& candidate.id == inlet_id;
				});
			if (source != annular_sources.end())
			{
				source->faces = shape_faces(wall_volumes[index + 1]);
				for (const TopoDS_Face& face : shape_faces(inlet_bridges[index]))
				{
					source->faces.push_back(face);
				}
			}
		}
		for (runner_source& source : annular_sources)
		{
			if (source.kind == FGCAD_SOURCE_COLLECTOR_OUTLET)
			{
				source.faces = shape_faces(wall_volumes.front());
			}
		}
		TopoDS_Shape annular_wall_union;
		bool collector_wall_ready = inlet_count != 2
			&& try_connected_fusion(
				wall_volumes,
				&annular_sources,
				annular_wall_union);
		if (collector_wall_ready)
		{
			collector_wall = annular_wall_union;
			replacement.sources = std::move(annular_sources);
			append_native_log(
				"collector",
				"Published the extent-preserving annular collector wall union.");
		}
		else
		{
		append_native_log(
			"collector",
			"Building the collector wall from complete outer and connected gas "
			"volumes: outerCount="
				+ std::to_string(outer_volumes.size())
				+ "; gasCount="
				+ std::to_string(inner_volumes.size() + 1));
		std::vector<runner_source> validation_sources =
			replacement.sources;
		TopoDS_Shape outer_union;
		if (!try_connected_fusion(
			outer_volumes,
			&validation_sources,
			outer_union))
		{
			throw std::runtime_error(
				"The complete outer collector branch volumes did not fuse into "
				"one valid connected solid.");
		}

		std::vector<TopoDS_Shape> gas_volumes = inner_volumes;
		gas_volumes.insert(gas_volumes.begin(), merge_inner);
		TopoDS_Shape inner_union;
		if (!try_connected_fusion(gas_volumes, nullptr, inner_union))
		{
			throw std::runtime_error(
				"The collector gas-flow volumes did not fuse into one valid "
				"connected solid.");
		}

		bool wall_cut_built = false;
		BRepAlgoAPI_Cut combined_wall_cut(outer_union, inner_union);
		combined_wall_cut.SetNonDestructive(true);
		combined_wall_cut.SetRunParallel(true);
		combined_wall_cut.SetFuzzyValue(1.0e-3);
		combined_wall_cut.Build();
		if (combined_wall_cut.IsDone()
			&& !combined_wall_cut.Shape().IsNull()
			&& solid_count(combined_wall_cut.Shape()) == 1)
		{
			TopoDS_Shape candidate = combined_wall_cut.Shape();
			if (!BRepCheck_Analyzer(candidate, true).IsValid())
			{
				ShapeFix_Shape repair(candidate);
				repair.Perform();
				candidate = repair.Shape();
				append_native_log(
					"collector",
					"Applied OCCT shape healing to the combined collector wall cut.");
			}
			if (!candidate.IsNull()
				&& solid_count(candidate) == 1
				&& BRepCheck_Analyzer(candidate, true).IsValid())
			{
				apply_boolean_history(combined_wall_cut, validation_sources);
				collector_wall = candidate;
				wall_cut_built = true;
			}
		}
		if (!wall_cut_built)
		{
			append_native_log(
				"collector",
				"Combined collector wall cut was inconclusive; trying sequential "
				"gas-volume subtraction.");
			collector_wall = outer_union;
			for (size_t index = 0; index < gas_volumes.size(); ++index)
			{
				BRepAlgoAPI_Cut wall_cut(collector_wall, gas_volumes[index]);
				wall_cut.SetNonDestructive(true);
				wall_cut.SetRunParallel(true);
				wall_cut.SetFuzzyValue(1.0e-3);
				wall_cut.Build();
				if (!wall_cut.IsDone()
					|| wall_cut.Shape().IsNull()
					|| solid_count(wall_cut.Shape()) != 1)
				{
					throw std::runtime_error(
						"Collector gas-volume subtraction failed at tool "
						+ std::to_string(index + 1)
						+ " of "
						+ std::to_string(gas_volumes.size())
						+ ".");
				}
				apply_boolean_history(wall_cut, validation_sources);
				collector_wall = wall_cut.Shape();
			}
			if (!BRepCheck_Analyzer(collector_wall, true).IsValid())
			{
				throw std::runtime_error(
					"Sequential collector gas-volume subtraction produced an invalid wall solid.");
			}
		}
		replacement.sources = std::move(validation_sources);
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
		std::vector<TopoDS_Shape> member_runner_walls;
		member_runner_walls.reserve(replacement.runner_ids.size());
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
				nullptr,
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
					nullptr,
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
			member_runner_walls.push_back(member->shape);
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
			"Fusing member runners directly into the validated collector wall: runners="
				+ std::to_string(replacement.runner_ids.size()));
		std::vector<TopoDS_Shape> system_assembly_inputs{
			collector_wall
		};
		system_assembly_inputs.insert(
			system_assembly_inputs.end(),
			member_runner_walls.begin(),
			member_runner_walls.end());
		TopoDS_Shape fused = collector_wall;
		bool published_as_connected_solid = try_connected_fusion(
			system_assembly_inputs,
			&replacement.sources,
			fused);
		if (!published_as_connected_solid)
		{
			append_native_log(
				"collector",
				"Collector/member fusion was inconclusive; retrying with the "
				"runner-owned branch assemblies at their shared endpoint.");
			published_as_connected_solid = try_connected_fusion(
				assembled_branch_walls,
				&replacement.sources,
				fused);
			if (!published_as_connected_solid)
			{
				append_native_log(
					"collector",
					"Direct collector/runner assembly was inconclusive; retrying "
					"with the merge collar plus runner-owned branch assemblies.");
				std::vector<TopoDS_Shape> repaired_system_inputs{
					collector_wall_components.front()
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
		TopoDS_Solid downstream_half_space = BRepPrimAPI_MakeHalfSpace(
			outlet_plane_face.Face(),
			downstream_reference).Solid();
		GProp_GProps untrimmed_properties;
		BRepGProp::VolumeProperties(fused, untrimmed_properties);
		double untrimmed_volume = std::abs(untrimmed_properties.Mass());
		double minimum_outlet_projection = std::numeric_limits<double>::infinity();
		double maximum_outlet_projection = -std::numeric_limits<double>::infinity();
		gp_Pnt outlet_origin_for_projection = point(system->outlet_frame.origin);
		gp_Vec outlet_axis_for_projection(unit(system->outlet_frame.tangent));
		for (TopExp_Explorer explorer(fused, TopAbs_VERTEX);
			explorer.More();
			explorer.Next())
		{
			gp_Pnt vertex = BRep_Tool::Pnt(TopoDS::Vertex(explorer.Current()));
			double projection = gp_Vec(
				outlet_origin_for_projection,
				vertex).Dot(outlet_axis_for_projection);
			minimum_outlet_projection = std::min(
				minimum_outlet_projection,
				projection);
			maximum_outlet_projection = std::max(
				maximum_outlet_projection,
				projection);
		}
		append_native_log(
			"collector",
			"Fused collector outlet-axis extent before cleanup: "
				+ std::to_string(minimum_outlet_projection)
				+ " to "
				+ std::to_string(maximum_outlet_projection)
				+ " millimetres.");
		double outlet_plane_tolerance = std::max(
			Precision::Confusion() * 10.0,
			1.0e-6);
		bool outlet_was_already_clean =
			maximum_outlet_projection <= outlet_plane_tolerance;
		double trimmed_volume = outlet_was_already_clean
			? untrimmed_volume
			: 0;
		bool outlet_was_trimmed = outlet_was_already_clean;
		if (outlet_was_already_clean)
		{
			append_native_log(
				"collector",
				"The fused collector already terminates at the shared outlet plane; "
				"no cleanup Boolean is required.");
		}
		auto accept_outlet_trim = [&](auto& operation, const char* method)
		{
			operation.Build();
			bool is_done = operation.IsDone();
			bool is_null = !is_done || operation.Shape().IsNull();
			size_t candidate_solid_count = is_null
				? 0
				: solid_count(operation.Shape());
			bool is_valid = !is_null
				&& candidate_solid_count == 1
				&& BRepCheck_Analyzer(operation.Shape(), true).IsValid();
			append_native_log(
				"collector",
				std::string("Outlet trim attempt ")
					+ method
					+ ": done="
					+ (is_done ? "true" : "false")
					+ "; solids="
					+ std::to_string(candidate_solid_count)
					+ "; valid="
					+ (is_valid ? "true" : "false"));
			if (!is_valid)
			{
				return false;
			}

			GProp_GProps candidate_properties;
			BRepGProp::VolumeProperties(
				operation.Shape(),
				candidate_properties);
			double candidate_volume = std::abs(candidate_properties.Mass());
			double minimum_removed_volume = std::max(
				Precision::Confusion(),
				untrimmed_volume * 1.0e-9);
			bool volume_is_plausible = untrimmed_volume > Precision::Confusion()
				&& candidate_volume >= untrimmed_volume * 0.5
				&& candidate_volume <= untrimmed_volume - minimum_removed_volume;
			append_native_log(
				"collector",
				std::string("Outlet trim candidate ")
					+ method
					+ " volume: "
					+ std::to_string(untrimmed_volume)
					+ " -> "
					+ std::to_string(candidate_volume)
					+ " cubic millimetres; plausible="
					+ (volume_is_plausible ? "true" : "false"));
			if (!volume_is_plausible)
			{
				return false;
			}

			std::vector<runner_source> trimmed_sources = replacement.sources;
			apply_boolean_history(operation, trimmed_sources);
			replacement.sources = std::move(trimmed_sources);
			fused = operation.Shape();
			trimmed_volume = candidate_volume;
			append_native_log(
				"collector",
				std::string("Outlet trim accepted using ") + method + ".");
			return true;
		};

		if (!outlet_was_trimmed)
		{
			BRepAlgoAPI_Cut parallel_outlet_cut(fused, downstream_half_space);
			parallel_outlet_cut.SetNonDestructive(true);
			parallel_outlet_cut.SetRunParallel(true);
			parallel_outlet_cut.SetFuzzyValue(1.0e-3);
			outlet_was_trimmed = accept_outlet_trim(
				parallel_outlet_cut,
				"parallel downstream subtraction");
		}
		if (!outlet_was_trimmed)
		{
			BRepAlgoAPI_Cut serial_outlet_cut(fused, downstream_half_space);
			serial_outlet_cut.SetNonDestructive(true);
			serial_outlet_cut.SetRunParallel(false);
			serial_outlet_cut.SetFuzzyValue(1.0e-3);
			outlet_was_trimmed = accept_outlet_trim(
				serial_outlet_cut,
				"serial downstream subtraction");
		}
		if (!outlet_was_trimmed)
		{
			gp_Pnt upstream_reference = point(system->outlet_frame.origin).Translated(
				-gp_Vec(unit(system->outlet_frame.tangent)));
			TopoDS_Solid upstream_half_space = BRepPrimAPI_MakeHalfSpace(
				outlet_plane_face.Face(),
				upstream_reference).Solid();
			BRepAlgoAPI_Common upstream_intersection(fused, upstream_half_space);
			upstream_intersection.SetNonDestructive(true);
			upstream_intersection.SetRunParallel(false);
			upstream_intersection.SetFuzzyValue(1.0e-3);
			outlet_was_trimmed = accept_outlet_trim(
				upstream_intersection,
				"serial upstream intersection");
		}
		if (!outlet_was_trimmed)
		{
			throw std::runtime_error(
				"The connected collector could not be trimmed cleanly at its "
				"shared outlet plane by subtraction or upstream intersection.");
		}
		if (!outlet_was_already_clean)
		{
			append_native_log(
				"collector",
				"Temporary downstream Boolean overlap trimmed at the shared outlet plane: "
					+ std::to_string(untrimmed_volume)
					+ " -> "
					+ std::to_string(trimmed_volume)
					+ " cubic millimetres.");
		}
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
