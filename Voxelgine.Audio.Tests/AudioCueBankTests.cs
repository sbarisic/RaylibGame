namespace Voxelgine.Audio.Tests;

public sealed class AudioCueBankTests
{
    [Fact]
    public void Load_ResolvesVariantPathsRelativeToBank()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"voxel-audio-bank-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string bankPath = Path.Combine(directory, "audio-bank.json");
            File.WriteAllText(
                bankPath,
                """
                {
                  "version": 1,
                  "cues": [
                    {
                      "id": "ambience.wind",
                      "variants": [{ "path": "wind.flac" }],
                      "bus": "Ambience",
                      "spatialMode": "2D",
                      "streamed": true,
                      "looping": true
                    }
                  ]
                }
                """);

            AudioCueBank bank = AudioCueBank.Load(bankPath);

            AudioCueDefinition cue = Assert.Single(bank.Cues);
            Assert.Equal(AudioBus.Ambience, cue.Bus);
            Assert.Equal(AudioSpatialMode.TwoDimensional, cue.SpatialMode);
            Assert.True(cue.Streamed);
            Assert.Equal(
                Path.Combine(directory, "wind.flac"),
                Assert.Single(cue.Variants).Path);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Load_RejectsDuplicateCueIdsIgnoringCase()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "version": 1,
                  "cues": [
                    { "id": "walk", "variants": [{ "path": "a.wav" }] },
                    { "id": "WALK", "variants": [{ "path": "b.wav" }] }
                  ]
                }
                """);

            Assert.Throws<FormatException>(() => AudioCueBank.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
