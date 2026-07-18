#include "voxel_audio.h"

#include "miniaudio.h"

#include <math.h>
#include <stddef.h>
#include <stdlib.h>
#include <string.h>

#define VA_DEFAULT_MAX_VOICES 256u
#define VA_EVENT_CAPACITY 256u
#define VA_DEFAULT_SAMPLE_RATE 48000u
#define VA_DEFAULT_CHANNELS 2u
#define VA_DEFAULT_PERIOD_FRAMES 256u

typedef struct va_voice_slot {
    ma_sound sound;
    ma_audio_buffer_ref buffer_ref;
    ma_resource_manager_data_source stream_source;
    struct va_clip* clip;
    uint32_t generation;
    uint8_t active;
    uint8_t paused;
    uint8_t stopping;
    uint8_t sound_initialized;
    uint8_t buffer_ref_initialized;
    uint8_t stream_source_initialized;
    va_bus bus;
} va_voice_slot;

struct va_clip {
    va_engine* engine;
    char* path;
    void* decoded_frames;
    uint64_t frame_count;
    uint32_t decoded_channels;
    uint32_t sample_rate;
    uint32_t source_channels;
    uint8_t streamed;
    uint8_t downmixed;
    va_spatial_mode spatial_mode;
};

struct va_pcm_stream {
    va_engine* engine;
    struct va_pcm_stream* next;
    ma_pcm_rb ring_buffer;
    ma_sound sound;
    uint32_t bytes_per_frame;
    uint8_t ring_buffer_initialized;
    uint8_t sound_initialized;
    uint8_t started;
    uint8_t paused;
    uint8_t stopping;
    uint8_t underrun_reported;
    va_bus bus;
};

struct va_engine {
    ma_engine audio_engine;
    ma_resource_manager mono_stream_manager;
    ma_sound_group buses[VA_MAX_BUSES];
    uint8_t audio_engine_initialized;
    uint8_t mono_stream_manager_initialized;
    uint8_t bus_initialized[VA_MAX_BUSES];
    uint8_t no_device;
    uint32_t max_voices;
    uint32_t sample_rate;
    uint32_t channels;
    uint32_t period_size_frames;
    va_voice_slot* voices;
    va_pcm_stream* pcm_streams;
    float* offline_scratch;
    va_event events[VA_EVENT_CAPACITY];
    uint32_t event_read;
    uint32_t event_write;
    uint32_t event_count;
    va_stats stats;
};

static va_result va_result_from_ma(ma_result result)
{
    if (result == MA_SUCCESS) {
        return VA_SUCCESS;
    }

    switch (result) {
        case MA_INVALID_ARGS:
            return VA_ERROR_INVALID_ARGUMENT;
        case MA_OUT_OF_MEMORY:
            return VA_ERROR_OUT_OF_MEMORY;
        case MA_NO_DEVICE:
            return VA_ERROR_DEVICE;
        case MA_DOES_NOT_EXIST:
        case MA_INVALID_FILE:
            return VA_ERROR_FILE;
        case MA_FORMAT_NOT_SUPPORTED:
            return VA_ERROR_FORMAT;
        default:
            return VA_ERROR_INTERNAL;
    }
}

static int va_bus_is_valid(va_bus bus)
{
    return bus >= VA_BUS_MASTER && bus <= VA_BUS_UI;
}

static char* va_string_duplicate(const char* value)
{
    size_t length;
    char* copy;

    if (value == NULL) {
        return NULL;
    }

    length = strlen(value);
    copy = (char*)malloc(length + 1u);
    if (copy != NULL) {
        memcpy(copy, value, length + 1u);
    }

    return copy;
}

static void va_push_event(
    va_engine* engine,
    va_event_type type,
    va_result result,
    va_voice_id voice,
    uint32_t code)
{
    va_event* event;

    if (engine->event_count == VA_EVENT_CAPACITY) {
        engine->stats.dropped_events += 1u;
        return;
    }

    event = &engine->events[engine->event_write];
    memset(event, 0, sizeof(*event));
    event->struct_size = (uint32_t)sizeof(*event);
    event->type = type;
    event->result = result;
    event->voice = voice;
    event->code = code;

    engine->event_write = (engine->event_write + 1u) % VA_EVENT_CAPACITY;
    engine->event_count += 1u;
}

static va_voice_id va_make_voice_id(uint32_t index, uint32_t generation)
{
    return ((uint64_t)generation << 32u) | ((uint64_t)index + 1u);
}

