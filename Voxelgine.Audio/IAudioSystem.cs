namespace Voxelgine.Audio;

public interface IAudioSystem : IDisposable
{
    bool IsAvailable { get; }

    void RegisterCue(AudioCueDefinition cue);

    void SetListener(in AudioListener listener);

    AudioVoiceHandle PlayCue(string cueId, in AudioEmitter emitter);

    AudioStreamHandle PlayStream(AudioStreamDefinition stream);

    IAudioPcmStream CreatePcmStream(
        AudioFormat format,
        AudioStreamOptions options);

    void SetBusGain(AudioBus bus, float gain);

    void Update(float deltaSeconds);

    void StopAll(AudioBus? bus = null, float fadeSeconds = 0);

    void Stop(AudioVoiceHandle voice, float fadeSeconds = 0);

    void Stop(AudioStreamHandle stream, float fadeSeconds = 0);

    void SetGain(AudioVoiceHandle voice, float gain, float fadeSeconds = 0);

    void SetGain(AudioStreamHandle stream, float gain, float fadeSeconds = 0);

    void SetPaused(AudioStreamHandle stream, bool paused);

    void Seek(AudioStreamHandle stream, float seconds);
}
public interface IAudioPcmStream : IDisposable
{
    AudioFormat Format { get; }

    int AvailableWriteFrames { get; }

    int Write(ReadOnlySpan<byte> interleavedFrames, int frameCount);

    void Start();

    void SetPaused(bool paused);
}
