using System.Numerics;

namespace Voxelgine.Engine.Audio;

public readonly record struct GameAudioEvent(
	string CueId,
	Vector3 Position,
	Vector3 Velocity,
	int SourcePlayerId);

public interface IGameAudioSink
{
	void Emit(in GameAudioEvent audioEvent);
}

public sealed class NullGameAudioSink : IGameAudioSink
{
	public static NullGameAudioSink Instance { get; } = new();

	private NullGameAudioSink()
	{
	}

	public void Emit(in GameAudioEvent audioEvent)
	{
	}
}
