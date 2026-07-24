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
	require(fgcad_api_version() == 5, "ABI version mismatch");
	fgcad_document* document = nullptr;
	require(fgcad_document_create(&document) == FGCAD_STATUS_OK, "Document creation failed");
	require(document != nullptr, "Document handle was null");

	fgcad_runner_profile profile = circular(42, 2);
	fgcad_frame bezier_start = frame({ 0, -100, 0 }, { 1, 0, 0 });
	fgcad_point3 bezier_control1{ 33.333, -100, 0 };
	fgcad_point3 bezier_control2{ 66.667, -90, 0 };
	fgcad_point3 bezier_end{ 100, -80, 0 };
	fgcad_bezier_evaluation bezier_evaluation{};
	require(
		fgcad_evaluate_cubic_bezier(
			&bezier_start,
			&bezier_control1,
			&bezier_control2,
			&bezier_end,
			21,
			&bezier_evaluation
		) == FGCAD_STATUS_OK,
		"Tolerance-controlled cubic Bezier evaluation failed"
	);
	require(bezier_evaluation.length > 100 && std::isfinite(bezier_evaluation.length),
		"Cubic Bezier length was invalid");
	require(std::abs(bezier_evaluation.exit_frame.origin.y + 80) < 1e-9,
		"Native cubic Bezier exit frame did not reach the endpoint");
	fgcad_frame spatial_start = frame({ 0, 0, 0 }, { -1, 0, 0 }, { 0, 0, 1 });
	fgcad_point3 spatial_control1{ -30, 0, 0 };
	fgcad_point3 spatial_control2{ -70, 20, 15 };
	fgcad_point3 spatial_end{ -100, 40, 20 };
	require(
		fgcad_evaluate_cubic_bezier(
			&spatial_start,
			&spatial_control1,
			&spatial_control2,
			&spatial_end,
			1,
			&bezier_evaluation
		) == FGCAD_STATUS_OK,
		"Spatial negative-facing cubic Bezier evaluation failed"
	);
	require(bezier_evaluation.exit_frame.tangent.x < 0,
		"Spatial native transport returned the wrong exit-facing direction");

	fgcad_point3 loop_control1{ -33.3333333333, -27.0833333333, 0 };
	fgcad_point3 loop_control2{ -33.3333333333, -54.1666666667, 0 };
	fgcad_point3 loop_end{ 0, 18.75, 0 };
	require(
		fgcad_evaluate_cubic_bezier(
			&spatial_start,
			&loop_control1,
			&loop_control2,
			&loop_end,
			0.1,
			&bezier_evaluation
		) != FGCAD_STATUS_OK,
		"Self-intersecting cubic Bezier was not rejected"
	);
	require(std::string(fgcad_last_error()).find("self-intersects") != std::string::npos,
		"Self-intersection diagnostic was not specific");

	fgcad_frame small_curve_start = frame({ 0, 0, 0 }, { 1, 0, 0 });
	fgcad_point3 small_curve_control1{ 0.0001, 0, 0 };
	fgcad_point3 small_curve_control2{ 0.0001, 0.0001, 0 };
	fgcad_point3 small_curve_end{ 0, 0.0001, 0 };
	require(
		fgcad_evaluate_cubic_bezier(
			&small_curve_start,
			&small_curve_control1,
			&small_curve_control2,
			&small_curve_end,
			0.001,
			&bezier_evaluation
		) != FGCAD_STATUS_OK,
		"Small-scale tight cubic Bezier curvature was incorrectly certified"
	);
	require(std::string(fgcad_last_error()).find("curvature") != std::string::npos,
		"Small-scale curvature diagnostic was not specific");

	fgcad_runner_feature_spec bezier_spec{};
	bezier_spec.kind = FGCAD_FEATURE_CUBIC_BEZIER;
	std::snprintf(
		bezier_spec.source_node_id,
		sizeof(bezier_spec.source_node_id),
		"%s",
		"aaaaaaaa-1111-1111-1111-111111111111"
	);
	bezier_spec.start_handle_length = 33.333;
	bezier_spec.control2_local = { 66.667, 0, 10 };
	bezier_spec.end_local = { 100, 0, 20 };
	bezier_spec.output_profile = profile;
	fgcad_runner_feature evaluated_bezier{};
	size_t evaluated_count = 0;
	require(
		fgcad_evaluate_runner_features(
			&bezier_start,
			&profile,
			&bezier_spec,
			1,
			&evaluated_bezier,
			1,
			&evaluated_count
		) == FGCAD_STATUS_OK,
		"Caller-allocated runner feature evaluation failed"
	);
	require(evaluated_bezier.kind == FGCAD_FEATURE_CUBIC_BEZIER
		&& evaluated_bezier.length > 100 && evaluated_count == 1,
		"Evaluated cubic feature metadata was invalid");
	fgcad_runner_profile exact_mate_profile{};
	exact_mate_profile.kind = FGCAD_PROFILE_MATE;
	exact_mate_profile.equivalent_radius = 10;
	exact_mate_profile.wall_thickness = 2;
	evaluated_count = 0;
	require(
		fgcad_evaluate_runner_features(
			&bezier_start,
			&exact_mate_profile,
			&bezier_spec,
			1,
			&evaluated_bezier,
			1,
			&evaluated_count
		) == FGCAD_STATUS_OK && evaluated_count == 1,
		"Exact mate-derived active profile could not evaluate a cubic Bezier"
	);
	evaluated_count = 0;
	require(
		fgcad_evaluate_runner_features(
			&bezier_start,
			&profile,
			&bezier_spec,
			1,
			&evaluated_bezier,
			1,
			&evaluated_count
		) == FGCAD_STATUS_OK,
		"Circular cubic Bezier re-evaluation failed"
	);
	require(
		fgcad_document_build_runner(
			document,
			"runner-bezier",
			"Bezier Runner",
			&evaluated_bezier,
			1
		) == FGCAD_STATUS_OK,
		"Exact cubic Bezier runner sweep failed"
	);
	fgcad_point3 zero_exit = bezier_control2;
	require(
		fgcad_evaluate_cubic_bezier(
			&bezier_start,
			&bezier_control1,
			&bezier_control2,
			&zero_exit,
			21,
			&bezier_evaluation
		) != FGCAD_STATUS_OK,
		"Zero cubic Bezier exit handle was not rejected"
	);
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
	fgcad_runner_feature transitioned[4]{};
	transitioned[0] = loft(
		"44444444-4444-4444-4444-444444444444",
		{ 0, 250, 0 }, { 35, 250, 0 }, { 1, 0, 0 }, profile, larger_profile
	);
	transitioned[1] = straight(
		"55555555-5555-5555-5555-555555555555",
		{ 35, 250, 0 }, { 135, 250, 0 }, { 1, 0, 0 }, larger_profile
	);
	transitioned[2] = loft(
		"66666666-6666-6666-6666-666666666666",
		{ 135, 250, 0 }, { 165, 250, 0 }, { 1, 0, 0 }, larger_profile, profile
	);
	transitioned[3] = straight(
		"77777777-7777-7777-7777-777777777777",
		{ 165, 250, 0 }, { 215, 250, 0 }, { 1, 0, 0 }, profile
	);
	require(
		fgcad_document_build_runner(document, "runner-b", "Runner B", transitioned, 4) == FGCAD_STATUS_OK,
		"Repeated exact hollow profile transitions failed"
	);
	fgcad_runner_feature reversed[2]{};
	reversed[0] = loft(
		"88888888-8888-8888-8888-888888888888",
		{ 215, 325, 0 }, { 180, 325, 0 }, { -1, 0, 0 }, profile, larger_profile
	);
	reversed[1] = straight(
		"99999999-9999-9999-9999-999999999999",
		{ 180, 325, 0 }, { 80, 325, 0 }, { -1, 0, 0 }, larger_profile
	);
	require(
		fgcad_document_build_runner(document, "runner-c", "Runner C", reversed, 2) == FGCAD_STATUS_OK,
		"Negative-facing exact profile transition failed"
	);

	fgcad_runner_profile collector_branch_profile = circular(4, 0.5);
	fgcad_runner_feature collector_runner_a = straight(
		"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		{ -100, -50, 0 }, { 0, -50, 0 }, { 1, 0, 0 }, collector_branch_profile);
	fgcad_runner_feature collector_runner_b = straight(
		"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
		{ -100, 50, 0 }, { 0, 50, 0 }, { 1, 0, 0 }, collector_branch_profile);
	require(fgcad_document_begin_collector_system_build(
		document,
		"20000000-0000-0000-0000-000000000001",
		1) == FGCAD_STATUS_OK,
		"Initial collector-system staging did not begin");
	require(fgcad_document_build_runner(
		document, "10000000-0000-0000-0000-000000000001", "Collector runner 1",
		&collector_runner_a, 1) == FGCAD_STATUS_OK,
		"First collector member runner failed");
	require(fgcad_document_build_runner(
		document, "10000000-0000-0000-0000-000000000002", "Collector runner 2",
		&collector_runner_b, 1) == FGCAD_STATUS_OK,
		"Second collector member runner failed");
	fgcad_collector_system_spec collector{};
	std::snprintf(collector.system_id, sizeof(collector.system_id), "%s",
		"20000000-0000-0000-0000-000000000001");
	std::snprintf(collector.name, sizeof(collector.name), "%s", "Two: into, one");
	collector.generation_revision = 1;
	collector.outlet_frame = frame({ 200, 0, 0 }, { 1, 0, 0 });
	collector.outlet_profile = circular(8, 0.75);
	collector.outlet_stub_length = 50;
	collector.merge_length = 120;
	collector.overlap_length = 12;
	collector.branch_end_handle_length = 30;
	fgcad_collector_inlet collector_inlets[2]{};
	for (size_t index = 0; index < 2; ++index)
	{
		std::snprintf(collector_inlets[index].inlet_id,
			sizeof(collector_inlets[index].inlet_id),
			"30000000-0000-0000-0000-00000000000%zu", index + 1);
		std::snprintf(collector_inlets[index].runner_id,
			sizeof(collector_inlets[index].runner_id),
			"10000000-0000-0000-0000-00000000000%zu", index + 1);
		collector_inlets[index].frame = frame(
			{ 0, index == 0 ? -50.0 : 50.0, 0 },
			{ 1, 0, 0 });
		collector_inlets[index].profile = collector_branch_profile;
		collector_inlets[index].merge_station = index == 0 ? 0.35 : 0.65;
		collector_inlets[index].branch_start_handle_length = 30;
	}
	fgcad_status collector_status = fgcad_document_build_collector_system(
		document, &collector, collector_inlets, 2);
	std::string collector_error = std::string("Circular 2 into 1 collector failed: ")
		+ fgcad_last_error();
	require(collector_status == FGCAD_STATUS_OK, collector_error.c_str());
	fgcad_tessellation* collector_tessellation = nullptr;
	require(fgcad_document_tessellate_collector_system(
		document,
		collector.system_id,
		0.25,
		0.2,
		&collector_tessellation) == FGCAD_STATUS_OK,
		"Collector-system tessellation failed");
	require(fgcad_tessellation_source_count(collector_tessellation) > 0,
		"Collector tessellation lost provenance");
	std::vector<fgcad_mesh_vertex> collector_vertices(
		fgcad_tessellation_vertex_count(collector_tessellation));
	std::vector<uint32_t> collector_indices(
		fgcad_tessellation_index_count(collector_tessellation));
	std::vector<fgcad_face_range> collector_faces(
		fgcad_tessellation_face_count(collector_tessellation));
	std::vector<fgcad_geometry_source_ref> collector_sources(
		fgcad_tessellation_source_count(collector_tessellation));
	std::vector<fgcad_edge_range> collector_edges(
		fgcad_tessellation_edge_count(collector_tessellation));
	std::vector<fgcad_point3> collector_points(
		fgcad_tessellation_edge_point_count(collector_tessellation));
	fgcad_point3 collector_minimum{};
	fgcad_point3 collector_maximum{};
	require(fgcad_tessellation_copy(
		collector_tessellation,
		collector_vertices.data(), collector_vertices.size(),
		collector_indices.data(), collector_indices.size(),
		collector_faces.data(), collector_faces.size(),
		collector_sources.data(), collector_sources.size(),
		collector_edges.data(), collector_edges.size(),
		collector_points.data(), collector_points.size(),
		&collector_minimum, &collector_maximum) == FGCAD_STATUS_OK,
		"Collector tessellation provenance copy failed");
	require(std::any_of(
		collector_sources.begin(),
		collector_sources.end(),
		[](const fgcad_geometry_source_ref& source)
		{
			return source.kind == FGCAD_SOURCE_COLLECTOR_INLET;
		}),
		"Collector inlet provenance did not survive booleans");
	require(std::any_of(
		collector_sources.begin(),
		collector_sources.end(),
		[](const fgcad_geometry_source_ref& source)
		{
			return source.kind == FGCAD_SOURCE_COLLECTOR_TRUNK
				|| source.kind == FGCAD_SOURCE_COLLECTOR_OUTLET;
		}),
		"Collector trunk/outlet provenance did not survive booleans");
	bool isolated_outlet_face = false;
	for (const fgcad_face_range& face : collector_faces)
	{
		if (face.source_count == 1
			&& collector_sources[face.first_source].kind == FGCAD_SOURCE_COLLECTOR_OUTLET)
		{
			isolated_outlet_face = true;
			break;
		}
	}
	require(isolated_outlet_face,
		"Collector outlet provenance was polluted by unrelated nearest-source guesses");
	fgcad_tessellation_destroy(collector_tessellation);

	fgcad_collector_system_spec invalid_replacement = collector;
	invalid_replacement.generation_revision = 2;
	invalid_replacement.overlap_length = -1;
	require(fgcad_document_begin_collector_system_build(
		document,
		collector.system_id,
		invalid_replacement.generation_revision) == FGCAD_STATUS_OK,
		"Collector-system staging did not begin");
	fgcad_runner_feature staged_runner_a = straight(
		"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
		{ -150, -50, 0 }, { 0, -50, 0 }, { 1, 0, 0 }, collector_branch_profile);
	require(fgcad_document_build_runner(
		document, "10000000-0000-0000-0000-000000000001", "Collector runner 1",
		&staged_runner_a, 1) == FGCAD_STATUS_OK,
		"Replacement member runner was not staged");
	require(fgcad_document_build_collector_system(
		document, &invalid_replacement, collector_inlets, 2) != FGCAD_STATUS_OK,
		"Invalid staged collector unexpectedly published");
	require(fgcad_document_abort_collector_system_build(
		document,
		collector.system_id,
		invalid_replacement.generation_revision) == FGCAD_STATUS_OK,
		"Collector-system staging did not abort");
	collector_tessellation = nullptr;
	require(fgcad_document_tessellate_collector_system(
		document,
		collector.system_id,
		0.25,
		0.2,
		&collector_tessellation) == FGCAD_STATUS_OK,
		"Failed replacement discarded the previously published collector");
	fgcad_tessellation_destroy(collector_tessellation);

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
	std::vector<fgcad_geometry_source_ref> sources(fgcad_tessellation_source_count(tessellation));
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
			sources.data(), sources.size(),
			edges.data(), edges.size(),
			points.data(), points.size(),
			&minimum, &maximum
		) == FGCAD_STATUS_OK,
		"Tessellation copy failed"
	);
	require(maximum.x > minimum.x && maximum.y > minimum.y, "Tessellation bounds were invalid");
	require(!sources.empty(), "Generated face lacked a source reference");
	std::unordered_set<std::string> source_ids;
	for (const fgcad_geometry_source_ref& source : sources)
	{
		source_ids.emplace(source.element_id);
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
	require(
		fgcad_document_tessellate_runner(document, "runner-c", 0.25, 0.2, &tessellation) == FGCAD_STATUS_OK,
		"Negative-facing runner tessellation failed"
	);
	require(fgcad_tessellation_face_count(tessellation) > 0, "Negative-facing runner had no faces");
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
	fgcad_tessellation* hidden_member = nullptr;
	require(fgcad_document_tessellate_runner(
		selected_reopened,
		"10000000-0000-0000-0000-000000000001",
		0.25,
		0.2,
		&hidden_member) == FGCAD_STATUS_OK,
		"Hidden collector member runner was not retained by project XCAF");
	fgcad_tessellation_destroy(hidden_member);
	fgcad_tessellation* reopened_collector = nullptr;
	require(fgcad_document_tessellate_collector_system(
		selected_reopened,
		"20000000-0000-0000-0000-000000000001",
		0.25,
		0.2,
		&reopened_collector) == FGCAD_STATUS_OK,
		"Fused collector system was not retained by project XCAF");
	fgcad_tessellation_destroy(reopened_collector);
	require(
		fgcad_document_tessellate_part(selected_reopened, part_id, 0.25, 0.2, &tessellation) == FGCAD_STATUS_OK,
		"Reopened placed component tessellation failed"
	);
	vertices.resize(fgcad_tessellation_vertex_count(tessellation));
	indices.resize(fgcad_tessellation_index_count(tessellation));
	faces.resize(fgcad_tessellation_face_count(tessellation));
	sources.resize(fgcad_tessellation_source_count(tessellation));
	edges.resize(fgcad_tessellation_edge_count(tessellation));
	points.resize(fgcad_tessellation_edge_point_count(tessellation));
	require(
		fgcad_tessellation_copy(
			tessellation,
			vertices.data(), vertices.size(),
			indices.data(), indices.size(),
			faces.data(), faces.size(),
			sources.data(), sources.size(),
			edges.data(), edges.size(),
			points.data(), points.size(),
			&minimum, &maximum
		) == FGCAD_STATUS_OK,
		"Reopened placed component tessellation copy failed"
	);
	std::string placement_error = "Rigid component placement was not retained by XCAF: "
		+ std::to_string(minimum.x);
	require(minimum.x > 190, placement_error.c_str());
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
