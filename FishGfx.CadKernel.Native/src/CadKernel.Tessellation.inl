// Versioned C ABI entry points for immutable preview tessellation transfer.

namespace
{
constexpr size_t tessellation_cache_capacity = 24;

void cache_tessellation(
	fgcad_document& document,
	const std::string& key,
	const fgcad_tessellation& value)
{
	auto existing = document.tessellation_cache.find(key);
	if (existing != document.tessellation_cache.end())
	{
		existing->second = value;
		return;
	}
	while (document.tessellation_cache.size() >= tessellation_cache_capacity
		&& !document.tessellation_cache_order.empty())
	{
		document.tessellation_cache.erase(document.tessellation_cache_order.front());
		document.tessellation_cache_order.pop_front();
	}
	document.tessellation_cache.emplace(key, value);
	document.tessellation_cache_order.push_back(key);
}
}

fgcad_status fgcad_document_tessellate_part(
	fgcad_document* document,
	const char* part_id,
	double linear_deflection,
	double angular_deflection,
	fgcad_tessellation** output
)
{
	return not_found_guarded([&]()
	{
		if (document == nullptr || output == nullptr)
		{
			throw std::invalid_argument("Tessellation arguments cannot be null.");
		}

		auto result = tessellate(
			placed(find_part(*document, require_text(part_id, "part_id"))),
			linear_deflection,
			angular_deflection
		);
		*output = result.release();
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_tessellate_runner(
	fgcad_document* document,
	const char* runner_id,
	double linear_deflection,
	double angular_deflection,
	fgcad_tessellation** output
)
{
	return not_found_guarded([&]()
	{
		if (document == nullptr || output == nullptr)
		{
			throw std::invalid_argument("Tessellation arguments cannot be null.");
		}

		std::string id = require_text(runner_id, "runner_id");
		auto found = document->runners.find(id);
		if (found == document->runners.end()) throw std::out_of_range("The runner was not found.");
		std::ostringstream key_stream;
		key_stream << std::setprecision(17)
			<< "runner:" << id << ':' << found->second.geometry_key
			<< ":mesh-schema=2:" << linear_deflection << ':' << angular_deflection;
		std::string key = key_stream.str();
		auto cached = document->tessellation_cache.find(key);
		if (cached != document->tessellation_cache.end())
		{
			auto metrics = document->build_metrics.find(id);
			if (metrics != document->build_metrics.end())
			{
				metrics->second.cache_flags |= FGCAD_CACHE_TESSELLATION;
			}
			*output = new fgcad_tessellation(cached->second);
			return FGCAD_STATUS_OK;
		}
		auto started = std::chrono::steady_clock::now();
		auto result = tessellate(
			found->second.shape,
			linear_deflection,
			angular_deflection,
			found->second.sources);
		auto metrics = document->build_metrics.find(id);
		if (metrics != document->build_metrics.end())
		{
			metrics->second.tessellation_microseconds = static_cast<uint64_t>(
				std::chrono::duration_cast<std::chrono::microseconds>(
					std::chrono::steady_clock::now() - started).count());
		}
		cache_tessellation(*document, key, *result);
		*output = result.release();
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_tessellate_collector_system(
	fgcad_document* document,
	const char* system_id,
	double linear_deflection,
	double angular_deflection,
	fgcad_tessellation** output
)
{
	return not_found_guarded([&]()
	{
		if (document == nullptr || output == nullptr)
		{
			throw std::invalid_argument("Tessellation arguments cannot be null.");
		}
		std::string id = require_text(system_id, "system_id");
		auto found = document->collectors.find(id);
		if (found == document->collectors.end())
		{
			throw std::out_of_range("The collector system was not found.");
		}
		std::ostringstream key_stream;
		key_stream << std::setprecision(17)
			<< "collector:" << id << ':' << found->second.assembly_key
			<< ":mesh-schema=2:" << linear_deflection << ':' << angular_deflection;
		std::string key = key_stream.str();
		auto cached = document->tessellation_cache.find(key);
		if (cached != document->tessellation_cache.end())
		{
			auto metrics = document->build_metrics.find(id);
			if (metrics != document->build_metrics.end())
			{
				metrics->second.cache_flags |= FGCAD_CACHE_TESSELLATION;
			}
			*output = new fgcad_tessellation(cached->second);
			return FGCAD_STATUS_OK;
		}
		auto started = std::chrono::steady_clock::now();
		auto result = tessellate(
			found->second.shape,
			linear_deflection,
			angular_deflection,
			found->second.sources
		);
		auto metrics = document->build_metrics.find(id);
		if (metrics != document->build_metrics.end())
		{
			metrics->second.tessellation_microseconds = static_cast<uint64_t>(
				std::chrono::duration_cast<std::chrono::microseconds>(
					std::chrono::steady_clock::now() - started).count());
		}
		cache_tessellation(*document, key, *result);
		*output = result.release();
		return FGCAD_STATUS_OK;
	});
}

void fgcad_tessellation_destroy(fgcad_tessellation* tessellation)
{
	delete tessellation;
}

size_t fgcad_tessellation_vertex_count(const fgcad_tessellation* value) { return value == nullptr ? 0 : value->vertices.size(); }
size_t fgcad_tessellation_index_count(const fgcad_tessellation* value) { return value == nullptr ? 0 : value->indices.size(); }
size_t fgcad_tessellation_face_count(const fgcad_tessellation* value) { return value == nullptr ? 0 : value->faces.size(); }
size_t fgcad_tessellation_source_count(const fgcad_tessellation* value) { return value == nullptr ? 0 : value->sources.size(); }
size_t fgcad_tessellation_edge_count(const fgcad_tessellation* value) { return value == nullptr ? 0 : value->edges.size(); }
size_t fgcad_tessellation_edge_point_count(const fgcad_tessellation* value) { return value == nullptr ? 0 : value->edge_points.size(); }

fgcad_status fgcad_tessellation_copy(
	const fgcad_tessellation* value,
	fgcad_mesh_vertex* vertices,
	size_t vertex_capacity,
	uint32_t* indices,
	size_t index_capacity,
	fgcad_face_range* faces,
	size_t face_capacity,
	fgcad_geometry_source_ref* sources,
	size_t source_capacity,
	fgcad_edge_range* edges,
	size_t edge_capacity,
	fgcad_point3* edge_points,
	size_t edge_point_capacity,
	fgcad_point3* minimum,
	fgcad_point3* maximum
)
{
	return guarded([&]()
	{
		if (value == nullptr || minimum == nullptr || maximum == nullptr)
		{
			throw std::invalid_argument("Tessellation copy arguments cannot be null.");
		}

		if (vertex_capacity < value->vertices.size()
			|| index_capacity < value->indices.size()
			|| face_capacity < value->faces.size()
			|| source_capacity < value->sources.size()
			|| edge_capacity < value->edges.size()
			|| edge_point_capacity < value->edge_points.size())
		{
			throw std::invalid_argument("A tessellation destination buffer is too small.");
		}

		std::copy(value->vertices.begin(), value->vertices.end(), vertices);
		std::copy(value->indices.begin(), value->indices.end(), indices);
		std::copy(value->faces.begin(), value->faces.end(), faces);
		std::copy(value->sources.begin(), value->sources.end(), sources);
		std::copy(value->edges.begin(), value->edges.end(), edges);
		std::copy(value->edge_points.begin(), value->edge_points.end(), edge_points);
		*minimum = value->minimum;
		*maximum = value->maximum;
		return FGCAD_STATUS_OK;
	});
}
