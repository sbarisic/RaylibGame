using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using RaylibSharp;

namespace RaylibGame.Engine {
	public class SoundMgr {
		Random Rnd = new Random();

		Dictionary<string, Sound> SoundDict = new Dictionary<string, Sound>();
		Dictionary<string, List<string>> ComboDict = new Dictionary<string, List<string>>();

		public void Init() {
			Raylib.InitAudioDevice();

			LoadCombo("walk", "data/sound/walk/walk{0}.wav", 5);
			LoadCombo("jump", "data/sound/jump/jump{0}.wav", 1);
			LoadCombo("crash1", "data/sound/crash1/crash{0}.wav", 1);
			LoadCombo("crash2", "data/sound/crash2/crash{0}.wav", 2);
			LoadCombo("crash3", "data/sound/crash3/crash{0}.wav", 1);
		}

		public void LoadSound(string Name, string FilePath) {
			Sound Snd = Raylib.LoadSound(FilePath);
			SoundDict.Add(Name, Snd);
		}

		public void LoadCombo(string Name, string FilePath, int Count) {
			CreateCombo(Name);

			for (int i = 1; i < Count + 1; i++) {
				string NewFileName = string.Format(FilePath, i);
				string[] Toks = NewFileName.Split('/').ToArray();

				string SoundName = Toks[Toks.Length - 2] + "_" + Path.GetFileNameWithoutExtension(NewFileName);


				LoadSound(SoundName, NewFileName);
				AddCombo(Name, SoundName);
			}
		}

		public void PlaySound(string Name) {
			if (SoundDict.ContainsKey(Name)) {
				Raylib.PlaySound(SoundDict[Name]);
			}
		}

		public void CreateCombo(string ComboName) {
			ComboDict.Add(ComboName, new List<string>());
		}

		public void AddCombo(string ComboName, string SoundName) {
			ComboDict[ComboName].Add(SoundName);
		}

		public void PlayCombo(string ComboName) {
			if (!ComboDict.ContainsKey(ComboName))
				return;

			List<string> Sounds = ComboDict[ComboName];
			PlaySound(Sounds[Rnd.Next(0, Sounds.Count)]);
		}
	}
}
