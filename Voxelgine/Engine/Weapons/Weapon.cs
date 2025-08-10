using Raylib_cs;

using RaylibGame.States;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Graphics;
using Voxelgine.GUI;


namespace Voxelgine.Engine {
	record struct InventoryClickEventArgs(ChunkMap Map, Vector3 Start, Vector3 Dir, float MaxLen);

	class InventoryItem {
		public Player ParentPlayer;

		public string Name;

		public bool UseViewmodel;
		public bool UseBlockIcon;
		public BlockType BlockIcon;
		public IconType Icon;

		bool HasRenderModel = false;
		Model RenderModel;

		public ViewModelRotationMode ViewModelRotationMode;

		// TODO: Maybe draw that item as disabled when count is 0?
		public int Count = -1; // -1 means infinite, 0 means no items left, >0 item count

		public InventoryItem(Player ParentPlayer, string Name, BlockType BlockIcon) {
			this.ParentPlayer = ParentPlayer;
			this.Name = Name;
			this.HasRenderModel = false;
			this.UseViewmodel = false;

			UseBlockIcon = true;
			this.BlockIcon = BlockIcon;

			if (BlockIcon != BlockType.None) {
				SetViewModelInfo(ViewModelRotationMode.Block);
			}
		}

		public InventoryItem(Player ParentPlayer, string Name, IconType Icon) {
			this.ParentPlayer = ParentPlayer;
			this.Name = Name;
			this.HasRenderModel = false;
			this.UseViewmodel = false;

			UseBlockIcon = false;
			this.Icon = Icon;
		}

		public virtual InventoryItem SetViewModelInfo(ViewModelRotationMode ViewModelRotationMode) {
			this.ViewModelRotationMode = ViewModelRotationMode;
			this.UseViewmodel = true;
			return this;
		}

		public virtual InventoryItem SetupModel(string ModelName) {
			if (ModelName == null) {
				HasRenderModel = false;
				UseViewmodel = false;
				return this;
			}

			this.RenderModel = ResMgr.GetModel(ModelName);
			HasRenderModel = true;
			UseViewmodel = true;

			return this;
		}

		public virtual InventoryItem SetCount(int Count) {
			this.Count = Count;
			return this;
		}

		public virtual string GetInvText() {
			if (Count != -1)
				return Count.ToString();

			return null;
		}

		public virtual void SetupItemBox(GUIItemBox ItmBox) {
			if (UseBlockIcon && BlockIcon != BlockType.None) {
				BlockInfo.GetBlockTexCoords(BlockIcon, new Vector3(0, 1, 0), out Vector2 UVSize, out Vector2 UVPos);
				ItmBox.SetIcon(ResMgr.AtlasTexture, 0.092f, UVPos, UVSize);
			} else if (!UseBlockIcon && Icon != IconType.None) {
				BlockInfo.GetIconTexCoords(Icon, out Texture2D Texture, out Vector2 UVSize, out Vector2 UVPos, out float Scale);
				ItmBox.SetIcon(Texture, Scale, UVPos, UVSize);
			}
		}

		public virtual void OnSelected(ViewModel CurViewModel) {
			Console.WriteLine("Selected '{0}'", Name);

			if (UseViewmodel) {
				if (HasRenderModel) {
					CurViewModel.IsActive = true;
					CurViewModel.SetModel(RenderModel);
					CurViewModel.SetRotationMode(ViewModelRotationMode);
				} else {
					CurViewModel.IsActive = false;
				}

			} else {
				CurViewModel.IsActive = false;
			}
		}

		// Ticks only when active in a player
		public virtual void Tick(ViewModel ViewMdl, InputMgr InMgr) {
			ViewMdl.SetRotationMode(ViewModelRotationMode);

			if (Name == "Gun") {

				if (InMgr.IsInputDown(InputKey.Click_Right)) {
					ViewModelRotationMode = ViewModelRotationMode.GunIronsight;
				} else {
					ViewModelRotationMode = ViewModelRotationMode.Gun;
				}

			}
		}

		public virtual void OnDeselected(ViewModel CurViewModel) {
			Console.WriteLine("Deselected '{0}'", Name);
		}

		public virtual void OnLeftClick(InventoryClickEventArgs E) {
			Console.WriteLine("Left click '{0}'", Name);

			if (UseViewmodel && (UseBlockIcon || (!UseBlockIcon && Icon == IconType.Hammer))) {
				DestroyBlock(E.Map, E.Start, E.Dir, E.MaxLen);
			}
		}

		public virtual void OnRightClick(InventoryClickEventArgs E) {
			Console.WriteLine("Right click '{0}'", Name);

			if (UseViewmodel && UseBlockIcon && (Count > 0 || Count == -1)) {
				Console.WriteLine("Use block: {0}", Name);

				if (PlaceBlock(E.Map, E.Start, E.Dir, E.MaxLen, BlockIcon)) {
					if (Count > 0)
						Count--;
				}
			}
		}

		public virtual void DestroyBlock(ChunkMap Map, Vector3 Start, Vector3 Dir, float MaxLen) {
			Utils.Raycast(Start, Dir, MaxLen, (X, Y, Z, Face) => {
				if (Map.GetBlock(X, Y, Z) != BlockType.None) {
					//Snd.PlayCombo("block_break", Start, Dir, new Vector3(X, Y, Z));
					Map.SetBlock(X, Y, Z, BlockType.None);
					return true;
				}
				return false;
			});
		}

		public virtual bool PlaceBlock(ChunkMap Map, Vector3 Start, Vector3 Dir, float MaxLen, BlockType BlockType) {
			return Utils.Raycast(Start, Dir, MaxLen, (X, Y, Z, Face) => {
				if (Map.GetBlock(X, Y, Z) != BlockType.None) {
					X += (int)Face.X;
					Y += (int)Face.Y;
					Z += (int)Face.Z;
					//Snd.PlayCombo("block_place", Start, Dir, new Vector3(X, Y, Z));
					Map.SetBlock(X, Y, Z, BlockType);
					return true;
				}
				return false;
			});
		}

		public virtual void OnMiddleClick(InventoryClickEventArgs E) {
			Console.WriteLine("Middle click '{0}'", Name);
		}
	}

	class Weapon : InventoryItem {
		public Weapon(Player ParentPlayer, string Name, BlockType BlockIcon) : base(ParentPlayer, Name, BlockIcon) {
		}

		public Weapon(Player ParentPlayer, BlockType BlockIcon) : this(ParentPlayer, BlockIcon.ToString(), BlockIcon) {
		}

		public Weapon(Player ParentPlayer, string Name, IconType Icon) : base(ParentPlayer, Name, Icon) {
		}
	}
}
