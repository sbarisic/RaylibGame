#if WINDOWS
using System.Numerics;
using System.Text.Json;

namespace Voxelgine.FishGfxClient.Entities;

internal sealed class FishGfxAnimationLibrary
{
	private static readonly string[] StandardClips = ["idle", "walk", "attack", "crouch"];
	private readonly Dictionary<string, FishGfxAnimationClip> clips = new(StringComparer.OrdinalIgnoreCase);

	public static FishGfxAnimationLibrary LoadStandard(string animationDirectory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(animationDirectory);
		FishGfxAnimationLibrary library = new();
		foreach (string clipName in StandardClips)
		{
			string path = Path.Combine(animationDirectory, $"{clipName}.npcanim.json");
			library.clips.Add(clipName, FishGfxAnimationClip.Load(path));
		}
		return library;
	}

	public FishGfxAnimationClip Get(string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		if (!clips.TryGetValue(name, out FishGfxAnimationClip clip))
		{
			throw new KeyNotFoundException($"Animation clip '{name}' is not registered.");
		}
		return clip;
	}

	public bool Contains(string name)
	{
		return !string.IsNullOrWhiteSpace(name) && clips.ContainsKey(name);
	}
}

internal sealed class FishGfxAnimationPlayer
{
	private readonly Func<FishGfxAnimationLibrary> libraryAccessor;
	private string baseClipName = "idle";
	private float baseTime;
	private string actionClipName;
	private float actionTime;
	private byte previousRemoteState;

	public FishGfxAnimationPlayer(Func<FishGfxAnimationLibrary> libraryAccessor)
	{
		this.libraryAccessor = libraryAccessor
			?? throw new ArgumentNullException(nameof(libraryAccessor));
	}

	public EntityModelPose Pose { get; } = new();

	public string CurrentAnimation => baseClipName;

