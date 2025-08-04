using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using static System.Runtime.InteropServices.JavaScript.JSType;

using Windows.Devices.Radios;
using Voxelgine.Graphics;

namespace Voxelgine.Engine {
	delegate bool TraceFunc(Vector3 BlockPos);

	delegate bool VoxRaycastFunc(float X, float Y, float Z, int Counter, Vector3 Face);

	static class VoxCollision {
	}
}
