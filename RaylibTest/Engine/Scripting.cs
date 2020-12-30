using MoonSharp;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Debugging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaylibTest.Engine {
	static class Scripting {
		static Script Script = CreateContext();

		public static void Init() {
			UserData.RegisterAssembly(typeof(Scripting).Assembly);
		}

		public static Script GetContext() {
			return Script;
		}

		static Script CreateContext() {
			Script S = new Script();
			S.Globals[nameof(AnimatedEntity)] = (Func<AnimatedEntity>)AnimatedEntity.Create;
			return S;
		}
	}
}
