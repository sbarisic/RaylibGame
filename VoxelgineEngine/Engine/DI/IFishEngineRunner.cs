namespace Voxelgine.Engine.DI;

/// <summary>
/// Backend-neutral runtime services required by authoritative simulation code.
/// Client state navigation lives in the client-only IClientEngineRunner contract.
/// </summary>
public interface IFishEngineRunner
{
	/// <summary>Structured process logger. Never returns null.</summary>
	IFishLogging Logging { get; }

	/// <summary>Animation interpolation service. Never returns null.</summary>
	ILerpManager LerpManager { get; }

	int ChunkDrawCalls { get; set; }

	bool DebugMode { get; set; }

	float TotalTime { get; set; }

}
