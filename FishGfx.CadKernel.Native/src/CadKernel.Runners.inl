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

		const bool trace_timing = std::getenv("FGCAD_TRACE_TIMING") != nullptr;
		auto trace_last = std::chrono::steady_clock::now();
		auto trace = [&](const char* label)
		{
			if (!trace_timing) return;
			auto now = std::chrono::steady_clock::now();
			std::cerr << "[FGCAD] " << label << "="
				<< std::chrono::duration_cast<std::chrono::milliseconds>(now - trace_last).count()
				<< " ms\n";
			trace_last = now;
		};

		struct profile_wires
		{
			TopoDS_Wire inner;
			TopoDS_Wire outer;
		};

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
			BRepBuilderAPI_MakeFace builder(profile.outer);
			builder.Add(TopoDS::Wire(profile.inner.Reversed()));
			if (!builder.IsDone()) throw std::runtime_error("A hollow runner profile face could not be built.");
			return builder.Face();
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
				fgcad_bezier_evaluation evaluation = evaluate_cubic_bezier_internal(
					feature.entry_frame,
					feature.control1,
					feature.control2,
					feature.exit_frame.origin,
					feature.input_profile.kind == FGCAD_PROFILE_CIRCULAR
						? feature.input_profile.outer_diameter * 0.5
						: feature.input_profile.equivalent_radius + feature.input_profile.wall_thickness
				);
				(void)evaluation;
				return BRepBuilderAPI_MakeEdge(make_bezier(
					feature.entry_frame,
					feature.control1,
					feature.control2,
					feature.exit_frame.origin
				)).Edge();
			}
			throw std::invalid_argument("A profile transition does not have a sweep edge.");
		};

		struct generated_section
		{
			TopoDS_Shape shape;
			std::vector<runner_source> sources;
		};

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

		auto make_sweep = [&](size_t first, size_t last)
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
			TopoDS_Face section_face = annular_face(profile_at(profile, features[first].entry_frame));
			BRepOffsetAPI_MakePipe pipe = contains_bezier
				? BRepOffsetAPI_MakePipe(
					wire.Wire(),
					section_face,
					GeomFill_IsDiscreteTrihedron,
					true)
				: BRepOffsetAPI_MakePipe(wire.Wire(), section_face);
			if (!pipe.IsDone()) throw std::runtime_error("Open CASCADE could not sweep a grouped runner spine.");
			trace("grouped sweep");

			generated_section section;
			section.shape = pipe.Shape();
			for (size_t index = first; index < last; ++index)
			{
				runner_source source;
				source.id = features[index].source_node_id;
				source.feature = features[index];
				const NCollection_List<TopoDS_Shape>& generated = pipe.Generated(edges[index - first]);
				for (NCollection_List<TopoDS_Shape>::Iterator iterator(generated); iterator.More(); iterator.Next())
				{
					append_faces(iterator.Value(), source.faces);
				}
				const NCollection_List<TopoDS_Shape>& modified = pipe.Modified(edges[index - first]);
				for (NCollection_List<TopoDS_Shape>::Iterator iterator(modified); iterator.More(); iterator.Next())
				{
					append_faces(iterator.Value(), source.faces);
				}
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
				double edge_first = 0;
				double edge_last = 0;
				Handle(Geom_Curve) curve = BRep_Tool::Curve(
					edges[edge_index],
					edge_first,
					edge_last);
				if (curve.IsNull())
				{
					return std::numeric_limits<double>::infinity();
				}
				GeomAPI_ProjectPointOnCurve projection(
					value,
					curve,
					edge_first,
					edge_last);
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

			for (const TopoDS_Face& face : sweep_faces)
			{
				if (face_is_claimed(face))
				{
					continue;
				}
				GProp_GProps properties;
				BRepGProp::SurfaceProperties(face, properties);
				gp_Pnt center = properties.CentreOfMass();
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
				for (const TopoDS_Face& face : sweep_faces)
				{
					GProp_GProps properties;
					BRepGProp::SurfaceProperties(face, properties);
					double distance = distance_to_spine_edge(
						properties.CentreOfMass(),
						source_index);
					if (distance < best)
					{
						best = distance;
						nearest = &face;
					}
				}
				if (nearest != nullptr)
				{
					append_face(section.sources[source_index], *nearest);
				}
			}
			return section;
		};

		auto make_loft = [&](const fgcad_runner_feature& feature)
		{
			profile_wires input = profile_at(feature.input_profile, feature.entry_frame);
			profile_wires output = profile_at(feature.output_profile, feature.exit_frame);
			trace("loft profiles");
			BRepOffsetAPI_ThruSections outer(true, false);
			outer.CheckCompatibility(true);
			outer.AddWire(input.outer);
			outer.AddWire(output.outer);
			outer.Build();
			BRepOffsetAPI_ThruSections inner(true, false);
			inner.CheckCompatibility(true);
			inner.AddWire(input.inner);
			inner.AddWire(output.inner);
			inner.Build();
			if (!outer.IsDone() || !inner.IsDone())
			{
				throw std::runtime_error(
					"Open CASCADE could not loft the profile-transition volumes.");
			}
			trace("loft volumes");
			BRepAlgoAPI_Cut hollow(outer.Shape(), inner.Shape());
			hollow.Build();
			if (!hollow.IsDone() || hollow.Shape().IsNull())
			{
				throw std::runtime_error(
					"The inner profile-transition volume could not be subtracted.");
			}
			trace("loft hollow cut");
			return hollow.Shape();
		};

		runner_record replacement;
		replacement.id = require_text(runner_id, "runner_id");
		replacement.name = require_text(runner_name, "runner_name");
		TopoDS_Shape result;
		auto is_joint_cap = [](const TopoDS_Face& face, const fgcad_frame& frame)
		{
			BRepAdaptor_Surface surface(face, true);
			if (surface.GetType() != GeomAbs_Plane) return false;
			gp_Pln plane = surface.Plane();
			double distance = plane.Distance(point(frame.origin));
			double alignment = std::abs(plane.Axis().Direction().Dot(unit(frame.tangent)));
			return distance <= 1.0e-6 && alignment >= 1.0 - 1.0e-9;
		};
		auto try_sew_join = [&](const TopoDS_Shape& left, const TopoDS_Shape& right,
			const fgcad_frame& frame, TopoDS_Shape& joined)
		{
			BRepBuilderAPI_Sewing sewing;
			size_t removed_caps = 0;
			auto add_without_joint_cap = [&](const TopoDS_Shape& shape)
			{
				for (TopExp_Explorer explorer(shape, TopAbs_FACE); explorer.More(); explorer.Next())
				{
					TopoDS_Face face = TopoDS::Face(explorer.Current());
					if (is_joint_cap(face, frame))
					{
						++removed_caps;
						continue;
					}
					sewing.Add(face);
				}
			};
			add_without_joint_cap(left);
			add_without_joint_cap(right);
			if (removed_caps != 2) return false;

			sewing.Perform();
			TopoDS_Shape sewed = sewing.SewedShape();
			if (sewed.IsNull() || sewed.ShapeType() != TopAbs_SHELL) return false;
			BRepBuilderAPI_MakeSolid solid(TopoDS::Shell(sewed));
			if (!solid.IsDone()) return false;
			TopoDS_Shape candidate = solid.Solid();
			if (!BRepCheck_Analyzer(candidate, true).IsValid()) return false;

			for (runner_source& source : replacement.sources)
			{
				for (TopoDS_Face& face : source.faces)
				{
					if (sewing.IsModifiedSubShape(face))
					{
						TopoDS_Shape modified = sewing.ModifiedSubShape(face);
						if (!modified.IsNull() && modified.ShapeType() == TopAbs_FACE)
						{
							face = TopoDS::Face(modified);
						}
					}
				}
			}
			joined = candidate;
			return true;
		};
		auto join_section = [&](generated_section&& section, const fgcad_frame* joint_frame)
		{
			if (section.shape.IsNull() || !BRepCheck_Analyzer(section.shape, true).IsValid())
			{
				throw std::runtime_error("A generated runner feature failed exact B-rep validation.");
			}
			trace("section validation");
			for (runner_source& source : section.sources)
			{
				replacement.sources.push_back(std::move(source));
			}

			if (result.IsNull()) result = section.shape;
			else
			{
				TopoDS_Shape sewn;
				if (joint_frame != nullptr && try_sew_join(result, section.shape, *joint_frame, sewn))
				{
					result = sewn;
					trace("section sew");
					return;
				}
				BRepAlgoAPI_Fuse fuse(result, section.shape);
				fuse.Build();
				if (!fuse.IsDone()) throw std::runtime_error("Adjacent runner features could not be joined.");
				trace("section fuse");
				apply_boolean_history(fuse, replacement.sources);
				result = fuse.Shape();
				if (result.IsNull() || !BRepCheck_Analyzer(result, true).IsValid())
				{
					throw std::runtime_error("An intermediate runner join failed exact B-rep validation.");
				}
				trace("joined validation");
			}
		};

		for (size_t index = 0; index < feature_count;)
		{
			if (features[index].kind == FGCAD_FEATURE_LOFT_TRANSITION
				|| features[index].kind == FGCAD_FEATURE_CLOCKING_TRANSITION)
			{
				generated_section section;
				section.shape = make_loft(features[index]);
				section.sources.push_back({
					features[index].source_node_id,
					features[index],
					shape_faces(section.shape)
				});
				join_section(std::move(section), &features[index].entry_frame);
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
			join_section(make_sweep(index, end), &features[index].entry_frame);
			index = end;
		}

		if (result.IsNull() || !BRepCheck_Analyzer(result, true).IsValid())
		{
			throw std::runtime_error("The complete runner failed exact B-rep validation.");
		}
		trace("final validation");

		replacement.shape = result;
		for (runner_source& source : replacement.sources)
		{
			source.kind = FGCAD_SOURCE_RUNNER_NODE;
			source.owner_id = replacement.id;
		}
		if (!document->staged_collector_id.empty())
		{
			document->staged_runners[replacement.id] = std::move(replacement);
		}
		else
		{
			document->runners[replacement.id] = std::move(replacement);
		}
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_remove_runner(fgcad_document* document, const char* runner_id)
{
	return guarded([&]()
	{
		if (document == nullptr) throw std::invalid_argument("The document cannot be null.");
		std::string id = require_text(runner_id, "runner_id");
		document->runners.erase(id);
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
