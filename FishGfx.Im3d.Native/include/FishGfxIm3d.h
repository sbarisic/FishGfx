#pragma once

#include <stdint.h>

#if defined(_WIN32)
#  if defined(FGIM3D_BUILD_DLL)
#    define FGIM3D_API __declspec(dllexport)
#  else
#    define FGIM3D_API __declspec(dllimport)
#  endif
#  define FGIM3D_CALL __cdecl
#else
#  define FGIM3D_API
#  define FGIM3D_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

enum { FGIM3D_ABI_VERSION = 1, FGIM3D_INVALID_ID = 0 };

typedef enum fgim3d_status {
    FGIM3D_STATUS_OK = 0,
    FGIM3D_STATUS_INVALID_ARGUMENT = 1,
    FGIM3D_STATUS_INVALID_STATE = 2,
    FGIM3D_STATUS_BUFFER_TOO_SMALL = 3,
    FGIM3D_STATUS_INTERNAL_ERROR = 4
} fgim3d_status;

typedef enum fgim3d_pose_operation {
    FGIM3D_POSE_TRANSLATE = 0,
    FGIM3D_POSE_ROTATE = 1
} fgim3d_pose_operation;

typedef enum fgim3d_primitive {
    FGIM3D_PRIMITIVE_TRIANGLES = 0,
    FGIM3D_PRIMITIVE_LINES = 1,
    FGIM3D_PRIMITIVE_POINTS = 2
} fgim3d_primitive;

typedef struct fgim3d_context fgim3d_context;

typedef struct fgim3d_vec3 {
    float x;
    float y;
    float z;
} fgim3d_vec3;

typedef struct fgim3d_quaternion {
    float x;
    float y;
    float z;
    float w;
} fgim3d_quaternion;

typedef struct fgim3d_pose {
    fgim3d_vec3 position;
    fgim3d_quaternion rotation;
} fgim3d_pose;

typedef struct fgim3d_frame_input {
    fgim3d_vec3 cursor_ray_origin;
    fgim3d_vec3 cursor_ray_direction;
    fgim3d_vec3 world_up;
    fgim3d_vec3 view_origin;
    fgim3d_vec3 view_direction;
    float viewport_width;
    float viewport_height;
    float projection_scale_y;
    float delta_time;
    int32_t select_down;
    int32_t projection_orthographic;
    int32_t flip_gizmo_when_behind;
} fgim3d_frame_input;

typedef struct fgim3d_interaction {
    uint32_t active_id;
    uint32_t hot_id;
    uint32_t activated_id;
    int32_t changed;
} fgim3d_interaction;

typedef struct fgim3d_vertex {
    float x;
    float y;
    float z;
    float size;
    uint8_t r;
    uint8_t g;
    uint8_t b;
    uint8_t a;
} fgim3d_vertex;

typedef struct fgim3d_draw_command {
    uint32_t primitive;
    uint32_t layer;
    uint32_t source_order;
    uint32_t first_vertex;
    uint32_t vertex_count;
} fgim3d_draw_command;

FGIM3D_API uint32_t FGIM3D_CALL fgim3d_get_abi_version(void);
FGIM3D_API const char* FGIM3D_CALL fgim3d_get_last_error(void);

FGIM3D_API fgim3d_status FGIM3D_CALL fgim3d_context_create(fgim3d_context** out_context);
FGIM3D_API fgim3d_status FGIM3D_CALL fgim3d_context_destroy(fgim3d_context* context);
FGIM3D_API fgim3d_status FGIM3D_CALL fgim3d_context_reset_interaction(fgim3d_context* context);

FGIM3D_API fgim3d_status FGIM3D_CALL fgim3d_begin_frame(
    fgim3d_context* context,
    const fgim3d_frame_input* input);

FGIM3D_API fgim3d_status FGIM3D_CALL fgim3d_manipulate_pose(
    fgim3d_context* context,
    uint32_t id,
    fgim3d_pose_operation operation,
    fgim3d_pose* pose,
    fgim3d_interaction* interaction);

FGIM3D_API fgim3d_status FGIM3D_CALL fgim3d_manipulate_axis_translation(
    fgim3d_context* context,
    uint32_t id,
    fgim3d_vec3 axis_origin,
    fgim3d_vec3 axis_direction,
    uint32_t rgba,
    fgim3d_vec3* position,
    fgim3d_interaction* interaction);

FGIM3D_API fgim3d_status FGIM3D_CALL fgim3d_end_frame(
    fgim3d_context* context,
    fgim3d_interaction* interaction,
    uint32_t* vertex_count,
    uint32_t* command_count);

FGIM3D_API fgim3d_status FGIM3D_CALL fgim3d_copy_draw_data(
    fgim3d_context* context,
    fgim3d_vertex* vertices,
    uint32_t vertex_capacity,
    fgim3d_draw_command* commands,
    uint32_t command_capacity);

FGIM3D_API fgim3d_status FGIM3D_CALL fgim3d_test_pose_round_trip(
    const fgim3d_pose* input,
    fgim3d_pose* output);

#ifdef __cplusplus
}
#endif