static va_voice_slot* va_find_voice(
    va_engine* engine,
    va_voice_id voice,
    uint32_t* index_out)
{
    uint32_t encoded_index;
    uint32_t index;
    uint32_t generation;
    va_voice_slot* slot;

    if (engine == NULL || voice == 0u) {
        return NULL;
    }

    encoded_index = (uint32_t)(voice & 0xFFFFFFFFu);
    generation = (uint32_t)(voice >> 32u);
    if (encoded_index == 0u) {
        return NULL;
    }

    index = encoded_index - 1u;
    if (index >= engine->max_voices) {
        return NULL;
    }

    slot = &engine->voices[index];
    if (!slot->active || slot->generation != generation) {
        return NULL;
    }

    if (index_out != NULL) {
        *index_out = index;
    }

    return slot;
}

static void va_release_voice_resources(va_engine* engine, va_voice_slot* slot)
{
    uint8_t was_active = slot->active;

    if (!slot->sound_initialized &&
        !slot->buffer_ref_initialized &&
        !slot->stream_source_initialized) {
        return;
    }

    if (slot->sound_initialized) {
        ma_sound_uninit(&slot->sound);
    }

    if (slot->buffer_ref_initialized) {
        ma_audio_buffer_ref_uninit(&slot->buffer_ref);
    }

    if (slot->stream_source_initialized) {
        ma_resource_manager_data_source_uninit(&slot->stream_source);
    }

    slot->active = 0u;
    slot->paused = 0u;
    slot->stopping = 0u;
    slot->sound_initialized = 0u;
    slot->buffer_ref_initialized = 0u;
    slot->stream_source_initialized = 0u;
    slot->clip = NULL;
    if (was_active && engine->stats.active_voices > 0u) {
        engine->stats.active_voices -= 1u;
    }
}

static void va_finish_voice(
    va_engine* engine,
    uint32_t index,
    va_event_type event_type)
{
    va_voice_slot* slot = &engine->voices[index];
    va_voice_id voice = va_make_voice_id(index, slot->generation);

    va_release_voice_resources(engine, slot);
    if (event_type == VA_EVENT_VOICE_FINISHED) {
        engine->stats.completed_voices += 1u;
    } else {
        engine->stats.stopped_voices += 1u;
    }

    va_push_event(engine, event_type, VA_SUCCESS, voice, 0u);
}

static va_result va_initialize_buses(va_engine* engine)
{
    uint32_t index;
    ma_result result;

    for (index = 0u; index < VA_MAX_BUSES; index += 1u) {
        ma_sound_group* parent = index == VA_BUS_MASTER
            ? NULL
            : &engine->buses[VA_BUS_MASTER];

        result = ma_sound_group_init(
            &engine->audio_engine,
            0u,
            parent,
            &engine->buses[index]);
        if (result != MA_SUCCESS) {
            return va_result_from_ma(result);
        }

        engine->bus_initialized[index] = 1u;
    }

    return VA_SUCCESS;
}

static void va_uninitialize_buses(va_engine* engine)
{
    uint32_t index = VA_MAX_BUSES;

    while (index > 0u) {
        index -= 1u;
        if (engine->bus_initialized[index]) {
            ma_sound_group_uninit(&engine->buses[index]);
            engine->bus_initialized[index] = 0u;
        }
    }
}

static va_result va_probe_file(
    const char* path,
    uint32_t* channels_out,
    uint32_t* sample_rate_out)
{
    ma_decoder decoder;
    ma_result result;
    ma_uint32 channels = 0u;
    ma_uint32 sample_rate = 0u;

    result = ma_decoder_init_file(path, NULL, &decoder);
    if (result != MA_SUCCESS) {
        return va_result_from_ma(result);
    }

    result = ma_decoder_get_data_format(
        &decoder,
        NULL,
        &channels,
        &sample_rate,
        NULL,
        0u);
    ma_decoder_uninit(&decoder);
    if (result != MA_SUCCESS) {
        return va_result_from_ma(result);
    }

    *channels_out = channels;
    *sample_rate_out = sample_rate;
    return VA_SUCCESS;
}

uint32_t VA_CALL va_get_abi_version(void)
{
    return VA_ABI_VERSION;
}

