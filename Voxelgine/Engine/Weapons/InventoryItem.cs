using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;
using Voxelgine.GUI;

namespace Voxelgine.Engine
{
	public record struct InventoryClickEventArgs(ChunkMap Map, Vector3 Start, Vector3 Dir, float MaxLen);

	public class InventoryItem
	{
		protected IFishEngineRunner Eng;
		protected IFishLogging Logging;

		public Player ParentPlayer;

		public string Name;

		public bool UseViewmodel;
		public bool UseBlockIcon;
		public BlockType BlockIcon;
		public IconType Icon;

		bool HasRenderModel = false;
		Model RenderModel;

		bool HasWeaponJsonModel = false;
		CustomModel WeaponJsonModel;

		public ViewModelRotationMode ViewModelRotationMode;

		/// <summary>
		/// When true, OnLeftClick fires continuously while mouse button is held.
		/// </summary>
		public virtual bool SupportsAutoFire => false;

		/// <summary>
		/// Shots per second for automatic fire. Only used when SupportsAutoFire is true.
		/// </summary>
		public virtual float AutoFireRate => 10f;

		/// <summary>Timestamp of last auto-fire shot.</summary>
		private float _lastAutoFireTime;

		/// <summary>
		/// Checks if enough time has passed to fire again based on AutoFireRate.
		/// Updates internal timing when returning true.
		/// </summary>
		public bool CanAutoFire(float currentTime)
		{
			float interval = 1f / AutoFireRate;
			if (currentTime - _lastAutoFireTime >= interval)
			{
				_lastAutoFireTime = currentTime;
				return true;
			}
			return false;
		}

		// TODO: Maybe draw that item as disabled when count is 0?
		public int Count = -1; // -1 means infinite, 0 means no items left, >0 item count

		InventoryItem(IFishEngineRunner Eng)
		{
			this.Eng = Eng;
			this.Logging = Eng.DI.GetRequiredService<IFishLogging>();
		}

		public InventoryItem(IFishEngineRunner Eng, Player ParentPlayer, string Name, BlockType BlockIcon) : this(Eng)
		{
			this.ParentPlayer = ParentPlayer;
			this.Name = Name;
			this.HasRenderModel = false;
			this.UseViewmodel = false;

			UseBlockIcon = true;
			this.BlockIcon = BlockIcon;

			if (BlockIcon != BlockType.None)
			{
				SetViewModelInfo(ViewModelRotationMode.Block);
			}
		}

		public InventoryItem(IFishEngineRunner Eng, Player ParentPlayer, string Name, IconType Icon) : this(Eng)
		{
			this.ParentPlayer = ParentPlayer;
			this.Name = Name;
			this.HasRenderModel = false;
			this.UseViewmodel = false;

			UseBlockIcon = false;
			this.Icon = Icon;
		}

		public virtual InventoryItem SetViewModelInfo(ViewModelRotationMode ViewModelRotationMode)
		{
			this.ViewModelRotationMode = ViewModelRotationMode;
			this.UseViewmodel = true;
			return this;
		}

		public virtual InventoryItem SetupModel(string ModelName)
		{
			if (ModelName == null)
			{
				HasRenderModel = false;
				UseViewmodel = false;
				return this;
			}

			this.RenderModel = ResMgr.GetModel(ModelName);
			HasRenderModel = true;
			UseViewmodel = true;

			return this;
		}

		public virtual InventoryItem SetupJsonModel(string jsonPath, string texturePath)
		{
			try
			{
				MinecraftModel jsonModel = ResMgr.GetJsonModel(jsonPath);
				WeaponJsonModel = MeshGenerator.Generate(jsonModel);
				WeaponJsonModel.SetTexture(ResMgr.GetModelTexture(texturePath));
				HasWeaponJsonModel = true;
				UseViewmodel = true;
				Logging.WriteLine($"SetupJsonModel: Loaded '{jsonPath}' with {WeaponJsonModel.Meshes.Count} meshes, texture='{texturePath}'");
			}
			catch (Exception ex)
			{
				Logging.WriteLine($"Failed to load JSON model '{jsonPath}': {ex.Message}");
				HasWeaponJsonModel = false;
			}
			return this;
		}

		public virtual InventoryItem SetCount(int Count)
		{
			this.Count = Count;
			return this;
		}

		public virtual string GetInvText()
		{
			if (Count != -1)
				return Count.ToString();

			return null;
		}

		/// <summary>
		/// Sets up a FishUI item box with the correct icon from atlas textures.
		/// </summary>
		public virtual void SetupFishUIItemBox(FishUIItemBox itmBox)
		{
			if (UseBlockIcon && BlockIcon != BlockType.None)
			{
				BlockInfo.GetBlockTexCoords(BlockIcon, new Vector3(0, 1, 0), out Vector2 UVSize, out Vector2 UVPos);
				itmBox.SetIcon(ResMgr.AtlasTexture, 0.092f, UVPos, UVSize);
			}
			else if (!UseBlockIcon && Icon != IconType.None)
			{
				BlockInfo.GetIconTexCoords(Icon, out Texture2D Texture, out Vector2 UVSize, out Vector2 UVPos, out float Scale);
				itmBox.SetIcon(Texture, Scale, UVPos, UVSize);
			}
		}

