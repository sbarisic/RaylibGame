#if WINDOWS
using System.Numerics;
using Voxelgine.Audio;
using Voxelgine.FishGfxClient.Voxels;

namespace Voxelgine.FishGfxClient.Audio;

/// <summary>
/// Adapts FishGfx voxel-environment samples to the process-level audio system.
/// It owns only world-scoped ambience voices; the audio device and voxel scene
/// remain owned by their respective client services.
/// </summary>
public sealed class FishGfxAmbienceSession : IDisposable
{
	private readonly FishGfxVoxelScene scene;
	private readonly AmbienceController controller;
	private bool disposed;

	public FishGfxAmbienceSession(
		IAudioSystem audio,
		FishGfxVoxelScene scene,
		AmbienceControllerOptions options = null)
	{
		ArgumentNullException.ThrowIfNull(audio);
		this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
		controller = new AmbienceController(audio, options);
	}

	public void Update(float deltaSeconds, Vector3 listenerPosition, float daylight)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		VoxelEnvironmentSample environment = scene.SampleEnvironment(listenerPosition);
		AmbienceSample sample = new(
			environment.OutdoorExposure,
			environment.DirectSkylight,
			Math.Clamp(daylight, 0, 1),
			environment.IsUnderwater,
			listenerPosition,
			scene.CampfirePositions);
		controller.Update(deltaSeconds, sample);
	}

	public void ResetWorld()
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		controller.ResetWorld();
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		controller.Dispose();
		disposed = true;
	}
}
#endif
