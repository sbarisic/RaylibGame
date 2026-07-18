#if WINDOWS
namespace Voxelgine.FishGfxClient.Assets;

public readonly struct AssetHandle<T>
	where T : class
{
	private readonly AssetSlot<T> slot;

	internal AssetHandle(AssetSlot<T> slot)
	{
		this.slot = slot ?? throw new ArgumentNullException(nameof(slot));
	}

	public string Id => slot.Id;

	public T Value => slot.Value;

	public bool IsValid => slot is not null && slot.Value is not null;
}

internal sealed class AssetSlot<T> : IAssetSlot
	where T : class
{
	private T value;

	internal AssetSlot(string id, T value, Func<T> reload)
	{
		Id = id;
		this.value = value;
		Reload = reload;
	}

	public string Id { get; }

	public T Value => Volatile.Read(ref value);

	public Func<T> Reload { get; }

	public void ReloadAndSwap()
	{
		T replacement = Reload();
		T previous = Interlocked.Exchange(ref value, replacement);
		(previous as IDisposable)?.Dispose();
	}

	public void Dispose()
	{
		T previous = Interlocked.Exchange(ref value, null);
		(previous as IDisposable)?.Dispose();
	}
}

internal interface IAssetSlot : IDisposable
{
	string Id { get; }

	void ReloadAndSwap();
}
#endif
