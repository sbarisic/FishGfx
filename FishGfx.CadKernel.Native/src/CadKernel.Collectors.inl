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
		fgcad_build_metrics metrics{};
		metrics.revision = system->generation_revision;

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
		auto boundary_profile_faces = [](const runner_boundary_record& boundary)
		{
			BRepBuilderAPI_MakeFace outer_face(boundary.outer, true);
			BRepBuilderAPI_MakeFace inner_face(boundary.inner, true);
			if (!outer_face.IsDone() || !inner_face.IsDone())
			{
				throw std::runtime_error(
					"A runner terminal boundary could not seed its collector branch.");
			}
			return collector_profile_faces{
				outer_face.Face(),
				inner_face.Face()
			};
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
			for (bool reverse_inner : { false, true })
			{
				BRepBuilderAPI_MakeFace builder(outer_wire, true);
				builder.Add(reverse_inner
					? TopoDS::Wire(inner_wire.Reversed())
					: inner_wire);
				if (!builder.IsDone()
					|| !BRepCheck_Analyzer(builder.Face(), true).IsValid())
				{
					continue;
				}
				GProp_GProps properties;
				BRepGProp::SurfaceProperties(builder.Face(), properties);
				if (std::abs(properties.Mass()) > 1.0e-8)
				{
					return builder.Face();
				}
			}
			throw std::runtime_error(
				"A collector annular wall profile could not be built.");
		};
		struct collector_sweep_volume
		{
			TopoDS_Shape solid;
			TopoDS_Face first;
			TopoDS_Face last;
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
			if (interface_overlap > Precision::Confusion())
			{
				wire_builder.Add(
					BRepBuilderAPI_MakeEdge(lead_start, point(frame.origin)).Edge());
			}
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
			TopoDS_Solid solid;
			size_t count = 0;
			for (TopExp_Explorer explorer(pipe.Shape(), TopAbs_SOLID);
				explorer.More();
				explorer.Next())
			{
				solid = TopoDS::Solid(explorer.Current());
				++count;
			}
			if (count != 1 || solid.IsNull() || !BRepLib::OrientClosedSolid(solid))
			{
				throw std::runtime_error(
					"A collector branch sweep could not be oriented as one finite solid.");
			}
			TopoDS_Shape first = pipe.FirstShape();
			TopoDS_Shape last = pipe.LastShape();
			if (first.IsNull() || last.IsNull()
				|| first.ShapeType() != TopAbs_FACE
				|| last.ShapeType() != TopAbs_FACE)
			{
				throw std::runtime_error(
					"A collector branch sweep did not retain its exact section boundaries.");
			}
			return collector_sweep_volume{
				TopoDS_Shape(solid),
				TopoDS::Face(first),
				TopoDS::Face(last) };
		};
		auto hollow_wall = [&](const collector_sweep_volume& outer,
			const collector_sweep_volume& inner,
			double wall_thickness,
			const char* description)
		{
			TopoDS_Face first_cap = TopoDS::Face(annular_face({
				outer.first,
				inner.first }).Reversed());
			TopoDS_Face last_cap = annular_face({ outer.last, inner.last });
			double tolerance = std::max(
				Precision::Confusion() * 10.0,
				wall_thickness * 1.0e-4);
			auto sew_start = std::chrono::steady_clock::now();
			BRepBuilderAPI_Sewing sewing(tolerance, true, true, false, false);
			auto add_lateral_faces = [&](const collector_sweep_volume& sweep,
				bool reverse)
			{
				for (TopExp_Explorer explorer(sweep.solid, TopAbs_FACE);
					explorer.More();
					explorer.Next())
				{
					TopoDS_Face face = TopoDS::Face(explorer.Current());
					if (face.IsSame(sweep.first) || face.IsSame(sweep.last))
					{
						continue;
					}
					sewing.Add(reverse ? face.Reversed() : face);
				}
			};
			add_lateral_faces(outer, false);
			add_lateral_faces(inner, true);
			sewing.Add(first_cap);
			sewing.Add(last_cap);
			sewing.Perform();
			metrics.sewing_microseconds += static_cast<uint64_t>(
				std::chrono::duration_cast<std::chrono::microseconds>(
					std::chrono::steady_clock::now() - sew_start).count());
			++metrics.sew_count;
			if (sewing.NbFreeEdges() != 0 || sewing.NbMultipleEdges() != 0)
			{
				throw std::runtime_error(
					std::string(description)
						+ " could not sew exact outer/inner boundaries; freeEdges="
						+ std::to_string(sewing.NbFreeEdges())
						+ "; multipleEdges="
						+ std::to_string(sewing.NbMultipleEdges()) + ".");
			}
			BRepBuilderAPI_MakeSolid solid_builder;
			size_t shell_count = 0;
			for (TopExp_Explorer explorer(sewing.SewedShape(), TopAbs_SHELL);
				explorer.More();
				explorer.Next())
			{
				TopoDS_Shell shell = TopoDS::Shell(explorer.Current());
				if (!BRep_Tool::IsClosed(shell))
				{
					shell.Closed(true);
				}
				solid_builder.Add(shell);
				++shell_count;
			}
			TopoDS_Solid result = solid_builder.Solid();
			if (shell_count != 1 || result.IsNull()
				|| !BRepLib::OrientClosedSolid(result))
			{
				throw std::runtime_error(
					std::string(description) + " did not form exactly one finite closed shell.");
			}
			auto validation_start = std::chrono::steady_clock::now();
			if (!BRepCheck_Analyzer(result, true).IsValid())
			{
				throw std::runtime_error(
					std::string(description) + " is not a valid sewn wall solid.");
			}
			metrics.validation_microseconds += static_cast<uint64_t>(
				std::chrono::duration_cast<std::chrono::microseconds>(
					std::chrono::steady_clock::now() - validation_start).count());
			++metrics.validation_count;
			return TopoDS_Shape(result);
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
			std::vector<TopoDS_Shape> unique_values;
			unique_values.reserve(values.size());
			for (const TopoDS_Shape& value : values)
			{
				if (value.IsNull())
				{
					throw std::invalid_argument("A collector fusion input cannot be null.");
				}
				if (std::none_of(
					unique_values.begin(),
					unique_values.end(),
					[&](const TopoDS_Shape& existing)
					{
						return existing.IsSame(value);
					}))
				{
					unique_values.push_back(value);
				}
			}
			if (unique_values.empty()) throw std::invalid_argument("A collector fusion cannot be empty.");
			if (unique_values.size() == 1) return unique_values.front();
			NCollection_List<TopoDS_Shape> arguments;
			NCollection_List<TopoDS_Shape> tools;
			arguments.Append(unique_values.front());
			for (size_t index = 1; index < unique_values.size(); ++index)
			{
				tools.Append(unique_values[index]);
			}
			BRepAlgoAPI_Fuse fuse;
			fuse.SetArguments(arguments);
			fuse.SetTools(tools);
			fuse.SetNonDestructive(true);
			fuse.SetRunParallel(run_parallel);
			fuse.SetToFillHistory(false);
			if (glue) fuse.SetGlue(BOPAlgo_GlueShift);
			fuse.SetFuzzyValue(fuzzy_value);
			fuse.Build();
			if (!fuse.IsDone() || fuse.Shape().IsNull())
			{
				throw std::runtime_error(
					"Collector multi-argument fusion failed for "
					+ std::to_string(unique_values.size())
					+ " inputs.");
			}
			if (sources != nullptr)
			{
				remap_sources_to_result_surfaces(fuse.Shape(), *sources);
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
						+ std::to_string(unique_values.size())
						+ " inputs.");
			}
			return fuse.Shape();
		};
		auto fuse_sequential = [](const std::vector<TopoDS_Shape>& values,
			std::vector<runner_source>* sources)
		{
			std::vector<TopoDS_Shape> unique_values;
			unique_values.reserve(values.size());
			for (const TopoDS_Shape& value : values)
			{
				if (value.IsNull())
				{
					throw std::invalid_argument(
						"A sequential collector fusion input cannot be null.");
				}
				if (std::none_of(
					unique_values.begin(),
					unique_values.end(),
					[&](const TopoDS_Shape& existing)
					{
						return existing.IsSame(value);
					}))
				{
					unique_values.push_back(value);
				}
			}
			if (unique_values.empty())
			{
				throw std::invalid_argument(
					"A sequential collector fusion cannot be empty.");
			}
			TopoDS_Shape result = unique_values.front();
			for (size_t index = 1; index < unique_values.size(); ++index)
			{
				BRepAlgoAPI_Fuse fuse;
				NCollection_List<TopoDS_Shape> arguments;
				NCollection_List<TopoDS_Shape> tools;
				arguments.Append(result);
				tools.Append(unique_values[index]);
				fuse.SetArguments(arguments);
				fuse.SetTools(tools);
				fuse.SetNonDestructive(true);
				fuse.SetRunParallel(true);
				fuse.SetToFillHistory(false);
				fuse.SetFuzzyValue(1.0e-3);
				fuse.Build();
				if (!fuse.IsDone() || fuse.Shape().IsNull())
				{
					throw std::runtime_error(
						"Sequential collector fusion failed at input "
						+ std::to_string(index + 1)
						+ " of "
						+ std::to_string(unique_values.size())
						+ ".");
				}
				if (sources != nullptr)
				{
					remap_sources_to_result_surfaces(fuse.Shape(), *sources);
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
				double maximum_bounds_loss = std::max({
					candidate_min_x - input_min_x,
					candidate_min_y - input_min_y,
					candidate_min_z - input_min_z,
					input_max_x - candidate_max_x,
					input_max_y - candidate_max_y,
					input_max_z - candidate_max_z,
					0.0
				});
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
						+ "; maximumBoundsLoss="
						+ std::to_string(maximum_bounds_loss)
						+ "; candidateBounds=["
						+ std::to_string(candidate_min_x) + ","
						+ std::to_string(candidate_min_y) + ","
						+ std::to_string(candidate_min_z) + "]-["
						+ std::to_string(candidate_max_x) + ","
						+ std::to_string(candidate_max_y) + ","
						+ std::to_string(candidate_max_z) + "]"
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
		auto build_start = std::chrono::steady_clock::now();
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
		for (size_t index = 0; index < inlet_count; ++index)
		{
			replacement.runner_ids.push_back(require_text(inlets[index].runner_id, "runner_id"));
		}
		TopoDS_Shape collector_wall;
		std::vector<TopoDS_Shape> collector_wall_components;
		std::vector<TopoDS_Shape> collector_couplers;
		auto previous = document->collectors.find(system_id);
		std::ostringstream assembly_key_stream;
		assembly_key_stream << std::setprecision(17)
			<< "abi=7;builder=runner-sew-2;collector-sew-3;transactional-publish=1;occt=8.0.0;"
			<< system->outlet_frame.origin.x << ','
			<< system->outlet_frame.origin.y << ','
			<< system->outlet_frame.origin.z << ','
			<< system->outlet_frame.tangent.x << ','
			<< system->outlet_frame.tangent.y << ','
			<< system->outlet_frame.tangent.z << ','
			<< system->outlet_frame.normal.x << ','
			<< system->outlet_frame.normal.y << ','
			<< system->outlet_frame.normal.z << ';'
			<< system->outlet_profile.outer_diameter << ','
			<< system->outlet_profile.wall_thickness << ','
			<< system->outlet_stub_length << ','
			<< system->merge_length << ','
			<< system->overlap_length << ','
			<< system->branch_end_handle_length << ';';
		for (size_t index = 0; index < inlet_count; ++index)
		{
			const fgcad_collector_inlet& inlet = inlets[index];
			auto staged_runner = document->staged_runners.find(inlet.runner_id);
			auto published_runner = document->runners.find(inlet.runner_id);
			const runner_record* member = staged_runner != document->staged_runners.end()
				? &staged_runner->second
				: published_runner != document->runners.end()
					? &published_runner->second
					: nullptr;
			if (member == nullptr || member->shape.IsNull())
			{
				throw std::out_of_range(
					"A collector member runner has no valid staged or published exact shape.");
			}
			assembly_key_stream
				<< inlet.inlet_id << ',' << inlet.runner_id << ','
				<< inlet.frame.origin.x << ',' << inlet.frame.origin.y << ',' << inlet.frame.origin.z << ','
				<< inlet.frame.tangent.x << ',' << inlet.frame.tangent.y << ',' << inlet.frame.tangent.z << ','
				<< inlet.frame.normal.x << ',' << inlet.frame.normal.y << ',' << inlet.frame.normal.z << ','
				<< inlet.profile.outer_diameter << ',' << inlet.profile.wall_thickness << ','
				<< inlet.merge_station << ',' << inlet.branch_start_handle_length << ','
				<< member->geometry_key << ';';
		}
		replacement.assembly_key = assembly_key_stream.str();
		auto publish_staged_runners = [&]()
		{
			document->staged_collector_published = true;
			for (const std::string& runner_id : replacement.runner_ids)
			{
				auto staged = document->staged_runners.find(runner_id);
				if (staged != document->staged_runners.end())
				{
					auto published = document->runners.find(runner_id);
					if (published != document->runners.end())
					{
						document->staged_previous_member_runners.emplace(
							runner_id,
							published->second);
					}
					else
					{
						document->staged_missing_member_runners.push_back(runner_id);
					}
					document->runners[runner_id] = std::move(staged->second);
				}
			}
			document->staged_runners.clear();
		};
		if (previous != document->collectors.end()
			&& previous->second.assembly_key == replacement.assembly_key
			&& !previous->second.shape.IsNull())
		{
			replacement = previous->second;
			replacement.name = require_text(system->name, "name");
			replacement.generation_revision = system->generation_revision;
			replacement.geometry_spec = *system;
			replacement.inlet_specs.assign(inlets, inlets + inlet_count);
			metrics.cache_flags |= FGCAD_CACHE_SYSTEM_ASSEMBLY;
			metrics.total_microseconds = static_cast<uint64_t>(
				std::chrono::duration_cast<std::chrono::microseconds>(
					std::chrono::steady_clock::now() - build_start).count());
			capture_topology_metrics(replacement.shape, metrics);
			publish_staged_runners();
			document->build_metrics[system_id] = metrics;
			document->collectors[system_id] = std::move(replacement);
			append_native_log("collector", "System assembly cache hit; exact shape reused.");
			return FGCAD_STATUS_OK;
		}
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
			metrics.cache_flags |= FGCAD_CACHE_COLLECTOR_BODY;
			collector_wall = previous->second.wall_shape;
			collector_wall_components = previous->second.wall_component_shapes;
			collector_couplers = previous->second.coupler_shapes;
			replacement.sources = previous->second.collector_sources;
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
		double merge_boolean_overlap = 1.5;
		double branch_endpoint_offset = 1.5;
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
				inlet_radii.first * 0.75);
			branch_endpoint_offset = std::max(
				branch_endpoint_offset,
				std::max(
					inlets[index].profile.wall_thickness * 1.5,
					inlet_radii.first * 0.1));
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
		gp_Pnt merge_start = outlet_origin.Translated(
			-gp_Vec(outlet_tangent) * (merge_boolean_overlap * 2.0));
		std::vector<TopoDS_Shape> outer_volumes;
		std::vector<TopoDS_Shape> inner_volumes;
		std::vector<TopoDS_Shape> wall_volumes;
		std::vector<TopoDS_Face> exact_inlet_caps(inlet_count);
		std::vector<TopoDS_Face> opening_source_faces;
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
			auto staged_runner = document->staged_runners.find(
				require_text(inlet.runner_id, "runner_id"));
			auto published_runner = document->runners.find(
				require_text(inlet.runner_id, "runner_id"));
			const runner_record* member = staged_runner != document->staged_runners.end()
				? &staged_runner->second
				: published_runner != document->runners.end()
					? &published_runner->second
					: nullptr;
			if (member == nullptr
				|| member->shape.IsNull()
				|| member->end_boundary.inner.IsNull()
				|| member->end_boundary.outer.IsNull())
			{
				throw std::out_of_range(
					"A collector member runner has no exact terminal boundary.");
			}
			collector_profile_faces inlet_faces = boundary_profile_faces(
				member->end_boundary);
			exact_inlet_caps[index] = TopoDS::Face(annular_face(inlet_faces).Reversed());
			constexpr double outer_interface_overlap = 0;
			gp_Pnt p0 = point(inlet.frame.origin);
			gp_Dir inlet_tangent = unit(inlet.frame.tangent);
			gp_Pnt p1 = p0.Translated(
				gp_Vec(inlet_tangent) * inlet.branch_start_handle_length);
			double outer_radius = inlet_radii.first;
			// Terminate every branch inside the tapered merge core.  This creates
			// deliberate positive-volume penetration without appending a shared
			// coincident line segment to every branch (which gives OCCT duplicate
			// Boolean history entries and an unstable common boundary).
			gp_Pnt p3 = outlet_origin.Translated(
				-gp_Vec(outlet_tangent) * branch_endpoint_offset);
			gp_Vec branch_radial(outlet_origin, p0);
			branch_radial -= gp_Vec(outlet_tangent)
				* branch_radial.Dot(gp_Vec(outlet_tangent));
			if (branch_radial.SquareMagnitude() > Precision::SquareConfusion())
			{
				branch_radial.Normalize();
				double junction_spread = std::max(
					inlet.profile.wall_thickness * 0.75,
					outer_radius * 0.12);
				p3.Translate(branch_radial * junction_spread);
			}
			gp_Pnt p2 = p3.Translated(
				-gp_Vec(outlet_tangent) * system->branch_end_handle_length);
			auto evaluation_start = std::chrono::steady_clock::now();
			fgcad_bezier_evaluation evaluation{};
			try
			{
				evaluation = evaluate_cubic_bezier_internal(
					inlet.frame,
					point(p1),
					point(p2),
					point(p3),
					outer_radius);
			}
			catch (const std::invalid_argument& exception)
			{
				throw std::invalid_argument(
					"Collector inlet " + require_text(inlet.inlet_id, "inlet_id")
					+ " cannot reach the current outlet direction without violating "
					+ "the tube bend radius. Move the outlet farther in its new direction "
					+ "or adjust the inlet and branch handles. " + exception.what());
			}
			metrics.evaluation_microseconds += static_cast<uint64_t>(
				std::chrono::duration_cast<std::chrono::microseconds>(
					std::chrono::steady_clock::now() - evaluation_start).count());

			auto sweep_start = std::chrono::steady_clock::now();
			collector_sweep_volume branch_outer_sweep = swept_volume(
				inlet.frame,
				p1,
				p2,
				p3,
				inlet_faces.outer,
				outer_interface_overlap,
				outlet_tangent,
				0
			);
			collector_sweep_volume branch_inner_sweep = swept_volume(
				inlet.frame,
				p1,
				p2,
				p3,
				inlet_faces.inner,
				outer_interface_overlap,
				outlet_tangent,
				0
			);
			metrics.sweep_microseconds += static_cast<uint64_t>(
				std::chrono::duration_cast<std::chrono::microseconds>(
					std::chrono::steady_clock::now() - sweep_start).count());
			metrics.sweep_count += 2;
			TopoDS_Shape branch_outer = branch_outer_sweep.solid;
			TopoDS_Shape branch_inner = branch_inner_sweep.solid;
			opening_source_faces.push_back(branch_outer_sweep.first);
			opening_source_faces.push_back(branch_inner_sweep.first);
			auto branch_validation_start = std::chrono::steady_clock::now();
			if (solid_count(branch_outer) != 1
				|| solid_count(branch_inner) != 1)
			{
				throw std::runtime_error(
					"A collector branch did not produce one outer and inner solid.");
			}
			metrics.validation_microseconds += static_cast<uint64_t>(
				std::chrono::duration_cast<std::chrono::microseconds>(
					std::chrono::steady_clock::now() - branch_validation_start).count());
			++metrics.validation_count;
			append_native_log(
				"collector",
				"Branch swept: index="
					+ std::to_string(index + 1)
					+ "/"
					+ std::to_string(inlet_count)
					+ "; inlet="
					+ require_text(inlet.inlet_id, "inlet_id")
					+ "; runner="
					+ require_text(inlet.runner_id, "runner_id")
					+ "; topologyCheck=ok");
			outer_volumes.push_back(branch_outer);
			inner_volumes.push_back(branch_inner);
			wall_volumes.push_back(branch_outer);

			runner_source inlet_source;
			inlet_source.id = require_text(inlet.inlet_id, "inlet_id");
			inlet_source.feature.kind = FGCAD_FEATURE_CUBIC_BEZIER;
			inlet_source.feature.entry_frame = inlet.frame;
			inlet_source.feature.exit_frame = evaluation.exit_frame;
			inlet_source.feature.control1 = point(p1);
			inlet_source.feature.control2 = point(p2);
			inlet_source.faces = shape_faces(branch_outer);
			for (const TopoDS_Face& face : shape_faces(branch_inner))
			{
				inlet_source.faces.push_back(face);
			}
			inlet_source.faces.push_back(exact_inlet_caps[index]);
			inlet_source.kind = FGCAD_SOURCE_COLLECTOR_INLET;
			inlet_source.owner_id = system_id;
			replacement.sources.push_back(std::move(inlet_source));
		}
		gp_Ax2 merge_start_axes(
			merge_start,
			outlet_tangent,
			unit(system->outlet_frame.normal));
		gp_Ax2 merge_end_axes(
			outlet_origin,
			outlet_tangent,
			unit(system->outlet_frame.normal));
		double merge_transition_allowance = std::max(
			0.25,
			system->outlet_profile.wall_thickness * 0.25);
		auto circle_wire = [](const gp_Ax2& axes, double radius)
		{
			return BRepBuilderAPI_MakeWire(
				BRepBuilderAPI_MakeEdge(gp_Circ(axes, radius)).Edge()).Wire();
		};
		auto merge_loft_start = std::chrono::steady_clock::now();
		BRepOffsetAPI_ThruSections merge_outer_loft(true, false);
		merge_outer_loft.CheckCompatibility(true);
		merge_outer_loft.AddWire(circle_wire(
			merge_start_axes,
			merge_outer_radius + merge_transition_allowance));
		merge_outer_loft.AddWire(circle_wire(merge_end_axes, merge_outer_radius));
		merge_outer_loft.Build();
		BRepOffsetAPI_ThruSections merge_inner_loft(true, false);
		merge_inner_loft.CheckCompatibility(true);
		merge_inner_loft.AddWire(circle_wire(
			merge_start_axes,
			std::max(
				Precision::Confusion() * 10.0,
				merge_inner_radius - merge_transition_allowance)));
		merge_inner_loft.AddWire(circle_wire(
			merge_end_axes,
			merge_inner_radius));
		merge_inner_loft.Build();
		metrics.loft_microseconds += static_cast<uint64_t>(
			std::chrono::duration_cast<std::chrono::microseconds>(
				std::chrono::steady_clock::now() - merge_loft_start).count());
		metrics.loft_count += 2;
		if (!merge_outer_loft.IsDone() || !merge_inner_loft.IsDone())
		{
			throw std::runtime_error(
				"The collector tapered merge core could not be built.");
		}
		auto finite_loft_solid = [](const TopoDS_Shape& shape, const char* description)
		{
			TopoDS_Solid solid;
			size_t count = 0;
			for (TopExp_Explorer explorer(shape, TopAbs_SOLID);
				explorer.More();
				explorer.Next())
			{
				solid = TopoDS::Solid(explorer.Current());
				++count;
			}
			if (count != 1 || solid.IsNull() || !BRepLib::OrientClosedSolid(solid))
			{
				throw std::runtime_error(
					std::string(description) + " could not be oriented as one finite solid.");
			}
			return TopoDS_Shape(solid);
		};
		TopoDS_Shape merge_outer = finite_loft_solid(
			merge_outer_loft.Shape(),
			"The collector outer merge core");
		TopoDS_Shape merge_inner = finite_loft_solid(
			merge_inner_loft.Shape(),
			"The collector inner merge core");
		if (merge_outer_loft.FirstShape().ShapeType() != TopAbs_FACE
			|| merge_outer_loft.LastShape().ShapeType() != TopAbs_FACE
			|| merge_inner_loft.FirstShape().ShapeType() != TopAbs_FACE
			|| merge_inner_loft.LastShape().ShapeType() != TopAbs_FACE)
		{
			throw std::runtime_error(
				"The collector merge loft did not retain exact section boundaries.");
		}
		collector_sweep_volume merge_outer_sweep{
			merge_outer,
			TopoDS::Face(merge_outer_loft.FirstShape()),
			TopoDS::Face(merge_outer_loft.LastShape()) };
		collector_sweep_volume merge_inner_sweep{
			merge_inner,
			TopoDS::Face(merge_inner_loft.FirstShape()),
			TopoDS::Face(merge_inner_loft.LastShape()) };
		opening_source_faces.push_back(merge_outer_sweep.last);
		opening_source_faces.push_back(merge_inner_sweep.last);
		outer_volumes.insert(
			outer_volumes.begin(),
			merge_outer);
		inner_volumes.insert(
			inner_volumes.begin(),
			merge_inner);
		wall_volumes.insert(wall_volumes.begin(), merge_outer);
		outlet_source.faces = shape_faces(merge_outer);
		for (const TopoDS_Face& face : shape_faces(merge_inner))
		{
			outlet_source.faces.push_back(face);
		}
		replacement.sources.push_back(std::move(outlet_source));

		auto legacy_material_boundary_assembly = [&]() -> TopoDS_Shape
		{
		collector_wall_components = wall_volumes;
		NCollection_List<TopoDS_Shape> cell_arguments;
		for (const TopoDS_Shape& outer : outer_volumes)
		{
			cell_arguments.Append(outer);
		}
		for (const TopoDS_Shape& inner : inner_volumes)
		{
			cell_arguments.Append(inner);
		}
		auto merge_start_time = std::chrono::steady_clock::now();
		BOPAlgo_CellsBuilder cells;
		cells.SetArguments(cell_arguments);
		cells.SetNonDestructive(true);
		cells.SetRunParallel(true);
		double cell_fuzzy_tolerance = std::max(
			Precision::Confusion() * 10.0,
			system->outlet_profile.wall_thickness * 1.0e-4);
		cells.SetFuzzyValue(cell_fuzzy_tolerance);
		cells.Perform();
		if (cells.HasErrors())
		{
			std::ostringstream errors;
			cells.DumpErrors(errors);
			throw std::runtime_error(
				"The collector N-to-one general fuse failed while partitioning wall cells: "
					+ errors.str());
		}
		std::vector<TopoDS_Face> opening_result_faces;
		auto add_opening_result_face = [&](const TopoDS_Shape& shape)
		{
			if (shape.ShapeType() != TopAbs_FACE)
			{
				return;
			}
			TopoDS_Face face = TopoDS::Face(shape);
			if (std::none_of(
				opening_result_faces.begin(),
				opening_result_faces.end(),
				[&](const TopoDS_Face& existing)
				{
					return existing.IsSame(face);
				}))
			{
				opening_result_faces.push_back(face);
			}
		};
		for (const TopoDS_Face& source : opening_source_faces)
		{
			add_opening_result_face(source);
			for (const TopoDS_Shape& modified : cells.Modified(source))
			{
				add_opening_result_face(modified);
			}
			for (const TopoDS_Shape& generated : cells.Generated(source))
			{
				add_opening_result_face(generated);
			}
		}
		struct wall_face_occurrence
		{
			TopoDS_Face face;
			size_t forward_count{};
			size_t reversed_count{};
		};
		auto union_boundary_faces = [&](const std::vector<TopoDS_Shape>& volumes,
			const char* description,
			int material)
		{
			BOPAlgo_PPaveFiller shared_filler = cells.PPaveFiller();
			if (!shared_filler)
			{
				throw std::runtime_error(
					"The collector general fuse did not retain reusable intersection data.");
			}
			BOPAlgo_CellsBuilder material_cells;
			material_cells.SetArguments(cell_arguments);
			material_cells.SetNonDestructive(true);
			material_cells.SetRunParallel(true);
			material_cells.SetFuzzyValue(cell_fuzzy_tolerance);
			material_cells.PerformWithFiller(*shared_filler);
			if (material_cells.HasErrors())
			{
				std::ostringstream errors;
				material_cells.DumpErrors(errors);
				throw std::runtime_error(
					std::string(description) + " could not reuse the general-fuse partition: "
						+ errors.str());
			}
			if (volumes.size() >= 63)
			{
				throw std::invalid_argument(
					std::string(description) + " has too many exact membership combinations.");
			}
			material_cells.RemoveAllFromResult();
			const uint64_t membership_count = uint64_t{ 1 } << volumes.size();
			for (uint64_t membership = 1; membership < membership_count; ++membership)
			{
				NCollection_List<TopoDS_Shape> take;
				NCollection_List<TopoDS_Shape> avoid;
				for (size_t index = 0; index < volumes.size(); ++index)
				{
					if ((membership & (uint64_t{ 1 } << index)) != 0)
					{
						take.Append(volumes[index]);
					}
					else
					{
						avoid.Append(volumes[index]);
					}
				}
				material_cells.AddToResult(take, avoid, material, false);
			}
			material_cells.RemoveInternalBoundaries();
			if (material_cells.HasErrors())
			{
				std::ostringstream errors;
				material_cells.DumpErrors(errors);
				throw std::runtime_error(
					std::string(description) + " could not remove internal boundaries: "
						+ errors.str());
			}
			std::vector<wall_face_occurrence> occurrences;
			for (TopExp_Explorer solid_explorer(material_cells.Shape(), TopAbs_SOLID);
				solid_explorer.More();
				solid_explorer.Next())
			{
				for (TopExp_Explorer face_explorer(
					solid_explorer.Current(),
					TopAbs_FACE);
					face_explorer.More();
					face_explorer.Next())
				{
					TopoDS_Face face = TopoDS::Face(face_explorer.Current());
					auto occurrence = std::find_if(
						occurrences.begin(),
						occurrences.end(),
						[&](const wall_face_occurrence& candidate)
						{
							return candidate.face.IsSame(face);
						});
					if (occurrence == occurrences.end())
					{
						occurrences.push_back({
							face,
							face.Orientation() == TopAbs_FORWARD ? 1u : 0u,
							face.Orientation() == TopAbs_REVERSED ? 1u : 0u });
					}
					else if (face.Orientation() == TopAbs_FORWARD)
					{
						++occurrence->forward_count;
					}
					else if (face.Orientation() == TopAbs_REVERSED)
					{
						++occurrence->reversed_count;
					}
				}
			}
			std::vector<TopoDS_Face> boundary;
			for (const wall_face_occurrence& occurrence : occurrences)
			{
				if (occurrence.forward_count != occurrence.reversed_count)
				{
					TopoDS_Face face = occurrence.face;
					face.Orientation(
						occurrence.forward_count > occurrence.reversed_count
							? TopAbs_FORWARD
							: TopAbs_REVERSED);
					boundary.push_back(face);
				}
			}
			append_native_log(
				"collector",
				std::string(description) + " boundary extracted: faces="
					+ std::to_string(boundary.size()) + ".");
			return boundary;
		};
		std::vector<TopoDS_Face> outer_boundary = union_boundary_faces(
			outer_volumes,
			"Outer material union",
			1);
		std::vector<TopoDS_Face> inner_boundary = union_boundary_faces(
			inner_volumes,
			"Inner gas union",
			2);
		double wall_boundary_tolerance = std::max(
			cell_fuzzy_tolerance,
			system->outlet_profile.wall_thickness * 0.05);
		auto wall_sew_start = std::chrono::steady_clock::now();
		BRepBuilderAPI_Sewing wall_sewing(
			wall_boundary_tolerance,
			true,
			true,
			true,
			false);
		auto is_opening_plane_cap = [&](const TopoDS_Face& face)
		{
			if (std::any_of(
				opening_result_faces.begin(),
				opening_result_faces.end(),
				[&](const TopoDS_Face& opening)
				{
					return opening.IsSame(face);
				}))
			{
				return true;
			}
			BRepAdaptor_Surface surface(face, true);
			if (surface.GetType() != GeomAbs_Plane)
			{
				return false;
			}
			gp_Pln plane = surface.Plane();
			GProp_GProps properties;
			BRepGProp::SurfaceProperties(face, properties);
			gp_Pnt center = properties.CentreOfMass();
			for (size_t inlet_index = 0; inlet_index < inlet_count; ++inlet_index)
			{
				const fgcad_collector_inlet& inlet = inlets[inlet_index];
				double outer_radius = radii(inlet.profile).first;
				gp_Pnt origin = point(inlet.frame.origin);
				gp_Dir tangent = unit(inlet.frame.tangent);
				if (std::abs(plane.Axis().Direction().Dot(tangent)) < 1.0 - 1.0e-8
					|| plane.Distance(origin) > wall_boundary_tolerance)
				{
					continue;
				}
				gp_Vec offset(origin, center);
				double axial = offset.Dot(gp_Vec(tangent));
				double radial = (offset - gp_Vec(tangent) * axial).Magnitude();
				if (radial <= outer_radius * 1.05)
				{
					return true;
				}
			}
			double outlet_outer_radius = radii(system->outlet_profile).first;
			gp_Pnt outlet_point = point(system->outlet_frame.origin);
			gp_Dir outlet_direction = unit(system->outlet_frame.tangent);
			if (std::abs(plane.Axis().Direction().Dot(outlet_direction)) >= 1.0 - 1.0e-8
				&& plane.Distance(outlet_point) <= wall_boundary_tolerance)
			{
				gp_Vec offset(outlet_point, center);
				double axial = offset.Dot(gp_Vec(outlet_direction));
				double radial = (
					offset - gp_Vec(outlet_direction) * axial).Magnitude();
				if (radial <= outlet_outer_radius * 1.05)
				{
					return true;
				}
			}
			return false;
		};
		size_t external_face_count = 0;
		for (const TopoDS_Face& face : outer_boundary)
		{
			if (is_opening_plane_cap(face))
			{
				continue;
			}
			wall_sewing.Add(face);
			++external_face_count;
		}
		for (const TopoDS_Face& face : inner_boundary)
		{
			if (is_opening_plane_cap(face))
			{
				continue;
			}
			wall_sewing.Add(face.Reversed());
			++external_face_count;
		}
		for (const TopoDS_Face& inlet_cap : exact_inlet_caps)
		{
			wall_sewing.Add(inlet_cap);
			++external_face_count;
		}
		auto outlet_radii = radii(system->outlet_profile);
		TopoDS_Face outlet_cap = annular_face({
			disk(system->outlet_frame, outlet_radii.first),
			disk(system->outlet_frame, outlet_radii.second) });
		wall_sewing.Add(outlet_cap);
		++external_face_count;
		wall_sewing.Perform();
		metrics.sewing_microseconds += static_cast<uint64_t>(
			std::chrono::duration_cast<std::chrono::microseconds>(
				std::chrono::steady_clock::now() - wall_sew_start).count());
		++metrics.sew_count;
		if (external_face_count == 0
			|| wall_sewing.NbFreeEdges() != 0
			|| wall_sewing.NbMultipleEdges() != 0)
		{
			std::ostringstream edge_diagnostics;
			for (int index = 1; index <= wall_sewing.NbFreeEdges(); ++index)
			{
				GProp_GProps properties;
				BRepGProp::LinearProperties(wall_sewing.FreeEdge(index), properties);
				const gp_Pnt& center = properties.CentreOfMass();
				edge_diagnostics << " [" << index
					<< ":length=" << properties.Mass()
					<< ";center=" << center.X() << '/' << center.Y() << '/'
					<< center.Z() << ']';
			}
			append_native_log(
				"collector",
				"Unsewn collector boundary edges:" + edge_diagnostics.str());
			throw std::runtime_error(
				"The selected N-to-one wall cells could not be sewn into manifold boundaries; faces="
					+ std::to_string(external_face_count)
					+ "; freeEdges=" + std::to_string(wall_sewing.NbFreeEdges())
					+ "; multipleEdges=" + std::to_string(wall_sewing.NbMultipleEdges()) + ".");
		}
		BRepBuilderAPI_MakeSolid wall_solid_builder;
		size_t wall_shell_count = 0;
		for (TopExp_Explorer shell_explorer(
			wall_sewing.SewedShape(),
			TopAbs_SHELL);
			shell_explorer.More();
			shell_explorer.Next())
		{
			TopoDS_Shell shell = TopoDS::Shell(shell_explorer.Current());
			if (!BRep_Tool::IsClosed(shell))
			{
				// The sewing report above is authoritative for free and
				// multiply-connected edges.  OCCT does not always propagate the
				// Closed metadata bit to shells assembled from CellsBuilder faces.
				shell.Closed(true);
			}
			wall_solid_builder.Add(shell);
			++wall_shell_count;
		}
		if (wall_shell_count == 0)
		{
			throw std::runtime_error(
				"The selected N-to-one wall produced no closed shell.");
		}
		TopoDS_Solid wall_result = wall_solid_builder.Solid();
		if (!BRepLib::OrientClosedSolid(wall_result))
		{
			throw std::runtime_error(
				"The selected N-to-one wall could not be oriented as one finite solid.");
		}
		metrics.merge_microseconds += static_cast<uint64_t>(
			std::chrono::duration_cast<std::chrono::microseconds>(
				std::chrono::steady_clock::now() - merge_start_time).count());
		++metrics.merge_boolean_count;
		return TopoDS_Shape(wall_result);
		};
		collector_wall_components = wall_volumes;
		double cell_fuzzy_tolerance = std::max(
			Precision::Confusion() * 10.0,
			system->outlet_profile.wall_thickness * 1.0e-4);
		auto merge_start_time = std::chrono::steady_clock::now();
		TopoDS_Shape cell_result;
		#if 0
		{
			NCollection_List<TopoDS_Shape> outer_arguments;
			for (const TopoDS_Shape& outer : outer_volumes)
			{
				outer_arguments.Append(outer);
			}
			BOPAlgo_CellsBuilder outer_cells;
			outer_cells.SetArguments(outer_arguments);
			outer_cells.SetNonDestructive(true);
			outer_cells.SetRunParallel(true);
			outer_cells.SetFuzzyValue(cell_fuzzy_tolerance);
			outer_cells.Perform();
			if (outer_cells.HasErrors())
			{
				std::ostringstream errors;
				outer_cells.DumpErrors(errors);
				throw std::runtime_error(
					"The collector outer N-to-one partition failed: " + errors.str());
			}
			outer_cells.AddAllToResult(1, false);
			outer_cells.RemoveInternalBoundaries();
			if (outer_cells.HasErrors())
			{
				std::ostringstream errors;
				outer_cells.DumpErrors(errors);
				throw std::runtime_error(
					"The collector outer N-to-one union failed: " + errors.str());
			}
			TopoDS_Shape outer_union_cells = outer_cells.Shape();
			if (false)
			{
			auto inside_any_outer = [&](const gp_Pnt& sample)
			{
				return std::any_of(
					outer_volumes.begin(),
					outer_volumes.end(),
					[&](const TopoDS_Shape& outer)
					{
						BRepClass3d_SolidClassifier classifier(
							outer,
							sample,
							cell_fuzzy_tolerance);
						return classifier.State() == TopAbs_IN;
					});
			};
			BRep_Builder selected_outer_builder;
			TopoDS_Compound selected_outer_cells;
			selected_outer_builder.MakeCompound(selected_outer_cells);
			size_t selected_outer_cell_count = 0;
			for (TopExp_Explorer cell_explorer(
				outer_cells.GetAllParts(),
				TopAbs_SOLID);
				cell_explorer.More();
				cell_explorer.Next())
			{
				TopoDS_Solid cell = TopoDS::Solid(cell_explorer.Current());
				BRepLib::OrientClosedSolid(cell);
				GProp_GProps properties;
				BRepGProp::VolumeProperties(cell, properties);
				bool select = inside_any_outer(properties.CentreOfMass());
				for (TopExp_Explorer face_explorer(cell, TopAbs_FACE);
					!select && face_explorer.More();
					face_explorer.Next())
				{
					TopoDS_Face face = TopoDS::Face(face_explorer.Current());
					gp_Pnt surface_point;
					double u = 0;
					double v = 0;
					double parameter = 0.5;
					gp_Vec derivative_u;
					gp_Vec derivative_v;
					if (!BRepClass3d_SolidExplorer::FindAPointInTheFace(
						face,
						surface_point,
						u,
						v,
						parameter,
						derivative_u,
						derivative_v))
					{
						continue;
					}
					gp_Vec normal = derivative_u.Crossed(derivative_v);
					if (normal.SquareMagnitude() <= Precision::SquareConfusion())
					{
						continue;
					}
					normal.Normalize();
					if (face.Orientation() == TopAbs_REVERSED)
					{
						normal.Reverse();
					}
					gp_Pnt inward = surface_point.Translated(
						-normal * std::max(0.001, cell_fuzzy_tolerance * 2.0));
					select = inside_any_outer(inward);
				}
				if (select)
				{
					selected_outer_builder.Add(selected_outer_cells, cell);
					++selected_outer_cell_count;
				}
			}
			append_native_log(
				"collector",
				"Selected outer general-fuse cells: "
					+ std::to_string(selected_outer_cell_count) + ".");
			if (selected_outer_cell_count == 0)
			{
				throw std::runtime_error(
					"The collector outer partition contained no classified outer cells.");
			}
			outer_union_cells = selected_outer_cells;
			}
			struct outer_boundary_face_occurrence
			{
				TopoDS_Face face;
				size_t forward_count{};
				size_t reversed_count{};
			};
			std::vector<outer_boundary_face_occurrence> outer_face_occurrences;
			for (TopExp_Explorer solid_explorer(outer_union_cells, TopAbs_SOLID);
				solid_explorer.More();
				solid_explorer.Next())
			{
				TopoDS_Solid cell = TopoDS::Solid(solid_explorer.Current());
				BRepLib::OrientClosedSolid(cell);
				for (TopExp_Explorer face_explorer(cell, TopAbs_FACE);
					face_explorer.More();
					face_explorer.Next())
				{
					TopoDS_Face face = TopoDS::Face(face_explorer.Current());
					auto occurrence = std::find_if(
						outer_face_occurrences.begin(),
						outer_face_occurrences.end(),
						[&](const outer_boundary_face_occurrence& candidate)
						{
							return candidate.face.IsSame(face);
						});
					if (occurrence == outer_face_occurrences.end())
					{
						outer_face_occurrences.push_back({
							face,
							face.Orientation() == TopAbs_FORWARD ? 1u : 0u,
							face.Orientation() == TopAbs_REVERSED ? 1u : 0u });
					}
					else if (face.Orientation() == TopAbs_FORWARD)
					{
						++occurrence->forward_count;
					}
					else if (face.Orientation() == TopAbs_REVERSED)
					{
						++occurrence->reversed_count;
					}
				}
			}
			BRepBuilderAPI_Sewing outer_boundary_sewing(
				std::max(
					cell_fuzzy_tolerance,
					system->outlet_profile.wall_thickness * 0.05),
				true,
				true,
				false,
				false);
			size_t outer_boundary_face_count = 0;
			for (const outer_boundary_face_occurrence& occurrence : outer_face_occurrences)
			{
				if (occurrence.forward_count == occurrence.reversed_count)
				{
					continue;
				}
				TopoDS_Face face = occurrence.face;
				face.Orientation(
					occurrence.forward_count > occurrence.reversed_count
						? TopAbs_FORWARD
						: TopAbs_REVERSED);
				outer_boundary_sewing.Add(face);
				++outer_boundary_face_count;
			}
			auto outer_sew_start = std::chrono::steady_clock::now();
			outer_boundary_sewing.Perform();
			metrics.sewing_microseconds += static_cast<uint64_t>(
				std::chrono::duration_cast<std::chrono::microseconds>(
					std::chrono::steady_clock::now() - outer_sew_start).count());
			++metrics.sew_count;
			if (outer_boundary_face_count == 0
				|| outer_boundary_sewing.NbFreeEdges() != 0
				|| outer_boundary_sewing.NbMultipleEdges() != 0)
			{
				throw std::runtime_error(
					"The collector outer union cells did not retain one manifold boundary; faces="
						+ std::to_string(outer_boundary_face_count)
						+ "; freeEdges="
						+ std::to_string(outer_boundary_sewing.NbFreeEdges())
						+ "; multipleEdges="
						+ std::to_string(outer_boundary_sewing.NbMultipleEdges()) + ".");
			}
			BRepBuilderAPI_MakeSolid outer_solid_builder;
			size_t outer_shell_count = 0;
			TopoDS_Shell selected_outer_shell;
			double selected_outer_shell_area = 0;
			double selected_outer_shell_extent = 0;
			size_t selected_outer_shell_openings = 0;
			auto shell_face_matches_opening = [&](const TopoDS_Face& face,
				const fgcad_frame& frame,
				double radius)
			{
				BRepAdaptor_Surface surface(face, true);
				if (surface.GetType() != GeomAbs_Plane)
				{
					return false;
				}
				gp_Pln plane = surface.Plane();
				gp_Dir tangent = unit(frame.tangent);
				if (std::abs(plane.Axis().Direction().Dot(tangent)) < 1.0 - 1.0e-8
					|| plane.Distance(point(frame.origin)) > std::max(0.1, cell_fuzzy_tolerance * 10.0))
				{
					return false;
				}
				GProp_GProps properties;
				BRepGProp::SurfaceProperties(face, properties);
				gp_Vec offset(point(frame.origin), properties.CentreOfMass());
				double axial = offset.Dot(gp_Vec(tangent));
				double radial = (
					offset - gp_Vec(tangent) * axial).Magnitude();
				return radial <= radius * 1.1;
			};
			for (TopExp_Explorer shell_explorer(
				outer_boundary_sewing.SewedShape(),
				TopAbs_SHELL);
				shell_explorer.More();
				shell_explorer.Next())
			{
				TopoDS_Shell shell = TopoDS::Shell(shell_explorer.Current());
				if (!BRep_Tool::IsClosed(shell))
				{
					shell.Closed(true);
				}
				GProp_GProps shell_properties;
				BRepGProp::SurfaceProperties(shell, shell_properties);
				double shell_area = std::abs(shell_properties.Mass());
				Bnd_Box shell_bounds;
				BRepBndLib::Add(shell, shell_bounds);
				double shell_extent = 0;
				if (!shell_bounds.IsVoid())
				{
					double min_x = 0;
					double min_y = 0;
					double min_z = 0;
					double max_x = 0;
					double max_y = 0;
					double max_z = 0;
					shell_bounds.Get(min_x, min_y, min_z, max_x, max_y, max_z);
					shell_extent = std::max(0.0, max_x - min_x)
						* std::max(0.0, max_y - min_y)
						* std::max(0.0, max_z - min_z);
				}
				size_t shell_opening_count = 0;
				for (TopExp_Explorer face_explorer(shell, TopAbs_FACE);
					face_explorer.More();
					face_explorer.Next())
				{
					TopoDS_Face face = TopoDS::Face(face_explorer.Current());
					bool matches = false;
					for (size_t inlet_index = 0;
						inlet_index < inlet_count && !matches;
						++inlet_index)
					{
						matches = shell_face_matches_opening(
							face,
							inlets[inlet_index].frame,
							radii(inlets[inlet_index].profile).first);
					}
					if (!matches)
					{
						matches = shell_face_matches_opening(
							face,
							system->outlet_frame,
							radii(system->outlet_profile).first);
					}
					shell_opening_count += matches ? 1u : 0u;
				}
				append_native_log(
					"collector",
					"Outer union shell candidate: openings="
						+ std::to_string(shell_opening_count)
						+ "; area=" + std::to_string(shell_area)
						+ "; extent=" + std::to_string(shell_extent) + ".");
				if (selected_outer_shell.IsNull()
					|| shell_opening_count > selected_outer_shell_openings
					|| (shell_opening_count == selected_outer_shell_openings
						&& shell_extent > selected_outer_shell_extent)
					|| (shell_opening_count == selected_outer_shell_openings
						&& shell_extent == selected_outer_shell_extent
						&& shell_area > selected_outer_shell_area))
				{
					selected_outer_shell = shell;
					selected_outer_shell_area = shell_area;
					selected_outer_shell_extent = shell_extent;
					selected_outer_shell_openings = shell_opening_count;
				}
				++outer_shell_count;
			}
			if (selected_outer_shell.IsNull())
			{
				throw std::runtime_error(
					"The collector outer union produced no enclosing shell.");
			}
			outer_solid_builder.Add(selected_outer_shell);
			append_native_log(
				"collector",
				"Outer union boundary selected its enclosing shell: shells="
					+ std::to_string(outer_shell_count)
					+ "; selectedArea="
					+ std::to_string(selected_outer_shell_area)
					+ "; selectedExtent="
					+ std::to_string(selected_outer_shell_extent)
					+ "; selectedOpenings="
					+ std::to_string(selected_outer_shell_openings) + ".");
			TopoDS_Solid outer_union = outer_solid_builder.Solid();
			if (!BRepLib::OrientClosedSolid(outer_union))
			{
				throw std::runtime_error(
					"The collector outer union could not be oriented as a finite solid.");
			}
			NCollection_List<TopoDS_Shape> opening_faces;
			auto matches_opening = [&](const TopoDS_Face& face,
				const fgcad_frame& frame,
				double radius)
			{
				return shell_face_matches_opening(face, frame, radius);
			};
			for (TopExp_Explorer face_explorer(outer_union, TopAbs_FACE);
				face_explorer.More();
				face_explorer.Next())
			{
				TopoDS_Face face = TopoDS::Face(face_explorer.Current());
				bool remove = false;
				for (size_t inlet_index = 0;
					inlet_index < inlet_count && !remove;
					++inlet_index)
				{
					remove = matches_opening(
						face,
						inlets[inlet_index].frame,
						radii(inlets[inlet_index].profile).first);
				}
				if (!remove)
				{
					remove = matches_opening(
						face,
						system->outlet_frame,
						radii(system->outlet_profile).first);
				}
				if (remove)
				{
					opening_faces.Append(face);
				}
			}
			if (opening_faces.Size() != static_cast<int>(inlet_count + 1))
			{
				throw std::runtime_error(
					"The collector outer merge retained "
						+ std::to_string(opening_faces.Size())
						+ " opening caps; expected "
						+ std::to_string(inlet_count + 1) + ".");
			}
			BRepOffsetAPI_MakeThickSolid thick_wall;
			thick_wall.MakeThickSolidByJoin(
				outer_union,
				opening_faces,
				-system->outlet_profile.wall_thickness,
				std::max(Precision::Confusion() * 10.0, cell_fuzzy_tolerance),
				BRepOffset_Skin,
				false,
				false,
				GeomAbs_Intersection,
				true);
			if (!thick_wall.IsDone() || thick_wall.Shape().IsNull())
			{
				throw std::runtime_error(
					"The connected collector outer merge could not be hollowed inward.");
			}
			cell_result = thick_wall.Shape();
		}
		if (false)
		{
		NCollection_List<TopoDS_Shape> merge_arguments;
		for (const TopoDS_Shape& outer : outer_volumes)
		{
			merge_arguments.Append(outer);
		}
		for (const TopoDS_Shape& inner : inner_volumes)
		{
			merge_arguments.Append(inner);
		}
		BOPAlgo_CellsBuilder wall_cells;
		wall_cells.SetArguments(merge_arguments);
		wall_cells.SetNonDestructive(true);
		wall_cells.SetRunParallel(true);
		wall_cells.SetFuzzyValue(cell_fuzzy_tolerance);
		wall_cells.Perform();
		if (wall_cells.HasErrors())
		{
			std::ostringstream errors;
			wall_cells.DumpErrors(errors);
			throw std::runtime_error(
				"The collector N-to-one general fuse failed while partitioning wall cells: "
					+ errors.str());
		}
		auto classifies_inside = [&](const TopoDS_Shape& solid, const gp_Pnt& sample)
		{
			BRepClass3d_SolidClassifier classifier(solid, sample, cell_fuzzy_tolerance);
			return classifier.State() == TopAbs_IN;
		};
		auto is_wall_sample = [&](const gp_Pnt& sample)
		{
			bool inside_outer = std::any_of(
				outer_volumes.begin(),
				outer_volumes.end(),
				[&](const TopoDS_Shape& outer)
				{
					return classifies_inside(outer, sample);
				});
			if (!inside_outer)
			{
				return false;
			}
			return std::none_of(
				inner_volumes.begin(),
				inner_volumes.end(),
				[&](const TopoDS_Shape& inner)
				{
					return classifies_inside(inner, sample);
				});
		};
			auto find_cell_interior_point = [&](const TopoDS_Solid& source, gp_Pnt& sample)
		{
			TopoDS_Solid cell = source;
			BRepLib::OrientClosedSolid(cell);
			gp_Pnt oriented_face_fallback;
			bool has_oriented_face_fallback = false;
			gp_Pnt classified_interior_fallback;
			bool has_classified_interior_fallback = false;
			GProp_GProps properties;
			BRepGProp::VolumeProperties(cell, properties);
			sample = properties.CentreOfMass();
			if (classifies_inside(cell, sample))
			{
				classified_interior_fallback = sample;
				has_classified_interior_fallback = true;
			}
			Bnd_Box bounds;
			BRepBndLib::Add(cell, bounds);
			double local_scale = 1.0;
			if (!bounds.IsVoid())
			{
				double min_x = 0;
				double min_y = 0;
				double min_z = 0;
				double max_x = 0;
				double max_y = 0;
				double max_z = 0;
				bounds.Get(min_x, min_y, min_z, max_x, max_y, max_z);
				local_scale = std::max(
					1.0,
					gp_Vec(
						gp_Pnt(min_x, min_y, min_z),
						gp_Pnt(max_x, max_y, max_z)).Magnitude());
			}
			for (TopExp_Explorer face_explorer(cell, TopAbs_FACE);
				face_explorer.More();
				face_explorer.Next())
			{
				TopoDS_Face face = TopoDS::Face(face_explorer.Current());
				gp_Pnt surface_point;
				double u = 0;
				double v = 0;
				double parameter = 0.5;
				gp_Vec derivative_u;
				gp_Vec derivative_v;
				if (!BRepClass3d_SolidExplorer::FindAPointInTheFace(
					face,
					surface_point,
					u,
					v,
					parameter,
					derivative_u,
					derivative_v))
				{
					continue;
				}
				gp_Vec normal = derivative_u.Crossed(derivative_v);
				if (normal.SquareMagnitude() <= Precision::SquareConfusion())
				{
					continue;
				}
				normal.Normalize();
				if (face.Orientation() == TopAbs_REVERSED)
				{
					normal.Reverse();
				}
				for (double fraction : { 1.0e-6, 1.0e-5, 1.0e-4, 1.0e-3, 1.0e-2 })
				{
					double distance = std::max(
						cell_fuzzy_tolerance * 2.0,
						local_scale * fraction);
					gp_Pnt inward_candidate = surface_point.Translated(
						-normal * distance);
					if (is_wall_sample(inward_candidate))
					{
						sample = inward_candidate;
						return true;
					}
					if (!has_oriented_face_fallback)
					{
						oriented_face_fallback = inward_candidate;
						has_oriented_face_fallback = true;
					}
					for (double sign : { -1.0, 1.0 })
					{
						gp_Pnt candidate = surface_point.Translated(normal * (sign * distance));
						if (classifies_inside(cell, candidate))
						{
							if (!has_classified_interior_fallback)
							{
								classified_interior_fallback = candidate;
								has_classified_interior_fallback = true;
							}
						}
					}
				}
			}
			if (has_classified_interior_fallback)
			{
				sample = classified_interior_fallback;
				return true;
			}
			if (has_oriented_face_fallback)
			{
				sample = oriented_face_fallback;
				return true;
			}
			return std::isfinite(sample.X())
				&& std::isfinite(sample.Y())
				&& std::isfinite(sample.Z());
		};
		std::vector<TopoDS_Solid> selected_cells;
		size_t unclassified_cell_count = 0;
		for (TopExp_Explorer cell_explorer(wall_cells.GetAllParts(), TopAbs_SOLID);
			cell_explorer.More();
			cell_explorer.Next())
		{
			TopoDS_Solid cell = TopoDS::Solid(cell_explorer.Current());
			gp_Pnt sample;
			if (!find_cell_interior_point(cell, sample))
			{
				++unclassified_cell_count;
				continue;
			}
			if (is_wall_sample(sample))
			{
				BRepLib::OrientClosedSolid(cell);
				selected_cells.push_back(cell);
			}
		}
		append_native_log(
			"collector",
			"Classified general-fuse wall cells: selected="
				+ std::to_string(selected_cells.size())
				+ "; ignoredDegenerate="
				+ std::to_string(unclassified_cell_count) + ".");
		if (selected_cells.empty())
		{
			throw std::runtime_error(
				"The collector wall partition did not contain any classified wall cells.");
		}
		struct boundary_face_occurrence
		{
			TopoDS_Face face;
			size_t forward_count{};
			size_t reversed_count{};
		};
		std::vector<boundary_face_occurrence> face_occurrences;
		for (const TopoDS_Solid& cell : selected_cells)
		{
			for (TopExp_Explorer face_explorer(cell, TopAbs_FACE);
				face_explorer.More();
				face_explorer.Next())
			{
				TopoDS_Face face = TopoDS::Face(face_explorer.Current());
				auto occurrence = std::find_if(
					face_occurrences.begin(),
					face_occurrences.end(),
					[&](const boundary_face_occurrence& candidate)
					{
						return candidate.face.IsSame(face);
					});
				if (occurrence == face_occurrences.end())
				{
					face_occurrences.push_back({
						face,
						face.Orientation() == TopAbs_FORWARD ? 1u : 0u,
						face.Orientation() == TopAbs_REVERSED ? 1u : 0u });
				}
				else if (face.Orientation() == TopAbs_FORWARD)
				{
					++occurrence->forward_count;
				}
				else if (face.Orientation() == TopAbs_REVERSED)
				{
					++occurrence->reversed_count;
				}
			}
		}
		double wall_boundary_tolerance = std::max(
			cell_fuzzy_tolerance,
			system->outlet_profile.wall_thickness * 0.05);
		BRepBuilderAPI_Sewing wall_boundary_sewing(
			wall_boundary_tolerance,
			true,
			true,
			false,
			false);
		size_t boundary_face_count = 0;
		for (const boundary_face_occurrence& occurrence : face_occurrences)
		{
			if (occurrence.forward_count == occurrence.reversed_count)
			{
				continue;
			}
			TopoDS_Face face = occurrence.face;
			face.Orientation(
				occurrence.forward_count > occurrence.reversed_count
					? TopAbs_FORWARD
					: TopAbs_REVERSED);
			wall_boundary_sewing.Add(face);
			++boundary_face_count;
		}
		auto boundary_sew_start = std::chrono::steady_clock::now();
		wall_boundary_sewing.Perform();
		metrics.sewing_microseconds += static_cast<uint64_t>(
			std::chrono::duration_cast<std::chrono::microseconds>(
				std::chrono::steady_clock::now() - boundary_sew_start).count());
		++metrics.sew_count;
		if (boundary_face_count == 0
			|| wall_boundary_sewing.NbFreeEdges() != 0
			|| wall_boundary_sewing.NbMultipleEdges() != 0)
		{
			std::ostringstream edge_diagnostics;
			for (int index = 1; index <= wall_boundary_sewing.NbFreeEdges(); ++index)
			{
				GProp_GProps properties;
				BRepGProp::LinearProperties(
					wall_boundary_sewing.FreeEdge(index),
					properties);
				const gp_Pnt& center = properties.CentreOfMass();
				edge_diagnostics << " [" << index
					<< ":length=" << properties.Mass()
					<< ";center=" << center.X() << '/' << center.Y() << '/'
					<< center.Z() << ']';
			}
			append_native_log(
				"collector",
				"Unsewn classified wall edges:" + edge_diagnostics.str());
			throw std::runtime_error(
				"The classified collector wall cells did not retain one manifold boundary; cells="
					+ std::to_string(selected_cells.size())
					+ "; faces=" + std::to_string(boundary_face_count)
					+ "; freeEdges="
					+ std::to_string(wall_boundary_sewing.NbFreeEdges())
					+ "; multipleEdges="
					+ std::to_string(wall_boundary_sewing.NbMultipleEdges()) + ".");
		}
		BRepBuilderAPI_MakeSolid classified_wall_builder;
		size_t classified_shell_count = 0;
		for (TopExp_Explorer shell_explorer(
			wall_boundary_sewing.SewedShape(),
			TopAbs_SHELL);
			shell_explorer.More();
			shell_explorer.Next())
		{
			TopoDS_Shell shell = TopoDS::Shell(shell_explorer.Current());
			if (!BRep_Tool::IsClosed(shell))
			{
				shell.Closed(true);
			}
			classified_wall_builder.Add(shell);
			++classified_shell_count;
		}
		if (classified_shell_count != 1)
		{
			throw std::runtime_error(
				"The classified collector wall produced "
					+ std::to_string(classified_shell_count)
					+ " closed shells instead of exactly one.");
		}
		cell_result = classified_wall_builder.Solid();
		}
		metrics.merge_microseconds += static_cast<uint64_t>(
			std::chrono::duration_cast<std::chrono::microseconds>(
				std::chrono::steady_clock::now() - merge_start_time).count());
		++metrics.merge_boolean_count;
		#endif
		{
			NCollection_List<TopoDS_Shape> merge_arguments;
			NCollection_List<TopoDS_Shape> inner_arguments;
			for (const TopoDS_Shape& outer : outer_volumes)
			{
				merge_arguments.Append(outer);
			}
			for (const TopoDS_Shape& inner : inner_volumes)
			{
				merge_arguments.Append(inner);
				inner_arguments.Append(inner);
			}
			BOPAlgo_CellsBuilder wall_cells;
			wall_cells.SetArguments(merge_arguments);
			wall_cells.SetNonDestructive(true);
			wall_cells.SetRunParallel(true);
			wall_cells.SetUseOBB(true);
			wall_cells.SetFuzzyValue(cell_fuzzy_tolerance);
			wall_cells.Perform();
			if (wall_cells.HasErrors())
			{
				std::ostringstream errors;
				wall_cells.DumpErrors(errors);
				throw std::runtime_error(
					"The hollow collector N-to-one partition failed: " + errors.str());
			}
			for (const TopoDS_Shape& outer : outer_volumes)
			{
				NCollection_List<TopoDS_Shape> outer_argument;
				outer_argument.Append(outer);
				wall_cells.AddToResult(
					outer_argument,
					inner_arguments,
					1,
					false);
			}
			wall_cells.RemoveInternalBoundaries();
			if (wall_cells.HasErrors() || wall_cells.Shape().IsNull())
			{
				std::ostringstream errors;
				wall_cells.DumpErrors(errors);
				throw std::runtime_error(
					"The hollow collector N-to-one wall selection failed: "
						+ errors.str());
			}
			TopoDS_Shape selected_wall = wall_cells.Shape();
			wall_cells.RemoveAllFromResult();
			for (const TopoDS_Shape& inner : inner_volumes)
			{
				NCollection_List<TopoDS_Shape> inner_argument;
				NCollection_List<TopoDS_Shape> no_avoidance;
				inner_argument.Append(inner);
				wall_cells.AddToResult(
					inner_argument,
					no_avoidance,
					2,
					false);
			}
			wall_cells.RemoveInternalBoundaries();
			TopoDS_Shape gas_union = wall_cells.Shape();
			TopoDS_Solid connected_gas;
			size_t gas_solid_count = 0;
			for (TopExp_Explorer gas_explorer(gas_union, TopAbs_SOLID);
				gas_explorer.More();
				gas_explorer.Next())
			{
				connected_gas = TopoDS::Solid(gas_explorer.Current());
				++gas_solid_count;
			}
			if (wall_cells.HasErrors()
				|| gas_solid_count != 1
				|| connected_gas.IsNull()
				|| !BRepLib::OrientClosedSolid(connected_gas))
			{
				std::ostringstream errors;
				wall_cells.DumpErrors(errors);
				throw std::runtime_error(
					"The collector gas-flow union is not one connected solid; solids="
						+ std::to_string(gas_solid_count)
						+ "; errors=" + errors.str());
			}
			auto require_inside_connected_gas = [&](const gp_Pnt& sample,
				const std::string& opening)
			{
				BRepClass3d_SolidClassifier classifier(
					connected_gas,
					sample,
					cell_fuzzy_tolerance);
				++metrics.classification_count;
				if (classifier.State() != TopAbs_IN)
				{
					throw std::runtime_error(
						"The collector gas-flow union does not contain the "
							+ opening + " interior sample.");
				}
			};
			for (size_t index = 0; index < inlet_count; ++index)
			{
				auto inlet_radii = radii(inlets[index].profile);
				double sample_distance = std::max(
					cell_fuzzy_tolerance * 10.0,
					std::min(
						inlets[index].branch_start_handle_length * 0.1,
						inlet_radii.second * 0.25));
				require_inside_connected_gas(
					point(inlets[index].frame.origin).Translated(
						gp_Vec(unit(inlets[index].frame.tangent)) * sample_distance),
					"inlet " + std::to_string(index + 1));
			}
			auto outlet_inner_radius = radii(system->outlet_profile).second;
			double outlet_sample_distance = std::max(
				cell_fuzzy_tolerance * 10.0,
				std::min(
					system->outlet_stub_length * 0.1,
					outlet_inner_radius * 0.25));
			require_inside_connected_gas(
				point(system->outlet_frame.origin).Translated(
					-gp_Vec(unit(system->outlet_frame.tangent))
						* outlet_sample_distance),
				"outlet");
			cell_result = selected_wall;
			metrics.classification_count += static_cast<uint32_t>(outer_volumes.size());
		}
		metrics.merge_microseconds += static_cast<uint64_t>(
			std::chrono::duration_cast<std::chrono::microseconds>(
				std::chrono::steady_clock::now() - merge_start_time).count());
		++metrics.merge_boolean_count;
		TopoDS_Solid collector_solid;
		size_t collector_solid_count = 0;
		double collector_solid_volume = 0;
		double discarded_cell_volume = 0;
		for (TopExp_Explorer explorer(cell_result, TopAbs_SOLID);
			explorer.More();
			explorer.Next())
		{
			GProp_GProps properties;
			BRepGProp::VolumeProperties(explorer.Current(), properties);
			double volume = std::abs(properties.Mass());
			if (volume > collector_solid_volume)
			{
				discarded_cell_volume += collector_solid_volume;
				collector_solid_volume = volume;
				collector_solid = TopoDS::Solid(explorer.Current());
			}
			else
			{
				discarded_cell_volume += volume;
			}
			++collector_solid_count;
		}
		double discarded_cell_ceiling = std::max(
			Precision::Confusion(),
			collector_solid_volume * 0.005);
		bool collector_orientation_valid = !collector_solid.IsNull()
			&& BRepLib::OrientClosedSolid(collector_solid);
		if (collector_solid.IsNull()
			|| discarded_cell_volume > discarded_cell_ceiling
			|| !collector_orientation_valid)
		{
			std::ostringstream solid_diagnostics;
			size_t diagnostic_index = 0;
			for (TopExp_Explorer explorer(cell_result, TopAbs_SOLID);
				explorer.More();
				explorer.Next())
			{
				GProp_GProps properties;
				BRepGProp::VolumeProperties(explorer.Current(), properties);
				const gp_Pnt& center = properties.CentreOfMass();
				solid_diagnostics << " [" << diagnostic_index++
					<< ":volume=" << std::abs(properties.Mass())
					<< ";center=" << center.X() << '/' << center.Y() << '/'
					<< center.Z() << ']';
			}
			throw std::runtime_error(
				"The collector N-to-one general fuse did not produce one valid connected wall solid; solids="
					+ std::to_string(collector_solid_count)
					+ "; dominantVolume=" + std::to_string(collector_solid_volume)
					+ "; discardedVolume=" + std::to_string(discarded_cell_volume)
					+ "; discardedCeiling=" + std::to_string(discarded_cell_ceiling)
					+ "; oriented=" + std::to_string(collector_orientation_valid)
					+ "; components=" + solid_diagnostics.str() + ".");
		}
		if (collector_solid_count > 1)
		{
			append_native_log(
				"collector",
				"Discarded numerical general-fuse cells outside the unique dominant wall: cells="
					+ std::to_string(collector_solid_count - 1)
					+ "; volume=" + std::to_string(discarded_cell_volume)
					+ "; dominantVolume=" + std::to_string(collector_solid_volume) + ".");
		}
		++metrics.validation_count;
		const TopAbs_Orientation collector_cell_orientation = collector_solid.Orientation();
		collector_solid.Orientation(TopAbs_FORWARD);
		collector_wall = collector_solid;
		std::array<size_t, 4> collector_wall_face_orientations{};
		std::array<size_t, 4> collector_shell_face_orientations{};
		for (TopExp_Explorer face_explorer(collector_wall, TopAbs_FACE);
			face_explorer.More();
			face_explorer.Next())
		{
			const int orientation = static_cast<int>(face_explorer.Current().Orientation());
			if (orientation >= 0
				&& orientation < static_cast<int>(collector_wall_face_orientations.size()))
			{
				++collector_wall_face_orientations[static_cast<size_t>(orientation)];
			}
		}
		for (TopExp_Explorer shell_explorer(collector_wall, TopAbs_SHELL);
			shell_explorer.More();
			shell_explorer.Next())
		{
			for (TopoDS_Iterator face_iterator(shell_explorer.Current());
				face_iterator.More();
				face_iterator.Next())
			{
				if (face_iterator.Value().ShapeType() != TopAbs_FACE)
				{
					continue;
				}
				const int orientation = static_cast<int>(face_iterator.Value().Orientation());
				if (orientation >= 0
					&& orientation < static_cast<int>(collector_shell_face_orientations.size()))
				{
					++collector_shell_face_orientations[static_cast<size_t>(orientation)];
				}
			}
		}
		remap_sources_to_result_surfaces(collector_wall, replacement.sources);
		append_native_log(
			"collector",
			"Collector wall published from one N-to-one general fuse: outerInputs="
				+ std::to_string(outer_volumes.size())
				+ "; innerInputs=" + std::to_string(inner_volumes.size())
				+ "; fuzzyTolerance=" + std::to_string(cell_fuzzy_tolerance)
				+ "; cellOrientation="
				+ std::to_string(static_cast<int>(collector_cell_orientation))
				+ "; faceOrientations="
				+ std::to_string(collector_wall_face_orientations[0]) + "/"
				+ std::to_string(collector_wall_face_orientations[1]) + "/"
				+ std::to_string(collector_wall_face_orientations[2]) + "/"
				+ std::to_string(collector_wall_face_orientations[3])
				+ "; shellFaceOrientations="
				+ std::to_string(collector_shell_face_orientations[0]) + "/"
				+ std::to_string(collector_shell_face_orientations[1]) + "/"
				+ std::to_string(collector_shell_face_orientations[2]) + "/"
				+ std::to_string(collector_shell_face_orientations[3])
				+ "; mergeBooleans=1; interfaceBooleans=0.");
		collector_couplers.clear();
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
		std::vector<const runner_record*> members;
		members.reserve(replacement.runner_ids.size());
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
			members.push_back(member);
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
		auto face_area = [](const TopoDS_Face& face)
		{
			GProp_GProps properties;
			BRepGProp::SurfaceProperties(face, properties);
			return std::abs(properties.Mass());
		};
		auto face_center = [](const TopoDS_Face& face)
		{
			GProp_GProps properties;
			BRepGProp::SurfaceProperties(face, properties);
			return properties.CentreOfMass();
		};
		auto count_wires = [](const TopoDS_Face& face)
		{
			size_t count = 0;
			for (TopExp_Explorer explorer(face, TopAbs_WIRE);
				explorer.More();
				explorer.Next())
			{
				++count;
			}
			return count;
		};
		auto edge_support_gap = [](const TopoDS_Edge& first, const TopoDS_Edge& second)
		{
			BRepAdaptor_Curve first_curve(first);
			BRepAdaptor_Curve second_curve(second);
			if (first_curve.GetType() == GeomAbs_Circle
				&& second_curve.GetType() == GeomAbs_Circle)
			{
				gp_Circ first_circle = first_curve.Circle();
				gp_Circ second_circle = second_curve.Circle();
				double axis_dot = std::abs(
					first_circle.Axis().Direction().Dot(second_circle.Axis().Direction()));
				double axis_gap = std::max(first_circle.Radius(), second_circle.Radius())
					* std::sqrt(std::max(0.0, 1.0 - axis_dot * axis_dot));
				return std::max({
					first_circle.Location().Distance(second_circle.Location()),
					std::abs(first_circle.Radius() - second_circle.Radius()),
					axis_gap
				});
			}
			double first_start = first_curve.FirstParameter();
			double first_end = first_curve.LastParameter();
			double second_geometry_start = 0;
			double second_geometry_end = 0;
			Handle(Geom_Curve) second_geometry = BRep_Tool::Curve(
				second,
				second_geometry_start,
				second_geometry_end);
			if (second_geometry.IsNull())
			{
				return std::numeric_limits<double>::infinity();
			}
			double maximum_gap = 0;
			for (int sample = 0; sample <= 8; ++sample)
			{
				double parameter = first_start
					+ (first_end - first_start) * static_cast<double>(sample) / 8.0;
				GeomAPI_ProjectPointOnCurve projection(
					first_curve.Value(parameter),
					second_geometry,
					second_curve.FirstParameter(),
					second_curve.LastParameter());
				if (projection.NbPoints() == 0)
				{
					return std::numeric_limits<double>::infinity();
				}
				maximum_gap = std::max(maximum_gap, projection.LowerDistance());
			}
			return maximum_gap;
		};
		auto boundary_gap = [&](const TopoDS_Face& first, const TopoDS_Face& second)
		{
			std::vector<TopoDS_Edge> first_edges;
			std::vector<TopoDS_Edge> second_edges;
			for (TopExp_Explorer explorer(first, TopAbs_EDGE);
				explorer.More();
				explorer.Next())
			{
				first_edges.push_back(TopoDS::Edge(explorer.Current()));
			}
			for (TopExp_Explorer explorer(second, TopAbs_EDGE);
				explorer.More();
				explorer.Next())
			{
				second_edges.push_back(TopoDS::Edge(explorer.Current()));
			}
			auto directed_gap = [&](const std::vector<TopoDS_Edge>& from,
				const std::vector<TopoDS_Edge>& to)
			{
				double result = 0;
				for (const TopoDS_Edge& source : from)
				{
					double best = std::numeric_limits<double>::infinity();
					for (const TopoDS_Edge& target : to)
					{
						best = std::min(best, edge_support_gap(source, target));
					}
					result = std::max(result, best);
				}
				return result;
			};
			return std::max(
				directed_gap(first_edges, second_edges),
				directed_gap(second_edges, first_edges));
		};
		auto boundary_partition_gap = [&](const TopoDS_Face& reference,
			const std::vector<TopoDS_Face>& partition)
		{
			struct edge_occurrence
			{
				TopoDS_Edge edge;
				size_t count{};
			};
			std::vector<edge_occurrence> partition_edges;
			for (const TopoDS_Face& face : partition)
			{
				for (TopExp_Explorer explorer(face, TopAbs_EDGE);
					explorer.More();
					explorer.Next())
				{
					TopoDS_Edge edge = TopoDS::Edge(explorer.Current());
					auto occurrence = std::find_if(
						partition_edges.begin(),
						partition_edges.end(),
						[&](const edge_occurrence& candidate)
						{
							return candidate.edge.IsSame(edge);
						});
					if (occurrence == partition_edges.end())
					{
						partition_edges.push_back({ edge, 1 });
					}
					else
					{
						++occurrence->count;
					}
				}
			}
			std::vector<TopoDS_Edge> reference_edges;
			std::vector<TopoDS_Edge> exterior_partition_edges;
			for (TopExp_Explorer explorer(reference, TopAbs_EDGE);
				explorer.More();
				explorer.Next())
			{
				reference_edges.push_back(TopoDS::Edge(explorer.Current()));
			}
			for (const edge_occurrence& occurrence : partition_edges)
			{
				if (occurrence.count == 1)
				{
					exterior_partition_edges.push_back(occurrence.edge);
				}
			}
			auto directed_gap = [&](const std::vector<TopoDS_Edge>& from,
				const std::vector<TopoDS_Edge>& to)
			{
				double result = 0;
				for (const TopoDS_Edge& source : from)
				{
					double best = std::numeric_limits<double>::infinity();
					for (const TopoDS_Edge& target : to)
					{
						best = std::min(best, edge_support_gap(source, target));
					}
					result = std::max(result, best);
				}
				return result;
			};
			return std::max(
				directed_gap(reference_edges, exterior_partition_edges),
				directed_gap(exterior_partition_edges, reference_edges));
		};
		auto find_interface_caps = [&](const TopoDS_Shape& shape,
			const fgcad_frame& frame,
			double expected_area,
			double outer_radius,
			const char* owner)
		{
			std::vector<TopoDS_Face> selected;
			size_t candidate_count = 0;
			gp_Pnt origin = point(frame.origin);
			gp_Dir tangent = unit(frame.tangent);
			double plane_tolerance = std::max(
				Precision::Confusion() * 10.0,
				outer_radius * 1.0e-6);
			for (TopExp_Explorer explorer(shape, TopAbs_FACE);
				explorer.More();
				explorer.Next())
			{
				TopoDS_Face candidate = TopoDS::Face(explorer.Current());
				BRepAdaptor_Surface surface(candidate, true);
				if (surface.GetType() != GeomAbs_Plane)
				{
					continue;
				}
				gp_Pln plane = surface.Plane();
				double alignment = std::abs(
					plane.Axis().Direction().Dot(tangent));
				if (alignment < 1.0 - 1.0e-8)
				{
					continue;
				}
				++candidate_count;
				double plane_gap = plane.Distance(origin);
				gp_Vec center_offset(origin, face_center(candidate));
				double axial = center_offset.Dot(gp_Vec(tangent));
				double radial = (center_offset - gp_Vec(tangent) * axial).Magnitude();
				if (plane_gap <= plane_tolerance
					&& radial <= outer_radius * 1.05)
				{
					selected.push_back(candidate);
				}
			}
			double selected_area = 0;
			for (const TopoDS_Face& face : selected)
			{
				selected_area += face_area(face);
			}
			double allowed_area_error = std::max(
				1.0e-4,
				expected_area * 1.0e-4);
			if (selected.empty()
				|| std::abs(selected_area - expected_area) > allowed_area_error)
			{
				throw std::runtime_error(
					std::string(owner)
					+ " has no complete annular interface partition at the constrained runner endpoint; candidates="
					+ std::to_string(candidate_count)
					+ "; selectedFaces=" + std::to_string(selected.size())
					+ "; selectedArea=" + std::to_string(selected_area)
					+ "; expectedArea=" + std::to_string(expected_area) + ".");
			}
			return selected;
		};

		std::vector<std::vector<TopoDS_Face>> collector_caps_by_inlet;
		std::vector<TopoDS_Face> collector_caps;
		std::vector<TopoDS_Face> runner_caps;
		collector_caps_by_inlet.reserve(members.size());
		runner_caps.reserve(members.size());
		double measured_gap = 0;
		double maximum_source_tolerance = Precision::Confusion();
		auto include_tolerances = [&](const TopoDS_Shape& shape)
		{
			for (TopExp_Explorer explorer(shape, TopAbs_EDGE);
				explorer.More();
				explorer.Next())
			{
				maximum_source_tolerance = std::max(
					maximum_source_tolerance,
					BRep_Tool::Tolerance(TopoDS::Edge(explorer.Current())));
			}
			for (TopExp_Explorer explorer(shape, TopAbs_VERTEX);
				explorer.More();
				explorer.Next())
			{
				maximum_source_tolerance = std::max(
					maximum_source_tolerance,
					BRep_Tool::Tolerance(TopoDS::Vertex(explorer.Current())));
			}
		};
		for (size_t index = 0; index < members.size(); ++index)
		{
			const runner_record& member = *members[index];
			if (member.end_cap.IsNull())
			{
				throw std::runtime_error(
					"A collector member runner did not retain its exact terminal cap.");
			}
			const std::string& runner_id = replacement.runner_ids[index];
			auto inlet = std::find_if(
				inlets,
				inlets + inlet_count,
				[&](const fgcad_collector_inlet& candidate)
				{
					return require_text(candidate.runner_id, "runner_id") == runner_id;
				});
			if (inlet == inlets + inlet_count)
			{
				throw std::runtime_error(
					"A collector member runner has no matching inlet specification.");
			}
			double expected_area = face_area(member.end_cap);
			TopoDS_Face runner_cap = member.end_cap;
			double outer_radius = inlet->profile.kind == FGCAD_PROFILE_CIRCULAR
				? inlet->profile.outer_diameter * 0.5
				: inlet->profile.equivalent_radius + inlet->profile.wall_thickness;
			std::vector<TopoDS_Face> inlet_caps = find_interface_caps(
				collector_wall,
				inlet->frame,
				expected_area,
				outer_radius,
				"The collector");
			measured_gap = std::max(
				measured_gap,
				boundary_partition_gap(runner_cap, inlet_caps));
			runner_caps.push_back(runner_cap);
			collector_caps_by_inlet.push_back(inlet_caps);
			include_tolerances(runner_cap);
			for (const TopoDS_Face& collector_cap : inlet_caps)
			{
				collector_caps.push_back(collector_cap);
				include_tolerances(collector_cap);
			}
		}

		Bnd_Box assembly_bounds;
		BRepBndLib::Add(collector_wall, assembly_bounds, false);
		for (const runner_record* member : members)
		{
			BRepBndLib::Add(member->shape, assembly_bounds, false);
		}
		double local_scale = 1.0;
		if (!assembly_bounds.IsVoid())
		{
			double minimum_x = 0;
			double minimum_y = 0;
			double minimum_z = 0;
			double maximum_x = 0;
			double maximum_y = 0;
			double maximum_z = 0;
			assembly_bounds.Get(
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
		double minimum_wall = system->outlet_profile.wall_thickness;
		for (size_t index = 0; index < inlet_count; ++index)
		{
			minimum_wall = std::min(
				minimum_wall,
				inlets[index].profile.wall_thickness);
		}
		double selected_tolerance = std::max({
			Precision::Confusion(),
			maximum_source_tolerance,
			local_scale * 1.0e-10,
			measured_gap * 1.05
		});
		metrics.measured_gap = measured_gap;
		metrics.selected_tolerance = selected_tolerance;
		double tolerance_ceiling = std::min(
			minimum_wall * 0.05,
			std::max(1.0e-4, local_scale * 2.0e-5));
		if (measured_gap > tolerance_ceiling || selected_tolerance > tolerance_ceiling)
		{
			throw std::runtime_error(
				"A runner/collector interface gap exceeds the wall-scale sewing safety limit; "
				"measuredGap=" + std::to_string(measured_gap)
				+ "; selectedTolerance=" + std::to_string(selected_tolerance)
				+ "; ceiling=" + std::to_string(tolerance_ceiling) + ".");
		}

		auto add_unique_face = [](std::vector<TopoDS_Face>& faces, const TopoDS_Face& face)
		{
			if (std::none_of(
				faces.begin(),
				faces.end(),
				[&](const TopoDS_Face& existing) { return existing.IsSame(face); }))
			{
				faces.push_back(face);
			}
		};
		auto adjacent_faces = [&](const TopoDS_Shape& owner,
			const std::vector<TopoDS_Face>& caps)
		{
			std::vector<TopoDS_Face> result;
			for (const TopoDS_Face& cap : caps)
			{
				for (TopExp_Explorer cap_edges(cap, TopAbs_EDGE);
					cap_edges.More();
					cap_edges.Next())
				{
					TopoDS_Edge cap_edge = TopoDS::Edge(cap_edges.Current());
					for (TopExp_Explorer face_explorer(owner, TopAbs_FACE);
						face_explorer.More();
						face_explorer.Next())
					{
						TopoDS_Face face = TopoDS::Face(face_explorer.Current());
						if (std::any_of(
							caps.begin(),
							caps.end(),
							[&](const TopoDS_Face& candidate)
							{
								return candidate.IsSame(face);
							}))
						{
							continue;
						}
						for (TopExp_Explorer face_edges(face, TopAbs_EDGE);
							face_edges.More();
							face_edges.Next())
						{
							if (cap_edge.IsSame(face_edges.Current()))
							{
								add_unique_face(result, face);
								break;
							}
						}
					}
				}
			}
			return result;
		};
		std::vector<TopoDS_Face> collector_interface_faces;
		std::vector<std::vector<TopoDS_Face>> collector_interface_faces_by_inlet(
			members.size());
		std::vector<std::vector<TopoDS_Face>> runner_interface_faces(members.size());
		for (size_t index = 0; index < members.size(); ++index)
		{
			collector_interface_faces_by_inlet[index] = adjacent_faces(
				collector_wall,
				collector_caps_by_inlet[index]);
			for (const TopoDS_Face& face : collector_interface_faces_by_inlet[index])
			{
				add_unique_face(collector_interface_faces, face);
			}
			runner_interface_faces[index] = adjacent_faces(
				members[index]->shape,
				{ runner_caps[index] });
			if (runner_interface_faces[index].size() < 2)
			{
				throw std::runtime_error(
					"A runner terminal boundary did not retain both wall faces.");
			}
		}
		std::vector<std::pair<TopoDS_Edge, TopoDS_Shape>> boundary_edge_replacements;
		size_t contiguous_interface_edge_count = 0;
		auto retain_boundary_replacements = [&](
			BRepBuilderAPI_Sewing& sewing,
			const TopoDS_Face& face)
		{
			for (TopExp_Explorer edge_explorer(face, TopAbs_EDGE);
				edge_explorer.More();
				edge_explorer.Next())
			{
				TopoDS_Edge edge = TopoDS::Edge(edge_explorer.Current());
				if (!sewing.IsModifiedSubShape(edge))
				{
					continue;
				}
				TopoDS_Shape modified = sewing.ModifiedSubShape(edge);
				if (modified.IsNull())
				{
					continue;
				}
				auto existing = std::find_if(
					boundary_edge_replacements.begin(),
					boundary_edge_replacements.end(),
					[&](const auto& item) { return item.first.IsSame(edge); });
				if (existing == boundary_edge_replacements.end())
				{
					boundary_edge_replacements.emplace_back(edge, modified);
				}
				else
				{
					existing->second = modified;
				}
			}
		};
		auto interface_sewing_start = std::chrono::steady_clock::now();
		for (size_t index = 0; index < members.size(); ++index)
		{
			BRepBuilderAPI_Sewing boundary_sewing(
				selected_tolerance,
				true,
				true,
				false,
				false);
			for (const TopoDS_Face& face : collector_interface_faces_by_inlet[index])
			{
				boundary_sewing.Add(face);
			}
			for (const TopoDS_Face& face : runner_interface_faces[index])
			{
				boundary_sewing.Add(face);
			}
			boundary_sewing.Perform();
			if (boundary_sewing.NbMultipleEdges() != 0)
			{
				throw std::runtime_error(
					"An exact interface boundary remap is non-manifold.");
			}
			contiguous_interface_edge_count += static_cast<size_t>(
				boundary_sewing.NbContigousEdges());
			for (const TopoDS_Face& face : collector_interface_faces_by_inlet[index])
			{
				retain_boundary_replacements(boundary_sewing, face);
			}
			for (const TopoDS_Face& face : runner_interface_faces[index])
			{
				retain_boundary_replacements(boundary_sewing, face);
			}
		}
		metrics.sewing_microseconds += static_cast<uint64_t>(
			std::chrono::duration_cast<std::chrono::microseconds>(
				std::chrono::steady_clock::now() - interface_sewing_start).count());
		metrics.sew_count += static_cast<uint32_t>(members.size());
		append_native_log(
			"collector",
			"Interface cap staging: collectorCaps="
				+ std::to_string(collector_caps.size())
				+ "; runnerCaps=" + std::to_string(runner_caps.size())
				+ "; boundaryFaces="
				+ std::to_string(collector_interface_faces.size())
				+ "; contiguousEdges="
				+ std::to_string(contiguous_interface_edge_count));
		BRep_Builder assembly_builder;
		TopoDS_Shell direct_assembly_shell;
		assembly_builder.MakeShell(direct_assembly_shell);
		auto append_faces = [&](const TopoDS_Shape& shape,
			const std::vector<TopoDS_Face>& removed_caps)
		{
			for (TopExp_Explorer shell_explorer(shape, TopAbs_SHELL);
				shell_explorer.More();
				shell_explorer.Next())
			{
				TopoDS_Shell owner_shell = TopoDS::Shell(shell_explorer.Current());
				bool internal_shell = false;
				for (TopoDS_Iterator probe(owner_shell); probe.More(); probe.Next())
				{
					if (probe.Value().ShapeType() == TopAbs_FACE
						&& (probe.Value().Orientation() == TopAbs_INTERNAL
							|| probe.Value().Orientation() == TopAbs_EXTERNAL))
					{
						internal_shell = true;
						break;
					}
				}
				if (internal_shell)
				{
					continue;
				}
				for (TopoDS_Iterator face_iterator(owner_shell);
					face_iterator.More();
					face_iterator.Next())
				{
					if (face_iterator.Value().ShapeType() != TopAbs_FACE)
					{
						continue;
					}
					TopoDS_Face face = TopoDS::Face(face_iterator.Value());
				if (std::any_of(
					removed_caps.begin(),
					removed_caps.end(),
					[&](const TopoDS_Face& cap) { return cap.IsSame(face); }))
				{
					continue;
				}
				Handle(BRepTools_ReShape) edge_changes = new BRepTools_ReShape();
				bool has_edge_changes = false;
				for (TopExp_Explorer edge_explorer(face, TopAbs_EDGE);
					edge_explorer.More();
					edge_explorer.Next())
				{
					TopoDS_Edge edge = TopoDS::Edge(edge_explorer.Current());
					auto replacement = std::find_if(
						boundary_edge_replacements.begin(),
						boundary_edge_replacements.end(),
						[&](const auto& item) { return item.first.IsSame(edge); });
					if (replacement != boundary_edge_replacements.end())
					{
						edge_changes->Replace(edge, replacement->second);
						has_edge_changes = true;
					}
				}
				TopoDS_Shape adjusted_face = has_edge_changes
					? edge_changes->Apply(face)
					: TopoDS_Shape(face);
				for (TopExp_Explorer adjusted_faces(adjusted_face, TopAbs_FACE);
					adjusted_faces.More();
					adjusted_faces.Next())
				{
					assembly_builder.Add(direct_assembly_shell, adjusted_faces.Current());
				}
				}
			}
		};
		append_faces(collector_wall, collector_caps);
		for (size_t index = 0; index < members.size(); ++index)
		{
			append_faces(
				members[index]->shape,
				{ runner_caps[index] });
		}
		direct_assembly_shell.Closed(true);
		TopoDS_Shape sewed_assembly = direct_assembly_shell;
		append_native_log(
			"collector",
			"Interface topology assembly: sewOperations="
				+ std::to_string(members.size())
				+ "; interfaceBooleans=0");
		TopoDS_Shell assembly_shell;
		size_t assembly_shell_count = 0;
		if (sewed_assembly.ShapeType() == TopAbs_SHELL)
		{
			assembly_shell = TopoDS::Shell(sewed_assembly);
			assembly_shell_count = 1;
		}
		else
		{
			for (TopExp_Explorer explorer(sewed_assembly, TopAbs_SHELL);
				explorer.More();
				explorer.Next())
			{
				assembly_shell = TopoDS::Shell(explorer.Current());
				++assembly_shell_count;
			}
		}
		if (assembly_shell_count != 1
			|| assembly_shell.IsNull()
			|| !BRep_Tool::IsClosed(assembly_shell))
		{
			throw std::runtime_error(
				"Runner/collector sewing must produce exactly one closed shell; shells="
					+ std::to_string(assembly_shell_count) + ".");
		}
		BRep_Builder filtered_shell_builder;
		TopoDS_Shell filtered_shell;
		filtered_shell_builder.MakeShell(filtered_shell);
		size_t retained_face_count = 0;
		size_t raw_internal_face_count = 0;
		for (TopoDS_Iterator iterator(assembly_shell); iterator.More(); iterator.Next())
		{
			if (iterator.Value().ShapeType() != TopAbs_FACE)
			{
				continue;
			}
			TopoDS_Face normalized_face = TopoDS::Face(iterator.Value());
			if (normalized_face.Orientation() == TopAbs_INTERNAL)
			{
				++raw_internal_face_count;
				BRepAdaptor_Surface internal_surface(normalized_face, true);
				GProp_GProps internal_properties;
				BRepGProp::SurfaceProperties(normalized_face, internal_properties);
				append_native_log(
					"collector",
					"Internal sewn face: surface="
						+ std::to_string(static_cast<int>(internal_surface.GetType()))
						+ "; area=" + std::to_string(std::abs(internal_properties.Mass()))
						+ "; center=" + std::to_string(internal_properties.CentreOfMass().X())
						+ "/" + std::to_string(internal_properties.CentreOfMass().Y())
						+ "/" + std::to_string(internal_properties.CentreOfMass().Z()));
				continue;
			}
			if (normalized_face.Orientation() == TopAbs_EXTERNAL)
			{
				continue;
			}
			filtered_shell_builder.Add(filtered_shell, normalized_face);
			++retained_face_count;
		}
		append_native_log(
			"collector",
			"Raw sewn shell: retainedFaces=" + std::to_string(retained_face_count)
				+ "; internalFaces=" + std::to_string(raw_internal_face_count));
		filtered_shell.Closed(true);
		assembly_shell = filtered_shell;
		assembly_shell.Orientation(TopAbs_FORWARD);
		auto final_validation_start = std::chrono::steady_clock::now();
		ShapeFix_Solid solid_builder;
		solid_builder.SetPrecision(selected_tolerance);
		TopoDS_Shape fused = solid_builder.SolidFromShell(assembly_shell);
		if (fused.IsNull() || fused.ShapeType() != TopAbs_SOLID)
		{
			throw std::runtime_error(
				"The sewn runner/collector shell could not be oriented as a solid.");
		}
		// The direct shell assembly is known to need tolerance normalization after
		// edge remapping.  Repair it once, then perform one authoritative geometric
		// validation below.  Probing it first and constructing a second analyzer for
		// diagnostics doubled validation time on the Corsa 4-to-1 fixture.
		#if defined(FGCAD_ENABLE_EXPENSIVE_DIAGNOSTICS)
		if (solid_count(fused) == 1
			&& !BRepCheck_Analyzer(fused, true).IsValid())
		{
			BRepCheck_Analyzer diagnostic_analyzer(fused, true);
			std::ostringstream statuses;
			size_t status_count = 0;
			Handle(BRepCheck_Result) root_result = diagnostic_analyzer.Result(fused);
			if (!root_result.IsNull())
			{
				for (BRepCheck_Status status : root_result->Status())
				{
					if (status != BRepCheck_NoError)
					{
						statuses << "root:" << static_cast<int>(status) << ',';
						++status_count;
					}
				}
				root_result->InitContextIterator();
				while (root_result->MoreShapeInContext())
				{
					for (BRepCheck_Status status : root_result->StatusOnShape())
					{
						if (status != BRepCheck_NoError)
						{
							statuses << "rootc:" << static_cast<int>(status) << ',';
							++status_count;
						}
					}
					root_result->NextShapeInContext();
				}
			}
			std::array<size_t, 4> face_orientations{};
			for (TopExp_Explorer face_explorer(fused, TopAbs_FACE);
				face_explorer.More();
				face_explorer.Next())
			{
				const int orientation = static_cast<int>(face_explorer.Current().Orientation());
				if (orientation >= 0 && orientation < static_cast<int>(face_orientations.size()))
				{
					++face_orientations[static_cast<size_t>(orientation)];
				}
			}
			for (TopAbs_ShapeEnum type : {
				TopAbs_SOLID,
				TopAbs_SHELL,
				TopAbs_FACE,
				TopAbs_WIRE,
				TopAbs_EDGE })
			{
				for (TopExp_Explorer explorer(fused, type);
					explorer.More();
					explorer.Next())
				{
					Handle(BRepCheck_Result) result = diagnostic_analyzer.Result(
						explorer.Current());
					if (result.IsNull())
					{
						continue;
					}
					for (BRepCheck_Status status : result->Status())
					{
						if (status != BRepCheck_NoError)
						{
							statuses << static_cast<int>(type)
								<< ':' << static_cast<int>(status) << ',';
							if (type == TopAbs_FACE)
							{
								GProp_GProps invalid_face_properties;
								BRepGProp::SurfaceProperties(
									explorer.Current(),
									invalid_face_properties);
								statuses << "faceArea="
									<< std::abs(invalid_face_properties.Mass())
									<< ";faceCenter="
									<< invalid_face_properties.CentreOfMass().X()
									<< '/' << invalid_face_properties.CentreOfMass().Y()
									<< '/' << invalid_face_properties.CentreOfMass().Z()
									<< ',';
							}
							++status_count;
						}
					}
					result->InitContextIterator();
					while (result->MoreShapeInContext())
					{
						for (BRepCheck_Status status : result->StatusOnShape())
						{
							if (status != BRepCheck_NoError)
							{
								statuses << static_cast<int>(type)
									<< "c:" << static_cast<int>(status) << ',';
								if (type == TopAbs_FACE)
								{
									GProp_GProps invalid_face_properties;
									BRepGProp::SurfaceProperties(
										explorer.Current(),
										invalid_face_properties);
									statuses << "faceArea="
										<< std::abs(invalid_face_properties.Mass())
										<< ";faceCenter="
										<< invalid_face_properties.CentreOfMass().X()
										<< '/' << invalid_face_properties.CentreOfMass().Y()
										<< '/' << invalid_face_properties.CentreOfMass().Z()
										<< ',';
								}
								++status_count;
							}
						}
						result->NextShapeInContext();
					}
				}
			}
			append_native_log(
				"collector",
				"Sewn-system BRep diagnostics: count="
					+ std::to_string(status_count)
					+ "; statuses=" + statuses.str()
					+ "; faceOrientations="
					+ std::to_string(face_orientations[0]) + "/"
					+ std::to_string(face_orientations[1]) + "/"
					+ std::to_string(face_orientations[2]) + "/"
					+ std::to_string(face_orientations[3]));
			ShapeFix_Shape repair(fused);
			repair.Perform();
			TopoDS_Shape repaired = repair.Shape();
			if (!repaired.IsNull()
				&& solid_count(repaired) == 1
				&& BRepCheck_Analyzer(repaired, true).IsValid())
			{
				fused = repaired;
				append_native_log(
					"collector",
					"Healed tolerance-level defects in the sewn runner/collector shell.");
			}
		}
		#endif
		ShapeFix_Shape publication_repair(fused);
		publication_repair.SetPrecision(selected_tolerance);
		publication_repair.Perform();
		TopoDS_Shape publication_candidate = publication_repair.Shape();
		if (!publication_candidate.IsNull()
			&& solid_count(publication_candidate) == 1)
		{
			fused = publication_candidate;
		}
		size_t published_shell_count = 0;
		bool published_shell_closed = false;
		for (TopExp_Explorer shell_explorer(fused, TopAbs_SHELL);
			shell_explorer.More();
			shell_explorer.Next())
		{
			published_shell_closed = BRep_Tool::IsClosed(
				TopoDS::Shell(shell_explorer.Current()));
			++published_shell_count;
		}
		if (solid_count(fused) != 1
			|| published_shell_count != 1
			|| !published_shell_closed
			|| !BRepCheck_Analyzer(fused, true, true).IsValid())
		{
			throw std::runtime_error(
				"The sewn runner/collector system is not exactly one valid connected "
				"closed-shell solid; shells="
					+ std::to_string(published_shell_count) + ".");
		}
		metrics.validation_microseconds += static_cast<uint64_t>(
			std::chrono::duration_cast<std::chrono::microseconds>(
				std::chrono::steady_clock::now() - final_validation_start).count());
		++metrics.validation_count;
		remap_sources_to_result_surfaces(fused, replacement.sources);
		std::vector<TopoDS_Face> published_faces = shape_faces(fused);
		std::vector<gp_Pnt> published_face_centers;
		published_face_centers.reserve(published_faces.size());
		for (const TopoDS_Face& face : published_faces)
		{
			published_face_centers.push_back(face_representative_point(face));
		}
		append_native_log(
			"collector",
			"Sewn collector/member assembly: runners="
				+ std::to_string(members.size())
				+ "; measuredGap=" + std::to_string(measured_gap)
				+ "; selectedTolerance=" + std::to_string(selected_tolerance)
				+ "; interfaceBooleans=0.");
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
		double outlet_plane_tolerance = std::max(
			Precision::Confusion() * 10.0,
			selected_tolerance);
		if (maximum_outlet_projection > outlet_plane_tolerance)
		{
			throw std::runtime_error(
				"The sewn collector extends downstream of its designed outlet plane; "
				"a cleanup Boolean is intentionally not permitted. Maximum projection="
					+ std::to_string(maximum_outlet_projection)
					+ " mm; tolerance=" + std::to_string(outlet_plane_tolerance) + ".");
		}
		append_native_log(
			"collector",
			"Outlet boundary is clean by construction: extent="
				+ std::to_string(minimum_outlet_projection)
				+ " to " + std::to_string(maximum_outlet_projection)
				+ " mm; finalBooleans=0.");
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
		for (size_t face_index = 0;
			face_index < published_faces.size();
			++face_index)
		{
			gp_Vec from_outlet(
				published_outlet_origin,
				published_face_centers[face_index]);
			double outlet_distance = std::abs(
				from_outlet.Dot(gp_Vec(published_outlet_tangent)));
			nearest_outlet_face_distance = std::min(
				nearest_outlet_face_distance,
				outlet_distance);
			if (outlet_distance
				<= Precision::Confusion() * 1000.0)
			{
				published_outlet->faces.push_back(published_faces[face_index]);
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
		for (runner_source& source : replacement.sources)
		{
			if (source.kind != FGCAD_SOURCE_COLLECTOR_INLET
				|| source.owner_id != system_id)
			{
				continue;
			}
			bool has_published_face = std::any_of(
				source.faces.begin(),
				source.faces.end(),
				[&](const TopoDS_Face& source_face)
				{
					return std::any_of(
						published_faces.begin(),
						published_faces.end(),
						[&](const TopoDS_Face& candidate)
						{
							return candidate.IsSame(source_face);
						});
				});
			if (has_published_face)
			{
				continue;
			}

			gp_Pnt p0 = point(source.feature.entry_frame.origin);
			gp_Pnt p1 = point(source.feature.control1);
			gp_Pnt p2 = point(source.feature.control2);
			gp_Pnt p3 = point(source.feature.exit_frame.origin);
			gp_Pnt branch_midpoint(
				(p0.X() + 3.0 * p1.X() + 3.0 * p2.X() + p3.X()) / 8.0,
				(p0.Y() + 3.0 * p1.Y() + 3.0 * p2.Y() + p3.Y()) / 8.0,
				(p0.Z() + 3.0 * p1.Z() + 3.0 * p2.Z() + p3.Z()) / 8.0);
			double nearest_distance = std::numeric_limits<double>::infinity();
			const TopoDS_Face* nearest_face = nullptr;
			for (size_t face_index = 0;
				face_index < published_faces.size();
				++face_index)
			{
				double distance = branch_midpoint.Distance(
					published_face_centers[face_index]);
				if (distance < nearest_distance)
				{
					nearest_distance = distance;
					nearest_face = &published_faces[face_index];
				}
			}
			if (nearest_face != nullptr)
			{
				source.faces.clear();
				source.faces.push_back(*nearest_face);
				append_native_log(
					"collector",
					"Recovered inlet provenance near its branch midpoint: inlet="
						+ source.id
						+ "; distance="
						+ std::to_string(nearest_distance));
			}
		}
		replacement.shape = fused;
		metrics.total_microseconds = static_cast<uint64_t>(
			std::chrono::duration_cast<std::chrono::microseconds>(
				std::chrono::steady_clock::now() - build_start).count());
		capture_topology_metrics(replacement.shape, metrics);
		publish_staged_runners();
		document->build_metrics[system_id] = metrics;
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
		if (!document->staged_collector_id.empty()
			|| !document->staged_runner_id.empty())
		{
			throw std::invalid_argument("Another collector-system build is already staged.");
		}
		document->staged_collector_id = std::move(id);
		document->staged_generation_revision = generation_revision;
		document->staged_runners.clear();
		document->staged_previous_collector = collector_record{};
		document->staged_previous_collector_exists = false;
		document->staged_collector_published = false;
		document->staged_previous_member_runners.clear();
		document->staged_missing_member_runners.clear();
		auto previous = document->collectors.find(document->staged_collector_id);
		if (previous != document->collectors.end())
		{
			document->staged_previous_collector = previous->second;
			document->staged_previous_collector_exists = true;
		}
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_commit_collector_system_build(
	fgcad_document* document,
	const char* system_id,
	uint64_t generation_revision)
{
	return guarded([&]()
	{
		if (document == nullptr) throw std::invalid_argument("The document cannot be null.");
		std::string id = require_text(system_id, "system_id");
		if (document->staged_collector_id != id
			|| document->staged_generation_revision != generation_revision
			|| !document->staged_collector_published)
		{
			throw std::invalid_argument(
				"The collector publication commit does not match a completed active generation.");
		}
		document->staged_runners.clear();
		document->staged_collector_id.clear();
		document->staged_generation_revision = 0;
		document->staged_previous_collector = collector_record{};
		document->staged_previous_collector_exists = false;
		document->staged_collector_published = false;
		document->staged_previous_member_runners.clear();
		document->staged_missing_member_runners.clear();
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
		if (document->staged_collector_id == id
			&& document->staged_collector_published)
		{
			if (document->staged_previous_collector_exists)
			{
				document->collectors[id] = document->staged_previous_collector;
			}
			else
			{
				document->collectors.erase(id);
			}
			for (const auto& previous : document->staged_previous_member_runners)
			{
				document->runners[previous.first] = previous.second;
			}
			for (const std::string& missing : document->staged_missing_member_runners)
			{
				document->runners.erase(missing);
			}
		}
		document->staged_runners.clear();
		document->staged_collector_id.clear();
		document->staged_generation_revision = 0;
		document->staged_previous_collector = collector_record{};
		document->staged_previous_collector_exists = false;
		document->staged_collector_published = false;
		document->staged_previous_member_runners.clear();
		document->staged_missing_member_runners.clear();
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
		std::string id = require_text(system_id, "system_id");
		if (document->staged_collector_id == id)
		{
			document->staged_runners.clear();
			document->staged_collector_id.clear();
			document->staged_generation_revision = 0;
			document->staged_previous_collector = collector_record{};
			document->staged_previous_collector_exists = false;
			document->staged_collector_published = false;
			document->staged_previous_member_runners.clear();
			document->staged_missing_member_runners.clear();
		}
		document->collectors.erase(id);
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
