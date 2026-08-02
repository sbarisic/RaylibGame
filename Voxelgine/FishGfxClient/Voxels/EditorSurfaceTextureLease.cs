#if WINDOWS
namespace Voxelgine.FishGfxClient.Voxels;

internal sealed class EditorSurfaceTextureLease : IDisposable
{
	private FishGfxVoxelScene scene;
	private readonly long leaseId;
	private long generation;

	internal EditorSurfaceTextureLease(FishGfxVoxelScene scene, long leaseId)
	{
		this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
		this.leaseId = leaseId;
	}

	internal long QueueReplacement(OwnedVoxelSurfaceTextureSet textures)
	{
		FishGfxVoxelScene current = scene ?? throw new ObjectDisposedException(nameof(EditorSurfaceTextureLease));
		long next = checked(++generation);
		current.QueueEditorSurfaceTextures(leaseId, next, textures);
		return next;
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
	long Generation,
	OwnedVoxelSurfaceTextureSet Textures);

internal sealed record RetiredEditorSurfaceTextures(
	long DisposeAfterFrame,
	OwnedVoxelSurfaceTextureSet Textures);
#endif
