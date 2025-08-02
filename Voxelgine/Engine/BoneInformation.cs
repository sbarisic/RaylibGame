using RaylibGame.Engine;

using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Voxelgine.Graphics;
using Voxelgine.GUI;

namespace Voxelgine.Engine {
	public unsafe class BoneInformation {
		public string Name;
		public int ID;

		public Transform* Transform;
		public Matrix4x4 LocalTransform;

		public BoneInformation Parent;
		public List<BoneInformation> Children;

		public BoneInformation(string Name, int ID) {
			Children = new List<BoneInformation>();
			LocalTransform = Matrix4x4.Identity;

			this.Name = Name;
			this.ID = ID;
		}

		public void AddChild(BoneInformation Bone) {
			Bone.Parent = this;
			Children.Add(Bone);
		}

		public Matrix4x4 GetTransform() {
			if (Transform == null)
				return Matrix4x4.Identity;

			return Matrix4x4.CreateFromQuaternion(Transform->Rotation) * Matrix4x4.CreateScale(Transform->Scale) * Matrix4x4.CreateTranslation(Transform->Translation);
		}

		public Matrix4x4 GetLocalTransform() {
			if (Parent == null)
				return GetTransform();

			Matrix4x4 ParentWorld = Parent.GetTransform();
			Matrix4x4.Invert(ParentWorld, out Matrix4x4 ParentWorldInv);
			return ParentWorldInv * GetTransform();
		}

		public Matrix4x4 CalcWorldTransform() {
			if (Parent == null)
				return GetTransform();

			return Parent.CalcWorldTransform() * GetLocalTransform();
		}

		public void UpdateTransforms() {
			if (Transform == null)
				return;

			Matrix4x4.Decompose(CalcWorldTransform(), out Transform->Scale, out Transform->Rotation, out Transform->Translation);
		}

		public void RecalcTransforms() {
			UpdateTransforms();

			foreach (var C in Children)
				C.RecalcTransforms();
		}

		public override string ToString() {
			return string.Format("{0} - {1}", ID, Name);
		}

		public static BoneInformation FindBone(BoneInformation Root, int ID) {
			if (Root.ID == ID)
				return Root;

			foreach (var C in Root.Children) {
				BoneInformation B = FindBone(C, ID);

				if (B != null)
					return B;
			}

			return null;
		}

		public static BoneInformation FindBoneByName(BoneInformation Root, string Name) {
			if (Root.Name == Name)
				return Root;

			foreach (var C in Root.Children) {
				BoneInformation B = FindBoneByName(C, Name);

				if (B != null)
					return B;
			}

			return null;
		}
	}

}
