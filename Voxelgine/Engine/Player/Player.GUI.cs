using System;
using System.Numerics;
using Voxelgine.Graphics;
using Voxelgine.GUI;

namespace Voxelgine.Engine
{
	public unsafe partial class Player
	{
		FishUIItemBox Box_Health;
		FishUIInfoLabel InfoLbl;
		FishUIInventory Inventory;

		InventoryItem ActiveSelection;

		/// <summary>
		/// Gets the currently selected inventory item, or null if none selected.
		/// </summary>
		public InventoryItem GetActiveItem() => ActiveSelection;

		public int GetSelectedInventoryIndex() => Inventory?.GetSelectedIndex() ?? 0;
		public void SetSelectedInventoryIndex(int index) => Inventory?.SetSelectedIndex(index);

		/// <summary>
		/// Gets the inventory item at the given slot index, or null if the slot is empty or out of range.
		/// </summary>
		public InventoryItem GetInventoryItem(int slot)
		{
			return Inventory?.GetItem(slot)?.Item;
		}

		public void RecalcGUI(IGameWindow Window)
		{
			// FishUI handles positioning via control properties
		}

		public void InitGUI(IGameWindow Window, FishUIManager gui)
		{
			// Health box
			Box_Health = new FishUIItemBox
			{
				Position = new Vector2(100, Window.Height - 100),
				Size = new Vector2(64, 64)
			};
			Box_Health.LoadTextures(gui.UI);
			Box_Health.SetIcon(gui.UI, "data/textures/items/heart_full.png", 3);
			Box_Health.Text = "100";
			gui.AddControl(Box_Health);

			// Debug info label
			InfoLbl = new FishUIInfoLabel
			{
				Position = new Vector2(20, 40),
				Size = new Vector2(300, 250),
				Visible = Eng.DebugMode
			};
			InfoLbl.WriteLine("Hello World!");
			gui.AddControl(InfoLbl);

			// Inventory
			Inventory = new FishUIInventory(gui.UI, 10);
			Inventory.Position = new Vector2(
				(Window.Width / 2f) - (Inventory.Size.X / 2f),
				Window.Height - 80
			);
			gui.AddControl(Inventory);

			Inventory.OnActiveSelectionChanged = (E) =>
			{
				if (ActiveSelection != null)
				{
					ActiveSelection.OnDeselected(ViewMdl);
					ActiveSelection = null;
				}

				ActiveSelection = E.ItemBox.Item;

				if (ActiveSelection != null)
				{
					ActiveSelection.OnSelected(ViewMdl);
				}
			};

			int ItmIdx = 0;
			SetInvItem(gui.UI, Inventory, ItmIdx++, new WeaponGun(Eng, this, "Gun"));
			SetInvItem(gui.UI, Inventory, ItmIdx++, new WeaponPicker(Eng, this, "Hammer"));
			SetInvItem(gui.UI, Inventory, ItmIdx++, new Weapon(Eng, this, BlockType.Dirt).SetCount(64));
			SetInvItem(gui.UI, Inventory, ItmIdx++, new Weapon(Eng, this, BlockType.Stone).SetCount(64));
			SetInvItem(gui.UI, Inventory, ItmIdx++, new Weapon(Eng, this, BlockType.Plank).SetCount(64));
			SetInvItem(gui.UI, Inventory, ItmIdx++, new Weapon(Eng, this, BlockType.Bricks).SetCount(64));
			SetInvItem(gui.UI, Inventory, ItmIdx++, new Weapon(Eng, this, BlockType.StoneBrick).SetCount(64));
			SetInvItem(gui.UI, Inventory, ItmIdx++, new Weapon(Eng, this, BlockType.Glowstone).SetCount(64));
			SetInvItem(gui.UI, Inventory, ItmIdx++, new Weapon(Eng, this, BlockType.Glass).SetCount(64));
			SetInvItem(gui.UI, Inventory, ItmIdx++, new Weapon(Eng, this, BlockType.Water).SetCount(64));

			Inventory.SetSelectedIndex(0);
			Inventory.SetSelectedIndex(1);
			Inventory.SetSelectedIndex(0);
		}

