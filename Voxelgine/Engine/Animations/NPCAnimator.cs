using System;
using System.Collections.Generic;
using System.Numerics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// NPC animation state for tracking playback.
	/// </summary>
	public enum NPCAnimationState
	{
		Stopped,
		Playing,
		Paused
	}

	/// <summary>
	/// Plays animations on a CustomModel by manipulating element transforms.
	/// Supports blending between animations and playback control.
	/// </summary>
	public class NPCAnimator
	{
		private CustomModel _model;
		private Dictionary<string, NPCAnimationClip> _clips = new Dictionary<string, NPCAnimationClip>();

		private NPCAnimationClip _currentClip;
		private float _currentTime;
		private NPCAnimationState _state = NPCAnimationState.Stopped;
		private float _playbackSpeed = 1.0f;

		/// <summary>Name of the currently playing animation.</summary>
		public string CurrentAnimation => _currentClip?.Name;

		/// <summary>Current playback time in seconds.</summary>
		public float CurrentTime => _currentTime;

		/// <summary>Current playback state.</summary>
		public NPCAnimationState State => _state;

		/// <summary>Playback speed multiplier (1.0 = normal).</summary>
		public float PlaybackSpeed
		{
			get => _playbackSpeed;
			set => _playbackSpeed = value;
		}

		/// <summary>All registered animation names.</summary>
		public IEnumerable<string> AnimationNames => _clips.Keys;

		/// <summary>
		/// Creates a new animator for the specified model.
		/// </summary>
		public NPCAnimator(CustomModel model)
		{
			_model = model;
		}

		/// <summary>
		/// Registers an animation clip with this animator.
		/// </summary>
		public void AddClip(NPCAnimationClip clip)
		{
			_clips[clip.Name] = clip;
		}

		/// <summary>
		/// Registers multiple animation clips.
		/// </summary>
		public void AddClips(params NPCAnimationClip[] clips)
		{
			foreach (var clip in clips)
				AddClip(clip);
		}

		/// <summary>
		/// Loads an animation clip from file and registers it with this animator.
		/// </summary>
		/// <param name="filename">Animation filename without path or extension.</param>
		/// <returns>True if the clip was loaded successfully.</returns>
		public bool LoadClip(string filename)
		{
			var clip = NPCAnimationClip.Load(filename);
			if (clip != null)
			{
				AddClip(clip);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Loads all animation clips from the animations folder.
		/// </summary>
		/// <returns>Number of clips loaded.</returns>
		public int LoadAllClips()
		{
			int count = 0;
			foreach (var name in NPCAnimationClip.GetAvailableAnimations())
			{
				if (LoadClip(name))
					count++;
			}
			return count;
		}

		/// <summary>
		/// Plays the specified animation from the beginning.
		/// </summary>
		public void Play(string animationName)
		{
			if (_clips.TryGetValue(animationName, out var clip))
			{
				_currentClip = clip;
				_currentTime = 0;
				_state = NPCAnimationState.Playing;
			}
			else
			{
				Console.WriteLine($"[NPCAnimator] Animation '{animationName}' not found");
			}
		}

		/// <summary>
		/// Stops the current animation and resets to default pose.
		/// </summary>
		public void Stop()
		{
			_state = NPCAnimationState.Stopped;
			_currentTime = 0;
			ResetToDefaultPose();
		}

		/// <summary>
		/// Pauses the current animation at the current time.
		/// </summary>
		public void Pause()
		{
			if (_state == NPCAnimationState.Playing)
				_state = NPCAnimationState.Paused;
		}

		/// <summary>
		/// Resumes a paused animation.
		/// </summary>
		public void Resume()
		{
			if (_state == NPCAnimationState.Paused)
				_state = NPCAnimationState.Playing;
		}

		/// <summary>
		/// Updates the animation playback. Call this each frame.
		/// </summary>
		public void Update(float deltaTime)
		{
			if (_state != NPCAnimationState.Playing || _currentClip == null)
				return;

			_currentTime += deltaTime * _playbackSpeed;

			// Handle looping or completion
			if (_currentTime >= _currentClip.Duration)
			{
				if (_currentClip.Loop)
				{
					_currentTime %= _currentClip.Duration;
				}
				else
				{
					_currentTime = _currentClip.Duration;
					_state = NPCAnimationState.Stopped;
				}
			}

			// Sample the animation and apply to model
			ApplyAnimation();
		}

		/// <summary>
		/// Seeks to a specific time in the current animation.
		/// </summary>
		public void Seek(float time)
		{
			if (_currentClip == null)
				return;

			_currentTime = Math.Clamp(time, 0, _currentClip.Duration);
			ApplyAnimation();
		}

		/// <summary>
		/// Applies the current animation frame to the model.
		/// </summary>
		private void ApplyAnimation()
		{
			if (_currentClip == null || _model == null)
				return;

			var transforms = _currentClip.Sample(_currentTime);

			foreach (var kvp in transforms)
			{
				string elementName = kvp.Key;
				var (rotation, position) = kvp.Value;

				// Find the mesh with this name
				var mesh = _model.GetMeshByName(elementName);
				if (mesh != null)
				{
					// Apply rotation around the element's rotation origin
					mesh.AnimationRotation = rotation;
					mesh.AnimationPosition = position;
					mesh.UpdateAnimationMatrix();
				}
			}
		}

		/// <summary>
		/// Resets all elements to their default pose (no animation).
		/// </summary>
		public void ResetToDefaultPose()
		{
			if (_model == null)
				return;

			foreach (var mesh in _model.Meshes)
			{
				mesh.AnimationRotation = Vector3.Zero;
				mesh.AnimationPosition = Vector3.Zero;
				mesh.UpdateAnimationMatrix();
			}
		}
	}
}
