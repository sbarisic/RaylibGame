using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Voxelgine.Audio.Tests;

public sealed class NativeAudioTests
{
    [Fact]
    public void OfflineEngine_PlaysDecodedClipAndCompletesVoice()
    {
        string path = Path.Combine(Path.GetTempPath(), $"voxel-audio-{Guid.NewGuid():N}.wav");
        WriteTestWave(path, channels: 1, frameCount: 480);
        try
        {
            List<string> diagnostics = [];
            using AudioSystem system = new(new AudioSystemOptions
            {
                NoDevice = true,
                SampleRate = 48_000,
                Channels = 2,
                Log = diagnostics.Add,
                ThrowOnInitializationFailure = true
            });
            system.RegisterCue(new AudioCueDefinition
            {
                CueId = "test",
                Variants = [new AudioCueVariant { Path = path }],
                SpatialMode = AudioSpatialMode.ThreeDimensional
            });

            AudioVoiceHandle voice = system.PlayCue(
                "test",
                new AudioEmitter(Vector3.UnitX, Vector3.Zero));
            system.Update(0.1f);

            Assert.True(voice.IsValid);
            Assert.Equal(0u, system.Stats.ActiveVoices);
            Assert.Equal(1ul, system.Stats.CompletedVoices);
            Assert.Empty(diagnostics);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SpatialStereoClip_IsDownmixedAndReported()
    {
        string path = Path.Combine(Path.GetTempPath(), $"voxel-audio-{Guid.NewGuid():N}.wav");
        WriteTestWave(path, channels: 2, frameCount: 480);
        try
        {
            List<string> diagnostics = [];
            using AudioSystem system = new(new AudioSystemOptions
            {
                NoDevice = true,
                SampleRate = 48_000,
                Channels = 2,
                Log = diagnostics.Add,
                ThrowOnInitializationFailure = true
            });
            system.RegisterCue(new AudioCueDefinition
            {
                CueId = "stereo",
                Variants = [new AudioCueVariant { Path = path }],
                SpatialMode = AudioSpatialMode.ThreeDimensional
            });

            Assert.Contains(
                diagnostics,
                message => message.Contains("downmixed to mono", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PcmStream_AcceptsInterleavedFrames()
    {
        using AudioSystem system = new(new AudioSystemOptions
        {
            NoDevice = true,
            SampleRate = 48_000,
            Channels = 2,
            ThrowOnInitializationFailure = true
        });
        AudioFormat format = new(AudioPcmSampleFormat.Float32, 1, 48_000);
        using IAudioPcmStream stream = system.CreatePcmStream(
            format,
            new AudioStreamOptions { CapacityFrames = 1_024 });
        float[] samples = new float[256];
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(samples.AsSpan());

        int written = stream.Write(bytes, samples.Length);
        stream.Start();
        system.Update(0.002f);

        Assert.Equal(samples.Length, written);
        Assert.Equal(1u, system.Stats.ActivePcmStreams);
    }

    [Fact]
    public void ChildResources_RetainEngineDuringHostileDisposalOrder()
    {
        string path = Path.Combine(Path.GetTempPath(), $"voxel-audio-{Guid.NewGuid():N}.wav");
        WriteTestWave(path, channels: 1, frameCount: 480);
        NativeAudioBackend? backend = null;
        BackendClip? clip = null;
        BackendPcmStream? stream = null;
        try
        {
            backend = new NativeAudioBackend(
                CreateOfflineOptions(),
                _ => { });
            clip = backend.LoadClip(
                path,
                streamed: false,
                AudioSpatialMode.ThreeDimensional);
            stream = backend.CreatePcmStream(
                new AudioFormat(AudioPcmSampleFormat.Float32, 1, 48_000),
                new AudioStreamOptions { CapacityFrames = 256 });

            backend.Dispose();
            clip!.Dispose();
            stream.Dispose();
        }
        finally
        {
            stream?.Dispose();
            clip?.Dispose();
            backend?.Dispose();
            File.Delete(path);
        }
    }

    [Fact]
    public void StopAll_ForBusStopsMatchingPcmStreamOnly()
    {
        using AudioSystem system = CreateOfflineSystem();
        AudioFormat format = new(AudioPcmSampleFormat.Float32, 1, 48_000);
        using IAudioPcmStream sfx = system.CreatePcmStream(
            format,
            new AudioStreamOptions
            {
                Bus = AudioBus.Sfx,
                CapacityFrames = 2_048
            });
        using IAudioPcmStream music = system.CreatePcmStream(
            format,
            new AudioStreamOptions
            {
                Bus = AudioBus.Music,
                CapacityFrames = 2_048
            });
        float[] samples = new float[1_024];
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(samples.AsSpan());
        Assert.Equal(samples.Length, sfx.Write(bytes, samples.Length));
        Assert.Equal(samples.Length, music.Write(bytes, samples.Length));
        sfx.Start();
        music.Start();
        int sfxAvailableBefore = sfx.AvailableWriteFrames;
        int musicAvailableBefore = music.AvailableWriteFrames;

        system.StopAll(AudioBus.Sfx);
        system.Update(0.01f);

        Assert.Equal(sfxAvailableBefore, sfx.AvailableWriteFrames);
        Assert.True(music.AvailableWriteFrames > musicAvailableBefore);
    }

    [Fact]
    public void ReusedVoiceSlot_IncrementsGenerationAndIgnoresOldCompletionEvent()
    {
        string path = Path.Combine(Path.GetTempPath(), $"voxel-audio-{Guid.NewGuid():N}.wav");
        WriteTestWave(path, channels: 1, frameCount: 4_800);
        try
        {
            using AudioSystem system = CreateOfflineSystem();
            system.RegisterCue(new AudioCueDefinition
            {
                CueId = "generation",
                Variants = [new AudioCueVariant { Path = path }]
            });

            AudioVoiceHandle first = system.PlayCue("generation", AudioEmitter.NonSpatial());
            system.Stop(first);
            AudioVoiceHandle second = system.PlayCue("generation", AudioEmitter.NonSpatial());
            system.Update(0);

            Assert.Equal(first.Value & 0xFFFFFFFFul, second.Value & 0xFFFFFFFFul);
            Assert.NotEqual(first.Value >> 32, second.Value >> 32);
            Assert.Equal(1u, system.Stats.ActiveVoices);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FileStream_CanPauseResumeSeekAndStop()
    {
        string path = Path.Combine(Path.GetTempPath(), $"voxel-audio-{Guid.NewGuid():N}.wav");
        WriteTestWave(path, channels: 2, frameCount: 48_000);
        try
        {
            using AudioSystem system = CreateOfflineSystem();
            AudioStreamHandle stream = system.PlayStream(new AudioStreamDefinition
            {
                Path = path,
                SpatialMode = AudioSpatialMode.TwoDimensional,
                Looping = true
            });

            system.SetPaused(stream, true);
            system.Seek(stream, 0.5f);
            system.SetPaused(stream, false);
            system.Update(0.1f);

            Assert.True(stream.IsValid);
            Assert.Equal(1u, system.Stats.ActiveVoices);

            system.Stop(stream);
            Assert.Equal(0u, system.Stats.ActiveVoices);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingFile_IsLoggedAndReturnsInvalidVoice()
    {
        List<string> diagnostics = [];
        using AudioSystem system = new(new AudioSystemOptions
        {
            NoDevice = true,
            SampleRate = 48_000,
            Channels = 2,
            Log = diagnostics.Add,
            ThrowOnInitializationFailure = true
        });
        system.RegisterCue(new AudioCueDefinition
        {
            CueId = "missing",
            Variants = [new AudioCueVariant { Path = $"missing-{Guid.NewGuid():N}.wav" }]
        });

        AudioVoiceHandle voice = system.PlayCue("missing", AudioEmitter.NonSpatial());

        Assert.False(voice.IsValid);
        Assert.Equal(2, diagnostics.Count);
    }

    [Fact]
    public void LongFlacLoop_StreamsOneHundredSecondsWithBoundedMemory()
    {
        string repositoryRoot = FindRepositoryRoot();
        string path = Path.Combine(
            repositoryRoot,
            "Voxelgine",
            "data",
            "sound",
            "wind",
            "wind_loop.flac");
        Assert.True(File.Exists(path), $"Expected migrated ambience file '{path}'.");
        using AudioSystem system = CreateOfflineSystem();

        AudioStreamHandle stream = system.PlayStream(new AudioStreamDefinition
        {
            Path = path,
            Bus = AudioBus.Ambience,
            SpatialMode = AudioSpatialMode.TwoDimensional,
            Looping = true
        });

        system.SetPaused(stream, true);
        system.Seek(stream, 50.0f);
        system.SetPaused(stream, false);
        system.Update(0.1f);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        long privateBytesBefore = process.PrivateMemorySize64;

        system.Update(100.0f);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        process.Refresh();
        long privateByteGrowth = process.PrivateMemorySize64 - privateBytesBefore;

        Assert.True(stream.IsValid);
        Assert.Equal(1u, system.Stats.ActiveVoices);
        Assert.Equal(0ul, system.Stats.StreamUnderruns);
        Assert.True(
            privateByteGrowth < 32L * 1024L * 1024L,
            $"Streaming grew private memory by {privateByteGrowth:N0} bytes.");
    }

    [Fact]
    public void ManagedNativeTarget_SkipsSecondIncrementalBuild()
    {
        string repositoryRoot = FindRepositoryRoot();
        string project = Path.Combine(
            repositoryRoot,
            "Voxelgine.Audio",
            "Voxelgine.Audio.csproj");
        string buildDirectory = Path.Combine(
            Path.GetTempPath(),
            $"voxel-audio-build-{Guid.NewGuid():N}");
        try
        {
            ProcessResult first = RunDotnet(
                repositoryRoot,
                "msbuild",
                project,
                "/target:BuildVoxelAudioNative",
                "/property:Configuration=Release",
                $"/property:NativeAudioBuildDirectory={buildDirectory}",
                "/verbosity:minimal");
            AssertProcessSucceeded(first);

            string nativeBinary = Path.Combine(
                buildDirectory,
                "Release",
                "VoxelAudioNative.dll");
            Assert.True(File.Exists(nativeBinary), $"Expected native output '{nativeBinary}'.");
            DateTime firstWrite = File.GetLastWriteTimeUtc(nativeBinary);

            ProcessResult second = RunDotnet(
                repositoryRoot,
                "msbuild",
                project,
                "/target:BuildVoxelAudioNative",
                "/property:Configuration=Release",
                $"/property:NativeAudioBuildDirectory={buildDirectory}",
                "/verbosity:diagnostic");
            AssertProcessSucceeded(second);

            Assert.Contains(
                "Skipping target \"BuildVoxelAudioNative\" because all output files are up-to-date",
                second.StandardOutput,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(nativeBinary));
        }
        finally
        {
            if (Directory.Exists(buildDirectory))
            {
                Directory.Delete(buildDirectory, recursive: true);
            }
        }
    }

    private static AudioSystemOptions CreateOfflineOptions() => new()
    {
        NoDevice = true,
        SampleRate = 48_000,
        Channels = 2,
        ThrowOnInitializationFailure = true
    };

    private static AudioSystem CreateOfflineSystem() => new(CreateOfflineOptions());

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Voxelgine")) &&
                Directory.Exists(Path.Combine(directory.FullName, "thirdparty", "miniaudio")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the RaylibGame repository root.");
    }

    private static void WriteTestWave(string path, short channels, int frameCount)
    {
        const int sampleRate = 48_000;
        const short bitsPerSample = 16;
        int dataLength = checked(frameCount * channels * sizeof(short));
        using BinaryWriter writer = new(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataLength);
        for (int index = 0; index < frameCount * channels; index++)
        {
            writer.Write((short)0);
        }
    }

    private static ProcessResult RunDotnet(
        string workingDirectory,
        params string[] arguments)
    {
        ProcessStartInfo startInfo = new("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        Assert.True(process.Start(), "Failed to start dotnet.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(standardOutput, standardError);
        return new ProcessResult(
            process.ExitCode,
            standardOutput.Result,
            standardError.Result);
    }

    private static void AssertProcessSucceeded(ProcessResult result) =>
        Assert.True(
            result.ExitCode == 0,
            $"dotnet exited with {result.ExitCode}.\nstdout:\n{result.StandardOutput}\nstderr:\n{result.StandardError}");

    private readonly record struct ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
