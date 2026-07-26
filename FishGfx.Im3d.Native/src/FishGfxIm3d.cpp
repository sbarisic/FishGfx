#include "FishGfxIm3d.h"

#include "im3d.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <exception>
#include <limits>
#include <new>
#include <string>
#include <thread>
#include <vector>

namespace {

thread_local std::string last_error;

struct ContextRecord {
    Im3d::Context context;
    std::thread::id owner = std::this_thread::get_id();
    bool frame_open = false;
    bool draw_ready = false;
    std::vector<fgim3d_vertex> vertices;
    std::vector<fgim3d_draw_command> commands;
};

static_assert(sizeof(fgim3d_vertex) == 20, "The public vertex ABI must remain packed and predictable.");

fgim3d_status fail(fgim3d_status status, const char* message) {
    last_error = message == nullptr ? "Unknown im3d bridge error." : message;
    return status;
}

template <typename Function>
fgim3d_status guard(Function&& function) noexcept {
    try {
        last_error.clear();
        return function();
    } catch (const std::exception& error) {
        return fail(FGIM3D_STATUS_INTERNAL_ERROR, error.what());
    } catch (...) {
        return fail(FGIM3D_STATUS_INTERNAL_ERROR, "Unknown native exception in the im3d bridge.");
    }
}

bool finite(float value) {
    return std::isfinite(value);
}

bool finite(const fgim3d_vec3& value) {
    return finite(value.x) && finite(value.y) && finite(value.z);
}

bool finite(const fgim3d_quaternion& value) {
    return finite(value.x) && finite(value.y) && finite(value.z) && finite(value.w);
}

Im3d::Vec3 to_vec3(const fgim3d_vec3& value) {
    return Im3d::Vec3(value.x, value.y, value.z);
}

fgim3d_vec3 from_vec3(const Im3d::Vec3& value) {
    return { value.x, value.y, value.z };
}

float dot(const Im3d::Vec3& a, const Im3d::Vec3& b) {
    return a.x * b.x + a.y * b.y + a.z * b.z;
}

Im3d::Vec3 cross(const Im3d::Vec3& a, const Im3d::Vec3& b) {
    return Im3d::Vec3(
        a.y * b.z - a.z * b.y,
        a.z * b.x - a.x * b.z,
        a.x * b.y - a.y * b.x);
}

float length(const Im3d::Vec3& value) {
    return std::sqrt(dot(value, value));
}

bool normalize(Im3d::Vec3* value) {
    const float magnitude = length(*value);
    if (!finite(magnitude) || magnitude <= 1.0e-7f) {
        return false;
    }

    value->x /= magnitude;
    value->y /= magnitude;
    value->z /= magnitude;
    return true;
}

bool normalize(fgim3d_quaternion* value) {
    const double magnitude_squared =
        static_cast<double>(value->x) * value->x +
        static_cast<double>(value->y) * value->y +
        static_cast<double>(value->z) * value->z +
        static_cast<double>(value->w) * value->w;
    if (!std::isfinite(magnitude_squared) || magnitude_squared <= 1.0e-20) {
        return false;
    }

    const float inverse = static_cast<float>(1.0 / std::sqrt(magnitude_squared));
    value->x *= inverse;
    value->y *= inverse;
    value->z *= inverse;
    value->w *= inverse;
    return finite(*value);
}

Im3d::Mat3 quaternion_to_matrix(fgim3d_quaternion quaternion) {
    normalize(&quaternion);
    const float xx = quaternion.x * quaternion.x;
    const float yy = quaternion.y * quaternion.y;
    const float zz = quaternion.z * quaternion.z;
    const float xy = quaternion.x * quaternion.y;
    const float xz = quaternion.x * quaternion.z;
    const float yz = quaternion.y * quaternion.z;
    const float wx = quaternion.w * quaternion.x;
    const float wy = quaternion.w * quaternion.y;
    const float wz = quaternion.w * quaternion.z;

    return Im3d::Mat3(
        1.0f - 2.0f * (yy + zz), 2.0f * (xy - wz),        2.0f * (xz + wy),
        2.0f * (xy + wz),        1.0f - 2.0f * (xx + zz), 2.0f * (yz - wx),
        2.0f * (xz - wy),        2.0f * (yz + wx),        1.0f - 2.0f * (xx + yy));
}

bool orthonormalize(Im3d::Mat3* matrix) {
    Im3d::Vec3 x = matrix->getCol(0);
    Im3d::Vec3 y = matrix->getCol(1);
    if (!normalize(&x)) {
        return false;
    }

    const float projection = dot(y, x);
    y = Im3d::Vec3(y.x - projection * x.x, y.y - projection * x.y, y.z - projection * x.z);
    if (!normalize(&y)) {
        return false;
    }

    Im3d::Vec3 z = cross(x, y);
    if (!normalize(&z)) {
        return false;
    }

    // Recompute Y to remove the last bit of accumulated skew while preserving handedness.
    y = cross(z, x);
    if (!normalize(&y)) {
        return false;
    }

    const float determinant = dot(x, cross(y, z));
    if (!finite(determinant) || determinant <= 0.0f) {
        return false;
    }

    matrix->setCol(0, x);
    matrix->setCol(1, y);
    matrix->setCol(2, z);
    return true;
}

fgim3d_quaternion matrix_to_quaternion(const Im3d::Mat3& matrix) {
    fgim3d_quaternion result{};
    const float trace = matrix(0, 0) + matrix(1, 1) + matrix(2, 2);
    if (trace > 0.0f) {
        const float s = std::sqrt(trace + 1.0f) * 2.0f;
        result.w = 0.25f * s;
        result.x = (matrix(2, 1) - matrix(1, 2)) / s;
        result.y = (matrix(0, 2) - matrix(2, 0)) / s;
        result.z = (matrix(1, 0) - matrix(0, 1)) / s;
    } else if (matrix(0, 0) > matrix(1, 1) && matrix(0, 0) > matrix(2, 2)) {
        const float s = std::sqrt(1.0f + matrix(0, 0) - matrix(1, 1) - matrix(2, 2)) * 2.0f;
        result.w = (matrix(2, 1) - matrix(1, 2)) / s;
        result.x = 0.25f * s;
        result.y = (matrix(0, 1) + matrix(1, 0)) / s;
        result.z = (matrix(0, 2) + matrix(2, 0)) / s;
    } else if (matrix(1, 1) > matrix(2, 2)) {
        const float s = std::sqrt(1.0f + matrix(1, 1) - matrix(0, 0) - matrix(2, 2)) * 2.0f;
        result.w = (matrix(0, 2) - matrix(2, 0)) / s;
        result.x = (matrix(0, 1) + matrix(1, 0)) / s;
        result.y = 0.25f * s;
        result.z = (matrix(1, 2) + matrix(2, 1)) / s;
    } else {
        const float s = std::sqrt(1.0f + matrix(2, 2) - matrix(0, 0) - matrix(1, 1)) * 2.0f;
        result.w = (matrix(1, 0) - matrix(0, 1)) / s;
        result.x = (matrix(0, 2) + matrix(2, 0)) / s;
        result.y = (matrix(1, 2) + matrix(2, 1)) / s;
        result.z = 0.25f * s;
    }

    normalize(&result);
    return result;
}

void preserve_hemisphere(const fgim3d_quaternion& reference, fgim3d_quaternion* value) {
    const float hemisphere =
        reference.x * value->x + reference.y * value->y +
        reference.z * value->z + reference.w * value->w;
    if (hemisphere < 0.0f) {
        value->x = -value->x;
        value->y = -value->y;
        value->z = -value->z;
        value->w = -value->w;
    }
}

ContextRecord* record(fgim3d_context* context) {
    return reinterpret_cast<ContextRecord*>(context);
}

bool valid_thread(ContextRecord* context) {
    return context != nullptr && context->owner == std::this_thread::get_id();
}

fgim3d_status require_context(fgim3d_context* context, ContextRecord** output) {
    if (context == nullptr) {
        return fail(FGIM3D_STATUS_INVALID_ARGUMENT, "The im3d context is null.");
    }

    auto* value = record(context);
    if (!valid_thread(value)) {
        return fail(FGIM3D_STATUS_INVALID_STATE, "The im3d context was accessed from a different thread.");
    }

    Im3d::SetContext(value->context);
    *output = value;
    return FGIM3D_STATUS_OK;
}

void set_interaction(fgim3d_interaction* interaction, bool changed, uint32_t activated_id = 0) {
    if (interaction == nullptr) {
        return;
    }

    interaction->active_id = Im3d::GetActiveId();
    interaction->hot_id = Im3d::GetHotId();
    interaction->activated_id = activated_id;
    interaction->changed = changed ? 1 : 0;
}

fgim3d_status validate_pose(fgim3d_pose* pose) {
    if (pose == nullptr || !finite(pose->position) || !finite(pose->rotation)) {
        return fail(FGIM3D_STATUS_INVALID_ARGUMENT, "The pose contains non-finite values.");
    }

    if (!normalize(&pose->rotation)) {
        return fail(FGIM3D_STATUS_INVALID_ARGUMENT, "The pose quaternion has zero length.");
    }

    return FGIM3D_STATUS_OK;
}

} // namespace

