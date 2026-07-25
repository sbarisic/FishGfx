#include "FishGfxCadKernel.h"

#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <string>

namespace
{
void require(bool condition, const char* message)
{
	if (!condition)
	{
		std::cerr << message << ": " << fgcad_last_error() << '\n';
		std::exit(1);
	}
}

fgcad_frame frame(fgcad_point3 origin, fgcad_point3 tangent)
{
	return { origin, tangent, { 0, 1, 0 } };
}

fgcad_runner_profile circular(double outer_diameter, double wall_thickness)
{
	fgcad_runner_profile result{};
	result.kind = FGCAD_PROFILE_CIRCULAR;
	result.outer_diameter = outer_diameter;
	result.wall_thickness = wall_thickness;
	result.equivalent_radius = outer_diameter * 0.5;
	return result;
}
}

int main()
{
	constexpr double radius = 100.0;
	constexpr double handle = 0.5522847498307936 * radius;
	fgcad_frame start = frame({ 0, 0, 0 }, { 1, 0, 0 });
	fgcad_point3 control1{ handle, 0, 0 };
	fgcad_point3 control2{ radius, radius - handle, 0 };
	fgcad_point3 end{ radius, radius, 0 };
	fgcad_bezier_evaluation evaluation{};

	auto stress_start = std::chrono::steady_clock::now();
	fgcad_status stress_status = fgcad_evaluate_cubic_bezier(
		&start,
		&control1,
		&control2,
		&end,
		97.85,
		&evaluation);
	auto stress_elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
		std::chrono::steady_clock::now() - stress_start);
	require(stress_status == FGCAD_STATUS_OK
		|| std::string(fgcad_last_error()).find("curvature") != std::string::npos,
		"Near-threshold curvature returned an unrelated failure");
	require(stress_elapsed < std::chrono::seconds(2),
		"Near-threshold curvature certification exceeded its deterministic budget");

	require(fgcad_evaluate_cubic_bezier(
		&start,
		&control1,
		&control2,
		&end,
		21,
		&evaluation) == FGCAD_STATUS_OK,
		"Valid cubic Bezier evaluation failed");

	fgcad_runner_feature feature{};
	feature.kind = FGCAD_FEATURE_CUBIC_BEZIER;
	std::snprintf(
		feature.source_node_id,
		sizeof(feature.source_node_id),
		"%s",
		"aaaaaaaa-1111-1111-1111-111111111111");
	feature.entry_frame = start;
	feature.exit_frame = evaluation.exit_frame;
	feature.input_profile = circular(42, 2);
	feature.output_profile = feature.input_profile;
	feature.length = evaluation.length;
	feature.radius = evaluation.minimum_radius;
	feature.control1 = control1;
	feature.control2 = control2;

	fgcad_document* document = nullptr;
	require(fgcad_document_create(&document) == FGCAD_STATUS_OK,
		"Performance-test document creation failed");
	require(fgcad_document_build_runner(
		document,
		"bbbbbbbb-2222-2222-2222-222222222222",
		"Cached runner",
		&feature,
		1) == FGCAD_STATUS_OK,
		"Initial exact Bezier runner build failed");

	auto cached_start = std::chrono::steady_clock::now();
	require(fgcad_document_build_runner(
		document,
		"bbbbbbbb-2222-2222-2222-222222222222",
		"Cached runner",
		&feature,
		1) == FGCAD_STATUS_OK,
		"Cached exact Bezier runner build failed");
	auto cached_elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
		std::chrono::steady_clock::now() - cached_start);
	require(cached_elapsed < std::chrono::milliseconds(500),
		"Unchanged runner did not use the exact-build cache");

	fgcad_document_destroy(document);
	std::cout << "Curve performance checks passed; stress=" << stress_elapsed.count()
		<< " ms, cachedBuild=" << cached_elapsed.count() << " ms\n";
	return 0;
}
