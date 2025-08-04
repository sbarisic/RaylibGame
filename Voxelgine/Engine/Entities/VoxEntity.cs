﻿using Raylib_cs;

using RaylibGame.States;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Graphics;

namespace Voxelgine.Engine {
	public abstract class VoxEntity {
		public Vector3 Position;
		public Vector3 Size;
		public Vector3 Velocity;

		protected bool HasModel;
		protected string EntModelName;
		protected Model EntModel;

		protected Vector3 CenterOffset;
		protected Vector3 ModelOffset;

		protected float ModelRotationDeg;
		protected Color ModelColor;
		protected Vector3 ModelScale;

		// Rotate around Y axis at set sped
		public bool IsRotating = false;
		public float RotationSpeed = 30;

		EntityManager EntMgr;
		GameState GameState;

		public virtual void SetModel(string MdlName) {
			HasModel = false;
			ModelOffset = Vector3.Zero;
			ModelRotationDeg = 0;
			ModelColor = Color.White;
			ModelScale = Vector3.One;

			if (Size != Vector3.Zero) {
				CenterOffset = new Vector3(Size.X / 2, ModelOffset.Y, Size.Y / 2);
			}

			EntModelName = MdlName;
			EntModel = ResMgr.GetModel(MdlName);
			HasModel = EntModel.MeshCount > 0;
		}

		public virtual Vector3 GetPosition() {
			return Position;
		}

		public virtual void SetPosition(Vector3 Pos) {
			Position = Pos;
		}

		public virtual Vector3 GetSize() {
			return Size;
		}

		public virtual void SetSize(Vector3 Size) {
			this.Size = Size;
		}

		public virtual Vector3 GetVelocity() {
			return Velocity;
		}

		public virtual void SetVelocity(Vector3 Velocity) {
			this.Velocity = Velocity;
		}

		public virtual EntityManager GetEntityManager() {
			return EntMgr;
		}

		public virtual void SetEntityManager(EntityManager EntMgr) {
			this.EntMgr = EntMgr;
		}

		public virtual GameState GetGameState() {
			return GameState;
		}

		public virtual void SetGameState(GameState State) {
			GameState = State;
		}

		public virtual void OnPlayerTouch(Player Ply) {
			Console.WriteLine("Player touched me!");
		}

		public virtual void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr) {
			if (IsRotating)
				ModelRotationDeg = (ModelRotationDeg + RotationSpeed * Dt) % 360;

		}

		// Applies simple physics: gravity, velocity integration, and block collision (AABB sweep, no input)
		// Also checks for collision with the player and triggers OnPlayerTouch only once per entry
		public bool _WasPlayerTouching = false;

		public virtual void OnUpdatePhysics(float Dt) {
		}

		protected Vector3 GetDrawPosition() {
			return Position + ModelOffset + CenterOffset;
		}

		protected virtual void EntityDrawModel(float TimeAlpha, ref GameFrameInfo LastFrame) {
			if (HasModel) {
				Raylib.DrawModelEx(EntModel, GetDrawPosition(), Vector3.UnitY, ModelRotationDeg, ModelScale, ModelColor);
			}
		}

		public virtual void Draw3D(float TimeAlpha, ref GameFrameInfo LastFrame) {
			EntityDrawModel(TimeAlpha, ref LastFrame);
			DrawCollisionBox();
		}

		// Draws the collision box at Position with Size
		protected virtual void DrawCollisionBox() {
			if (!Program.DebugMode)
				return;

			Vector3 min = Position;
			Vector3 max = Position + Size;
			Color color = Color.Red;
			Vector3[] corners = new Vector3[8];
			corners[0] = new Vector3(min.X, min.Y, min.Z);
			corners[1] = new Vector3(max.X, min.Y, min.Z);
			corners[2] = new Vector3(max.X, min.Y, max.Z);
			corners[3] = new Vector3(min.X, min.Y, max.Z);
			corners[4] = new Vector3(min.X, max.Y, min.Z);
			corners[5] = new Vector3(max.X, max.Y, min.Z);
			corners[6] = new Vector3(max.X, max.Y, max.Z);
			corners[7] = new Vector3(min.X, max.Y, max.Z);
			Raylib.DrawLine3D(corners[0], corners[1], color);
			Raylib.DrawLine3D(corners[1], corners[2], color);
			Raylib.DrawLine3D(corners[2], corners[3], color);
			Raylib.DrawLine3D(corners[3], corners[0], color);
			Raylib.DrawLine3D(corners[4], corners[5], color);
			Raylib.DrawLine3D(corners[5], corners[6], color);
			Raylib.DrawLine3D(corners[6], corners[7], color);
			Raylib.DrawLine3D(corners[7], corners[4], color);
			Raylib.DrawLine3D(corners[0], corners[4], color);
			Raylib.DrawLine3D(corners[1], corners[5], color);
			Raylib.DrawLine3D(corners[2], corners[6], color);
			Raylib.DrawLine3D(corners[3], corners[7], color);
		}
	}
}
