using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Voxelgine.Engine.DI;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Helper class for serializing EasingFunc delegates to/from string names.
	/// </summary>
	public static class EasingSerializer
	{
		static readonly Dictionary<string, EasingFunc> NameToFunc = new Dictionary<string, EasingFunc>(StringComparer.OrdinalIgnoreCase)
		{
			["Linear"] = Easing.Linear,
			["EaseInQuad"] = Easing.EaseInQuad,
			["EaseOutQuad"] = Easing.EaseOutQuad,
			["EaseInOutQuad"] = Easing.EaseInOutQuad,
			["EaseInCubic"] = Easing.EaseInCubic,
			["EaseOutCubic"] = Easing.EaseOutCubic,
			["EaseInOutCubic"] = Easing.EaseInOutCubic,
			["EaseInQuart"] = Easing.EaseInQuart,
			["EaseOutQuart"] = Easing.EaseOutQuart,
			["EaseInOutQuart"] = Easing.EaseInOutQuart,
			["EaseInQuint"] = Easing.EaseInQuint,
			["EaseOutQuint"] = Easing.EaseOutQuint,
			["EaseInOutQuint"] = Easing.EaseInOutQuint,
			["EaseInSine"] = Easing.EaseInSine,
			["EaseOutSine"] = Easing.EaseOutSine,
			["EaseInOutSine"] = Easing.EaseInOutSine,
			["EaseInExpo"] = Easing.EaseInExpo,
			["EaseOutExpo"] = Easing.EaseOutExpo,
			["EaseInOutExpo"] = Easing.EaseInOutExpo,
			["EaseInCirc"] = Easing.EaseInCirc,
			["EaseOutCirc"] = Easing.EaseOutCirc,
			["EaseInOutCirc"] = Easing.EaseInOutCirc,
			["EaseInBack"] = Easing.EaseInBack,
			["EaseOutBack"] = Easing.EaseOutBack,
			["EaseInOutBack"] = Easing.EaseInOutBack,
			["EaseInElastic"] = Easing.EaseInElastic,
			["EaseOutElastic"] = Easing.EaseOutElastic,
			["EaseInOutElastic"] = Easing.EaseInOutElastic,
			["EaseInBounce"] = Easing.EaseInBounce,
			["EaseOutBounce"] = Easing.EaseOutBounce,
			["EaseInOutBounce"] = Easing.EaseInOutBounce,
		};

		static readonly Dictionary<EasingFunc, string> FuncToName;

		static EasingSerializer()
		{
			FuncToName = new Dictionary<EasingFunc, string>();
			foreach (var kv in NameToFunc)
				FuncToName[kv.Value] = kv.Key;
		}

		public static string GetName(EasingFunc func)
		{
			if (func == null) return "Linear";
			return FuncToName.TryGetValue(func, out var name) ? name : "Linear";
		}

		public static EasingFunc GetFunc(string name)
		{
			if (string.IsNullOrEmpty(name)) return Easing.Linear;
			return NameToFunc.TryGetValue(name, out var func) ? func : Easing.Linear;
		}
	}

	/// <summary>
	/// Defines a single keyframe for an element's transform at a specific time.
	/// </summary>
	public struct AnimationKeyframe
	{
		/// <summary>Time in seconds when this keyframe occurs.</summary>
		public float Time;
		/// <summary>Rotation in degrees around the element's rotation origin.</summary>
		public Vector3 Rotation;
		/// <summary>Position offset from the element's base position.</summary>
		public Vector3 Position;
		/// <summary>Easing function to use when interpolating TO this keyframe.</summary>
		public EasingFunc Easing;

		public AnimationKeyframe(float time, Vector3 rotation, Vector3 position, EasingFunc easing = null)
		{
			Time = time;
			Rotation = rotation;
			Position = position;
			Easing = easing ?? Voxelgine.Engine.Easing.Linear;
		}
	}

	/// <summary>
	/// Animation track for a single element (e.g., "leg_l", "hand_r").
	/// Contains keyframes that define how the element transforms over time.
	/// </summary>
	public class AnimationTrack
	{
		/// <summary>Name of the element this track animates (matches JSON element name).</summary>
		public string ElementName;
		/// <summary>Keyframes sorted by time.</summary>
		public List<AnimationKeyframe> Keyframes = new List<AnimationKeyframe>();

		public AnimationTrack(string elementName)
		{
			ElementName = elementName;
		}

		/// <summary>
		/// Adds a keyframe to this track.
		/// </summary>
		public void AddKeyframe(float time, Vector3 rotation, Vector3 position = default, EasingFunc easing = null)
		{
			Keyframes.Add(new AnimationKeyframe(time, rotation, position, easing));
			// Keep keyframes sorted by time
			Keyframes.Sort((a, b) => a.Time.CompareTo(b.Time));
		}

		/// <summary>
		/// Samples the track at the given time, returning interpolated rotation and position.
		/// </summary>
		public (Vector3 Rotation, Vector3 Position) Sample(float time)
		{
			if (Keyframes.Count == 0)
				return (Vector3.Zero, Vector3.Zero);

			if (Keyframes.Count == 1)
				return (Keyframes[0].Rotation, Keyframes[0].Position);

			// Find the two keyframes to interpolate between
			int nextIndex = 0;
			for (int i = 0; i < Keyframes.Count; i++)
			{
				if (Keyframes[i].Time > time)
				{
					nextIndex = i;
					break;
				}
				nextIndex = i + 1;
			}

			if (nextIndex == 0)
			{
				return (Keyframes[0].Rotation, Keyframes[0].Position);
			}

			if (nextIndex >= Keyframes.Count)
			{
				return (Keyframes[Keyframes.Count - 1].Rotation, Keyframes[Keyframes.Count - 1].Position);
			}

			var prev = Keyframes[nextIndex - 1];
			var next = Keyframes[nextIndex];

			float segmentDuration = next.Time - prev.Time;
			float t = segmentDuration > 0 ? (time - prev.Time) / segmentDuration : 0;

			// Apply easing
			float easedT = next.Easing?.Invoke(t) ?? t;

			// Interpolate
			Vector3 rotation = Vector3.Lerp(prev.Rotation, next.Rotation, easedT);
			Vector3 position = Vector3.Lerp(prev.Position, next.Position, easedT);

			return (rotation, position);
		}
	}

	/// <summary>
	/// Defines a complete animation clip (e.g., "walk", "idle", "attack").
	/// Contains multiple tracks, one per animated element.
	/// </summary>
	public class NPCAnimationClip
	{
		/// <summary>Name of this animation (e.g., "walk", "idle").</summary>
		public string Name;
		/// <summary>Total duration of the animation in seconds.</summary>
		public float Duration;
		/// <summary>Whether the animation should loop.</summary>
		public bool Loop = true;
		/// <summary>Animation tracks, one per element.</summary>
		public Dictionary<string, AnimationTrack> Tracks = new Dictionary<string, AnimationTrack>();

		public NPCAnimationClip(string name, float duration)
		{
			Name = name;
			Duration = duration;
		}

		/// <summary>
		/// Gets or creates a track for the specified element.
		/// </summary>
		public AnimationTrack GetOrCreateTrack(string elementName)
		{
			if (!Tracks.TryGetValue(elementName, out var track))
			{
				track = new AnimationTrack(elementName);
				Tracks[elementName] = track;
			}
			return track;
		}

		/// <summary>
		/// Samples all tracks at the given time.
		/// </summary>
		public Dictionary<string, (Vector3 Rotation, Vector3 Position)> Sample(float time)
		{
			var result = new Dictionary<string, (Vector3, Vector3)>();
			foreach (var track in Tracks.Values)
			{
				result[track.ElementName] = track.Sample(time);
			}
			return result;
		}

		/// <summary>
		/// Default folder for NPC animation files.
		/// </summary>
		public const string AnimationsFolder = "data/animations/npc";

		/// <summary>
		/// Saves this animation clip to a JSON file.
		/// </summary>
		/// <param name="filename">Filename without path or extension (e.g., "walk"). Will be saved to data/animations/npc/{filename}.npcanim.json</param>
		public void Save(string filename)
		{
			var data = new JObject
			{
				["name"] = Name,
				["duration"] = Duration,
				["loop"] = Loop,
				["tracks"] = new JObject()
			};

			var tracksObj = (JObject)data["tracks"];
			foreach (var track in Tracks.Values)
			{
				var keyframesArr = new JArray();
				foreach (var kf in track.Keyframes)
				{
					keyframesArr.Add(new JObject
					{
						["time"] = kf.Time,
						["rotation"] = new JArray(kf.Rotation.X, kf.Rotation.Y, kf.Rotation.Z),
						["position"] = new JArray(kf.Position.X, kf.Position.Y, kf.Position.Z),
						["easing"] = EasingSerializer.GetName(kf.Easing)
					});
				}
				tracksObj[track.ElementName] = keyframesArr;
			}

			Directory.CreateDirectory(AnimationsFolder);
			string path = Path.Combine(AnimationsFolder, $"{filename}.npcanim.json");
			File.WriteAllText(path, data.ToString(Formatting.Indented));
		}

		/// <summary>
		/// Loads an animation clip from a JSON file.
		/// </summary>
		/// <param name="filename">Filename without path or extension (e.g., "walk"). Will be loaded from data/animations/npc/{filename}.npcanim.json</param>
		/// <returns>The loaded animation clip, or null if the file doesn't exist.</returns>
		public static NPCAnimationClip Load(string filename)
		{
			string path = Path.Combine(AnimationsFolder, $"{filename}.npcanim.json");
			if (!File.Exists(path))
				return null;

			string json = File.ReadAllText(path);
			var data = JObject.Parse(json);

			string name = data["name"]?.Value<string>() ?? filename;
			float duration = data["duration"]?.Value<float>() ?? 1.0f;
			bool loop = data["loop"]?.Value<bool>() ?? true;

			var clip = new NPCAnimationClip(name, duration) { Loop = loop };

			var tracksObj = data["tracks"] as JObject;
			if (tracksObj != null)
			{
				foreach (var prop in tracksObj.Properties())
				{
					string elementName = prop.Name;
					var keyframesArr = prop.Value as JArray;
					if (keyframesArr == null) continue;

					var track = clip.GetOrCreateTrack(elementName);
					foreach (var kfToken in keyframesArr)
					{
						var kfObj = kfToken as JObject;
						if (kfObj == null) continue;

						float time = kfObj["time"]?.Value<float>() ?? 0f;

						Vector3 rotation = Vector3.Zero;
						var rotArr = kfObj["rotation"] as JArray;
						if (rotArr != null && rotArr.Count >= 3)
							rotation = new Vector3(rotArr[0].Value<float>(), rotArr[1].Value<float>(), rotArr[2].Value<float>());

						Vector3 position = Vector3.Zero;
						var posArr = kfObj["position"] as JArray;
						if (posArr != null && posArr.Count >= 3)
							position = new Vector3(posArr[0].Value<float>(), posArr[1].Value<float>(), posArr[2].Value<float>());

						string easingName = kfObj["easing"]?.Value<string>() ?? "Linear";
						EasingFunc easing = EasingSerializer.GetFunc(easingName);

						track.AddKeyframe(time, rotation, position, easing);
					}
				}
			}

			return clip;
		}

		/// <summary>
		/// Gets all available animation clip filenames from the animations folder.
		/// </summary>
		/// <returns>List of animation names (without path or extension).</returns>
		public static List<string> GetAvailableAnimations()
		{
			var result = new List<string>();
			if (!Directory.Exists(AnimationsFolder))
				return result;

			foreach (var file in Directory.GetFiles(AnimationsFolder, "*.npcanim.json"))
			{
				string name = Path.GetFileNameWithoutExtension(file);
				// Remove the .npcanim part
				if (name.EndsWith(".npcanim", StringComparison.OrdinalIgnoreCase))
					name = name.Substring(0, name.Length - 8);
				result.Add(name);
			}
			return result;
		}
	}

	/// <summary>
	/// Library of predefined NPC animations.
	/// </summary>
	public static class NPCAnimations
	{
		/// <summary>Creates the walk animation for humanoid NPCs.</summary>
		/*public static NPCAnimationClip CreateWalkAnimation()
		{
			var clip = new NPCAnimationClip("walk", 0.8f);

			// Left leg: swing forward then back
			var legL = clip.GetOrCreateTrack("leg_l");
			legL.AddKeyframe(0.0f, new Vector3(-30, 0, 0), Vector3.Zero, Easing.EaseInOutSine);
			legL.AddKeyframe(0.4f, new Vector3(30, 0, 0), Vector3.Zero, Easing.EaseInOutSine);
			legL.AddKeyframe(0.8f, new Vector3(-30, 0, 0), Vector3.Zero, Easing.EaseInOutSine);

			// Right leg: opposite phase
			var legR = clip.GetOrCreateTrack("leg_r");
			legR.AddKeyframe(0.0f, new Vector3(30, 0, 0), Vector3.Zero, Easing.EaseInOutSine);
			legR.AddKeyframe(0.4f, new Vector3(-30, 0, 0), Vector3.Zero, Easing.EaseInOutSine);
			legR.AddKeyframe(0.8f, new Vector3(30, 0, 0), Vector3.Zero, Easing.EaseInOutSine);

			// Left arm: opposite to left leg
			var handL = clip.GetOrCreateTrack("hand_l");
			handL.AddKeyframe(0.0f, new Vector3(25, 0, 0), Vector3.Zero, Easing.EaseInOutSine);
			handL.AddKeyframe(0.4f, new Vector3(-25, 0, 0), Vector3.Zero, Easing.EaseInOutSine);
			handL.AddKeyframe(0.8f, new Vector3(25, 0, 0), Vector3.Zero, Easing.EaseInOutSine);

			// Right arm: opposite to right leg
			var handR = clip.GetOrCreateTrack("hand_r");
			handR.AddKeyframe(0.0f, new Vector3(-25, 0, 0), Vector3.Zero, Easing.EaseInOutSine);
			handR.AddKeyframe(0.4f, new Vector3(25, 0, 0), Vector3.Zero, Easing.EaseInOutSine);
			handR.AddKeyframe(0.8f, new Vector3(-25, 0, 0), Vector3.Zero, Easing.EaseInOutSine);

			return clip;
		}

		/// <summary>Creates the idle animation for humanoid NPCs.</summary>
		public static NPCAnimationClip CreateIdleAnimation()
		{
			var clip = new NPCAnimationClip("idle", 2.0f);

			// Subtle body sway
			var body = clip.GetOrCreateTrack("body");
			body.AddKeyframe(0.0f, new Vector3(0, 0, 0), Vector3.Zero, Easing.EaseInOutSine);
			body.AddKeyframe(1.0f, new Vector3(0, 2, 0), Vector3.Zero, Easing.EaseInOutSine);
			body.AddKeyframe(2.0f, new Vector3(0, 0, 0), Vector3.Zero, Easing.EaseInOutSine);

			// Head slight look around
			var head = clip.GetOrCreateTrack("head");
			head.AddKeyframe(0.0f, new Vector3(0, 0, 0), Vector3.Zero, Easing.EaseInOutSine);
			head.AddKeyframe(0.7f, new Vector3(0, -15, 0), Vector3.Zero, Easing.EaseInOutSine);
			head.AddKeyframe(1.4f, new Vector3(0, 15, 0), Vector3.Zero, Easing.EaseInOutSine);
			head.AddKeyframe(2.0f, new Vector3(0, 0, 0), Vector3.Zero, Easing.EaseInOutSine);

			// Legs stay planted (counteract body sway on Y axis - no effect needed since body only rotates Y)
			var legL = clip.GetOrCreateTrack("leg_l");
			legL.AddKeyframe(0.0f, Vector3.Zero, Vector3.Zero);
			legL.AddKeyframe(2.0f, Vector3.Zero, Vector3.Zero);

			var legR = clip.GetOrCreateTrack("leg_r");
			legR.AddKeyframe(0.0f, Vector3.Zero, Vector3.Zero);
			legR.AddKeyframe(2.0f, Vector3.Zero, Vector3.Zero);

			// Arms relaxed at sides
			var handL = clip.GetOrCreateTrack("hand_l");
			handL.AddKeyframe(0.0f, Vector3.Zero, Vector3.Zero);
			handL.AddKeyframe(2.0f, Vector3.Zero, Vector3.Zero);

			var handR = clip.GetOrCreateTrack("hand_r");
			handR.AddKeyframe(0.0f, Vector3.Zero, Vector3.Zero);
			handR.AddKeyframe(2.0f, Vector3.Zero, Vector3.Zero);

			return clip;
		}

		/// <summary>Creates the attack animation for humanoid NPCs.</summary>
		public static NPCAnimationClip CreateAttackAnimation()
		{
			var clip = new NPCAnimationClip("attack", 0.5f) { Loop = false };

			// Right arm swing
			var handR = clip.GetOrCreateTrack("hand_r");
			handR.AddKeyframe(0.0f, new Vector3(0, 0, 0), Vector3.Zero, Easing.EaseOutQuad);
			handR.AddKeyframe(0.15f, new Vector3(-90, 0, 0), Vector3.Zero, Easing.EaseOutQuad);
			handR.AddKeyframe(0.5f, new Vector3(0, 0, 0), Vector3.Zero, Easing.EaseInOutQuad);

			// Left arm stays relaxed
			var handL = clip.GetOrCreateTrack("hand_l");
			handL.AddKeyframe(0.0f, Vector3.Zero, Vector3.Zero);
			handL.AddKeyframe(0.5f, Vector3.Zero, Vector3.Zero);

			// Body lunge forward slightly
			var body = clip.GetOrCreateTrack("body");
			body.AddKeyframe(0.0f, new Vector3(0, 0, 0), Vector3.Zero, Easing.EaseOutQuad);
			body.AddKeyframe(0.15f, new Vector3(15, 0, 0), Vector3.Zero, Easing.EaseOutQuad);
			body.AddKeyframe(0.5f, new Vector3(0, 0, 0), Vector3.Zero, Easing.EaseInOutQuad);

			// Legs stay planted - counteract body's forward pitch to keep feet grounded
			var legL = clip.GetOrCreateTrack("leg_l");
			legL.AddKeyframe(0.0f, new Vector3(0, 0, 0), Vector3.Zero, Easing.EaseOutQuad);
			legL.AddKeyframe(0.15f, new Vector3(-15, 0, 0), Vector3.Zero, Easing.EaseOutQuad);  // Counter body's 15 deg pitch
			legL.AddKeyframe(0.5f, new Vector3(0, 0, 0), Vector3.Zero, Easing.EaseInOutQuad);

			var legR = clip.GetOrCreateTrack("leg_r");
			legR.AddKeyframe(0.0f, new Vector3(0, 0, 0), Vector3.Zero, Easing.EaseOutQuad);
			legR.AddKeyframe(0.15f, new Vector3(-15, 0, 0), Vector3.Zero, Easing.EaseOutQuad);  // Counter body's 15 deg pitch
			legR.AddKeyframe(0.5f, new Vector3(0, 0, 0), Vector3.Zero, Easing.EaseInOutQuad);

			// Head follows body naturally (no counter-rotation needed)
			var head = clip.GetOrCreateTrack("head");
			head.AddKeyframe(0.0f, Vector3.Zero, Vector3.Zero);
			head.AddKeyframe(0.5f, Vector3.Zero, Vector3.Zero);

			return clip;
		}

		/// <summary>Creates the crouch animation for humanoid NPCs.</summary>
		public static NPCAnimationClip CreateCrouchAnimation()
		{
			var clip = new NPCAnimationClip("crouch", 0.3f) { Loop = false };

			// Body tilts forward 30 degrees
			var body = clip.GetOrCreateTrack("body");
			body.AddKeyframe(0.0f, new Vector3(0, 0, 0), Vector3.Zero, Easing.EaseOutQuad);
			body.AddKeyframe(0.3f, new Vector3(30, 0, 0), Vector3.Zero, Easing.EaseOutQuad);

			// All other parts follow body naturally (no animation needed - they inherit body's tilt)
			var legL = clip.GetOrCreateTrack("leg_l");
			legL.AddKeyframe(0.0f, Vector3.Zero, Vector3.Zero);
			legL.AddKeyframe(0.3f, new Vector3(-30, 0, 0), Vector3.Zero);

			var legR = clip.GetOrCreateTrack("leg_r");
			legR.AddKeyframe(0.0f, Vector3.Zero, Vector3.Zero);
			legR.AddKeyframe(0.3f, new Vector3(-30, 0, 0), Vector3.Zero);

			var handL = clip.GetOrCreateTrack("hand_l");
			handL.AddKeyframe(0.0f, Vector3.Zero, Vector3.Zero);
			handL.AddKeyframe(0.3f, new Vector3(-30, 0, 0), Vector3.Zero);

			var handR = clip.GetOrCreateTrack("hand_r");
			handR.AddKeyframe(0.0f, Vector3.Zero, Vector3.Zero);
			handR.AddKeyframe(0.3f, new Vector3(-30, 0, 0), Vector3.Zero);

			var head = clip.GetOrCreateTrack("head");
			head.AddKeyframe(0.0f, Vector3.Zero, Vector3.Zero);
			head.AddKeyframe(0.3f, new Vector3(-15, 0, 0), Vector3.Zero);

			return clip;
		}

		/// <summary>
		/// Exports all default animations to .npcanim.json files in the animations folder.
		/// Useful for creating initial animation files that can be edited externally.
		/// </summary>
		public static void ExportAllDefaults(IFishLogging logging)
		{
			logging.WriteLine("[NPCAnimations] Exporting default animations...");

			CreateWalkAnimation().Save("walk");
			logging.WriteLine("  - Exported walk.npcanim.json");

			CreateIdleAnimation().Save("idle");
			logging.WriteLine("  - Exported idle.npcanim.json");

			CreateAttackAnimation().Save("attack");
			logging.WriteLine("  - Exported attack.npcanim.json");

			CreateCrouchAnimation().Save("crouch");
			logging.WriteLine("  - Exported crouch.npcanim.json");

			logging.WriteLine($"[NPCAnimations] Exported 4 animations to {NPCAnimationClip.AnimationsFolder}/");
		}//*/
	}
}
