using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	class PhysData {
		public float GroundFriction { get; set; } = 8.0f;
		public float GroundAccel { get; set; } = 50.0f;
		public float AirFriction { get; set; } = 0.2f;
		public float AirAccel { get; set; } = 20.0f;
		public float MaxGroundSpeed { get; set; } = 3.4f;
		public float MaxAirSpeed { get; set; } = 4.0f;
		public float JumpImpulse { get; set; } = 5.0f;
		public float Gravity { get; set; } = 10.5f;
		public float ClampHyst { get; set; } = 0.02f;
		public float NoClipMoveSpeed { get; set; } = 10.0f;
		public float GroundEpsilon { get; set; } = 0.02f;
		public float GroundCheckDist { get; set; } = 0.12f;
	}
}
