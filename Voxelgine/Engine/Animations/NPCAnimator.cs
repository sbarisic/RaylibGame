using System;
using System.Collections.Generic;
using System.Linq;
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
	/// Represents a single animation layer that can play independently.
	/// Multiple layers can be combined for layered animations (e.g., walk + attack).
	/// </summary>
	public class AnimationLayer
	{
		/// <summary>Name of this layer (e.g., "base", "upper", "action").</summary>
		public string Name { get; }
		/// <summary>Currently playing clip on this layer.</summary>
		public NPCAnimationClip Clip { get; set; }
		/// <summary>Current playback time in seconds.</summary>
		public float Time { get; set; }
		/// <summary>Playback state of this layer.</summary>
		public NPCAnimationState State { get; set; } = NPCAnimationState.Stopped;
		/// <summary>Blend weight for this layer (0-1). Used when combining with other layers.</summary>
		public float Weight { get; set; } = 1.0f;
		/// <summary>Playback speed multiplier for this layer.</summary>
		public float PlaybackSpeed { get; set; } = 1.0f;

		public AnimationLayer(string name)
		{
			Name = name;
		}
	}

	/// <summary>
	/// Plays animations on a CustomModel by manipulating element transforms.
	/// Supports layered playback for combining multiple animations simultaneously.
	/// </summary>
	public class NPCAnimator
	{
		private CustomModel _model;
		private Dictionary<string, NPCAnimationClip> _clips = new Dictionary<string, NPCAnimationClip>();

		// Legacy single-animation state (maps to "base" layer for compatibility)
		private NPCAnimationClip _currentClip;
		private float _currentTime;
		private NPCAnimationState _state = NPCAnimationState.Stopped;
		private float _playbackSpeed = 1.0f;

		// Layered animation support
		private Dictionary<string, AnimationLayer> _layers = new Dictionary<string, AnimationLayer>();
		private const string BaseLayerName = "base";

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

		#region Layered Animation API

		/// <summary>
		/// Gets or creates an animation layer with the specified name.
		/// </summary>
		public AnimationLayer GetOrCreateLayer(string layerName)
		{
			if (!_layers.TryGetValue(layerName, out var layer))
			{
				layer = new AnimationLayer(layerName);
				_layers[layerName] = layer;
			}
			return layer;
		}

		/// <summary>
		/// Gets an existing animation layer by name, or null if not found.
		/// </summary>
		public AnimationLayer GetLayer(string layerName)
		{
			return _layers.TryGetValue(layerName, out var layer) ? layer : null;
		}

		/// <summary>
		/// Plays an animation on the specified layer. Creates the layer if it doesn't exist.
		/// </summary>
		/// <param name="layerName">Name of the layer to play on.</param>
		/// <param name="animationName">Name of the animation clip to play.</param>
		/// <param name="weight">Blend weight for this layer (0-1).</param>
		public void PlayOnLayer(string layerName, string animationName, float weight = 1.0f)
		{
			if (!_clips.TryGetValue(animationName, out var clip))
			{
				Console.WriteLine($"[NPCAnimator] Animation '{animationName}' not found");
				return;
			}

			var layer = GetOrCreateLayer(layerName);
			layer.Clip = clip;
			layer.Time = 0;
			layer.State = NPCAnimationState.Playing;
			layer.Weight = weight;
		}

		/// <summary>
		/// Stops playback on the specified layer.
		/// </summary>
		public void StopLayer(string layerName)
		{
			if (_layers.TryGetValue(layerName, out var layer))
			{
				layer.State = NPCAnimationState.Stopped;
				layer.Time = 0;
				layer.Clip = null;
			}
		}

		/// <summary>
		/// Pauses playback on the specified layer.
		/// </summary>
		public void PauseLayer(string layerName)
		{
			if (_layers.TryGetValue(layerName, out var layer) && layer.State == NPCAnimationState.Playing)
			{
				layer.State = NPCAnimationState.Paused;
			}
		}

		/// <summary>
		/// Resumes playback on a paused layer.
		/// </summary>
		public void ResumeLayer(string layerName)
		{
			if (_layers.TryGetValue(layerName, out var layer) && layer.State == NPCAnimationState.Paused)
			{
				layer.State = NPCAnimationState.Playing;
			}
		}

		/// <summary>
		/// Sets the blend weight for a layer.
		/// </summary>
		public void SetLayerWeight(string layerName, float weight)
		{
			if (_layers.TryGetValue(layerName, out var layer))
			{
				layer.Weight = Math.Clamp(weight, 0f, 1f);
			}
		}

		/// <summary>
		/// Gets the names of all active layers (layers with playing animations).
		/// </summary>
		public IEnumerable<string> ActiveLayerNames => _layers.Where(l => l.Value.State == NPCAnimationState.Playing).Select(l => l.Key);

		/// <summary>
		/// Gets all layer names.
		/// </summary>
		public IEnumerable<string> LayerNames => _layers.Keys;

		/// <summary>
		/// Removes a layer entirely.
		/// </summary>
		public void RemoveLayer(string layerName)
		{
			_layers.Remove(layerName);
		}

		/// <summary>
		/// Stops all layers and clears them.
		/// </summary>
		public void StopAllLayers()
		{
			foreach (var layer in _layers.Values)
			{
				layer.State = NPCAnimationState.Stopped;
				layer.Time = 0;
				layer.Clip = null;
			}
		}

		#endregion

		/// <summary>
		/// Updates the animation playback. Call this each frame.
		/// </summary>
		public void Update(float deltaTime)
		{
			// Update legacy single-animation state
			if (_state == NPCAnimationState.Playing && _currentClip != null)
			{
				_currentTime += deltaTime * _playbackSpeed;

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
			}

			// Update all animation layers
			foreach (var layer in _layers.Values)
			{
				if (layer.State != NPCAnimationState.Playing || layer.Clip == null)
					continue;

				layer.Time += deltaTime * layer.PlaybackSpeed;

				if (layer.Time >= layer.Clip.Duration)
				{
					if (layer.Clip.Loop)
					{
						layer.Time %= layer.Clip.Duration;
					}
					else
					{
						layer.Time = layer.Clip.Duration;
						layer.State = NPCAnimationState.Stopped;
					}
				}
			}

			// Apply combined animations to model
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
		/// Combines transforms from the legacy single-animation and all active layers additively.
		/// </summary>
		private void ApplyAnimation()
		{
			if (_model == null)
				return;

			// Collect combined transforms from all sources
			var combinedTransforms = new Dictionary<string, (Vector3 Rotation, Vector3 Position)>();

			// Sample legacy single-animation state (playing or stopped-at-end for non-looping)
			if (_currentClip != null && (_state == NPCAnimationState.Playing || 
				(_state == NPCAnimationState.Stopped && !_currentClip.Loop && _currentTime > 0)))
			{
				var transforms = _currentClip.Sample(_currentTime);
				foreach (var kvp in transforms)
				{
					combinedTransforms[kvp.Key] = kvp.Value;
				}
			}

			// Sample and additively blend all layers (including stopped non-looping animations holding last frame)
			foreach (var layer in _layers.Values)
			{
				if (layer.Clip == null || layer.Weight <= 0)
					continue;

				// Include playing layers, and stopped non-looping layers that finished (holding last frame)
				bool isPlaying = layer.State == NPCAnimationState.Playing;
				bool isHoldingLastFrame = layer.State == NPCAnimationState.Stopped && !layer.Clip.Loop && layer.Time > 0;

				if (!isPlaying && !isHoldingLastFrame)
					continue;

				var layerTransforms = layer.Clip.Sample(layer.Time);
				foreach (var kvp in layerTransforms)
				{
					string elementName = kvp.Key;
					var (rotation, position) = kvp.Value;

					// Apply weight to the layer's contribution
					rotation *= layer.Weight;
					position *= layer.Weight;

					// Additively blend with existing transforms
					if (combinedTransforms.TryGetValue(elementName, out var existing))
					{
						combinedTransforms[elementName] = (existing.Rotation + rotation, existing.Position + position);
					}
					else
					{
						combinedTransforms[elementName] = (rotation, position);
					}
				}
			}

			// Apply combined transforms to model meshes
			// First reset meshes not affected by any animation
			foreach (var mesh in _model.Meshes)
			{
				if (!combinedTransforms.ContainsKey(mesh.Name))
				{
					mesh.AnimationRotation = Vector3.Zero;
					mesh.AnimationPosition = Vector3.Zero;
					mesh.UpdateAnimationMatrix();
				}
			}

			// Apply combined transforms
			foreach (var kvp in combinedTransforms)
			{
				string elementName = kvp.Key;
				var (rotation, position) = kvp.Value;

				var mesh = _model.GetMeshByName(elementName);
				if (mesh != null)
				{
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
