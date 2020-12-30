using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace RaylibSharp {
	public static unsafe class Rlgl {
		const string LibName = "raylib";
		const CallingConvention CConv = CallingConvention.Cdecl;
		const CharSet CSet = CharSet.Ansi;
		
		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void rlDrawMesh(Mesh Mesh, Material Material, Matrix4x4 Transform);
	}
}