		public void UpdateGUI()
		{
			InfoLbl.Visible = false;

			if (Eng.DebugMode)
			{
				InfoLbl.Visible = true;
				InfoLbl.Clear();
				InfoLbl.WriteLine("Pos: {0:0.00}, {1:0.00}, {2:0.00}", MathF.Round(Position.X, 2), MathF.Round(Position.Y, 2), MathF.Round(Position.Z, 2));
				InfoLbl.WriteLine("Vel: {0:0.000}", MathF.Round(GetVelocity().Length(), 3));
				InfoLbl.WriteLine("NoClip (C): {0}", NoClip ? "ON" : "OFF");
				InfoLbl.WriteLine("OnGround: {0}", GetWasLastLegsOnFloor() ? "YES" : "NO");
				InfoLbl.WriteLine("ChunkDraws: {0}", Eng.ChunkDrawCalls.ToString());

				int OnScreen = 0, Drawn = 0, Max = 0;
				var particle = Eng.MultiplayerGameState?.Particle;
				particle?.GetStats(out OnScreen, out Drawn, out Max);
				InfoLbl.WriteLine("Particles: {0}/{1}/{2}", OnScreen, Drawn, Max);
			}
		}

		void SetInvItem(global::FishUI.FishUI ui, FishUIInventory Inventory, int Idx, InventoryItem InvItem)
		{
			FishUIItemBox Itm = Inventory.GetItem(Idx);
			if (Itm == null) return;

			Itm.UpdateTextFromItem = true;
			Itm.Item = InvItem;

			// Set up icon from item using atlas coordinates
			InvItem.SetupFishUIItemBox(Itm);
		}

		public void TickGUI(InputMgr InMgr, ChunkMap Map)
		{
			// Check for auto-fire: use IsInputDown if item supports it, otherwise IsInputPressed
			bool LeftPressed = InMgr.IsInputPressed(InputKey.Click_Left);
			bool LeftHeld = InMgr.IsInputDown(InputKey.Click_Left);

			// For auto-fire weapons, check both held state and fire rate cooldown
			bool Left;
			if (ActiveSelection != null && ActiveSelection.SupportsAutoFire)
			{
				float currentTime = (float)Raylib_cs.Raylib.GetTime();
				Left = LeftHeld && ActiveSelection.CanAutoFire(currentTime);
			}
			else
			{
				Left = LeftPressed;
			}

			bool Right = InMgr.IsInputPressed(InputKey.Click_Right);
			bool Middle = InMgr.IsInputPressed(InputKey.Click_Middle);
			float Wheel = InMgr.GetMouseWheel();
			const float MaxLen = 20;

			if (Wheel >= 1)
				Inventory.SelectNext();
			else if (Wheel <= -1)
				Inventory.SelectPrevious();
			if ((Left || Right || Middle) && CursorDisabled)
			{
				if (ActiveSelection != null)
				{
					Vector3 Start = Position;
					Vector3 Dir = GetForward();
					InventoryClickEventArgs E = new InventoryClickEventArgs(Map, Start, Dir, MaxLen);

					if (Left)
						ActiveSelection.OnLeftClick(E);

					if (Right)
						ActiveSelection.OnRightClick(E);

					if (Middle)
						ActiveSelection.OnMiddleClick(E);
				}
			}

			if (!CursorDisabled)
			{
				// FishUI tick is called by MultiplayerGameState.Draw2D
			}
			else
			{
				if (InMgr.IsInputPressed(InputKey.Q))
					Inventory.SelectPrevious();

				if (InMgr.IsInputPressed(InputKey.E))
				{
					Vector3 Start = Position;
					Vector3 End = Map.RaycastPos(Start, 1.5f, GetForward(), out Vector3 Face);

					if (Face.Y == 1)
						End.Y -= 0.001f;

					PlacedBlock Blk = Map.GetPlacedBlock((int)End.X, (int)End.Y, (int)End.Z, out Chunk Chk);

					if (Blk.Type == BlockType.CraftingTable)
					{
						Logging.WriteLine($"Craft! {Face}, ({End.X - Math.Floor(End.X)}, {End.Z - Math.Floor(End.Z)})");
						return;
					}
					Inventory.SelectNext();
				}
				// FishUI handles updates internally
			}
		}
	}
}
