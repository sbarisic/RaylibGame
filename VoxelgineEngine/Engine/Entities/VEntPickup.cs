using System.Numerics;

namespace Voxelgine.Engine
{
	public class VEntPickup : VoxEntity
	{
		public bool IsBobbing;
		public float BobAmplitude = 0.15f;
		public float BobSpeed = 2f;

		private LerpVec3 _bobbing;

		public VEntPickup()
		{
			IsRotating = true;
		}

		public override void OnInit()
		{
			base.OnInit();

			_bobbing = new LerpVec3(Eng.DI.GetRequiredService<ILerpManager>())
			{
				Loop = true,
				Easing = Easing.EaseInOutQuart,
			};
			_bobbing.StartLerp(
				1f,
				new Vector3(0f, -BobAmplitude, 0f),
				new Vector3(0f, BobAmplitude, 0f)
			);
			IsBobbing = true;
		}

		public override void UpdateLockstep(float totalTime, float deltaTime, InputMgr inputManager)
		{
			base.UpdateLockstep(totalTime, deltaTime, inputManager);

			if (IsBobbing)
				SetPresentationOffset(new Vector3(0f, _bobbing.GetVec3().Y, 0f));
		}
	}
}
