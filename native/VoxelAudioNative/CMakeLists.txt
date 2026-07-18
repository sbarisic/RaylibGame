cmake_minimum_required(VERSION 3.24)

project(VoxelAudioNative VERSION 1.0.0 LANGUAGES C)

if(NOT DEFINED MINIAUDIO_ROOT)
    get_filename_component(MINIAUDIO_ROOT
        "${CMAKE_CURRENT_SOURCE_DIR}/../../thirdparty/miniaudio"
        ABSOLUTE)
endif()

if(NOT EXISTS "${MINIAUDIO_ROOT}/miniaudio.c")
    message(FATAL_ERROR
        "miniaudio.c was not found at '${MINIAUDIO_ROOT}'. Initialize the miniaudio submodule first.")
endif()

add_library(VoxelAudioNative SHARED
    src/voxel_audio.c
    "${MINIAUDIO_ROOT}/miniaudio.c")

target_include_directories(VoxelAudioNative
    PUBLIC
        "${CMAKE_CURRENT_SOURCE_DIR}/include"
    PRIVATE
        "${MINIAUDIO_ROOT}")

target_compile_features(VoxelAudioNative PRIVATE c_std_11)
target_compile_definitions(VoxelAudioNative PRIVATE VA_BUILD_DLL)

if(MSVC)
    target_compile_options(VoxelAudioNative PRIVATE /W4 /wd4100 /wd4244 /wd4267)
    set_property(TARGET VoxelAudioNative PROPERTY
        MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>DLL")
else()
    target_compile_options(VoxelAudioNative PRIVATE -Wall -Wextra -Wpedantic)
endif()

set_target_properties(VoxelAudioNative PROPERTIES
    C_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN YES
    OUTPUT_NAME "VoxelAudioNative")