va_result VA_CALL va_engine_create(
    const va_engine_config* config,
    va_engine** engine_out)
{
    va_engine* engine;
    ma_engine_config engine_config;
    ma_resource_manager_config resource_config;
    ma_result ma_result_code;
    va_result result;
    uint32_t max_voices = VA_DEFAULT_MAX_VOICES;
    uint32_t sample_rate = 0u;
    uint32_t channels = 0u;
    uint32_t period_size = VA_DEFAULT_PERIOD_FRAMES;
    uint32_t no_device = 0u;

    if (engine_out == NULL) {
        return VA_ERROR_INVALID_ARGUMENT;
    }

    *engine_out = NULL;
    if (config != NULL) {
        if (config->struct_size < sizeof(*config)) {
            return VA_ERROR_INVALID_ARGUMENT;
        }

        if (config->max_voices > 0u) {
            max_voices = config->max_voices;
        }
        sample_rate = config->sample_rate;
        channels = config->channels;
        if (config->period_size_frames > 0u) {
            period_size = config->period_size_frames;
        }
        no_device = config->no_device;
    }

    if (no_device) {
        if (sample_rate == 0u) {
            sample_rate = VA_DEFAULT_SAMPLE_RATE;
        }
        if (channels == 0u) {
            channels = VA_DEFAULT_CHANNELS;
        }
    }

    engine = (va_engine*)calloc(1u, sizeof(*engine));
    if (engine == NULL) {
        return VA_ERROR_OUT_OF_MEMORY;
    }

    engine->voices = (va_voice_slot*)calloc(max_voices, sizeof(*engine->voices));
    if (engine->voices == NULL) {
        free(engine);
        return VA_ERROR_OUT_OF_MEMORY;
    }

    engine->max_voices = max_voices;
    engine->period_size_frames = period_size;
    engine->no_device = no_device != 0u;
    engine->stats.struct_size = (uint32_t)sizeof(engine->stats);

    engine_config = ma_engine_config_init();
    engine_config.sampleRate = sample_rate;
    engine_config.channels = channels;
    engine_config.periodSizeInFrames = period_size;
    engine_config.noDevice = no_device ? MA_TRUE : MA_FALSE;
    engine_config.gainSmoothTimeInMilliseconds = 16u;

    ma_result_code = ma_engine_init(&engine_config, &engine->audio_engine);
    if (ma_result_code != MA_SUCCESS) {
        result = va_result_from_ma(ma_result_code);
        free(engine->voices);
        free(engine);
        return result;
    }
    engine->audio_engine_initialized = 1u;
    engine->sample_rate = ma_engine_get_sample_rate(&engine->audio_engine);
    engine->channels = ma_engine_get_channels(&engine->audio_engine);

    resource_config = ma_resource_manager_config_init();
    resource_config.decodedFormat = ma_format_f32;
    resource_config.decodedChannels = 1u;
    resource_config.decodedSampleRate = engine->sample_rate;
    resource_config.jobThreadCount = 1u;
    ma_result_code = ma_resource_manager_init(
        &resource_config,
        &engine->mono_stream_manager);
    if (ma_result_code != MA_SUCCESS) {
        result = va_result_from_ma(ma_result_code);
        va_engine_destroy(engine);
        return result;
    }
    engine->mono_stream_manager_initialized = 1u;

    result = va_initialize_buses(engine);
    if (result != VA_SUCCESS) {
        va_engine_destroy(engine);
        return result;
    }

    if (engine->no_device) {
        size_t scratch_samples =
            (size_t)engine->period_size_frames * (size_t)engine->channels;
        engine->offline_scratch = (float*)calloc(scratch_samples, sizeof(float));
        if (engine->offline_scratch == NULL) {
            va_engine_destroy(engine);
            return VA_ERROR_OUT_OF_MEMORY;
        }
    }

    *engine_out = engine;
    return VA_SUCCESS;
}

void VA_CALL va_engine_destroy(va_engine* engine)
{
    uint32_t index;
    va_pcm_stream* stream;

    if (engine == NULL) {
        return;
    }

    while (engine->pcm_streams != NULL) {
        stream = engine->pcm_streams;
        va_pcm_stream_destroy(stream);
    }

    if (engine->voices != NULL) {
        for (index = 0u; index < engine->max_voices; index += 1u) {
            va_release_voice_resources(engine, &engine->voices[index]);
        }
    }

    va_uninitialize_buses(engine);
    if (engine->mono_stream_manager_initialized) {
        ma_resource_manager_uninit(&engine->mono_stream_manager);
    }
    if (engine->audio_engine_initialized) {
        ma_engine_uninit(&engine->audio_engine);
    }

    free(engine->offline_scratch);
    free(engine->voices);
    free(engine);
}

