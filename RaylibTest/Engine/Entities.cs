using MoonSharp.Interpreter;
using RaylibSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	static class Entities {
		public static AnimatedEntity Load(string Name) {
			Script Scr = Scripting.GetContext();

			string PlyEntFile = Path.Combine("data/entities", Name + ".lua");
			string Source = File.ReadAllText(PlyEntFile);

			AnimatedEntity Ent = Scr.DoString(Source).CheckUserDataType<AnimatedEntity>(null);
			return Ent;
		}
	}
}
