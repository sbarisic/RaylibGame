using System.Text.Json;

namespace Voxelgine.Audio;

public sealed class AudioCueBank
{
    public const int CurrentVersion = 1;

    public AudioCueBank(IReadOnlyList<AudioCueDefinition> cues)
    {
        Cues = cues;
    }

    public IReadOnlyList<AudioCueDefinition> Cues { get; }

    public static AudioCueBank LoadDefault() => Load(Path.Combine(
        AppContext.BaseDirectory,
        "data",
        "audio",
        "audio-bank.json"));

    public static AudioCueBank Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        string baseDirectory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("Audio bank path has no parent directory.", nameof(path));

        using FileStream stream = File.OpenRead(fullPath);
        using JsonDocument document = JsonDocument.Parse(
            stream,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

        JsonElement root = document.RootElement;
        RequireKind(root, JsonValueKind.Object, "The audio bank root must be an object.");
        int version = GetRequiredInt(root, "version");
        if (version != CurrentVersion)
        {
            throw new FormatException(
                $"Unsupported audio bank version {version}; expected {CurrentVersion}.");
        }

        JsonElement cueElements = GetRequired(root, "cues");
        RequireKind(cueElements, JsonValueKind.Array, "'cues' must be an array.");
        List<AudioCueDefinition> cues = [];
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement cueElement in cueElements.EnumerateArray())
        {
            AudioCueDefinition cue = ParseCue(cueElement, baseDirectory);
            if (!ids.Add(cue.CueId))
            {
                throw new FormatException($"Audio cue '{cue.CueId}' is duplicated.");
            }
            cues.Add(cue);
        }

        return new AudioCueBank(cues);
    }

    public void RegisterWith(IAudioSystem audioSystem)
    {
        ArgumentNullException.ThrowIfNull(audioSystem);
        foreach (AudioCueDefinition cue in Cues)
        {
            audioSystem.RegisterCue(cue);
        }
    }

    private static AudioCueDefinition ParseCue(
        JsonElement element,
        string baseDirectory)
    {
        RequireKind(element, JsonValueKind.Object, "Each cue must be an object.");
        string id = GetRequiredString(element, "id");
        JsonElement variantElements = GetRequired(element, "variants");
        RequireKind(variantElements, JsonValueKind.Array, $"Cue '{id}' variants must be an array.");

        List<AudioCueVariant> variants = [];
        foreach (JsonElement variantElement in variantElements.EnumerateArray())
        {
            RequireKind(
                variantElement,
                JsonValueKind.Object,
                $"Cue '{id}' variants must be objects.");
            string relativePath = GetRequiredString(variantElement, "path");
            string resolvedPath = Path.GetFullPath(relativePath, baseDirectory);
            variants.Add(new AudioCueVariant
            {
                Path = resolvedPath,
                Gain = GetOptionalFloat(variantElement, "gain", 1.0f),
                PitchMin = GetOptionalFloat(variantElement, "pitchMin", 1.0f),
                PitchMax = GetOptionalFloat(variantElement, "pitchMax", 1.0f)
            });
        }

        if (variants.Count == 0)
        {
            throw new FormatException($"Audio cue '{id}' has no variants.");
        }

        return new AudioCueDefinition
        {
            CueId = id,
            Variants = variants,
            Bus = ParseBus(GetOptionalString(element, "bus", "Sfx"), id),
            Gain = GetOptionalFloat(element, "gain", 1.0f),
            SpatialMode = ParseSpatialMode(
                GetOptionalString(element, "spatialMode", "ThreeDimensional"),
                id),
            MinDistance = GetOptionalFloat(element, "minDistance", 1.0f),
            MaxDistance = GetOptionalFloat(element, "maxDistance", 32.0f),
            DopplerFactor = GetOptionalFloat(element, "dopplerFactor", 1.0f),
            Looping = GetOptionalBool(element, "looping", false),
            Streamed = GetOptionalBool(element, "streamed", false),
            MaxInstancesPerVariant = GetOptionalInt(
                element,
                "maxInstancesPerVariant",
                6)
        };
    }

    private static AudioBus ParseBus(string value, string cueId)
    {
        if (Enum.TryParse(value, true, out AudioBus bus))
        {
            return bus;
        }
        throw new FormatException($"Cue '{cueId}' has unknown bus '{value}'.");
    }

    private static AudioSpatialMode ParseSpatialMode(string value, string cueId)
    {
        if (value.Equals("2D", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("TwoDimensional", StringComparison.OrdinalIgnoreCase))
        {
            return AudioSpatialMode.TwoDimensional;
        }
        if (value.Equals("3D", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("ThreeDimensional", StringComparison.OrdinalIgnoreCase))
        {
            return AudioSpatialMode.ThreeDimensional;
        }
        throw new FormatException($"Cue '{cueId}' has unknown spatialMode '{value}'.");
    }

    private static JsonElement GetRequired(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            throw new FormatException($"Required audio bank property '{name}' is missing.");
        }
        return value;
    }

    private static string GetRequiredString(JsonElement element, string name)
    {
        JsonElement value = GetRequired(element, name);
        if (value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new FormatException($"Audio bank property '{name}' must be a non-empty string.");
        }
        return value.GetString()!;
    }

    private static int GetRequiredInt(JsonElement element, string name)
    {
        JsonElement value = GetRequired(element, name);
        if (!value.TryGetInt32(out int result))
        {
            throw new FormatException($"Audio bank property '{name}' must be an integer.");
        }
        return result;
    }

    private static string GetOptionalString(
        JsonElement element,
        string name,
        string defaultValue)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return defaultValue;
        }
        if (value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new FormatException($"Audio bank property '{name}' must be a non-empty string.");
        }
        return value.GetString()!;
    }

    private static float GetOptionalFloat(
        JsonElement element,
        string name,
        float defaultValue)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return defaultValue;
        }
        if (!value.TryGetSingle(out float result) || !float.IsFinite(result))
        {
            throw new FormatException($"Audio bank property '{name}' must be a finite number.");
        }
        return result;
    }

    private static int GetOptionalInt(
        JsonElement element,
        string name,
        int defaultValue)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return defaultValue;
        }
        if (!value.TryGetInt32(out int result))
        {
            throw new FormatException($"Audio bank property '{name}' must be an integer.");
        }
        return result;
    }

    private static bool GetOptionalBool(
        JsonElement element,
        string name,
        bool defaultValue)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return defaultValue;
        }
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new FormatException($"Audio bank property '{name}' must be a boolean.");
        }
        return value.GetBoolean();
    }

    private static void RequireKind(
        JsonElement element,
        JsonValueKind kind,
        string message)
    {
        if (element.ValueKind != kind)
        {
            throw new FormatException(message);
        }
    }
}
