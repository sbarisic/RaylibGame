#if WINDOWS
using System.Numerics;
using Voxelgine.Audio;
using Voxelgine.Engine.Audio;

namespace Voxelgine.FishGfxClient.Audio;

public sealed class MiniaudioGameAudioSink : IGameAudioSink
{
	private readonly IAudioSystem audioSystem;

	public MiniaudioGameAudioSink(IAudioSystem audioSystem)
	{
		this.audioSystem = audioSystem
			?? throw new ArgumentNullException(nameof(audioSystem));
	}

	public void Emit(in GameAudioEvent audioEvent)
	{
		audioSystem.PlayCue(
			audioEvent.CueId,
			new AudioEmitter(audioEvent.Position, audioEvent.Velocity)
		);
	}

	public void SetListener(
		Vector3 position,
		Vector3 forward,
		Vector3 up,
		Vector3 velocity)
	{
		audioSystem.SetListener(new AudioListener(
			position,
			forward,
			up,
			velocity
		));
	}
}
#endif
