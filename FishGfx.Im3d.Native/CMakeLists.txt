cmake_minimum_required(VERSION 3.24)

project(FishGfxIm3dNative VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

add_library(FishGfxIm3dNative SHARED
    src/FishGfxIm3d.cpp
    thirdparty/im3d/im3d.cpp)

target_compile_definitions(FishGfxIm3dNative PRIVATE FGIM3D_BUILD_DLL)
target_include_directories(FishGfxIm3dNative
    PUBLIC include
    PRIVATE thirdparty/im3d)

if(MSVC)
    target_compile_options(FishGfxIm3dNative PRIVATE /W4 /permissive- /EHsc)
    # Keep FishGfx bridge code at /W4 without carrying warnings emitted by the
    # pinned, unmodified third-party translation unit into solution builds.
    set_source_files_properties(thirdparty/im3d/im3d.cpp PROPERTIES COMPILE_OPTIONS "/W0")
endif()

include(CTest)
if(BUILD_TESTING)
    add_executable(FishGfxIm3dNativeTests tests/Im3dBridgeTests.cpp)
    target_link_libraries(FishGfxIm3dNativeTests PRIVATE FishGfxIm3dNative)
    target_include_directories(FishGfxIm3dNativeTests PRIVATE include)
    add_test(NAME FishGfxIm3dNativeTests COMMAND FishGfxIm3dNativeTests)
endif()
