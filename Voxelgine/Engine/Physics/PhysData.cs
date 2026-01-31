using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	public class PhysData {
		// Ground movement
		public float GroundFriction { get; set; } = 8.0f;
		public float GroundAccel { get; set; } = 10.0f;
		public float MaxGroundSpeed { get; set; } = 6.4f;
		public float MaxWalkSpeed { get; set; } = 2.8f;

		// Air movement (Quake-style)
		public float AirAccel { get; set; } = 12.0f;
		public float AirFriction { get; set; } = 0.0f;  // No air friction in Quake
		public float MaxAirWishSpeed { get; set; } = 0.7f;  // Key for strafe jumping - low wish speed allows acceleration

		// Jumping
		public float JumpImpulse { get; set; } = 6.0f;  // Increased 10% from 5.5
		public float Gravity { get; set; } = 15.0f;

		// Swimming (Quake-style)
			public float WaterAccel { get; set; } = 10.0f;      // Acceleration in water
			public float WaterFriction { get; set; } = 4.0f;    // Water drag/friction
			public float MaxWaterSpeed { get; set; } = 4.0f;    // Max swimming speed
			public float WaterGravity { get; set; } = 2.0f;     // Reduced gravity in water (when head above)
			public float WaterJumpImpulse { get; set; } = 4.5f; // Jump out of water impulse
			public float WaterSinkSpeed { get; set; } = 0.3f;   // How fast player sinks when not moving (reduced for buoyancy)
			public float WaterBuoyancy { get; set; } = 1.5f;    // Upward buoyancy force when submerged

		// Misc
		public float ClampHyst { get; set; } = 0.001f;
		public float NoClipMoveSpeed { get; set; } = 15.0f;
		public float GroundEpsilon { get; set; } = 0.02f;
		public float GroundCheckDist { get; set; } = 0.12f;
	}
}
