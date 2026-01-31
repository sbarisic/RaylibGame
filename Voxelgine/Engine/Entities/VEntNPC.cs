using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Raylib_cs;

using RaylibGame.States;

using Voxelgine.Graphics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// NPC entity with support for animated JSON models.
	/// Uses NPCAnimator to play predefined animations (walk, idle, attack, etc.).
	/// </summary>
	public class VEntNPC : VoxEntity
	{
		CustomModel CModel;
		BoundingBox BBox;
		NPCAnimator Animator;

		/// <summary>Gets the animator for this NPC (null if model not loaded).</summary>
		public NPCAnimator GetAnimator() => Animator;

		/// <summary>Gets the custom model for this NPC (null if not loaded).</summary>
		public CustomModel GetCustomModel() => CModel;

		public override void SetModel(string MdlName)
		{
			HasModel = false;
			ModelOffset = Vector3.Zero;
			ModelRotationDeg = 0;
			ModelColor = Color.White;
			ModelScale = Vector3.One;

			EntModelName = MdlName;
			MinecraftModel JMdl = ResMgr.GetJsonModel(MdlName);
			CModel = MeshGenerator.Generate(JMdl);
			HasModel = true;
			BBox = CModel.GetBoundingBox();

			// Initialize animator with standard animations
			Animator = new NPCAnimator(CModel);
			Animator.AddClips(
				NPCAnimations.CreateIdleAnimation(),
				NPCAnimations.CreateWalkAnimation(),
				NPCAnimations.CreateAttackAnimation(),
				NPCAnimations.CreateCrouchAnimation()
			);
			Animator.Play("idle");

			if (Size != Vector3.Zero)
			{
				Vector3 Off = (BBox.Max - BBox.Min) / 2;
				ModelOffset = new Vector3(Size.X / 2, 0, Size.Z / 2);
			}
		}

		public override void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr)
		{
			base.UpdateLockstep(TotalTime, Dt, InMgr);

			// Update animation
			Animator?.Update(Dt);

			// Simple AI: play walk animation when moving, idle when stationary
			if (Animator != null && Velocity.LengthSquared() > 0.01f)
			{
				if (Animator.CurrentAnimation != "walk")
					Animator.Play("walk");
			}
			else if (Animator != null)
			{
				if (Animator.CurrentAnimation != "idle")
					Animator.Play("idle");
			}
		}

		protected override void EntityDrawModel(float TimeAlpha, ref GameFrameInfo LastFrame)
		{
			if (HasModel)
			{
				BBox = CModel.GetBoundingBox();

				CModel.Position = GetDrawPosition();
				CModel.LookDirection = Vector3.UnitZ;
				CModel.Draw();

				if (Program.DebugMode)
					Raylib.DrawBoundingBox(BBox, Color.Blue);
			}
		}
	}
}