struct fgim3d_context {};

uint32_t FGIM3D_CALL fgim3d_get_abi_version(void) {
    return FGIM3D_ABI_VERSION;
}

const char* FGIM3D_CALL fgim3d_get_last_error(void) {
    return last_error.c_str();
}

fgim3d_status FGIM3D_CALL fgim3d_context_create(fgim3d_context** out_context) {
    return guard([&]() {
        if (out_context == nullptr) {
            return fail(FGIM3D_STATUS_INVALID_ARGUMENT, "The output context pointer is null.");
        }

        auto* value = new (std::nothrow) ContextRecord();
        if (value == nullptr) {
            return fail(FGIM3D_STATUS_INTERNAL_ERROR, "Unable to allocate an im3d context.");
        }

        *out_context = reinterpret_cast<fgim3d_context*>(value);
        return FGIM3D_STATUS_OK;
    });
}

fgim3d_status FGIM3D_CALL fgim3d_context_destroy(fgim3d_context* context) {
    return guard([&]() {
        if (context == nullptr) {
            return FGIM3D_STATUS_OK;
        }

        // Normal disposal is render-thread-owned. Destruction itself has no
        // im3d calls, however, so permit SafeHandle's finalizer fallback to
        // reclaim an abandoned context instead of leaking it.
        auto* value = record(context);
        delete value;
        return FGIM3D_STATUS_OK;
    });
}

