using MoonSharp.Interpreter;
using RaylibSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	[MoonSharpUserData]
	public unsafe class AnimatedEntity {
		[MoonSharpHidden]
		public Model Mdl;

		[MoonSharpHidden]
		Dictionary<string, List<EntityAnimation>> Anims = new Dictionary<string, List<EntityAnimation>>();

		public int UpperBodyMesh;

		public void SetModel(string ModelFile) {
			Mdl = Raylib.LoadModel(Path.Combine("data/models", ModelFile));
			UpperBodyMesh = 0;
		}

		public void RegisterAnimation(string Name, string AnimFile) {
			int AnimCount = 0;
			ModelAnimation* AnimArray = Raylib.LoadModelAnimations(Path.Combine("data/models", AnimFile), &AnimCount);

			if (!Anims.ContainsKey(Name))
				Anims.Add(Name, new List<EntityAnimation>());

			for (int i = 0; i < AnimCount; i++)
				Anims[Name].Add(new EntityAnimation(Name, AnimArray[i], this));
		}

		public void SetMeshTexture(int MeshNum, string TextureName) {
			Raylib.SetMaterialTexture(&Mdl.materials[MeshNum], MaterialMapType.MAP_ALBEDO, ResMgr.GetTexture(TextureName));
		}

		[MoonSharpHidden]
		public EntityAnimation GetAnim(string Name) {
			return Anims[Name].Random();
		}

		[MoonSharpHidden]
		public BoneInfo* GetBone(string Name, out int BoneID) {
			BoneID = -1;

			for (int i = 0; i < Mdl.boneCount; i++) {
				string BoneName = Encoding.UTF8.GetString(Mdl.bones[i].name, 32).Trim();

				if (BoneName.Contains("\0"))
					BoneName = BoneName.Split(new[] { (char)0 })[0];

				if (BoneName == Name) {
					BoneID = i;
					return &Mdl.bones[i];
				}
			}

			return null;
		}

		[MoonSharpHidden]
		public string[] GetBoneNames() {
			List<string> Names = new List<string>();

			for (int i = 0; i < Mdl.boneCount; i++) {
				string BoneName = Encoding.UTF8.GetString(Mdl.bones[i].name, 32).Trim();

				if (BoneName.Contains("\0"))
					BoneName = BoneName.Split(new[] { (char)0 })[0];


				Names.Add(BoneName);
			}

			return Names.ToArray();
		}

		[MoonSharpHidden]
		public BoneInformation GetBones() {
			BoneInformation Root = new BoneInformation("_ROOT", -1);

			for (int i = 0; i < Mdl.boneCount; i++) {
				BoneInfo BInfo = Mdl.bones[i];

				string BoneName = Encoding.UTF8.GetString(BInfo.name, 32).Trim();
				if (BoneName.Contains("\0"))
					BoneName = BoneName.Split(new[] { (char)0 })[0];

				BoneInformation Parent = BoneInformation.FindBone(Root, BInfo.parent);

				if (Parent == null)
					throw new Exception("Could not find bone with ID " + BInfo.parent);

				BoneInformation BoneInfo = new BoneInformation(BoneName, i);
				BoneInfo.Transform = &Mdl.bindPose[i];
				Parent.AddChild(BoneInfo);
			}

			Root.RecalcTransforms();
			return Root;
		}

		[MoonSharpHidden]
		public static AnimatedEntity Create() {
			return new AnimatedEntity();
		}
	}

	public class EntityAnimation {
		public string Name;
		public ModelAnimation Anim;
		public AnimatedEntity Entity;

		public int Frame;
		public int FrameCount;
		public double FrameRate;

		double LastFrame;

		public EntityAnimation(string Name, ModelAnimation Anim, AnimatedEntity Entity, double FrameRate = 1.0 / 120) {
			this.Name = Name;
			this.Anim = Anim;
			this.Entity = Entity;
			this.FrameRate = FrameRate;

			Frame = 0;
			FrameCount = Anim.frameCount;
			LastFrame = -1;
		}

		public void Apply() {
			Raylib.UpdateModelAnimation(Entity.Mdl, Anim, Frame);
		}

		public void Step() {
			double CurTime = Raylib.GetTime();
			double Delta = CurTime - LastFrame;

			if (Delta >= FrameRate) {
				LastFrame = CurTime;

				Frame++;
				if (Frame >= FrameCount)
					Frame = 0;

				Apply();
			}
		}
	}
}