va_result VA_CALL va_engine_update(va_engine* engine, float delta_seconds)
{
    uint32_t index;
    va_pcm_stream* stream;

    if (engine == NULL || !isfinite(delta_seconds) || delta_seconds < 0.0f) {
        return VA_ERROR_INVALID_ARGUMENT;
    }

    if (engine->no_device && delta_seconds > 0.0f) {
        uint64_t frames_remaining = (uint64_t)(
            delta_seconds * (float)engine->sample_rate + 0.5f);

        while (frames_remaining > 0u) {
            ma_uint64 frames_read = 0u;
            ma_uint64 frames_to_read = frames_remaining;
            ma_result result;

            if (frames_to_read > engine->period_size_frames) {
                frames_to_read = engine->period_size_frames;
            }

            result = ma_engine_read_pcm_frames(
                &engine->audio_engine,
                engine->offline_scratch,
                frames_to_read,
                &frames_read);
            if (result != MA_SUCCESS && result != MA_AT_END) {
                va_result mapped_result = va_result_from_ma(result);
                va_push_event(
                    engine,
                    VA_EVENT_ERROR,
                    mapped_result,
                    0u,
                    0u);
                return mapped_result;
            }
            if (frames_read == 0u) {
                break;
            }
            frames_remaining -= frames_read;
        }
    }

    for (index = 0u; index < engine->max_voices; index += 1u) {
        va_voice_slot* slot = &engine->voices[index];

        if (!slot->active || slot->paused) {
            continue;
        }

        if (slot->stream_source_initialized) {
            ma_result stream_result = ma_resource_manager_data_source_result(
                &slot->stream_source);
            if (stream_result != MA_SUCCESS && stream_result != MA_BUSY) {
                va_voice_id voice = va_make_voice_id(index, slot->generation);
                va_result mapped_result = va_result_from_ma(stream_result);
                va_release_voice_resources(engine, slot);
                va_push_event(
                    engine,
                    VA_EVENT_ERROR,
                    mapped_result,
                    voice,
                    0u);
                continue;
            }
        }

        if (slot->stopping && !ma_sound_is_playing(&slot->sound)) {
            va_finish_voice(engine, index, VA_EVENT_VOICE_STOPPED);
        } else if (!slot->stopping && ma_sound_at_end(&slot->sound)) {
            va_finish_voice(engine, index, VA_EVENT_VOICE_FINISHED);
        }
    }

    stream = engine->pcm_streams;
    while (stream != NULL) {
        if (stream->stopping && !ma_sound_is_playing(&stream->sound)) {
            stream->stopping = 0u;
            stream->paused = 1u;
        }

        if (stream->started && !stream->paused) {
            uint32_t available = ma_pcm_rb_available_read(&stream->ring_buffer);
            if (available == 0u && !stream->underrun_reported) {
                stream->underrun_reported = 1u;
                engine->stats.stream_underruns += 1u;
                va_push_event(
                    engine,
                    VA_EVENT_STREAM_UNDERRUN,
                    VA_SUCCESS,
                    0u,
                    0u);
            } else if (available > 0u) {
                stream->underrun_reported = 0u;
            }
        }
        stream = stream->next;
    }

    return VA_SUCCESS;
}

va_result VA_CALL va_engine_set_listener(
    va_engine* engine,
    const va_listener* listener)
{
    if (engine == NULL || listener == NULL ||
        listener->struct_size < sizeof(*listener)) {
        return VA_ERROR_INVALID_ARGUMENT;
    }

    ma_engine_listener_set_position(
        &engine->audio_engine,
        0u,
        listener->position[0],
        listener->position[1],
        listener->position[2]);
    ma_engine_listener_set_direction(
        &engine->audio_engine,
        0u,
        listener->forward[0],
        listener->forward[1],
        listener->forward[2]);
    ma_engine_listener_set_world_up(
        &engine->audio_engine,
        0u,
        listener->up[0],
        listener->up[1],
        listener->up[2]);
    ma_engine_listener_set_velocity(
        &engine->audio_engine,
        0u,
        listener->velocity[0],
        listener->velocity[1],
        listener->velocity[2]);

    return VA_SUCCESS;
}

va_result VA_CALL va_engine_set_bus_gain(
    va_engine* engine,
    va_bus bus,
    float gain)
{
    if (engine == NULL || !va_bus_is_valid(bus) ||
        !isfinite(gain) || gain < 0.0f) {
        return VA_ERROR_INVALID_ARGUMENT;
    }

    ma_sound_group_set_volume(&engine->buses[bus], gain);
    return VA_SUCCESS;
}

va_result VA_CALL va_engine_stop_all(
    va_engine* engine,
    int32_t bus_or_all,
    uint32_t fade_milliseconds)
{
    uint32_t index;
    va_pcm_stream* stream;

    if (engine == NULL ||
        (bus_or_all >= 0 && !va_bus_is_valid((va_bus)bus_or_all))) {
        return VA_ERROR_INVALID_ARGUMENT;
    }

    for (index = 0u; index < engine->max_voices; index += 1u) {
        va_voice_slot* slot = &engine->voices[index];

        if (!slot->active ||
            (bus_or_all >= 0 && slot->bus != (va_bus)bus_or_all)) {
            continue;
        }

        if (fade_milliseconds == 0u) {
            ma_sound_stop(&slot->sound);
            va_finish_voice(engine, index, VA_EVENT_VOICE_STOPPED);
        } else {
            ma_sound_stop_with_fade_in_milliseconds(
                &slot->sound,
                fade_milliseconds);
            slot->stopping = 1u;
            slot->paused = 0u;
        }
    }

    stream = engine->pcm_streams;
    while (stream != NULL) {
        if (bus_or_all < 0 || stream->bus == (va_bus)bus_or_all) {
            if (fade_milliseconds == 0u) {
                ma_sound_stop(&stream->sound);
                stream->paused = 1u;
                stream->stopping = 0u;
            } else {
                ma_sound_stop_with_fade_in_milliseconds(
                    &stream->sound,
                    fade_milliseconds);
                stream->paused = 0u;
                stream->stopping = 1u;
            }
        }
        stream = stream->next;
    }

    return VA_SUCCESS;
}