fgim3d_status FGIM3D_CALL fgim3d_context_reset_interaction(fgim3d_context* context) {
    return guard([&]() {
        ContextRecord* value = nullptr;
        const auto status = require_context(context, &value);
        if (status != FGIM3D_STATUS_OK) {
            return status;
        }
        if (value->frame_open) {
            return fail(FGIM3D_STATUS_INVALID_STATE, "Interaction reset is only valid between frames.");
        }

        value->context.resetId();
        return FGIM3D_STATUS_OK;
    });
}

fgim3d_status FGIM3D_CALL fgim3d_begin_frame(
    fgim3d_context* context,
    const fgim3d_frame_input* input) {
    return guard([&]() {
        ContextRecord* value = nullptr;
        const auto status = require_context(context, &value);
        if (status != FGIM3D_STATUS_OK) {
            return status;
        }
        if (input == nullptr || !finite(input->cursor_ray_origin) ||
            !finite(input->cursor_ray_direction) || !finite(input->world_up) ||
            !finite(input->view_origin) || !finite(input->view_direction) ||
            !finite(input->viewport_width) || !finite(input->viewport_height) ||
            !finite(input->projection_scale_y) || !finite(input->delta_time) ||
            input->viewport_width <= 0.0f || input->viewport_height <= 0.0f ||
            input->projection_scale_y <= 0.0f) {
            return fail(FGIM3D_STATUS_INVALID_ARGUMENT, "The im3d frame input is invalid.");
        }
        if (value->frame_open) {
            return fail(FGIM3D_STATUS_INVALID_STATE, "An im3d frame is already open.");
        }

        Im3d::Vec3 cursor_direction = to_vec3(input->cursor_ray_direction);
        Im3d::Vec3 world_up = to_vec3(input->world_up);
        Im3d::Vec3 view_direction = to_vec3(input->view_direction);
        if (!normalize(&cursor_direction) || !normalize(&world_up) || !normalize(&view_direction)) {
            return fail(
                FGIM3D_STATUS_INVALID_ARGUMENT,
                "The cursor ray, world-up, and view directions must be nonzero.");
        }

        auto& app = value->context.getAppData();
        app.m_cursorRayOrigin = to_vec3(input->cursor_ray_origin);
        app.m_cursorRayDirection = cursor_direction;
        app.m_worldUp = world_up;
        app.m_viewOrigin = to_vec3(input->view_origin);
        app.m_viewDirection = view_direction;
        app.m_viewportSize = Im3d::Vec2(input->viewport_width, input->viewport_height);
        app.m_projScaleY = input->projection_scale_y;
        app.m_projOrtho = input->projection_orthographic != 0;
        app.m_deltaTime = std::max(0.0f, input->delta_time);
        app.m_flipGizmoWhenBehind = input->flip_gizmo_when_behind != 0;
        app.m_keyDown[Im3d::Mouse_Left] = input->select_down != 0;
        app.m_keyDown[Im3d::Key_L] = false;
        app.m_keyDown[Im3d::Key_R] = false;
        app.m_keyDown[Im3d::Key_S] = false;
        app.m_keyDown[Im3d::Key_T] = false;

        Im3d::NewFrame();
        value->frame_open = true;
        value->draw_ready = false;
        value->vertices.clear();
        value->commands.clear();
        return FGIM3D_STATUS_OK;
    });
}

