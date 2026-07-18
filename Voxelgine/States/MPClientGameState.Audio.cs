using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.Audio;

#if WINDOWS
using Voxelgine.Audio;
using Voxelgine.FishGfxClient;
using Voxelgine.FishGfxClient.Audio;
#endif

namespace Voxelgine.States;

public unsafe partial class MPClientGameState
{
	private IGameAudioSink CreateAudioSink()
	{
		return new MiniaudioGameAudioSink(
			Eng.DI.GetRequiredService<IAudioSystem>()
		);
	}

	private void UpdateAudioListener()
	{
		if (_simulation?.LocalPlayer is null || _snd is null)
		{
			return;
		}

		Player player = _simulation.LocalPlayer;
		Vector3 position = player.RenderCam.Position;
		Vector3 forward = player.RenderCam.Target - position;
		if (forward.LengthSquared() < 0.000001f)
		{
			forward = player.GetForward();
		}

		if (forward.LengthSquared() >= 0.000001f)
		{
			forward = Vector3.Normalize(forward);
		}

		if (_snd is MiniaudioGameAudioSink miniaudio)
		{
			miniaudio.SetListener(
				position,
				forward,
				Vector3.UnitY,
				player.GetVelocity()
			);
		}
	}

	public override void BeginFrame(in FrameTiming timing)
	{
		PrepareFishGfxFrame(timing);
		UpdateAudioListener();
	}
}