int32_t VA_CALL va_engine_poll_event(va_engine* engine, va_event* event_out)
{
    if (engine == NULL || event_out == NULL || engine->event_count == 0u) {
        return 0;
    }

    *event_out = engine->events[engine->event_read];
    engine->event_read = (engine->event_read + 1u) % VA_EVENT_CAPACITY;
    engine->event_count -= 1u;
    return 1;
}

va_result VA_CALL va_engine_get_stats(va_engine* engine, va_stats* stats_out)
{
    if (engine == NULL || stats_out == NULL ||
        stats_out->struct_size < sizeof(*stats_out)) {
        return VA_ERROR_INVALID_ARGUMENT;
    }

    *stats_out = engine->stats;
    return VA_SUCCESS;
}

va_result VA_CALL va_clip_create(
    va_engine* engine,
    const char* path_utf8,
    const va_clip_config* config,
    va_clip** clip_out)
{
    va_clip* clip;
    va_result result;
    uint32_t source_channels;
    uint32_t source_sample_rate;

    if (engine == NULL || path_utf8 == NULL || path_utf8[0] == '\0' ||
        config == NULL || config->struct_size < sizeof(*config) ||
        clip_out == NULL) {
        return VA_ERROR_INVALID_ARGUMENT;
    }

    *clip_out = NULL;
    if (config->spatial_mode != VA_SPATIAL_2D &&
        config->spatial_mode != VA_SPATIAL_3D) {
        return VA_ERROR_INVALID_ARGUMENT;
    }

    result = va_probe_file(path_utf8, &source_channels, &source_sample_rate);
    if (result != VA_SUCCESS) {
        return result;
    }

    clip = (va_clip*)calloc(1u, sizeof(*clip));
    if (clip == NULL) {
        return VA_ERROR_OUT_OF_MEMORY;
    }

    clip->path = va_string_duplicate(path_utf8);
    if (clip->path == NULL) {
        free(clip);
        return VA_ERROR_OUT_OF_MEMORY;
    }

    clip->engine = engine;
    clip->streamed = config->streamed != 0u;
    clip->spatial_mode = config->spatial_mode;
    clip->source_channels = source_channels;
    clip->sample_rate = source_sample_rate;
    clip->downmixed =
        config->spatial_mode == VA_SPATIAL_3D && source_channels > 1u;

    if (!clip->streamed) {
        ma_decoder_config decoder_config;
        ma_result ma_result_code;
        ma_uint32 decoded_channels = clip->downmixed ? 1u : source_channels;

        decoder_config = ma_decoder_config_init(
            ma_format_f32,
            decoded_channels,
            source_sample_rate);
        ma_result_code = ma_decode_file(
            path_utf8,
            &decoder_config,
            &clip->frame_count,
            &clip->decoded_frames);
        if (ma_result_code != MA_SUCCESS) {
            result = va_result_from_ma(ma_result_code);
            free(clip->path);
            free(clip);
            return result;
        }
        clip->decoded_channels = decoded_channels;
    } else {
        clip->decoded_channels = clip->downmixed ? 1u : source_channels;
    }

    *clip_out = clip;
    return VA_SUCCESS;
}

void VA_CALL va_clip_destroy(va_clip* clip)
{
    uint32_t index;

    if (clip == NULL) {
        return;
    }

    if (clip->engine != NULL && clip->engine->voices != NULL) {
        for (index = 0u; index < clip->engine->max_voices; index += 1u) {
            va_voice_slot* slot = &clip->engine->voices[index];
            if (slot->active && slot->clip == clip) {
                ma_sound_stop(&slot->sound);
                va_finish_voice(
                    clip->engine,
                    index,
                    VA_EVENT_VOICE_STOPPED);
            }
        }
    }

    if (clip->decoded_frames != NULL) {
        ma_free(clip->decoded_frames, NULL);
    }
    free(clip->path);
    free(clip);
}