fgim3d_status FGIM3D_CALL fgim3d_manipulate_pose(
    fgim3d_context* context,
    uint32_t id,
    fgim3d_pose_operation operation,
    fgim3d_pose* pose,
    fgim3d_interaction* interaction) {
    return guard([&]() {
        ContextRecord* value = nullptr;
        auto status = require_context(context, &value);
        if (status != FGIM3D_STATUS_OK) {
            return status;
        }
        if (!value->frame_open) {
            return fail(FGIM3D_STATUS_INVALID_STATE, "Pose manipulation requires an open im3d frame.");
        }
        if (id == FGIM3D_INVALID_ID) {
            return fail(FGIM3D_STATUS_INVALID_ARGUMENT, "The gizmo ID must be nonzero.");
        }
        status = validate_pose(pose);
        if (status != FGIM3D_STATUS_OK) {
            return status;
        }

        const fgim3d_quaternion input_rotation = pose->rotation;
        Im3d::Mat3 rotation = quaternion_to_matrix(pose->rotation);
        Im3d::Mat4 pose_matrix(to_vec3(pose->position), rotation, Im3d::Vec3(1.0f));
        value->context.pushMatrix(pose_matrix);

        bool changed = false;
        if (operation == FGIM3D_POSE_TRANSLATE) {
            float position[3] = { pose->position.x, pose->position.y, pose->position.z };
            changed = Im3d::GizmoTranslation(id, position, false);
            pose->position = { position[0], position[1], position[2] };
        } else if (operation == FGIM3D_POSE_ROTATE) {
            changed = Im3d::GizmoRotation(id, rotation.m, false);
            if (!orthonormalize(&rotation)) {
                value->context.popMatrix();
                return fail(FGIM3D_STATUS_INTERNAL_ERROR, "im3d returned a non-orthonormal or reflected rotation.");
            }
            pose->rotation = matrix_to_quaternion(rotation);
            preserve_hemisphere(input_rotation, &pose->rotation);
        } else {
            value->context.popMatrix();
            return fail(FGIM3D_STATUS_INVALID_ARGUMENT, "The requested pose operation is unknown.");
        }

        const uint32_t activated = Im3d::GizmoWasActivated() ? id : FGIM3D_INVALID_ID;
        value->context.popMatrix();
        set_interaction(interaction, changed, activated);
        return FGIM3D_STATUS_OK;
    });
}

