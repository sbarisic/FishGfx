#include "FishGfxIm3d.h"

#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <iostream>
#include <utility>
#include <vector>

namespace {

bool near(float a, float b, float tolerance = 1.0e-5f) {
    return std::fabs(a - b) <= tolerance;
}

void require(bool condition, const char* message) {
    if (!condition) {
        std::cerr << message << '\n';
        std::exit(1);
    }
}

fgim3d_vec3 normalized(fgim3d_vec3 value) {
    const float magnitude = std::sqrt(value.x * value.x + value.y * value.y + value.z * value.z);
    return { value.x / magnitude, value.y / magnitude, value.z / magnitude };
}

fgim3d_vec3 difference(fgim3d_vec3 lhs, fgim3d_vec3 rhs) {
    return { lhs.x - rhs.x, lhs.y - rhs.y, lhs.z - rhs.z };
}

float distance(fgim3d_vec3 lhs, fgim3d_vec3 rhs) {
    const fgim3d_vec3 delta = difference(lhs, rhs);
    return std::sqrt(delta.x * delta.x + delta.y * delta.y + delta.z * delta.z);
}

fgim3d_vec3 rotate(fgim3d_quaternion quaternion, fgim3d_vec3 value) {
    const fgim3d_vec3 axis{ quaternion.x, quaternion.y, quaternion.z };
    const fgim3d_vec3 tangent{
        2.0f * (axis.y * value.z - axis.z * value.y),
        2.0f * (axis.z * value.x - axis.x * value.z),
        2.0f * (axis.x * value.y - axis.y * value.x)
    };
    const fgim3d_vec3 axis_cross_tangent{
        axis.y * tangent.z - axis.z * tangent.y,
        axis.z * tangent.x - axis.x * tangent.z,
        axis.x * tangent.y - axis.y * tangent.x
    };
    return {
        value.x + quaternion.w * tangent.x + axis_cross_tangent.x,
        value.y + quaternion.w * tangent.y + axis_cross_tangent.y,
        value.z + quaternion.w * tangent.z + axis_cross_tangent.z
    };
}

fgim3d_frame_input frame_input(
    bool select_down = false,
    fgim3d_vec3 view_origin = { 0.0f, 0.0f, 10.0f },
    fgim3d_vec3 cursor_target = { 0.0f, 0.0f, 0.0f },
    fgim3d_vec3 world_up = { 0.0f, 1.0f, 0.0f }) {
    fgim3d_frame_input input{};
    input.cursor_ray_origin = view_origin;
    input.cursor_ray_direction = normalized(difference(cursor_target, view_origin));
    input.world_up = world_up;
    input.view_origin = view_origin;
    input.view_direction = normalized(difference({ 0.0f, 0.0f, 0.0f }, view_origin));
    input.viewport_width = 1280.0f;
    input.viewport_height = 720.0f;
    input.projection_scale_y = 1.154700538f;
    input.delta_time = 1.0f / 60.0f;
    input.select_down = select_down ? 1 : 0;
    input.flip_gizmo_when_behind = 1;
    return input;
}

fgim3d_quaternion simulate_rotation(
    fgim3d_vec3 camera,
    fgim3d_vec3 world_up,
    fgim3d_vec3 start,
    fgim3d_vec3 finish,
    uint32_t id,
    fgim3d_quaternion initial_rotation = { 0.0f, 0.0f, 0.0f, 1.0f }) {
    fgim3d_context* context = nullptr;
    require(fgim3d_context_create(&context) == FGIM3D_STATUS_OK, "Rotation test context creation failed.");
    fgim3d_pose pose{};
    pose.rotation = initial_rotation;
    fgim3d_interaction interaction{};
    uint32_t vertex_count = 0;
    uint32_t command_count = 0;

    auto hover = frame_input(false, camera, start, world_up);
    require(fgim3d_begin_frame(context, &hover) == FGIM3D_STATUS_OK, "Rotation hover frame failed.");
    require(fgim3d_manipulate_pose(context, id, FGIM3D_POSE_ROTATE, &pose, &interaction) == FGIM3D_STATUS_OK,
        "Rotation hover submission failed.");
    require(fgim3d_end_frame(context, &interaction, &vertex_count, &command_count) == FGIM3D_STATUS_OK,
        "Rotation hover EndFrame failed.");
    require(interaction.hot_id == id, "The expected rotation ring did not become hot.");

    auto press = frame_input(true, camera, start, world_up);
    require(fgim3d_begin_frame(context, &press) == FGIM3D_STATUS_OK, "Rotation press frame failed.");
    require(fgim3d_manipulate_pose(context, id, FGIM3D_POSE_ROTATE, &pose, &interaction) == FGIM3D_STATUS_OK,
        "Rotation press submission failed.");
    require(interaction.activated_id == id, "Rotation activation was not attributed to the application ID.");
    require(fgim3d_end_frame(context, &interaction, &vertex_count, &command_count) == FGIM3D_STATUS_OK,
        "Rotation press EndFrame failed.");
    require(interaction.active_id == id, "Rotation ring did not remain active after pressing.");

    auto drag = frame_input(true, camera, finish, world_up);
    require(fgim3d_begin_frame(context, &drag) == FGIM3D_STATUS_OK, "Rotation drag frame failed.");
    require(fgim3d_manipulate_pose(context, id, FGIM3D_POSE_ROTATE, &pose, &interaction) == FGIM3D_STATUS_OK,
        "Rotation drag submission failed.");
    require(interaction.changed != 0, "Rotation drag did not update the quaternion.");
    require(fgim3d_end_frame(context, &interaction, &vertex_count, &command_count) == FGIM3D_STATUS_OK,
        "Rotation drag EndFrame failed.");

    auto release = frame_input(false, camera, finish, world_up);
    require(fgim3d_begin_frame(context, &release) == FGIM3D_STATUS_OK, "Rotation release frame failed.");
    require(fgim3d_manipulate_pose(context, id, FGIM3D_POSE_ROTATE, &pose, &interaction) == FGIM3D_STATUS_OK,
        "Rotation release submission failed.");
    require(fgim3d_end_frame(context, &interaction, &vertex_count, &command_count) == FGIM3D_STATUS_OK,
        "Rotation release EndFrame failed.");
    require(interaction.active_id == FGIM3D_INVALID_ID, "Rotation remained active after release.");
    require(fgim3d_context_destroy(context) == FGIM3D_STATUS_OK, "Rotation test context destruction failed.");
    return pose.rotation;
}

void verify_rotation_origin_uses_pose_translation() {
    fgim3d_context* context = nullptr;
    require(fgim3d_context_create(&context) == FGIM3D_STATUS_OK, "Origin test context creation failed.");
    fgim3d_pose pose{};
    pose.position = { 3.0f, -2.0f, 0.0f };
    pose.rotation.w = 1.0f;
    auto input = frame_input(false, { 3.0f, -2.0f, 10.0f }, pose.position);
    require(fgim3d_begin_frame(context, &input) == FGIM3D_STATUS_OK, "Origin test BeginFrame failed.");
    fgim3d_interaction interaction{};
    require(
        fgim3d_manipulate_pose(context, 401u, FGIM3D_POSE_ROTATE, &pose, &interaction) == FGIM3D_STATUS_OK,
        "Origin test gizmo submission failed.");
    uint32_t vertex_count = 0;
    uint32_t command_count = 0;
    require(
        fgim3d_end_frame(context, &interaction, &vertex_count, &command_count) == FGIM3D_STATUS_OK,
        "Origin test EndFrame failed.");
    std::vector<fgim3d_vertex> vertices(vertex_count);
    std::vector<fgim3d_draw_command> commands(command_count);
    require(
        fgim3d_copy_draw_data(
            context,
            vertices.data(),
            vertex_count,
            commands.data(),
            command_count) == FGIM3D_STATUS_OK,
        "Origin test draw copy failed.");
    float min_x = INFINITY;
    float max_x = -INFINITY;
    float min_y = INFINITY;
    float max_y = -INFINITY;
    for (const auto& vertex : vertices) {
        min_x = std::min(min_x, vertex.x);
        max_x = std::max(max_x, vertex.x);
        min_y = std::min(min_y, vertex.y);
        max_y = std::max(max_y, vertex.y);
    }
    require(near((min_x + max_x) * 0.5f, pose.position.x, 0.02f),
        "The global rotation gizmo ignored the pose X translation.");
    require(near((min_y + max_y) * 0.5f, pose.position.y, 0.02f),
        "The global rotation gizmo ignored the pose Y translation.");
    require(fgim3d_context_destroy(context) == FGIM3D_STATUS_OK, "Origin test context destruction failed.");
}

void verify_constrained_axis_drag() {
    fgim3d_context* context = nullptr;
    require(fgim3d_context_create(&context) == FGIM3D_STATUS_OK, "Axis test context creation failed.");

    constexpr uint32_t id = 501u;
    const fgim3d_vec3 origin{ 0.0f, 0.0f, 0.0f };
    const fgim3d_vec3 axis{ 1.0f, 0.0f, 0.0f };
    fgim3d_vec3 position{ 1.0f, 2.0f, -3.0f };
    fgim3d_interaction interaction{};
    uint32_t vertex_count = 0;
    uint32_t command_count = 0;

    auto submit = [&](bool select_down, fgim3d_vec3 target) {
        auto input = frame_input(select_down, { 0.0f, 0.0f, 10.0f }, target);
        require(fgim3d_begin_frame(context, &input) == FGIM3D_STATUS_OK, "Axis test BeginFrame failed.");
        require(
            fgim3d_manipulate_axis_translation(
                context,
                id,
                origin,
                axis,
                0xff0000ffu,
                &position,
                &interaction) == FGIM3D_STATUS_OK,
            "Axis test submission failed.");
        const uint32_t activated_id = interaction.activated_id;
        const int32_t changed = interaction.changed;
        require(
            fgim3d_end_frame(context, &interaction, &vertex_count, &command_count) == FGIM3D_STATUS_OK,
            "Axis test EndFrame failed.");
        return std::pair<uint32_t, int32_t>{ activated_id, changed };
    };

    submit(false, { 0.6f, 0.0f, 0.0f });
    require(interaction.hot_id == id, "The constrained axis did not become hot.");
    const auto [activated_id, press_changed] = submit(true, { 0.6f, 0.0f, 0.0f });
    require(interaction.active_id == id && activated_id == id,
        "The constrained axis activation was not attributed to its application ID.");
    require(press_changed == 0, "The constrained axis unexpectedly moved during activation.");
    const auto [drag_activated_id, drag_changed] = submit(true, { 0.9f, 0.0f, 0.0f });
    require(drag_activated_id == FGIM3D_INVALID_ID, "The constrained axis activated twice during one drag.");
    require(drag_changed != 0, "The constrained axis drag did not update its point.");
    require(position.x > 1.2f, "The constrained axis drag did not advance along the requested axis.");
    require(near(position.y, 2.0f) && near(position.z, -3.0f),
        "The constrained axis drag changed a perpendicular coordinate.");
    submit(false, { 0.9f, 0.0f, 0.0f });
    require(interaction.active_id == FGIM3D_INVALID_ID, "The constrained axis remained active after release.");

    require(fgim3d_context_destroy(context) == FGIM3D_STATUS_OK, "Axis test context destruction failed.");
}

} // namespace

