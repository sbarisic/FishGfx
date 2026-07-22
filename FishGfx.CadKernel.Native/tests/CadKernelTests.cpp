#include "FishGfxCadKernel.h"

#include <cmath>
#include <cstdlib>
#include <filesystem>
#include <iostream>
#include <string>
#include <vector>

namespace
{
void require(bool condition, const char* message)
{
	if (!condition)
	{
		std::cerr << message << ": " << fgcad_last_error() << '\n';
		std::exit(EXIT_FAILURE);
	}
}

fgcad_runner_segment straight(
	const char* id,
	fgcad_point3 start,
	fgcad_point3 end,
	fgcad_point3 tangent
)
{
	fgcad_runner_segment result{};
	result.kind = FGCAD_SEGMENT_STRAIGHT;
	std::snprintf(result.source_node_id, sizeof(result.source_node_id), "%s", id);
	result.start = start;
	result.end = end;
	result.start_tangent = tangent;
	result.end_tangent = tangent;
	return result;
}

std::filesystem::path temporary(const char* extension)
{
	return std::filesystem::temp_directory_path()
		/ (std::string("fishgfx-cad-native-test-") + std::to_string(std::rand()) + extension);
}
}

int main()
{
	require(fgcad_api_version() == 1, "ABI version mismatch");
	fgcad_document* document = nullptr;
	require(fgcad_document_create(&document) == FGCAD_STATUS_OK, "Document creation failed");
	require(document != nullptr, "Document handle was null");

	fgcad_runner_segment segments[3]{};
	segments[0] = straight(
		"11111111-1111-1111-1111-111111111111",
		{ 0, 0, 0 },
		{ 100, 0, 0 },
		{ 1, 0, 0 }
	);
	segments[1].kind = FGCAD_SEGMENT_BEND;
	std::snprintf(
		segments[1].source_node_id,
		sizeof(segments[1].source_node_id),
		"%s",
		"22222222-2222-2222-2222-222222222222"
	);
	segments[1].start = { 100, 0, 0 };
	segments[1].end = { 175, 75, 0 };
	segments[1].start_tangent = { 1, 0, 0 };
	segments[1].end_tangent = { 0, 1, 0 };
	segments[1].center = { 100, 75, 0 };
	segments[1].radius = 75;
	segments[1].sweep_radians = 3.14159265358979323846 * 0.5;
	segments[2] = straight(
		"33333333-3333-3333-3333-333333333333",
		{ 175, 75, 0 },
		{ 175, 175, 0 },
		{ 0, 1, 0 }
	);
	require(
		fgcad_document_build_runner(document, segments, 3, 42, 2) == FGCAD_STATUS_OK,
		"Exact annular runner sweep failed"
	);

	fgcad_tessellation* tessellation = nullptr;
	require(
		fgcad_document_tessellate_runner(document, 0.25, 0.2, &tessellation) == FGCAD_STATUS_OK,
		"Runner tessellation failed"
	);
	require(fgcad_tessellation_vertex_count(tessellation) > 0, "Tessellation had no vertices");
	require(fgcad_tessellation_index_count(tessellation) > 0, "Tessellation had no indices");
	require(fgcad_tessellation_face_count(tessellation) > 0, "Tessellation had no face ranges");
	require(fgcad_tessellation_edge_count(tessellation) > 0, "Tessellation had no edge polylines");

	std::vector<fgcad_mesh_vertex> vertices(fgcad_tessellation_vertex_count(tessellation));
	std::vector<uint32_t> indices(fgcad_tessellation_index_count(tessellation));
	std::vector<fgcad_face_range> faces(fgcad_tessellation_face_count(tessellation));
	std::vector<fgcad_edge_range> edges(fgcad_tessellation_edge_count(tessellation));
	std::vector<fgcad_point3> points(fgcad_tessellation_edge_point_count(tessellation));
	fgcad_point3 minimum{};
	fgcad_point3 maximum{};
	require(
		fgcad_tessellation_copy(
			tessellation,
			vertices.data(), vertices.size(),
			indices.data(), indices.size(),
			faces.data(), faces.size(),
			edges.data(), edges.size(),
			points.data(), points.size(),
			&minimum, &maximum
		) == FGCAD_STATUS_OK,
		"Tessellation copy failed"
	);
	require(maximum.x > minimum.x && maximum.y > minimum.y, "Tessellation bounds were invalid");
	require(faces.front().source_node_id[0] != '\0', "Generated face lacked a source node ID");
	fgcad_tessellation_destroy(tessellation);

	std::filesystem::path binary = temporary(".xbf");
	std::filesystem::path step = temporary(".step");
	require(
		fgcad_document_save_xcaf(document, binary.string().c_str()) == FGCAD_STATUS_OK,
		"XCAF save failed"
	);
	require(
		fgcad_document_export_step_ap242(document, step.string().c_str()) == FGCAD_STATUS_OK,
		"AP242 export failed"
	);
	require(std::filesystem::file_size(binary) > 0, "XCAF file was empty");
	require(std::filesystem::file_size(step) > 0, "STEP file was empty");

	fgcad_document* reopened = nullptr;
	require(fgcad_document_create(&reopened) == FGCAD_STATUS_OK, "Reopened document creation failed");
	require(
		fgcad_document_load_xcaf(reopened, binary.string().c_str()) == FGCAD_STATUS_OK,
		"XCAF reopen failed"
	);
	require(
		fgcad_document_tessellate_runner(reopened, 0.25, 0.2, &tessellation) == FGCAD_STATUS_OK,
		"Reopened exact runner tessellation failed"
	);
	fgcad_tessellation_destroy(tessellation);

	const char* part_id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
	require(
		fgcad_document_import_step(reopened, part_id, step.string().c_str(), "Imported Runner")
			== FGCAD_STATUS_OK,
		"STEP reimport failed"
	);
	size_t topology_count = 0;
	require(
		fgcad_document_get_topology_count(reopened, part_id, &topology_count) == FGCAD_STATUS_OK,
		"Topology enumeration failed"
	);
	require(topology_count > 0, "Imported STEP had no topology");
	std::vector<fgcad_topology_info> topology(topology_count);
	require(
		fgcad_document_copy_topology(reopened, part_id, topology.data(), topology.size()) == FGCAD_STATUS_OK,
		"Topology copy failed"
	);
	auto circular = std::find_if(topology.begin(), topology.end(), [](const fgcad_topology_info& item)
	{
		return item.kind == FGCAD_TOPOLOGY_CIRCULAR_EDGE;
	});
	require(circular != topology.end(), "Circular-edge recognition failed");
	fgcad_frame frame{};
	double radius = 0;
	fgcad_point3 hit = circular->center;
	require(
		fgcad_document_get_mate_frame(reopened, part_id, circular->id, &hit, &frame, &radius)
			== FGCAD_STATUS_OK,
		"Exact circular mate-frame extraction failed"
	);
	require(radius > 0 && std::isfinite(radius), "Mate radius was invalid");
	fgcad_transform placement{};
	placement.translation = { 300, 20, -10 };
	placement.rotation.w = 1;
	require(
		fgcad_document_set_part_transform(reopened, part_id, &placement) == FGCAD_STATUS_OK,
		"Rigid part placement failed"
	);
	require(
		fgcad_document_bind_topology_selector(
			reopened,
			"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
			part_id,
			circular->id
		) == FGCAD_STATUS_OK,
		"OCAF topology selector binding failed"
	);
	std::filesystem::path selected_binary = temporary("-selector.xbf");
	require(
		fgcad_document_save_xcaf(reopened, selected_binary.string().c_str()) == FGCAD_STATUS_OK,
		"OCAF topology selector persistence failed"
	);
	require(std::filesystem::file_size(selected_binary) > 0, "Selector XCAF file was empty");
	fgcad_document* selected_reopened = nullptr;
	require(fgcad_document_create(&selected_reopened) == FGCAD_STATUS_OK, "Selector reopen document creation failed");
	require(
		fgcad_document_load_xcaf(selected_reopened, selected_binary.string().c_str()) == FGCAD_STATUS_OK,
		"Placed assembly and selector XCAF reopen failed"
	);
	require(
		fgcad_document_tessellate_part(selected_reopened, part_id, 0.25, 0.2, &tessellation) == FGCAD_STATUS_OK,
		"Reopened placed component tessellation failed"
	);
	vertices.resize(fgcad_tessellation_vertex_count(tessellation));
	indices.resize(fgcad_tessellation_index_count(tessellation));
	faces.resize(fgcad_tessellation_face_count(tessellation));
	edges.resize(fgcad_tessellation_edge_count(tessellation));
	points.resize(fgcad_tessellation_edge_point_count(tessellation));
	require(
		fgcad_tessellation_copy(
			tessellation,
			vertices.data(), vertices.size(),
			indices.data(), indices.size(),
			faces.data(), faces.size(),
			edges.data(), edges.size(),
			points.data(), points.size(),
			&minimum, &maximum
		) == FGCAD_STATUS_OK,
		"Reopened placed component tessellation copy failed"
	);
	require(minimum.x > 200, "Rigid component placement was not retained by XCAF");
	fgcad_tessellation_destroy(tessellation);
	fgcad_document_destroy(selected_reopened);

	fgcad_document_destroy(reopened);
	fgcad_document_destroy(document);
	std::filesystem::remove(binary);
	std::filesystem::remove(step);
	std::filesystem::remove(selected_binary);
	std::cout << "FishGfx CAD native integration tests passed\n";
	return EXIT_SUCCESS;
}
