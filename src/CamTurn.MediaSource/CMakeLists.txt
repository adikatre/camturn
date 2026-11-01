cmake_minimum_required(VERSION 3.20)
project(CamTurnMediaSource LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Portable code: frame protocol, placeholder frame, pixel conversions. Builds and is tested on any OS.
add_library(camturn_common STATIC src/common/frames.cpp)
target_include_directories(camturn_common PUBLIC include PRIVATE src/common)

add_executable(camturn_portable_tests tests/portable_tests.cpp)
target_link_libraries(camturn_portable_tests PRIVATE camturn_common)

enable_testing()
add_test(NAME portable_tests
         COMMAND camturn_portable_tests ${CMAKE_CURRENT_SOURCE_DIR}/../../tests/fixtures/frame-header-v1.bin)

if(MSVC)
  target_compile_options(camturn_common PRIVATE /W4 /WX)
  target_compile_options(camturn_portable_tests PRIVATE /W4 /WX)
else()
  target_compile_options(camturn_common PRIVATE -Wall -Wextra -Werror)
  target_compile_options(camturn_portable_tests PRIVATE -Wall -Wextra -Werror)
endif()

if(WIN32)
  # Static CRT so Frame Server can load the DLL without a VC++ redistributable.
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
  set_property(TARGET camturn_common camturn_portable_tests PROPERTY
               MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

  set(MF_LIBS mfplat mf mfuuid mfsensorgroup propsys ole32 strmiids runtimeobject)

  # The custom media source loaded by Windows Frame Server.
  add_library(camturn_mediasource SHARED
              src/mediasource/dllmain.cpp
              src/mediasource/Activator.cpp
              src/mediasource/MediaSource.cpp
              src/mediasource/MediaStream.cpp
              src/mediasource/PipeClient.cpp
              src/mediasource/camturn_mediasource.def)
  target_link_libraries(camturn_mediasource PRIVATE camturn_common ${MF_LIBS})
  target_compile_definitions(camturn_mediasource PRIVATE UNICODE _UNICODE)
  target_compile_options(camturn_mediasource PRIVATE /W4 /WX /permissive-)
  set_property(TARGET camturn_mediasource PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

  # C ABI over MFCreateVirtualCamera for the app and installer.
  add_library(camturn_native SHARED src/native/native.cpp src/native/camturn_native.def)
  target_link_libraries(camturn_native PRIVATE ${MF_LIBS})
  target_compile_definitions(camturn_native PRIVATE UNICODE _UNICODE)
  target_compile_options(camturn_native PRIVATE /W4 /WX /permissive-)
  set_property(TARGET camturn_native PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()
