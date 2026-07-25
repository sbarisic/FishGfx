// Versioned C ABI entry points for document, STEP, topology, and selector operations.

fgcad_status fgcad_document_create(fgcad_document** document)
{
	return guarded([&]()
	{
		if (document == nullptr)
		{
			throw std::invalid_argument("The document output pointer cannot be null.");
		}

		*document = new fgcad_document();
		return FGCAD_STATUS_OK;
	});
}

void fgcad_document_destroy(fgcad_document* document)
{
	delete document;
}

fgcad_status fgcad_document_import_step(
	fgcad_document* document,
	const char* part_id,
	const char* path_utf8,
	const char* name_utf8
)
{
	return guarded([&]()
	{
		if (document == nullptr)
		{
			throw std::invalid_argument("The document cannot be null.");
		}

		part_record part;
		part.id = require_text(part_id, "part_id");
		part.name = require_text(name_utf8, "name_utf8");
		import_step(part, require_text(path_utf8, "path_utf8"));
		rebuild_topology(part);
		document->parts[part.id] = std::move(part);
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_replace_step(
	fgcad_document* document,
	const char* part_id,
	const char* path_utf8,
	const char* name_utf8
)
{
	return not_found_guarded([&]()
	{
		part_record& current = find_part(*document, require_text(part_id, "part_id"));
		part_record replacement;
		replacement.id = current.id;
		replacement.name = require_text(name_utf8, "name_utf8");
		replacement.placement = current.placement;
		import_step(replacement, require_text(path_utf8, "path_utf8"));
		rebuild_topology(replacement);
		current = std::move(replacement);

		for (auto selector = document->selectors.begin(); selector != document->selectors.end();)
		{
			if (selector->second.part_id == current.id)
			{
				selector = document->selectors.erase(selector);
			}
			else
			{
				++selector;
			}
		}

		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_set_part_transform(
	fgcad_document* document,
	const char* part_id,
	const fgcad_transform* value
)
{
	return not_found_guarded([&]()
	{
		if (document == nullptr || value == nullptr)
		{
			throw std::invalid_argument("The document and transform cannot be null.");
		}

		find_part(*document, require_text(part_id, "part_id")).placement = transform(*value);
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_get_topology_count(
	fgcad_document* document,
	const char* part_id,
	size_t* count
)
{
	return not_found_guarded([&]()
	{
		if (document == nullptr || count == nullptr)
		{
			throw std::invalid_argument("The document and count cannot be null.");
		}

		*count = find_part(*document, require_text(part_id, "part_id")).topology.size();
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_copy_topology(
	fgcad_document* document,
	const char* part_id,
	fgcad_topology_info* items,
	size_t capacity
)
{
	return not_found_guarded([&]()
	{
		if (document == nullptr)
		{
			throw std::invalid_argument("The document cannot be null.");
		}

		const part_record& part = find_part(*document, require_text(part_id, "part_id"));

		if (capacity < part.topology.size() || (items == nullptr && !part.topology.empty()))
		{
			throw std::invalid_argument("The topology destination buffer is too small.");
		}

		for (size_t index = 0; index < part.topology.size(); ++index)
		{
			items[index] = part.topology[index].info;
		}

		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_get_mate_frame(
	fgcad_document* document,
	const char* part_id,
	uint64_t topology_id,
	const fgcad_point3* local_hit,
	fgcad_frame* frame,
	double* radius
)
{
	return not_found_guarded([&]()
	{
		if (document == nullptr || local_hit == nullptr || frame == nullptr || radius == nullptr)
		{
			throw std::invalid_argument("Mate frame arguments cannot be null.");
		}

		part_record& part = find_part(*document, require_text(part_id, "part_id"));
		auto found = std::find_if(part.topology.begin(), part.topology.end(), [&](const topology_record& item)
		{
			return item.info.id == topology_id;
		});

		if (found == part.topology.end())
		{
			throw std::out_of_range("The requested topology selection was not found.");
		}

		gp_Circ circle;

		if (found->info.kind == FGCAD_TOPOLOGY_CIRCULAR_EDGE)
		{
			circle = BRepAdaptor_Curve(TopoDS::Edge(found->shape)).Circle();
		}
		else if (found->info.kind == FGCAD_TOPOLOGY_CYLINDRICAL_FACE)
		{
			gp_Pnt hit = point(*local_hit).Transformed(part.placement.Inverted());
			double nearest = std::numeric_limits<double>::infinity();
			bool has_circle = false;

			for (TopExp_Explorer explorer(found->shape, TopAbs_EDGE); explorer.More(); explorer.Next())
			{
				TopoDS_Edge edge = TopoDS::Edge(explorer.Current());
				BRepAdaptor_Curve curve(edge);

				if (curve.GetType() != GeomAbs_Circle)
				{
					continue;
				}

				gp_Circ candidate = curve.Circle();
				double distance = hit.SquareDistance(candidate.Location());

				if (distance < nearest)
				{
					nearest = distance;
					circle = candidate;
					has_circle = true;
				}
			}

			if (!has_circle)
			{
				last_error = "The cylindrical face has no usable circular boundary.";
				return FGCAD_STATUS_UNSUPPORTED_TOPOLOGY;
			}
		}
		else if (found->info.kind == FGCAD_TOPOLOGY_CLOSED_PROFILE)
		{
			gp_Pnt origin = point(found->info.center);
			gp_Dir tangent = unit(found->info.axis);
			gp_Ax2 axes(origin, tangent);
			frame->origin = found->info.center;
			frame->tangent = found->info.axis;
			frame->normal = direction(axes.XDirection());
			*radius = found->info.radius;
			return FGCAD_STATUS_OK;
		}
		else
		{
			last_error = "Mate creation requires a circular edge, cylindrical face, or planar closed profile.";
			return FGCAD_STATUS_UNSUPPORTED_TOPOLOGY;
		}

		frame->origin = point(circle.Location());
		frame->tangent = direction(circle.Axis().Direction());
		frame->normal = direction(circle.XAxis().Direction());
		*radius = circle.Radius();
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_bind_topology_selector(
	fgcad_document* document,
	const char* selector_id,
	const char* part_id,
	uint64_t topology_id
)
{
	return not_found_guarded([&]()
	{
		if (document == nullptr)
		{
			throw std::invalid_argument("The document cannot be null.");
		}

		std::string part_key = require_text(part_id, "part_id");
		part_record& part = find_part(*document, part_key);

		if (std::none_of(part.topology.begin(), part.topology.end(), [&](const topology_record& item)
		{
			return item.info.id == topology_id;
		}))
		{
			throw std::out_of_range("The topology selector target was not found.");
		}

		selector_record selector;
		selector.id = require_text(selector_id, "selector_id");
		selector.part_id = part_key;
		selector.topology_id = topology_id;
		document->selectors[selector.id] = std::move(selector);
		return FGCAD_STATUS_OK;
	});
}
