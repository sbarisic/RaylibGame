using System.Numerics;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;
using Voxelgine.GUI;

namespace Voxelgine.Engine;

public readonly record struct InventoryClickEventArgs(
	ChunkMap Map,
	Vector3 Start,
	Vector3 Dir,
	float MaxLen
);

/// <summary>
/// Backend-neutral inventory behavior. Concrete FishGfx client items provide
/// UI images and select stable viewmodel asset identifiers.
/// </summary>
public class InventoryItem
{
	protected readonly IFishEngineRunner Eng;
	protected readonly IFishLogging Logging;
	private float lastAutoFireTime;

	private InventoryItem(IFishEngineRunner engine)
	{
		Eng = engine ?? throw new ArgumentNullException(nameof(engine));
		Logging = engine.DI.GetRequiredService<IFishLogging>();
	}

	public InventoryItem(
		IFishEngineRunner engine,
		ClientPlayer parentPlayer,
		string name,
		BlockType blockIcon
	) : this(engine)
	{
		ParentPlayer = parentPlayer;
		Name = name;
		UseBlockIcon = true;
		BlockIcon = blockIcon;
		if (blockIcon != BlockType.None)
		{
			SetViewModelInfo(ViewModelRotationMode.Block);
		}
	}

	public InventoryItem(
		IFishEngineRunner engine,
		ClientPlayer parentPlayer,
		string name,
		IconType icon
	) : this(engine)
	{
		ParentPlayer = parentPlayer;
		Name = name;
		Icon = icon;
	}

	public ClientPlayer ParentPlayer { get; }
	public string Name { get; }
	public bool UseViewmodel { get; private set; }
	public bool UseBlockIcon { get; }
	public BlockType BlockIcon { get; }
	public IconType Icon { get; }
	public ViewModelRotationMode ViewModelRotationMode { get; protected set; }
	public virtual bool SupportsAutoFire => false;
	public virtual float AutoFireRate => 10;
	public int Count { get; set; } = -1;

	public bool CanAutoFire(float currentTime)
	{
		float interval = 1 / AutoFireRate;
		if (currentTime - lastAutoFireTime < interval)
		{
			return false;
		}

		lastAutoFireTime = currentTime;
		return true;
	}

	public virtual InventoryItem SetViewModelInfo(ViewModelRotationMode mode)
	{
		ViewModelRotationMode = mode;
		UseViewmodel = true;
		return this;
	}

	public virtual InventoryItem SetupModel(string modelName)
	{
		UseViewmodel = !string.IsNullOrWhiteSpace(modelName);
		return this;
	}

	public virtual InventoryItem SetupJsonModel(string jsonPath, string texturePath)
	{
		UseViewmodel = !string.IsNullOrWhiteSpace(jsonPath);
		return this;
	}

	public virtual InventoryItem SetCount(int count)
	{
		Count = count;
		return this;
	}

	public virtual string GetInvText()
	{
		return Count == -1 ? null : Count.ToString();
	}

	public virtual void SetupFishUIItemBox(FishUIItemBox itemBox)
	{
	}

	public virtual void OnSelected(ViewModel viewModel)
	{
		Logging.WriteLine($"Selected '{Name}'");
		viewModel.IsActive = true;
		viewModel.SetRotationMode(ViewModelRotationMode);
	}

	public virtual void Tick(ViewModel viewModel, InputMgr input)
	{
		viewModel.SetRotationMode(ViewModelRotationMode);
	}

	public virtual void OnDeselected(ViewModel viewModel)
	{
		Logging.WriteLine($"Deselected '{Name}'");
	}

	public virtual void OnLeftClick(InventoryClickEventArgs args)
	{
		if (!UseViewmodel || (!UseBlockIcon && Icon != IconType.Hammer))
		{
			return;
		}

		ParentPlayer.ViewMdl.ApplyJiggle();
		DestroyBlock(args.Map, args.Start, args.Dir, args.MaxLen);
	}

	public virtual void OnRightClick(InventoryClickEventArgs args)
	{
		if (!UseViewmodel || !UseBlockIcon || Count == 0)
		{
			return;
		}

		if (PlaceBlock(args.Map, args.Start, args.Dir, args.MaxLen, BlockIcon))
		{
			ParentPlayer.ViewMdl.ApplyJiggle();
			if (Count > 0)
			{
				Count--;
			}
		}
	}

	public virtual void DestroyBlock(
		ChunkMap map,
		Vector3 start,
		Vector3 direction,
		float maximumLength
	)
	{
		Utils.Raycast(start, direction, maximumLength, (x, y, z, _) =>
		{
			if (map.GetBlock(x, y, z) == BlockType.None)
			{
				return false;
			}

			ParentPlayer.PlaySound("block_break", new Vector3(x, y, z));
			map.SetBlock(x, y, z, BlockType.None);
			return true;
		});
	}

	public virtual bool TryRaycast(
		ChunkMap map,
		Vector3 start,
		Vector3 direction,
		float maximumLength,
		out Vector3 hitPoint,
		out Vector3 normal
	)
	{
		if (map.TryRaycast(start, direction, maximumLength, out VoxelRaycastHit hit))
		{
			hitPoint = hit.Point;
			normal = hit.Normal;
			return true;
		}

		hitPoint = Vector3.Zero;
		normal = Vector3.Zero;
		return false;
	}

	public virtual bool PlaceBlock(
		ChunkMap map,
		Vector3 start,
		Vector3 direction,
		float maximumLength,
		BlockType blockType
	)
	{
		return Utils.Raycast(start, direction, maximumLength, (x, y, z, face) =>
		{
			if (map.GetBlock(x, y, z) == BlockType.None)
			{
				return false;
			}

			x += (int)face.X;
			y += (int)face.Y;
			z += (int)face.Z;
			ParentPlayer.PlaySound("block_place", new Vector3(x, y, z));
			map.SetBlock(x, y, z, blockType);
			return true;
		});
	}

	public virtual Vector3? GetBlockPlacementPosition(
		ChunkMap map,
		Vector3 start,
		Vector3 direction,
		float maximumLength
	)
	{
		Vector3? result = null;
		Utils.Raycast(start, direction, maximumLength, (x, y, z, face) =>
		{
			if (map.GetBlock(x, y, z) == BlockType.None)
			{
				return false;
			}

			result = new Vector3(x + (int)face.X, y + (int)face.Y, z + (int)face.Z);
			return true;
		});
		return result;
	}

	public virtual bool IsPlaceableBlock()
	{
		return UseViewmodel
			&& UseBlockIcon
			&& BlockIcon != BlockType.None
			&& Count != 0;
	}

	public virtual void OnMiddleClick(InventoryClickEventArgs args)
	{
		Logging.WriteLine($"Middle click '{Name}'");
	}
}
