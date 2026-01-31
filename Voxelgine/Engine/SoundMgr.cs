using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Raylib_cs;

namespace RaylibGame.Engine {
	class FancySound {
		public string Name;
		public Sound Sound;
		public float Volume;

		public FancySound(string Name, Sound Sound, float Volume) {
			this.Name = Name;
			this.Sound = Sound;
			this.Volume = Volume;
		}

		public void Play(Vector3 Ears, Vector3 Dir, Vector3 Pos) {
			float Dist = Vector3.Distance(Ears, Pos);

			float Vol = Dist * Volume * 0.1f;
			Vol = Volume / (Vol * Vol + 1);

			Vol = Math.Clamp(Vol, 0, Volume);

			Raylib.SetSoundVolume(Sound, Vol);
			Raylib.PlaySound(Sound);
		}
	}

	public class SoundMgr {
		Random Rnd = new Random();

		//Dictionary<string, Sound> SoundDict = new Dictionary<string, Sound>();
		List<FancySound> SoundList = new List<FancySound>();

		Dictionary<string, List<string>> ComboDict = new Dictionary<string, List<string>>();

		public void Init() {
			Raylib.InitAudioDevice();

			Raylib.SetMasterVolume(0.5f);

			LoadCombo("walk", "data/sound/walk/walk{0}.wav", 5, 0.1f);
			LoadCombo("jump", "data/sound/jump/jump{0}.wav", 1, 0.5f);
			LoadCombo("crash1", "data/sound/crash1/crash{0}.wav", 1, 0.4f);
			//LoadCombo("crash2", "data/sound/crash2/crash{0}.wav", 2, 0.6f);
			//LoadCombo("crash3", "data/sound/crash3/crash{0}.wav", 1, 0.8f);

			LoadCombo("block_place", "data/sound/block/place{0}.wav", 1, 1.0f);
			LoadCombo("block_break", "data/sound/block/break{0}.wav", 1, 1.0f);
			LoadCombo("swim", "data/sound/swim/swim{0}.wav", 1, 0.5f);
		}

		public void LoadSound(string Name, string FilePath, float Volume) {
			Sound Snd = Raylib.LoadSound(FilePath);
			SoundList.Add(new FancySound(Name, Snd, Volume));
		}

		public void LoadCombo(string Name, string FilePath, int Count, float Volume) {
			CreateCombo(Name);

			for (int i = 1; i < Count + 1; i++) {
				string NewFileName = string.Format(FilePath, i);
				string[] Toks = NewFileName.Split('/').ToArray();

				string SoundName = Toks[Toks.Length - 2] + "_" + Path.GetFileNameWithoutExtension(NewFileName);


				LoadSound(SoundName, NewFileName, Volume);
				AddCombo(Name, SoundName);
			}
		}

		public void PlaySound(string Name, Vector3 Ears, Vector3 Dir, Vector3 Pos) {
			FancySound[] FancySounds = SoundList.Where(I => I.Name == Name).ToArray();

			foreach (FancySound FS in FancySounds) {
				FS.Play(Ears, Dir,Pos);
			}
		}

		public void CreateCombo(string ComboName) {
			ComboDict.Add(ComboName, new List<string>());
		}

		public void AddCombo(string ComboName, string SoundName) {
			ComboDict[ComboName].Add(SoundName);
		}

		public void PlayCombo(string ComboName, Vector3 Ears, Vector3 Dir, Vector3 Pos) {
			if (!ComboDict.ContainsKey(ComboName))
				return;

			List<string> Sounds = ComboDict[ComboName];
			PlaySound(Sounds[Rnd.Next(0, Sounds.Count)], Ears, Dir, Pos);
		}
	}
}