uint32_t VA_CALL va_clip_source_channels(const va_clip* clip)
{
    return clip == NULL ? 0u : clip->source_channels;
}

uint32_t VA_CALL va_clip_was_downmixed(const va_clip* clip)
{
    return clip == NULL ? 0u : clip->downmixed;
}

va_result VA_CALL va_voice_play(
    va_engine* engine,
    va_clip* clip,
    const va_play_params* params,
    va_voice_id* voice_out)
{
    uint32_t index;
    va_voice_slot* slot = NULL;
    ma_result ma_result_code;
    ma_uint32 sound_flags = 0u;

    if (engine == NULL || clip == NULL || clip->engine != engine ||
        params == NULL || params->struct_size < sizeof(*params) ||
        voice_out == NULL || !va_bus_is_valid(params->bus) ||
        !isfinite(params->gain) || params->gain < 0.0f ||
        !isfinite(params->pitch) || params->pitch <= 0.0f) {
        return VA_ERROR_INVALID_ARGUMENT;
    }

    *voice_out = 0u;
    if (params->source_processor != VA_SOURCE_PROCESSOR_BASIC) {
        return VA_ERROR_UNSUPPORTED;
    }
    if (params->spatial_mode != clip->spatial_mode) {
        return VA_ERROR_INVALID_ARGUMENT;
    }

    for (index = 0u; index < engine->max_voices; index += 1u) {
        if (!engine->voices[index].active) {
            slot = &engine->voices[index];
            break;
        }
    }
    if (slot == NULL) {
        return VA_ERROR_CAPACITY;
    }

    memset(&slot->sound, 0, sizeof(slot->sound));
    memset(&slot->buffer_ref, 0, sizeof(slot->buffer_ref));
    memset(&slot->stream_source, 0, sizeof(slot->stream_source));
    slot->generation += 1u;
    if (slot->generation == 0u) {
        slot->generation = 1u;
    }

    if (params->spatial_mode == VA_SPATIAL_2D) {
        sound_flags |= MA_SOUND_FLAG_NO_SPATIALIZATION;
    }

    if (clip->streamed) {
        ma_resource_manager* resource_manager = clip->downmixed
            ? &engine->mono_stream_manager
            : ma_engine_get_resource_manager(&engine->audio_engine);
        ma_uint32 stream_flags =
            MA_RESOURCE_MANAGER_DATA_SOURCE_FLAG_STREAM |
            MA_RESOURCE_MANAGER_DATA_SOURCE_FLAG_WAIT_INIT;

        ma_result_code = ma_resource_manager_data_source_init(
            resource_manager,
            clip->path,
            stream_flags,
            NULL,
            &slot->stream_source);
        if (ma_result_code != MA_SUCCESS) {
            return va_result_from_ma(ma_result_code);
        }
        slot->stream_source_initialized = 1u;

        ma_result_code = ma_sound_init_from_data_source(
            &engine->audio_engine,
            &slot->stream_source,
            sound_flags,
            &engine->buses[params->bus],
            &slot->sound);
    } else {
        ma_result_code = ma_audio_buffer_ref_init(
            ma_format_f32,
            clip->decoded_channels,
            clip->decoded_frames,
            clip->frame_count,
            &slot->buffer_ref);
        if (ma_result_code != MA_SUCCESS) {
            return va_result_from_ma(ma_result_code);
        }
        slot->buffer_ref.sampleRate = clip->sample_rate;
        slot->buffer_ref_initialized = 1u;

        ma_result_code = ma_sound_init_from_data_source(
            &engine->audio_engine,
            &slot->buffer_ref,
            sound_flags,
            &engine->buses[params->bus],
            &slot->sound);
    }

    if (ma_result_code != MA_SUCCESS) {
        if (slot->buffer_ref_initialized) {
            ma_audio_buffer_ref_uninit(&slot->buffer_ref);
            slot->buffer_ref_initialized = 0u;
        }
        if (slot->stream_source_initialized) {
            ma_resource_manager_data_source_uninit(&slot->stream_source);
            slot->stream_source_initialized = 0u;
        }
        return va_result_from_ma(ma_result_code);
    }

    slot->sound_initialized = 1u;

    slot->clip = clip;
    slot->bus = params->bus;
    ma_sound_set_pitch(&slot->sound, params->pitch);
    ma_sound_set_looping(&slot->sound, params->looping ? MA_TRUE : MA_FALSE);

    if (params->spatial_mode == VA_SPATIAL_3D) {
        float min_distance = params->min_distance > 0.0f
            ? params->min_distance
            : 1.0f;
        float max_distance = params->max_distance >= min_distance
            ? params->max_distance
            : min_distance;

        ma_sound_set_position(
            &slot->sound,
            params->position[0],
            params->position[1],
            params->position[2]);
        ma_sound_set_velocity(
            &slot->sound,
            params->velocity[0],
            params->velocity[1],
            params->velocity[2]);
        ma_sound_set_attenuation_model(
            &slot->sound,
            ma_attenuation_model_inverse);
        ma_sound_set_min_distance(&slot->sound, min_distance);
        ma_sound_set_max_distance(&slot->sound, max_distance);
        ma_sound_set_doppler_factor(
            &slot->sound,
            params->doppler_factor >= 0.0f
                ? params->doppler_factor
                : 1.0f);
    }

    if (params->fade_in_milliseconds > 0u) {
        ma_sound_set_fade_in_milliseconds(
            &slot->sound,
            0.0f,
            params->gain,
            params->fade_in_milliseconds);
    } else {
        ma_sound_set_fade_in_pcm_frames(
            &slot->sound,
            params->gain,
            params->gain,
            0u);
    }

    ma_result_code = ma_sound_start(&slot->sound);
    if (ma_result_code != MA_SUCCESS) {
        va_release_voice_resources(engine, slot);
        return va_result_from_ma(ma_result_code);
    }

    slot->active = 1u;
    engine->stats.active_voices += 1u;
    *voice_out = va_make_voice_id(index, slot->generation);
    return VA_SUCCESS;
}