fgim3d_status FGIM3D_CALL fgim3d_manipulate_axis_translation(
    fgim3d_context* context,
    uint32_t id,
    fgim3d_vec3 axis_origin,
    fgim3d_vec3 axis_direction,
    uint32_t rgba,
    fgim3d_vec3* position,
    fgim3d_interaction* interaction) {
    return guard([&]() {
        ContextRecord* value = nullptr;
        const auto status = require_context(context, &value);
        if (status != FGIM3D_STATUS_OK) {
            return status;
        }
        if (!value->frame_open) {
            return fail(FGIM3D_STATUS_INVALID_STATE, "Axis manipulation requires an open im3d frame.");
        }
        if (id == FGIM3D_INVALID_ID || position == nullptr || !finite(axis_origin) ||
            !finite(axis_direction) || !finite(*position)) {
            return fail(FGIM3D_STATUS_INVALID_ARGUMENT, "The constrained-axis arguments are invalid.");
        }

        Im3d::Vec3 axis = to_vec3(axis_direction);
        if (!normalize(&axis)) {
            return fail(FGIM3D_STATUS_INVALID_ARGUMENT, "The constrained axis has zero length.");
        }

        Im3d::Vec3 current = to_vec3(*position);
        const Im3d::Vec3 origin = to_vec3(axis_origin);
        const float world_height = value->context.pixelsToWorldSize(current, value->context.m_gizmoHeightPixels);
        const float world_size = value->context.pixelsToWorldSize(current, value->context.m_gizmoSizePixels);

        value->context.pushId(id);
        value->context.m_appId = id;
        const Im3d::Id axis_id = Im3d::MakeId("constrainedAxis");
        value->context.pushEnableSorting(true);
        value->context.pushMatrix(Im3d::Mat4(1.0f));
        value->context.gizmoAxisTranslation_Draw(axis_id, origin, axis, world_height, world_size, Im3d::Color(rgba));
        const bool changed = value->context.gizmoAxisTranslation_Behavior(
            axis_id,
            origin,
            axis,
            value->context.getAppData().m_snapTranslation,
            world_height,
            world_size,
            &current);
        const uint32_t activated = value->context.idWasActivated() ? id : FGIM3D_INVALID_ID;
        value->context.popMatrix();
        value->context.popEnableSorting();
        value->context.popId();

        *position = from_vec3(current);
        set_interaction(interaction, changed, activated);
        return FGIM3D_STATUS_OK;
    });
}

