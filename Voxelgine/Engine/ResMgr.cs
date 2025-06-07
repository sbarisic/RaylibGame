using Raylib_cs;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Numerics;

namespace Voxelgine.Engine {
	unsafe static class ResMgr {
		static Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
		static Dictionary<string, Model> Models = new Dictionary<string, Model>();
		static Dictionary<string, Shader> Shaders = new Dictionary<string, Shader>();

		public static Texture2D AtlasTexture = GetTexture("atlas.png");

		public static Texture2D GetTexture(string FilePath) {
			FilePath = Path.GetFullPath(Path.Combine("data/textures", FilePath)).Replace("\\", "/");

			if (Textures.ContainsKey(FilePath))
				return Textures[FilePath];

			if (!File.Exists(FilePath))
				throw new Exception("File not found " + FilePath);

			Image Img = Raylib.LoadImage(FilePath);
			Texture2D Tex = Raylib.LoadTextureFromImage(Img);
			Raylib.SetTextureFilter(Tex, TextureFilter.Anisotropic16X);

			Textures.Add(FilePath, Tex);
			return GetTexture(FilePath);
		}

		public static Model GetModel(string FilePath) {
			FilePath = Path.GetFullPath(Path.Combine("data/models", FilePath)).Replace("\\", "/");

			if (Models.ContainsKey(FilePath))
				return Models[FilePath];

			if (!File.Exists(FilePath))
				throw new Exception("File not found " + FilePath);

			Model mdl = Raylib.LoadModel(FilePath);
			Models.Add(FilePath, mdl);

			return mdl;
		}

		public static Shader GetShader(string FilePath) {
			FilePath = Path.GetFullPath(Path.Combine("data/shaders", FilePath)).Replace("\\", "/");

			if (Shaders.ContainsKey(FilePath))
				return Shaders[FilePath];

			if (!File.Exists(FilePath + ".vert") || !File.Exists(FilePath + ".frag"))
				throw new Exception("File not found " + FilePath + " (.vert and .frag)");

			Shader s = Raylib.LoadShader(FilePath + ".vert", FilePath + ".frag");
			Shaders.Add(FilePath, s);

			return s;
		}

		public static MinecraftModel GetJsonModel(string FilePath) {
			FilePath = Path.GetFullPath(Path.Combine("data/models", FilePath)).Replace("\\", "/");

			if (!File.Exists(FilePath))
				throw new Exception("File not found " + FilePath);


			string JsonSrc = File.ReadAllText(FilePath);
			JObject Obj = JObject.Parse(JsonSrc);

			float TexWidth = (float)Obj["texture_size"][0];
			float TexHeight = (float)Obj["texture_size"][1];

			MinecraftModel JMdl = new MinecraftModel();
			JMdl.TextureSize = new Vector2(TexWidth, TexHeight);

			foreach (var Itm in Obj) {
				if (Itm.Key == "textures") {
					List<MinecrafTexture> TexList = new List<MinecrafTexture>();

					foreach (JProperty Tex in Itm.Value) {
						TexList.Add(new MinecrafTexture() { Name = Tex.Name, TextureName = ((string)Tex.Value).Split(":")[1] + ".png" });
					}

					JMdl.Textures = TexList.ToArray();
				} else if (Itm.Key == "elements") {
					List<MinecraftMdlElement> ElemList = new List<MinecraftMdlElement>();

					foreach (var Element in Itm.Value) {
						MinecraftMdlElement MElem = JsonConvert.DeserializeObject<MinecraftMdlElement>(Element.ToString());
						ElemList.Add(MElem);
					}

					JMdl.Elements = ElemList.ToArray();
				}
			}

			return JMdl;
		}
	}

	class MinecraftModel {
		public Vector2 TextureSize;
		public MinecrafTexture[] Textures;
		public MinecraftMdlElement[] Elements;
	}

	class MinecrafTexture {
		public string Name;
		public string TextureName;
	}

	class MinecraftMdlElement {
		public string Name;
		public float[] From;
		public float[] To;
		public MinecraftRotation Rotation;
		public Dictionary<string, MinecraftMdlFace> Faces;
	}

	class MinecraftRotation {
		public float Angle;
		public string Axis;
		public float[] Origin;
	}

	class MinecraftMdlFace {
		//public float[] Dir;
		public float[] UV;
		public string Texture;
	}
}
