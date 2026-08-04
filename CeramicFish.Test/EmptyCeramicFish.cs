using Voxelgine.WorldGeneration;

namespace CeramicFish.TestHarness;

/// <summary>Compilation scaffold for the CeramicFish implementation that will be added later.</summary>
internal sealed class EmptyCeramicFish : ICeramicFish
{
	public CeramicValidationResult ValidateDefinition(CeramicFishDefinition definition) =>
		throw new NotImplementedException();

	public CeramicValidationResult ValidateRequest(
		CeramicGenerationRequest request,
		CeramicFishDefinition definition) => throw new NotImplementedException();

	public bool CanSocketsBeAdjacent(CeramicSocket first, CeramicSocket second) =>
		throw new NotImplementedException();

	public bool DoSocketsCreateConnection(CeramicSocket first, CeramicSocket second) =>
		throw new NotImplementedException();

	public CeramicGenerationResult Generate(
		CeramicGenerationRequest request,
		CeramicFishDefinition definition,
		CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
