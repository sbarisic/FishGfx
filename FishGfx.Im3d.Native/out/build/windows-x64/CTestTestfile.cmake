# CMake generated Testfile for 
# Source directory: E:/Projects/FishGfx/FishGfx.Im3d.Native
# Build directory: E:/Projects/FishGfx/FishGfx.Im3d.Native/out/build/windows-x64
# 
# This file includes the relevant testing commands required for 
# testing this directory and lists subdirectories to be tested as well.
if(CTEST_CONFIGURATION_TYPE MATCHES "^([Dd][Ee][Bb][Uu][Gg])$")
  add_test([=[FishGfxIm3dNativeTests]=] "E:/Projects/FishGfx/FishGfx.Im3d.Native/out/build/windows-x64/Debug/FishGfxIm3dNativeTests.exe")
  set_tests_properties([=[FishGfxIm3dNativeTests]=] PROPERTIES  _BACKTRACE_TRIPLES "E:/Projects/FishGfx/FishGfx.Im3d.Native/CMakeLists.txt;30;add_test;E:/Projects/FishGfx/FishGfx.Im3d.Native/CMakeLists.txt;0;")
elseif(CTEST_CONFIGURATION_TYPE MATCHES "^([Rr][Ee][Ll][Ee][Aa][Ss][Ee])$")
  add_test([=[FishGfxIm3dNativeTests]=] "E:/Projects/FishGfx/FishGfx.Im3d.Native/out/build/windows-x64/Release/FishGfxIm3dNativeTests.exe")
  set_tests_properties([=[FishGfxIm3dNativeTests]=] PROPERTIES  _BACKTRACE_TRIPLES "E:/Projects/FishGfx/FishGfx.Im3d.Native/CMakeLists.txt;30;add_test;E:/Projects/FishGfx/FishGfx.Im3d.Native/CMakeLists.txt;0;")
elseif(CTEST_CONFIGURATION_TYPE MATCHES "^([Mm][Ii][Nn][Ss][Ii][Zz][Ee][Rr][Ee][Ll])$")
  add_test([=[FishGfxIm3dNativeTests]=] "E:/Projects/FishGfx/FishGfx.Im3d.Native/out/build/windows-x64/MinSizeRel/FishGfxIm3dNativeTests.exe")
  set_tests_properties([=[FishGfxIm3dNativeTests]=] PROPERTIES  _BACKTRACE_TRIPLES "E:/Projects/FishGfx/FishGfx.Im3d.Native/CMakeLists.txt;30;add_test;E:/Projects/FishGfx/FishGfx.Im3d.Native/CMakeLists.txt;0;")
elseif(CTEST_CONFIGURATION_TYPE MATCHES "^([Rr][Ee][Ll][Ww][Ii][Tt][Hh][Dd][Ee][Bb][Ii][Nn][Ff][Oo])$")
  add_test([=[FishGfxIm3dNativeTests]=] "E:/Projects/FishGfx/FishGfx.Im3d.Native/out/build/windows-x64/RelWithDebInfo/FishGfxIm3dNativeTests.exe")
  set_tests_properties([=[FishGfxIm3dNativeTests]=] PROPERTIES  _BACKTRACE_TRIPLES "E:/Projects/FishGfx/FishGfx.Im3d.Native/CMakeLists.txt;30;add_test;E:/Projects/FishGfx/FishGfx.Im3d.Native/CMakeLists.txt;0;")
else()
  add_test([=[FishGfxIm3dNativeTests]=] NOT_AVAILABLE)
endif()
