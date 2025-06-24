using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine.Physics {
	class PhysWorld {
		List<PhysObject> PhysicsObjects = new List<PhysObject>();

		public void Add(PhysObject Obj) {
			if (!PhysicsObjects.Contains(Obj))
				PhysicsObjects.Add(Obj);
		}

		public void UpdateLockstep(float TotalTime, float Dt) {
		}
	}
}
