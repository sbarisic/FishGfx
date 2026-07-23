#include "FishGfxCadKernel.h"

#include <cmath>
#include <cstdlib>
#include <filesystem>
#include <iostream>
#include <string>
#include <unordered_set>
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

fgcad_runner_profile circular(double outer_diameter, double wall_thickness)
{
	fgcad_runner_profile result{};
	result.kind = FGCAD_PROFILE_CIRCULAR;
	result.outer_diameter = outer_diameter;
	result.wall_thickness = wall_thickness;
	return result;
}

fgcad_frame frame(fgcad_point3 origin, fgcad_point3 tangent, fgcad_point3 normal = { 0, 0, 1 })
{
	return { origin, tangent, normal };
}

fgcad_runner_feature straight(
	const char* id,
	fgcad_point3 start,
	fgcad_point3 end,
	fgcad_point3 tangent,
	fgcad_runner_profile profile
)
{
	fgcad_runner_feature result{};
	result.kind = FGCAD_FEATURE_STRAIGHT;
	std::snprintf(result.source_node_id, sizeof(result.source_node_id), "%s", id);
	result.entry_frame = frame(start, tangent);
	result.exit_frame = frame(end, tangent);
	result.input_profile = profile;
	result.output_profile = profile;
	return result;
}

fgcad_runner_feature loft(
	const char* id,
	fgcad_point3 start,
	fgcad_point3 end,
	fgcad_point3 tangent,
	fgcad_runner_profile input,
	fgcad_runner_profile output
)
{
	fgcad_runner_feature result{};
	result.kind = FGCAD_FEATURE_LOFT_TRANSITION;
	std::snprintf(result.source_node_id, sizeof(result.source_node_id), "%s", id);
	result.entry_frame = frame(start, tangent);
	result.exit_frame = frame(end, tangent);
	result.input_profile = input;
	result.output_profile = output;
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
	require(fgcad_api_version() == 3, "ABI version mismatch");
	fgcad_document* document = nullptr;
	require(fgcad_document_create(&document) == FGCAD_STATUS_OK, "Document creation failed");
	require(document != nullptr, "Document handle was null");

	fgcad_runner_profile profile = circular(42, 2);
	fgcad_runner_feature features[3]{};
	features[0] = straight(
		"11111111-1111-1111-1111-111111111111",
		{ 0, 0, 0 },
		{ 100, 0, 0 },
		{ 1, 0, 0 },
		profile
	);
	features[1].kind = FGCAD_FEATURE_BEND;
	std::snprintf(
		features[1].source_node_id,
		sizeof(features[1].source_node_id),
		"%s",
		"22222222-2222-2222-2222-222222222222"
	);
	features[1].entry_frame = frame({ 100, 0, 0 }, { 1, 0, 0 });
	features[1].exit_frame = frame({ 175, 75, 0 }, { 0, 1, 0 });
	features[1].input_profile = profile;
	features[1].output_profile = profile;
	features[1].center = { 100, 75, 0 };
	features[1].radius = 75;
	features[1].sweep_radians = 3.14159265358979323846 * 0.5;
	features[2] = straight(
		"33333333-3333-3333-3333-333333333333",
		{ 175, 75, 0 },
		{ 175, 175, 0 },
		{ 0, 1, 0 },
		profile
	);
	require(
		fgcad_document_build_runner(document, "runner-a", "Runner A", features, 3) == FGCAD_STATUS_OK,
		"Exact annular runner sweep failed"
	);
	fgcad_runner_profile larger_profile = circular(55, 2.5);
	fgcad_runner_feature transitioned[2]{};
	transitioned[0] = loft(
		"44444444-4444-4444-4444-444444444444",
		{ 0, 250, 0 }, { 35, 250, 0 }, { 1, 0, 0 }, profile, larger_profile
	);
	transitioned[1] = straight(
		"55555555-5555-5555-5555-555555555555",
		{ 35, 250, 0 }, { 135, 250, 0 }, { 1, 0, 0 }, larger_profile
	);
	require(
		fgcad_document_build_runner(document, "runner-b", "Runner B", transitioned, 2) == FGCAD_STATUS_OK,
		"Exact hollow profile transition failed"
	);

	fgcad_tessellation* tessellation = nullptr;
	require(
		fgcad_document_tessellate_runner(document, "runner-a", 0.25, 0.2, &tessellation) == FGCAD_STATUS_OK,
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
	std::unordered_set<std::string> source_ids;
	for (const fgcad_face_range& face : faces)
	{
		source_ids.emplace(face.source_node_id);
	}
	require(source_ids.count("11111111-1111-1111-1111-111111111111") != 0,
		"Straight build history was not retained");
	require(source_ids.count("22222222-2222-2222-2222-222222222222") != 0,
		"Bend build history was not retained");
	require(source_ids.count("33333333-3333-3333-3333-333333333333") != 0,
		"Trailing straight build history was not retained");
	fgcad_tessellation_destroy(tessellation);
	require(
		fgcad_document_tessellate_runner(document, "runner-b", 0.25, 0.2, &tessellation) == FGCAD_STATUS_OK,
		"Second runner tessellation failed"
	);
	require(fgcad_tessellation_face_count(tessellation) > 0, "Loft runner had no faces");
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
		fgcad_document_tessellate_runner(reopened, "runner-a", 0.25, 0.2, &tessellation) == FGCAD_STATUS_OK,
		"Reopened exact runner tessellation failed"
	);
	fgcad_tessellation_destroy(tessellation);
	require(
		fgcad_document_tessellate_runner(reopened, "runner-b", 0.25, 0.2, &tessellation) == FGCAD_STATUS_OK,
		"Second reopened runner tessellation failed"
	);
	fgcad_tessellation_destroy(tessellation);
	require(fgcad_document_remove_runner(reopened, "runner-b") == FGCAD_STATUS_OK,
		"Runner removal failed");
	require(fgcad_document_tessellate_runner(reopened, "runner-b", 0.25, 0.2, &tessellation)
		== FGCAD_STATUS_NOT_FOUND, "Removed runner remained available");

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
	std::filesystem::path missing_step = temporary("-missing.step");
	require(
		fgcad_document_replace_step(reopened, part_id, missing_step.string().c_str(), "Broken replacement")
			!= FGCAD_STATUS_OK,
		"Missing STEP replacement unexpectedly succeeded"
	);
	size_t retained_topology_count = 0;
	require(
		fgcad_document_get_topology_count(reopened, part_id, &retained_topology_count) == FGCAD_STATUS_OK
			&& retained_topology_count == topology_count,
		"Failed STEP replacement did not retain the original exact part"
	);
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
	auto closed_profile = std::find_if(topology.begin(), topology.end(), [](const fgcad_topology_info& item)
	{
		return item.kind == FGCAD_TOPOLOGY_CLOSED_PROFILE;
	});
	require(closed_profile != topology.end(), "Planar closed-profile recognition failed");
	fgcad_frame frame{};
	double radius = 0;
	fgcad_point3 hit = circular->center;
	require(
		fgcad_document_get_mate_frame(reopened, part_id, circular->id, &hit, &frame, &radius)
			== FGCAD_STATUS_OK,
		"Exact circular mate-frame extraction failed"
	);
	require(radius > 0 && std::isfinite(radius), "Mate radius was invalid");
	fgcad_point3 profile_hit = closed_profile->center;
	require(
		fgcad_document_get_mate_frame(reopened, part_id, closed_profile->id, &profile_hit, &frame, &radius)
			== FGCAD_STATUS_OK,
		"Exact closed-profile mate-frame extraction failed"
	);
	require(radius > 0 && std::isfinite(radius), "Closed-profile equivalent radius was invalid");
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
			closed_profile->id
		) == FGCAD_STATUS_OK,
		"OCAF closed-profile selector binding failed"
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
