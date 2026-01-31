using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	public class PhysData {
		// Ground movement
		public float GroundFriction { get; set; } = 6.0f;
		public float GroundAccel { get; set; } = 10.0f;
		public float MaxGroundSpeed { get; set; } = 6.4f;
		public float MaxWalkSpeed { get; set; } = 2.8f;

		// Air movement (Quake-style)
		public float AirAccel { get; set; } = 12.0f;
		public float AirFriction { get; set; } = 0.0f;  // No air friction in Quake
		public float MaxAirWishSpeed { get; set; } = 0.7f;  // Key for strafe jumping - low wish speed allows acceleration

		// Jumping
		public float JumpImpulse { get; set; } = 5.5f;
		public float Gravity { get; set; } = 15.0f;

		// Misc
		public float ClampHyst { get; set; } = 0.001f;
		public float NoClipMoveSpeed { get; set; } = 15.0f;
		public float GroundEpsilon { get; set; } = 0.02f;
		public float GroundCheckDist { get; set; } = 0.12f;
	}
}
