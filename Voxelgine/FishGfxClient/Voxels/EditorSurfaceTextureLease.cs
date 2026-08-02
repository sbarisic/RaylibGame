#if WINDOWS
namespace Voxelgine.FishGfxClient.Voxels;

internal sealed class EditorSurfaceTextureLease : IDisposable
{
	private FishGfxVoxelScene scene;
	private readonly long leaseId;
	private long materialGeneration;
	private long visualizationGeneration;

	internal EditorSurfaceTextureLease(FishGfxVoxelScene scene, long leaseId)
	{
		this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
		this.leaseId = leaseId;
	}

	internal long QueueMaterialReplacement(OwnedVoxelSurfaceTextureSet textures)
	{
		FishGfxVoxelScene current = scene ?? throw new ObjectDisposedException(nameof(EditorSurfaceTextureLease));
		long next = checked(++materialGeneration);
		current.QueueEditorSurfaceTextures(leaseId, EditorSurfaceTextureKind.Material, next, textures);
		return next;
	}

	internal long QueueVisualizationReplacement(OwnedVoxelSurfaceTextureSet textures)
	{
		FishGfxVoxelScene current = scene ?? throw new ObjectDisposedException(nameof(EditorSurfaceTextureLease));
		long next = checked(++visualizationGeneration);
		current.QueueEditorSurfaceTextures(leaseId, EditorSurfaceTextureKind.Visualization, next, textures);
		return next;
	}

	internal void UseVisualization(bool enabled)
	{
		FishGfxVoxelScene current = scene ?? throw new ObjectDisposedException(nameof(EditorSurfaceTextureLease));
		current.SetEditorTexturePresentation(leaseId, enabled);
	}

	internal void Clear()
	{
		FishGfxVoxelScene current = scene ?? throw new ObjectDisposedException(nameof(EditorSurfaceTextureLease));
		current.ClearEditorSurfaceTextures(leaseId, releaseLease: false);
	}

	public void Dispose()
	{
		FishGfxVoxelScene current = Interlocked.Exchange(ref scene, null);
		current?.ClearEditorSurfaceTextures(leaseId, releaseLease: true);
	}
}

internal sealed record PendingEditorSurfaceTextures(
	long LeaseId,
	EditorSurfaceTextureKind Kind,
	long Generation,
	OwnedVoxelSurfaceTextureSet Textures);

internal enum EditorSurfaceTextureKind
{
	Material,
	Visualization,
}

internal sealed record RetiredEditorSurfaceTextures(
	long DisposeAfterFrame,
	OwnedVoxelSurfaceTextureSet Textures);
#endif