	public void SetBaseAnimation(string name, bool restart = false)
	{
		FishGfxAnimationLibrary library = libraryAccessor();
		if (!library.Contains(name))
		{
			name = "idle";
		}
		if (!restart && string.Equals(name, baseClipName, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		baseClipName = name;
		baseTime = 0;
	}

	public void SetRemoteAnimationState(byte state)
	{
		SetBaseAnimation(state == 1 ? "walk" : "idle");
		if (state == 2 && previousRemoteState != 2)
		{
			actionClipName = "attack";
			actionTime = 0;
		}
		else if (state != 2)
		{
			actionClipName = null;
			actionTime = 0;
		}
		previousRemoteState = state;
	}

	public void Update(
		float deltaSeconds,
		Vector3 headRotation,
		bool replaceHeadPitch = false
	)
	{
		FishGfxAnimationLibrary library = libraryAccessor();
		float delta = Math.Max(0, deltaSeconds);
		FishGfxAnimationClip baseClip = library.Get(baseClipName);
		baseTime = baseClip.Advance(baseTime, delta);
		Pose.Clear();
		baseClip.Sample(baseTime, Pose);

		if (actionClipName is not null)
		{
			FishGfxAnimationClip action = library.Get(actionClipName);
			actionTime = action.Advance(actionTime, delta);
			action.Sample(actionTime, Pose);
			if (!action.Loop && actionTime >= action.Duration)
			{
				actionClipName = null;
			}
		}

		EntityPartPose head = Pose["head"];
		Vector3 rotation = replaceHeadPitch
			? new Vector3(headRotation.X, head.RotationDegrees.Y, head.RotationDegrees.Z)
			: head.RotationDegrees + headRotation;
		Pose["head"] = head with { RotationDegrees = rotation };
	}
}

internal sealed class FishGfxAnimationClip
{
	private readonly Dictionary<string, FishGfxAnimationTrack> tracks = new(StringComparer.Ordinal);

	private FishGfxAnimationClip(string name, float duration, bool loop)
	{
		Name = name;
		Duration = duration;
		Loop = loop;
	}

	public string Name { get; }

	public float Duration { get; }

	public bool Loop { get; }

	public static FishGfxAnimationClip Load(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		try
		{
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
			JsonElement root = document.RootElement;
			string name = root.TryGetProperty("name", out JsonElement nameElement)
				? nameElement.GetString()
				: Path.GetFileNameWithoutExtension(path);
			float duration = ReadFinite(root, "duration");
			if (duration <= 0)
			{
				throw new FormatException($"Animation '{name}' must have positive duration.");
			}
			bool loop = !root.TryGetProperty("loop", out JsonElement loopElement)
				|| loopElement.GetBoolean();
			FishGfxAnimationClip clip = new(name, duration, loop);
			if (!root.TryGetProperty("tracks", out JsonElement tracks)
				|| tracks.ValueKind != JsonValueKind.Object)
			{
				throw new FormatException($"Animation '{name}' requires a tracks object.");
			}

			foreach (JsonProperty property in tracks.EnumerateObject())
			{
				if (property.Value.ValueKind != JsonValueKind.Array)
				{
					throw new FormatException($"Animation track '{property.Name}' must be an array.");
				}
				List<FishGfxAnimationKeyframe> frames = new();
				foreach (JsonElement frame in property.Value.EnumerateArray())
				{
					frames.Add(new FishGfxAnimationKeyframe(
						ReadFinite(frame, "time"),
						ReadVector3(frame, "rotation"),
						ReadVector3(frame, "position"),
						frame.TryGetProperty("easing", out JsonElement easing)
							? easing.GetString()
							: "Linear"
					));
				}
				frames.Sort((left, right) => left.Time.CompareTo(right.Time));
				clip.tracks.Add(property.Name, new FishGfxAnimationTrack(frames));
			}
			return clip;
		}
		catch (JsonException exception)
		{
			throw new FormatException($"Animation '{path}' contains invalid JSON.", exception);
		}
	}

	public float Advance(float time, float deltaSeconds)
	{
		float next = time + deltaSeconds;
		if (Loop)
		{
			return next % Duration;
		}
		return Math.Min(next, Duration);
	}

	public void Sample(float time, EntityModelPose destination)
	{
		foreach ((string partName, FishGfxAnimationTrack track) in tracks)
		{
			destination[partName] = track.Sample(time);
		}
	}

	private static Vector3 ReadVector3(JsonElement owner, string name)
	{
		if (!owner.TryGetProperty(name, out JsonElement value)
			|| value.ValueKind != JsonValueKind.Array
			|| value.GetArrayLength() != 3)
		{
			return Vector3.Zero;
		}
		return new Vector3(ReadFinite(value[0]), ReadFinite(value[1]), ReadFinite(value[2]));
	}

	private static float ReadFinite(JsonElement owner, string name)
	{
		if (!owner.TryGetProperty(name, out JsonElement value))
		{
			throw new FormatException($"Animation property '{name}' is required.");
		}
		return ReadFinite(value);
	}

	private static float ReadFinite(JsonElement value)
	{
		if (!value.TryGetSingle(out float result) || !float.IsFinite(result))
		{
			throw new FormatException("Animation numeric values must be finite numbers.");
		}
		return result;
	}
}

internal sealed class FishGfxAnimationTrack
{
	private readonly IReadOnlyList<FishGfxAnimationKeyframe> frames;

	public FishGfxAnimationTrack(IReadOnlyList<FishGfxAnimationKeyframe> frames)
	{
		this.frames = frames ?? throw new ArgumentNullException(nameof(frames));
	}

	public EntityPartPose Sample(float time)
	{
		if (frames.Count == 0)
		{
			return default;
		}
		if (frames.Count == 1 || time <= frames[0].Time)
		{
			return frames[0].Pose;
		}

		for (int index = 1; index < frames.Count; index++)
		{
			FishGfxAnimationKeyframe next = frames[index];
			if (time > next.Time)
			{
				continue;
			}
			FishGfxAnimationKeyframe previous = frames[index - 1];
			float duration = next.Time - previous.Time;
			float amount = duration > 0 ? (time - previous.Time) / duration : 0;
			amount = ApplyEasing(next.Easing, Math.Clamp(amount, 0, 1));
			return new EntityPartPose(
				Vector3.Lerp(previous.Rotation, next.Rotation, amount),
				Vector3.Lerp(previous.Position, next.Position, amount)
			);
		}

		return frames[^1].Pose;
	}

	private static float ApplyEasing(string name, float value)
	{
		return name?.ToLowerInvariant() switch
		{
			"easeoutquad" => 1 - (1 - value) * (1 - value),
			"easeinoutquad" => value < 0.5f
				? 2 * value * value
				: 1 - MathF.Pow(-2 * value + 2, 2) * 0.5f,
			"easeinoutsine" => -(MathF.Cos(MathF.PI * value) - 1) * 0.5f,
			_ => value,
		};
	}
}

internal readonly record struct FishGfxAnimationKeyframe(
	float Time,
	Vector3 Rotation,
	Vector3 Position,
	string Easing
)
{
	public EntityPartPose Pose => new(Rotation, Position);
}
#endif
