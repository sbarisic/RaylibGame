using System;
using System.Collections.Generic;
using System.Numerics;

namespace Voxelgine.Engine
{
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
	}

	/// <summary>
	/// Library of predefined NPC animations.
	/// </summary>
	public static class NPCAnimations
	{
		/// <summary>Creates the walk animation for humanoid NPCs.</summary>
		public static NPCAnimationClip CreateWalkAnimation()
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

			// Body lunge forward slightly
			var body = clip.GetOrCreateTrack("body");
			body.AddKeyframe(0.0f, new Vector3(0, 0, 0), Vector3.Zero, Easing.EaseOutQuad);
			body.AddKeyframe(0.15f, new Vector3(15, 0, 0), Vector3.Zero, Easing.EaseOutQuad);
			body.AddKeyframe(0.5f, new Vector3(0, 0, 0), Vector3.Zero, Easing.EaseInOutQuad);

			return clip;
		}

		/// <summary>Creates the crouch animation for humanoid NPCs.</summary>
		public static NPCAnimationClip CreateCrouchAnimation()
		{
			var clip = new NPCAnimationClip("crouch", 0.3f) { Loop = false };

			// Legs bend
			var legL = clip.GetOrCreateTrack("leg_l");
			legL.AddKeyframe(0.0f, new Vector3(0, 0, 0), Vector3.Zero, Easing.EaseOutQuad);
			legL.AddKeyframe(0.3f, new Vector3(-45, 0, 0), new Vector3(0, -0.3f, 0), Easing.EaseOutQuad);

			var legR = clip.GetOrCreateTrack("leg_r");
			legR.AddKeyframe(0.0f, new Vector3(0, 0, 0), Vector3.Zero, Easing.EaseOutQuad);
			legR.AddKeyframe(0.3f, new Vector3(-45, 0, 0), new Vector3(0, -0.3f, 0), Easing.EaseOutQuad);

			// Body lowers
			var body = clip.GetOrCreateTrack("body");
			body.AddKeyframe(0.0f, new Vector3(0, 0, 0), Vector3.Zero, Easing.EaseOutQuad);
			body.AddKeyframe(0.3f, new Vector3(20, 0, 0), new Vector3(0, -0.4f, 0), Easing.EaseOutQuad);

			return clip;
		}
	}
}