va_result VA_CALL va_voice_stop(
    va_engine* engine,
    va_voice_id voice,
    uint32_t fade_milliseconds)
{
    uint32_t index;
    va_voice_slot* slot = va_find_voice(engine, voice, &index);

    if (slot == NULL) {
        return VA_ERROR_STALE_HANDLE;
    }

    if (fade_milliseconds == 0u) {
        ma_sound_stop(&slot->sound);
        va_finish_voice(engine, index, VA_EVENT_VOICE_STOPPED);
    } else {
        ma_sound_stop_with_fade_in_milliseconds(
            &slot->sound,
            fade_milliseconds);
        slot->stopping = 1u;
        slot->paused = 0u;
    }

    return VA_SUCCESS;
}

va_result VA_CALL va_voice_set_paused(
    va_engine* engine,
    va_voice_id voice,
    uint32_t paused)
{
    va_voice_slot* slot = va_find_voice(engine, voice, NULL);
    ma_result result;

    if (slot == NULL) {
        return VA_ERROR_STALE_HANDLE;
    }

    result = paused
        ? ma_sound_stop(&slot->sound)
        : ma_sound_start(&slot->sound);
    if (result != MA_SUCCESS) {
        return va_result_from_ma(result);
    }

    slot->paused = paused != 0u;
    return VA_SUCCESS;
}

va_result VA_CALL va_voice_set_gain(
    va_engine* engine,
    va_voice_id voice,
    float gain,
    uint32_t fade_milliseconds)
{
    va_voice_slot* slot = va_find_voice(engine, voice, NULL);

    if (slot == NULL) {
        return VA_ERROR_STALE_HANDLE;
    }
    if (!isfinite(gain) || gain < 0.0f) {
        return VA_ERROR_INVALID_ARGUMENT;
    }

    if (fade_milliseconds == 0u) {
        ma_sound_set_fade_in_pcm_frames(&slot->sound, gain, gain, 0u);
    } else {
        float current_gain = ma_sound_get_current_fade_volume(&slot->sound);
        ma_sound_set_fade_in_milliseconds(
            &slot->sound,
            current_gain,
            gain,
            fade_milliseconds);
    }

    return VA_SUCCESS;
}

va_result VA_CALL va_voice_seek_seconds(
    va_engine* engine,
    va_voice_id voice,
    float seconds)
{
    va_voice_slot* slot = va_find_voice(engine, voice, NULL);

    if (slot == NULL) {
        return VA_ERROR_STALE_HANDLE;
    }
    if (!isfinite(seconds) || seconds < 0.0f) {
        return VA_ERROR_INVALID_ARGUMENT;
    }

    return va_result_from_ma(ma_sound_seek_to_second(&slot->sound, seconds));
}

int32_t VA_CALL va_voice_is_active(va_engine* engine, va_voice_id voice)
{
    return va_find_voice(engine, voice, NULL) != NULL;
}

