using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Raylib_cs;

using Voxelgine.States;
using Voxelgine.Graphics;

namespace Voxelgine.Engine
{
	public class VEntPickup : VoxEntity
	{
		// Up down movement
		public bool IsBobbing = false;
		public float BobAmplitude = 0.15f;
		public float BobSpeed = 2;
		LerpVec3 BobbingLerp;

		Stopwatch SWatch = Stopwatch.StartNew();

		public VEntPickup() : base()
		{
			IsRotating = true;
			NextMs = 400;
		}

		public override void OnInit()
		{
			base.OnInit();

			BobbingLerp = new LerpVec3(Eng);
			BobbingLerp.Loop = true;
			BobbingLerp.Easing = Easing.EaseInOutQuart;
			BobbingLerp.StartLerp(1, new Vector3(0, -BobAmplitude, 0), new Vector3(0, BobAmplitude, 0));
			IsBobbing = true;
		}

		long NextMs;

		public override void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr)
		{
			base.UpdateLockstep(TotalTime, Dt, InMgr);

			if (IsBobbing)
			{
				ModelOffset = new Vector3(0, BobbingLerp.GetVec3().Y, 0);
			}

			if (SWatch.ElapsedMilliseconds > NextMs)
			{
				SWatch.Restart();
				NextMs = Random.Shared.Next(300, 700);

				ParticleSystem Part = ((GameState)Eng.GameState).Particle;

				Part.SpawnSmoke(Position + CenterOffset, Vector3.UnitY * 2.6f, Color.White);
			}
		}
	}
}