int main() {
    require(fgim3d_get_abi_version() == FGIM3D_ABI_VERSION, "ABI version mismatch.");

    fgim3d_pose pose{};
    pose.position = { 3.0f, -2.0f, 7.0f };
    pose.rotation = { 0.2f, -0.3f, 0.1f, 0.9f };
    fgim3d_pose round_trip{};
    require(fgim3d_test_pose_round_trip(&pose, &round_trip) == FGIM3D_STATUS_OK, "Quaternion round trip failed.");
    const float length = std::sqrt(
        round_trip.rotation.x * round_trip.rotation.x +
        round_trip.rotation.y * round_trip.rotation.y +
        round_trip.rotation.z * round_trip.rotation.z +
        round_trip.rotation.w * round_trip.rotation.w);
    require(near(length, 1.0f), "Round-trip quaternion was not normalized.");
    require(
        pose.rotation.x * round_trip.rotation.x + pose.rotation.y * round_trip.rotation.y +
        pose.rotation.z * round_trip.rotation.z + pose.rotation.w * round_trip.rotation.w > 0.0f,
        "Quaternion hemisphere continuity was lost.");

    fgim3d_context* context = nullptr;
    require(fgim3d_context_create(&context) == FGIM3D_STATUS_OK, "Context creation failed.");

    auto input = frame_input();
    require(fgim3d_begin_frame(context, &input) == FGIM3D_STATUS_OK, "BeginFrame failed.");
    fgim3d_interaction interaction{};
    require(
        fgim3d_manipulate_pose(context, 101u, FGIM3D_POSE_ROTATE, &round_trip, &interaction) == FGIM3D_STATUS_OK,
        "Rotation gizmo submission failed.");
    fgim3d_vec3 constrained_position{ 1.0f, 0.0f, 0.0f };
    require(
        fgim3d_manipulate_axis_translation(
            context,
            202u,
            constrained_position,
            { 1.0f, 0.0f, 0.0f },
            0xff0000ffu,
            &constrained_position,
            &interaction) == FGIM3D_STATUS_OK,
        "Pinned constrained-axis shim failed.");

    uint32_t vertex_count = 0;
    uint32_t command_count = 0;
    require(
        fgim3d_end_frame(context, &interaction, &vertex_count, &command_count) == FGIM3D_STATUS_OK,
        "EndFrame failed.");
    require(vertex_count > 0 && command_count > 0, "Gizmos emitted no draw commands.");

    std::vector<fgim3d_vertex> vertices(vertex_count);
    std::vector<fgim3d_draw_command> commands(command_count);
    require(
        fgim3d_copy_draw_data(
            context,
            vertices.data(),
            static_cast<uint32_t>(vertices.size()),
            commands.data(),
            static_cast<uint32_t>(commands.size())) == FGIM3D_STATUS_OK,
        "Caller-owned draw copy failed.");
    uint32_t expected_first = 0;
    for (uint32_t index = 0; index < command_count; ++index) {
        require(commands[index].source_order == index, "Draw source ordering was not preserved.");
        require(commands[index].first_vertex == expected_first, "Draw command ranges are not contiguous.");
        expected_first += commands[index].vertex_count;
    }
    require(expected_first == vertex_count, "Draw command ranges do not cover the copied vertices.");

    auto invalid_reset = frame_input();
    require(fgim3d_begin_frame(context, &invalid_reset) == FGIM3D_STATUS_OK, "Reset guard frame failed.");
    require(
        fgim3d_context_reset_interaction(context) == FGIM3D_STATUS_INVALID_STATE,
        "Interaction reset was incorrectly permitted during a frame.");
    require(
        fgim3d_end_frame(context, &interaction, &vertex_count, &command_count) == FGIM3D_STATUS_OK,
        "Reset guard EndFrame failed.");
    require(fgim3d_context_reset_interaction(context) == FGIM3D_STATUS_OK, "Boundary reset failed.");

    require(fgim3d_context_destroy(context) == FGIM3D_STATUS_OK, "Context destruction failed.");

    constexpr float axis_radius = 0.9237604f;
    constexpr float view_radius = 1.0264004f;
    constexpr float diagonal = 0.70710678f;
    const fgim3d_quaternion x_rotation = simulate_rotation(
        { 10.0f, 0.0f, 0.0f },
        { 0.0f, 0.0f, 1.0f },
        { 0.0f, axis_radius * diagonal, axis_radius * diagonal },
        { 0.0f, -axis_radius * diagonal, axis_radius * diagonal },
        301u);
    const fgim3d_quaternion y_rotation = simulate_rotation(
        { 0.0f, 10.0f, 0.0f },
        { 0.0f, 0.0f, 1.0f },
        { axis_radius * diagonal, 0.0f, axis_radius * diagonal },
        { -axis_radius * diagonal, 0.0f, axis_radius * diagonal },
        302u);
    const fgim3d_quaternion z_rotation = simulate_rotation(
        { 0.0f, 0.0f, 10.0f },
        { 0.0f, 1.0f, 0.0f },
        { axis_radius * diagonal, axis_radius * diagonal, 0.0f },
        { -axis_radius * diagonal, axis_radius * diagonal, 0.0f },
        303u);
    const fgim3d_quaternion view_rotation = simulate_rotation(
        { 0.0f, 0.0f, 10.0f },
        { 0.0f, 1.0f, 0.0f },
        { view_radius, 0.0f, 0.0f },
        { 0.0f, view_radius, 0.0f },
        304u);
    require(std::fabs(x_rotation.x) > 0.1f, "World X rotation did not affect the X quaternion component.");
    require(std::fabs(y_rotation.y) > 0.1f, "World Y rotation did not affect the Y quaternion component.");
    require(std::fabs(z_rotation.z) > 0.1f, "World Z rotation did not affect the Z quaternion component.");
    require(std::fabs(view_rotation.z) > 0.1f, "View-axis rotation did not rotate around the camera direction.");

    const fgim3d_quaternion minus_x_frame{ 0.0f, 0.0f, 1.0f, 0.0f };
    const fgim3d_quaternion tangent_roll = simulate_rotation(
        { 10.0f, 0.0f, 0.0f },
        { 0.0f, 0.0f, 1.0f },
        { 0.0f, axis_radius * diagonal, axis_radius * diagonal },
        { 0.0f, -axis_radius * diagonal, axis_radius * diagonal },
        305u,
        minus_x_frame);
    const fgim3d_quaternion tangent_reaim = simulate_rotation(
        { 0.0f, 10.0f, 0.0f },
        { 0.0f, 0.0f, 1.0f },
        { axis_radius * diagonal, 0.0f, axis_radius * diagonal },
        { -axis_radius * diagonal, 0.0f, axis_radius * diagonal },
        306u,
        minus_x_frame);
    const fgim3d_vec3 negative_x{ -1.0f, 0.0f, 0.0f };
    require(distance(rotate(tangent_roll, { 1.0f, 0.0f, 0.0f }), negative_x) < 1.0e-4f,
        "A world-X roll changed an outlet tangent parallel to world X.");
    require(distance(rotate(tangent_reaim, { 1.0f, 0.0f, 0.0f }), negative_x) > 0.1f,
        "A world-Y rotation failed to re-aim a negative-X outlet tangent.");
    verify_rotation_origin_uses_pose_translation();
    verify_constrained_axis_drag();
    return 0;
}
