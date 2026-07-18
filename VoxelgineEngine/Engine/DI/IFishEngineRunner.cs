namespace Voxelgine.Engine.DI;

/// <summary>
/// Backend-neutral runtime services required by authoritative simulation code.
/// Client state navigation lives in the client-only IClientEngineRunner contract.
/// </summary>
public interface IFishEngineRunner
{
	FishDI DI { get; set; }

	int ChunkDrawCalls { get; set; }

	bool DebugMode { get; set; }

	float TotalTime { get; set; }

	void Init();
}
