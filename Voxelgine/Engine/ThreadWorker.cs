using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	public class ThreadWorker {
		Action ThreadAction;
		Thread ThreadWorkerThread;
		bool Triggered;
		bool Running;

		public ThreadWorker(Action ThreadAction) {
			this.ThreadAction = ThreadAction;
			Triggered = false;
			ThreadWorkerThread = new Thread(Loop);
			ThreadWorkerThread.IsBackground = true;
			ThreadWorkerThread.Start();
		}

		void Loop() {
			while (true) {
				if (Triggered) {
					Triggered = false;
					Running = true;
					ThreadAction();
				}

				Running = false;
				Thread.Sleep(10);
			}
		}

		public bool IsRunning() {
			return Running;
		}

		public void Trigger() {
			Triggered = true;
		}
	}
}