fgim3d_status FGIM3D_CALL fgim3d_end_frame(
    fgim3d_context* context,
    fgim3d_interaction* interaction,
    uint32_t* vertex_count,
    uint32_t* command_count) {
    return guard([&]() {
        ContextRecord* value = nullptr;
        const auto status = require_context(context, &value);
        if (status != FGIM3D_STATUS_OK) {
            return status;
        }
        if (!value->frame_open || vertex_count == nullptr || command_count == nullptr) {
            return fail(FGIM3D_STATUS_INVALID_STATE, "Ending the im3d frame requires an open frame and count outputs.");
        }

        Im3d::EndFrame();
        value->frame_open = false;
        value->draw_ready = true;
        set_interaction(interaction, false);

        const Im3d::DrawList* lists = Im3d::GetDrawLists();
        const uint32_t list_count = Im3d::GetDrawListCount();
        uint32_t first_vertex = 0;
        for (uint32_t order = 0; order < list_count; ++order) {
            const auto& list = lists[order];
            if (list.m_primType < Im3d::DrawPrimitive_Triangles ||
                list.m_primType >= Im3d::DrawPrimitive_Count) {
                return fail(FGIM3D_STATUS_INTERNAL_ERROR, "im3d emitted an unsupported draw primitive.");
            }

            fgim3d_draw_command command{};
            command.primitive = static_cast<uint32_t>(list.m_primType);
            command.layer = list.m_layerId;
            command.source_order = order;
            command.first_vertex = first_vertex;
            command.vertex_count = list.m_vertexCount;
            value->commands.push_back(command);

            for (uint32_t index = 0; index < list.m_vertexCount; ++index) {
                const auto& source = list.m_vertexData[index];
                const Im3d::Color color = source.m_color;
                fgim3d_vertex vertex{};
                vertex.x = source.m_positionSize.x;
                vertex.y = source.m_positionSize.y;
                vertex.z = source.m_positionSize.z;
                vertex.size = source.m_positionSize.w;
                vertex.r = static_cast<uint8_t>(std::lround(color.getR() * 255.0f));
                vertex.g = static_cast<uint8_t>(std::lround(color.getG() * 255.0f));
                vertex.b = static_cast<uint8_t>(std::lround(color.getB() * 255.0f));
                vertex.a = static_cast<uint8_t>(std::lround(color.getA() * 255.0f));
                value->vertices.push_back(vertex);
            }
            first_vertex += list.m_vertexCount;
        }

        *vertex_count = static_cast<uint32_t>(value->vertices.size());
        *command_count = static_cast<uint32_t>(value->commands.size());
        return FGIM3D_STATUS_OK;
    });
}

fgim3d_status FGIM3D_CALL fgim3d_copy_draw_data(
    fgim3d_context* context,
    fgim3d_vertex* vertices,
    uint32_t vertex_capacity,
    fgim3d_draw_command* commands,
    uint32_t command_capacity) {
    return guard([&]() {
        ContextRecord* value = nullptr;
        const auto status = require_context(context, &value);
        if (status != FGIM3D_STATUS_OK) {
            return status;
        }
        if (!value->draw_ready || value->frame_open) {
            return fail(FGIM3D_STATUS_INVALID_STATE, "Draw data is only available after EndFrame and before NewFrame.");
        }
        if (vertex_capacity < value->vertices.size() || command_capacity < value->commands.size()) {
            return fail(FGIM3D_STATUS_BUFFER_TOO_SMALL, "The caller-owned im3d draw buffers are too small.");
        }
        if ((!value->vertices.empty() && vertices == nullptr) || (!value->commands.empty() && commands == nullptr)) {
            return fail(FGIM3D_STATUS_INVALID_ARGUMENT, "A non-empty im3d draw buffer is null.");
        }

        if (!value->vertices.empty()) {
            std::memcpy(vertices, value->vertices.data(), value->vertices.size() * sizeof(fgim3d_vertex));
        }
        if (!value->commands.empty()) {
            std::memcpy(commands, value->commands.data(), value->commands.size() * sizeof(fgim3d_draw_command));
        }
        return FGIM3D_STATUS_OK;
    });
}

fgim3d_status FGIM3D_CALL fgim3d_test_pose_round_trip(
    const fgim3d_pose* input,
    fgim3d_pose* output) {
    return guard([&]() {
        if (input == nullptr || output == nullptr) {
            return fail(FGIM3D_STATUS_INVALID_ARGUMENT, "The round-trip pose pointers are null.");
        }
        *output = *input;
        const auto status = validate_pose(output);
        if (status != FGIM3D_STATUS_OK) {
            return status;
        }
        const fgim3d_quaternion reference = output->rotation;
        Im3d::Mat3 matrix = quaternion_to_matrix(output->rotation);
        if (!orthonormalize(&matrix)) {
            return fail(FGIM3D_STATUS_INTERNAL_ERROR, "The round-trip matrix could not be orthonormalized.");
        }
        output->rotation = matrix_to_quaternion(matrix);
        preserve_hemisphere(reference, &output->rotation);
        return FGIM3D_STATUS_OK;
    });
}
