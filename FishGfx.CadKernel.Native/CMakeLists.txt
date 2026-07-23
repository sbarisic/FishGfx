cmake_minimum_required(VERSION 3.25)

project(FishGfxCadKernelNative VERSION 1.0.0 LANGUAGES CXX)

find_package(OpenCASCADE CONFIG REQUIRED)

add_library(FishGfx.CadKernel.Native SHARED
    src/CadKernel.cpp
)

target_compile_features(FishGfx.CadKernel.Native PRIVATE cxx_std_17)
target_compile_definitions(FishGfx.CadKernel.Native PRIVATE
    FGCAD_NATIVE_EXPORTS
    NOMINMAX
    WIN32_LEAN_AND_MEAN
)
target_include_directories(FishGfx.CadKernel.Native
    PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include
    PRIVATE ${OpenCASCADE_INCLUDE_DIR}
)
target_link_libraries(FishGfx.CadKernel.Native PRIVATE
    TKernel
    TKMath
    TKG2d
    TKG3d
    TKGeomBase
    TKBRep
    TKGeomAlgo
    TKTopAlgo
	TKBO
	TKBool
    TKPrim
    TKShHealing
    TKOffset
    TKMesh
    TKCDF
    TKLCAF
    TKCAF
    TKBinL
    TKBin
    TKBinXCAF
    TKXCAF
    TKDE
    TKXSBase
    TKDESTEP
)

if(MSVC)
    target_compile_options(FishGfx.CadKernel.Native PRIVATE /W4 /permissive- /EHsc /utf-8)
endif()

include(CTest)

if(BUILD_TESTING)
    add_executable(FishGfx.CadKernel.Native.Tests
        tests/CadKernelTests.cpp
    )
    target_compile_features(FishGfx.CadKernel.Native.Tests PRIVATE cxx_std_17)
    target_include_directories(FishGfx.CadKernel.Native.Tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
    )
    target_link_libraries(FishGfx.CadKernel.Native.Tests PRIVATE FishGfx.CadKernel.Native)
    add_test(NAME FishGfx.CadKernel.Native.Tests COMMAND FishGfx.CadKernel.Native.Tests)
endif()
