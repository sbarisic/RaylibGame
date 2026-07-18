#ifndef VOXEL_AUDIO_H
#define VOXEL_AUDIO_H

#include <stdint.h>

#if defined(_WIN32)
    #if defined(VA_BUILD_DLL)
        #define VA_API __declspec(dllexport)
    #else
        #define VA_API __declspec(dllimport)
    #endif
    #define VA_CALL __cdecl
#else
    #define VA_API __attribute__((visibility("default")))
    #define VA_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define VA_ABI_VERSION 1u
#define VA_MAX_BUSES 6u

typedef struct va_engine va_engine;
typedef struct va_clip va_clip;
typedef struct va_pcm_stream va_pcm_stream;
typedef uint64_t va_voice_id;

typedef enum va_result {
    VA_SUCCESS = 0,
    VA_ERROR_INVALID_ARGUMENT = -1,
    VA_ERROR_OUT_OF_MEMORY = -2,
    VA_ERROR_DEVICE = -3,
    VA_ERROR_FILE = -4,
    VA_ERROR_FORMAT = -5,
    VA_ERROR_CAPACITY = -6,
    VA_ERROR_STALE_HANDLE = -7,
    VA_ERROR_UNSUPPORTED = -8,
    VA_ERROR_INTERNAL = -9
} va_result;

typedef enum va_bus {
    VA_BUS_MASTER = 0,
    VA_BUS_SFX = 1,
    VA_BUS_AMBIENCE = 2,
    VA_BUS_MUSIC = 3,
    VA_BUS_VOICE = 4,
    VA_BUS_UI = 5
} va_bus;

typedef enum va_spatial_mode {
    VA_SPATIAL_2D = 0,
    VA_SPATIAL_3D = 1
} va_spatial_mode;

/*
 * This selector is the stable insertion seam for a future dry-mono source
 * processor. BASIC uses miniaudio's spatializer. STEAM_AUDIO is deliberately
 * reserved and returns VA_ERROR_UNSUPPORTED until that optional node exists.
 */
typedef enum va_source_processor {
    VA_SOURCE_PROCESSOR_BASIC = 0,
    VA_SOURCE_PROCESSOR_STEAM_AUDIO = 1
} va_source_processor;

typedef enum va_sample_format {
    VA_SAMPLE_F32 = 1,
    VA_SAMPLE_S16 = 2
} va_sample_format;

typedef enum va_event_type {
    VA_EVENT_NONE = 0,
    VA_EVENT_VOICE_FINISHED = 1,
    VA_EVENT_VOICE_STOPPED = 2,
    VA_EVENT_STREAM_UNDERRUN = 3,
    VA_EVENT_DIAGNOSTIC = 4,
    VA_EVENT_ERROR = 5
} va_event_type;

typedef struct va_engine_config {
    uint32_t struct_size;
    uint32_t max_voices;
    uint32_t sample_rate;
    uint32_t channels;
    uint32_t period_size_frames;
    uint32_t no_device;
} va_engine_config;

typedef struct va_clip_config {
    uint32_t struct_size;
    uint32_t streamed;
    va_spatial_mode spatial_mode;
} va_clip_config;

typedef struct va_play_params {
    uint32_t struct_size;
    va_bus bus;
    va_spatial_mode spatial_mode;
    va_source_processor source_processor;
    float gain;
    float pitch;
    float position[3];
    float velocity[3];
    float min_distance;
    float max_distance;
    float doppler_factor;
    uint32_t looping;
    uint32_t fade_in_milliseconds;
} va_play_params;

typedef struct va_listener {
    uint32_t struct_size;
    float position[3];
    float forward[3];
    float up[3];
    float velocity[3];
} va_listener;

typedef struct va_pcm_stream_config {
    uint32_t struct_size;
    va_sample_format format;
    uint32_t channels;
    uint32_t sample_rate;
    uint32_t capacity_frames;
    va_bus bus;
} va_pcm_stream_config;

typedef struct va_event {
    uint32_t struct_size;
    va_event_type type;
    va_result result;
    va_voice_id voice;
    uint32_t code;
} va_event;

typedef struct va_stats {
    uint32_t struct_size;
    uint32_t active_voices;
    uint32_t active_pcm_streams;
    uint64_t completed_voices;
    uint64_t stopped_voices;
    uint64_t stream_underruns;
    uint64_t dropped_events;
} va_stats;

VA_API uint32_t VA_CALL va_get_abi_version(void);

VA_API va_result VA_CALL va_engine_create(
    const va_engine_config* config,
    va_engine** engine_out);
VA_API void VA_CALL va_engine_destroy(va_engine* engine);
VA_API va_result VA_CALL va_engine_update(va_engine* engine, float delta_seconds);
VA_API va_result VA_CALL va_engine_set_listener(
    va_engine* engine,
    const va_listener* listener);
VA_API va_result VA_CALL va_engine_set_bus_gain(
    va_engine* engine,
    va_bus bus,
    float gain);
VA_API va_result VA_CALL va_engine_stop_all(
    va_engine* engine,
    int32_t bus_or_all,
    uint32_t fade_milliseconds);
VA_API int32_t VA_CALL va_engine_poll_event(va_engine* engine, va_event* event_out);
VA_API va_result VA_CALL va_engine_get_stats(va_engine* engine, va_stats* stats_out);

VA_API va_result VA_CALL va_clip_create(
    va_engine* engine,
    const char* path_utf8,
    const va_clip_config* config,
    va_clip** clip_out);
VA_API void VA_CALL va_clip_destroy(va_clip* clip);
VA_API uint32_t VA_CALL va_clip_source_channels(const va_clip* clip);
VA_API uint32_t VA_CALL va_clip_was_downmixed(const va_clip* clip);

VA_API va_result VA_CALL va_voice_play(
    va_engine* engine,
    va_clip* clip,
    const va_play_params* params,
    va_voice_id* voice_out);
VA_API va_result VA_CALL va_voice_stop(
    va_engine* engine,
    va_voice_id voice,
    uint32_t fade_milliseconds);
VA_API va_result VA_CALL va_voice_set_paused(
    va_engine* engine,
    va_voice_id voice,
    uint32_t paused);
VA_API va_result VA_CALL va_voice_set_gain(
    va_engine* engine,
    va_voice_id voice,
    float gain,
    uint32_t fade_milliseconds);
VA_API va_result VA_CALL va_voice_seek_seconds(
    va_engine* engine,
    va_voice_id voice,
    float seconds);
VA_API int32_t VA_CALL va_voice_is_active(va_engine* engine, va_voice_id voice);

VA_API va_result VA_CALL va_pcm_stream_create(
    va_engine* engine,
    const va_pcm_stream_config* config,
    va_pcm_stream** stream_out);
VA_API void VA_CALL va_pcm_stream_destroy(va_pcm_stream* stream);
VA_API va_result VA_CALL va_pcm_stream_write(
    va_pcm_stream* stream,
    const void* interleaved_frames,
    uint32_t frame_count,
    uint32_t* frames_written_out);
VA_API uint32_t VA_CALL va_pcm_stream_available_write(va_pcm_stream* stream);
VA_API va_result VA_CALL va_pcm_stream_set_paused(
    va_pcm_stream* stream,
    uint32_t paused);

#ifdef __cplusplus
}
#endif

#endif