va_result VA_CALL va_pcm_stream_create(
    va_engine* engine,
    const va_pcm_stream_config* config,
    va_pcm_stream** stream_out)
{
    va_pcm_stream* stream;
    ma_format format;
    ma_result result;

    if (engine == NULL || config == NULL ||
        config->struct_size < sizeof(*config) || stream_out == NULL ||
        config->channels == 0u || config->sample_rate == 0u ||
        config->capacity_frames == 0u || !va_bus_is_valid(config->bus)) {
        return VA_ERROR_INVALID_ARGUMENT;
    }

    *stream_out = NULL;
    if (config->format == VA_SAMPLE_F32) {
        format = ma_format_f32;
    } else if (config->format == VA_SAMPLE_S16) {
        format = ma_format_s16;
    } else {
        return VA_ERROR_FORMAT;
    }

    stream = (va_pcm_stream*)calloc(1u, sizeof(*stream));
    if (stream == NULL) {
        return VA_ERROR_OUT_OF_MEMORY;
    }

    result = ma_pcm_rb_init(
        format,
        config->channels,
        config->capacity_frames,
        NULL,
        NULL,
        &stream->ring_buffer);
    if (result != MA_SUCCESS) {
        free(stream);
        return va_result_from_ma(result);
    }
    stream->ring_buffer_initialized = 1u;
    ma_pcm_rb_set_sample_rate(&stream->ring_buffer, config->sample_rate);

    result = ma_sound_init_from_data_source(
        &engine->audio_engine,
        &stream->ring_buffer,
        MA_SOUND_FLAG_NO_SPATIALIZATION,
        &engine->buses[config->bus],
        &stream->sound);
    if (result != MA_SUCCESS) {
        ma_pcm_rb_uninit(&stream->ring_buffer);
        free(stream);
        return va_result_from_ma(result);
    }

    stream->sound_initialized = 1u;
    stream->engine = engine;
    stream->paused = 1u;
    stream->bus = config->bus;
    stream->bytes_per_frame = ma_get_bytes_per_frame(
        format,
        config->channels);
    stream->next = engine->pcm_streams;
    engine->pcm_streams = stream;
    engine->stats.active_pcm_streams += 1u;

    *stream_out = stream;
    return VA_SUCCESS;
}

void VA_CALL va_pcm_stream_destroy(va_pcm_stream* stream)
{
    va_pcm_stream** cursor;

    if (stream == NULL) {
        return;
    }

    if (stream->engine != NULL) {
        cursor = &stream->engine->pcm_streams;
        while (*cursor != NULL) {
            if (*cursor == stream) {
                *cursor = stream->next;
                break;
            }
            cursor = &(*cursor)->next;
        }
        if (stream->engine->stats.active_pcm_streams > 0u) {
            stream->engine->stats.active_pcm_streams -= 1u;
        }
    }

    if (stream->sound_initialized) {
        ma_sound_uninit(&stream->sound);
    }
    if (stream->ring_buffer_initialized) {
        ma_pcm_rb_uninit(&stream->ring_buffer);
    }
    free(stream);
}

va_result VA_CALL va_pcm_stream_write(
    va_pcm_stream* stream,
    const void* interleaved_frames,
    uint32_t frame_count,
    uint32_t* frames_written_out)
{
    const uint8_t* source = (const uint8_t*)interleaved_frames;
    uint32_t total_written = 0u;

    if (stream == NULL || interleaved_frames == NULL ||
        frames_written_out == NULL) {
        return VA_ERROR_INVALID_ARGUMENT;
    }

    while (total_written < frame_count) {
        uint32_t acquired = frame_count - total_written;
        void* destination = NULL;
        ma_result result = ma_pcm_rb_acquire_write(
            &stream->ring_buffer,
            &acquired,
            &destination);

        if (result != MA_SUCCESS || acquired == 0u) {
            break;
        }

        memcpy(
            destination,
            source + ((size_t)total_written * stream->bytes_per_frame),
            (size_t)acquired * stream->bytes_per_frame);
        result = ma_pcm_rb_commit_write(&stream->ring_buffer, acquired);
        if (result != MA_SUCCESS) {
            *frames_written_out = total_written;
            return va_result_from_ma(result);
        }
        total_written += acquired;
    }

    *frames_written_out = total_written;
    if (total_written > 0u) {
        stream->underrun_reported = 0u;
    }
    return VA_SUCCESS;
}

uint32_t VA_CALL va_pcm_stream_available_write(va_pcm_stream* stream)
{
    if (stream == NULL) {
        return 0u;
    }
    return ma_pcm_rb_available_write(&stream->ring_buffer);
}

va_result VA_CALL va_pcm_stream_set_paused(
    va_pcm_stream* stream,
    uint32_t paused)
{
    ma_result result;

    if (stream == NULL) {
        return VA_ERROR_INVALID_ARGUMENT;
    }

    result = paused
        ? ma_sound_stop(&stream->sound)
        : ma_sound_start(&stream->sound);
    if (result != MA_SUCCESS) {
        return va_result_from_ma(result);
    }

    stream->paused = paused != 0u;
    stream->stopping = 0u;
    if (!paused) {
        stream->started = 1u;
    }
    return VA_SUCCESS;
}
