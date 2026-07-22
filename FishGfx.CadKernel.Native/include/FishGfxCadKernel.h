#pragma once

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32) && defined(FGCAD_NATIVE_EXPORTS)
#define FGCAD_API __declspec(dllexport)
#elif defined(_WIN32)
#define FGCAD_API __declspec(dllimport)
#else
#define FGCAD_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct fgcad_document fgcad_document;
typedef struct fgcad_tessellation fgcad_tessellation;

typedef enum fgcad_status
{
	FGCAD_STATUS_OK = 0,
	FGCAD_STATUS_INVALID_ARGUMENT = 1,
	FGCAD_STATUS_NOT_FOUND = 2,
	FGCAD_STATUS_UNSUPPORTED_TOPOLOGY = 3,
	FGCAD_STATUS_IMPORT_FAILED = 4,
	FGCAD_STATUS_MODELING_FAILED = 5,
	FGCAD_STATUS_IO_FAILED = 6,
	FGCAD_STATUS_INTERNAL_ERROR = 7,
} fgcad_status;

typedef enum fgcad_topology_kind
{
	FGCAD_TOPOLOGY_UNKNOWN = 0,
	FGCAD_TOPOLOGY_FACE = 1,
	FGCAD_TOPOLOGY_EDGE = 2,
	FGCAD_TOPOLOGY_CYLINDRICAL_FACE = 3,
	FGCAD_TOPOLOGY_CIRCULAR_EDGE = 4,
} fgcad_topology_kind;

typedef enum fgcad_segment_kind
{
	FGCAD_SEGMENT_STRAIGHT = 0,
	FGCAD_SEGMENT_BEND = 1,
} fgcad_segment_kind;

typedef struct fgcad_point3
{
	double x;
	double y;
	double z;
} fgcad_point3;

typedef struct fgcad_quaternion
{
	double x;
	double y;
	double z;
	double w;
} fgcad_quaternion;

typedef struct fgcad_transform
{
	fgcad_point3 translation;
	fgcad_quaternion rotation;
} fgcad_transform;

typedef struct fgcad_frame
{
	fgcad_point3 origin;
	fgcad_point3 tangent;
	fgcad_point3 normal;
} fgcad_frame;

typedef struct fgcad_topology_info
{
	uint64_t id;
	fgcad_topology_kind kind;
	fgcad_point3 center;
	fgcad_point3 axis;
	double radius;
} fgcad_topology_info;

typedef struct fgcad_mesh_vertex
{
	float x;
	float y;
	float z;
	float normal_x;
	float normal_y;
	float normal_z;
} fgcad_mesh_vertex;

typedef struct fgcad_face_range
{
	uint64_t topology_id;
	char source_node_id[40];
	uint32_t first_index;
	uint32_t index_count;
} fgcad_face_range;

typedef struct fgcad_edge_range
{
	uint64_t topology_id;
	fgcad_topology_kind kind;
	uint32_t first_point;
	uint32_t point_count;
} fgcad_edge_range;

typedef struct fgcad_runner_segment
{
	fgcad_segment_kind kind;
	char source_node_id[40];
	fgcad_point3 start;
	fgcad_point3 end;
	fgcad_point3 start_tangent;
	fgcad_point3 end_tangent;
	fgcad_point3 center;
	double radius;
	double sweep_radians;
} fgcad_runner_segment;

FGCAD_API uint32_t fgcad_api_version(void);
FGCAD_API const char* fgcad_last_error(void);

FGCAD_API fgcad_status fgcad_document_create(fgcad_document** document);
FGCAD_API void fgcad_document_destroy(fgcad_document* document);
FGCAD_API fgcad_status fgcad_document_import_step(
	fgcad_document* document,
	const char* part_id,
	const char* path_utf8,
	const char* name_utf8
);
FGCAD_API fgcad_status fgcad_document_replace_step(
	fgcad_document* document,
	const char* part_id,
	const char* path_utf8,
	const char* name_utf8
);
FGCAD_API fgcad_status fgcad_document_set_part_transform(
	fgcad_document* document,
	const char* part_id,
	const fgcad_transform* transform
);
FGCAD_API fgcad_status fgcad_document_get_topology_count(
	fgcad_document* document,
	const char* part_id,
	size_t* count
);
FGCAD_API fgcad_status fgcad_document_copy_topology(
	fgcad_document* document,
	const char* part_id,
	fgcad_topology_info* items,
	size_t capacity
);
FGCAD_API fgcad_status fgcad_document_get_mate_frame(
	fgcad_document* document,
	const char* part_id,
	uint64_t topology_id,
	const fgcad_point3* local_hit,
	fgcad_frame* frame,
	double* radius
);
FGCAD_API fgcad_status fgcad_document_bind_topology_selector(
	fgcad_document* document,
	const char* selector_id,
	const char* part_id,
	uint64_t topology_id
);
FGCAD_API fgcad_status fgcad_document_build_runner(
	fgcad_document* document,
	const fgcad_runner_segment* segments,
	size_t segment_count,
	double outer_diameter,
	double wall_thickness
);
FGCAD_API fgcad_status fgcad_document_tessellate_part(
	fgcad_document* document,
	const char* part_id,
	double linear_deflection,
	double angular_deflection,
	fgcad_tessellation** tessellation
);
FGCAD_API fgcad_status fgcad_document_tessellate_runner(
	fgcad_document* document,
	double linear_deflection,
	double angular_deflection,
	fgcad_tessellation** tessellation
);
FGCAD_API void fgcad_tessellation_destroy(fgcad_tessellation* tessellation);
FGCAD_API size_t fgcad_tessellation_vertex_count(const fgcad_tessellation* tessellation);
FGCAD_API size_t fgcad_tessellation_index_count(const fgcad_tessellation* tessellation);
FGCAD_API size_t fgcad_tessellation_face_count(const fgcad_tessellation* tessellation);
FGCAD_API size_t fgcad_tessellation_edge_count(const fgcad_tessellation* tessellation);
FGCAD_API size_t fgcad_tessellation_edge_point_count(const fgcad_tessellation* tessellation);
FGCAD_API fgcad_status fgcad_tessellation_copy(
	const fgcad_tessellation* tessellation,
	fgcad_mesh_vertex* vertices,
	size_t vertex_capacity,
	uint32_t* indices,
	size_t index_capacity,
	fgcad_face_range* faces,
	size_t face_capacity,
	fgcad_edge_range* edges,
	size_t edge_capacity,
	fgcad_point3* edge_points,
	size_t edge_point_capacity,
	fgcad_point3* minimum,
	fgcad_point3* maximum
);
FGCAD_API fgcad_status fgcad_document_save_xcaf(
	fgcad_document* document,
	const char* path_utf8
);
FGCAD_API fgcad_status fgcad_document_load_xcaf(
	fgcad_document* document,
	const char* path_utf8
);
FGCAD_API fgcad_status fgcad_document_export_step_ap242(
	fgcad_document* document,
	const char* path_utf8
);

#ifdef __cplusplus
}
#endif