		public virtual void OnSelected(ViewModel CurViewModel)
		{
			Logging.WriteLine($"Selected '{Name}' (HasWeaponJsonModel={HasWeaponJsonModel}, UseViewmodel={UseViewmodel}, meshCount={WeaponJsonModel?.Meshes?.Count ?? -1})");

			CurViewModel.IsActive = true;

			if (HasWeaponJsonModel)
			{
				CurViewModel.SetWeaponModel(WeaponJsonModel);
				CurViewModel.SetRotationMode(ViewModelRotationMode);
			}
			else
			{
				CurViewModel.ClearWeaponModel();
			}
		}

		// Ticks only when active in a player
		public virtual void Tick(ViewModel ViewMdl, InputMgr InMgr)
		{
			ViewMdl.SetRotationMode(ViewModelRotationMode);
		}

		public virtual void OnDeselected(ViewModel CurViewModel)
		{
			Logging.WriteLine($"Deselected '{Name}'");
		}

		public virtual void OnLeftClick(InventoryClickEventArgs E)
		{
			Logging.WriteLine($"Left click '{Name}'");

			if (UseViewmodel && (UseBlockIcon || (!UseBlockIcon && Icon == IconType.Hammer)))
			{
				if (!HasWeaponJsonModel)
					ParentPlayer.ViewMdl.ApplyJiggle();
				DestroyBlock(E.Map, E.Start, E.Dir, E.MaxLen);
			}
		}

		public virtual void OnRightClick(InventoryClickEventArgs E)
		{
			Logging.WriteLine($"Right click '{Name}'");

			if (UseViewmodel && UseBlockIcon && (Count > 0 || Count == -1))
			{
				Logging.WriteLine($"Use block: {Name}");

				if (PlaceBlock(E.Map, E.Start, E.Dir, E.MaxLen, BlockIcon))
				{
					if (!HasWeaponJsonModel)
						ParentPlayer.ViewMdl.ApplyJiggle();
					if (Count > 0)
						Count--;
				}
			}
		}

		public virtual void DestroyBlock(ChunkMap Map, Vector3 Start, Vector3 Dir, float MaxLen)
		{
			Utils.Raycast(Start, Dir, MaxLen, (X, Y, Z, Face) =>
			{
				if (Map.GetBlock(X, Y, Z) != BlockType.None)
				{
					ParentPlayer.PlaySound("block_break", new Vector3(X, Y, Z));
					Map.SetBlock(X, Y, Z, BlockType.None);
					return true;
				}
				return false;
			});
		}

		public virtual Vector3 Raycast(ChunkMap Map, Vector3 Start, Vector3 Dir, float MaxLen, out Vector3 Normal)
		{
			Ray RR = new Ray(Start, Dir);
			RayCollision COl = Map.RaycastRay(RR, MaxLen);
			Normal = Vector3.Zero;

			if (COl.Hit)
			{
				Normal = COl.Normal;
				return COl.Point;
			}

			return Vector3.Zero;
		}

		public virtual bool PlaceBlock(ChunkMap Map, Vector3 Start, Vector3 Dir, float MaxLen, BlockType BlockType)
		{
			return Utils.Raycast(Start, Dir, MaxLen, (X, Y, Z, Face) =>
			{
				if (Map.GetBlock(X, Y, Z) != BlockType.None)
				{
					X += (int)Face.X;
					Y += (int)Face.Y;
					Z += (int)Face.Z;
					ParentPlayer.PlaySound("block_place", new Vector3(X, Y, Z));
					Map.SetBlock(X, Y, Z, BlockType);
					return true;
				}
				return false;
			});
		}

		/// <summary>
		/// Gets the position where a block would be placed without actually placing it.
		/// Returns null if no valid placement position is found.
		/// </summary>
		public virtual Vector3? GetBlockPlacementPosition(ChunkMap Map, Vector3 Start, Vector3 Dir, float MaxLen)
		{
			Vector3? result = null;
			Utils.Raycast(Start, Dir, MaxLen, (X, Y, Z, Face) =>
			{
				if (Map.GetBlock(X, Y, Z) != BlockType.None)
				{
					result = new Vector3(X + (int)Face.X, Y + (int)Face.Y, Z + (int)Face.Z);
					return true;
				}
				return false;
			});
			return result;
		}

		/// <summary>
		/// Returns true if this item is a placeable block that should show a placement preview.
		/// </summary>
		public virtual bool IsPlaceableBlock()
		{
			return UseViewmodel && UseBlockIcon && BlockIcon != BlockType.None && (Count > 0 || Count == -1);
		}

		public virtual void OnMiddleClick(InventoryClickEventArgs E)
		{
			Logging.WriteLine($"Middle click '{Name}'");
		}
	}

}
