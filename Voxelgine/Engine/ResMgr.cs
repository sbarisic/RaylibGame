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
using Voxelgine.Graphics;

namespace Voxelgine.Engine {
	class EngineResourceBase {
		public string Name;
		public string FileName;
		public DateTime LastUpdate;
	}

	class EngineResource<T> : EngineResourceBase {
		public T Value;

		public EngineResource(string Name, string FileName, T Value) {
			this.Value = Value;
			this.Name = Name;
			this.FileName = FileName;
			LastUpdate = DateTime.Now;
		}
	}

	unsafe static class ResMgr {
		static FileSystemWatcher FSW;

		static Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
		static Dictionary<string, Model> Models = new Dictionary<string, Model>();

		//static Dictionary<string, Shader> Shaders = new Dictionary<string, Shader>();
		static List<EngineResourceBase> ResourceList = new List<EngineResourceBase>();

		public static Texture2D AtlasTexture;
		public static Texture2D ItemTexture;

		public const int ItemSize = 16;

		static List<string> ReloadList = new List<string>();

		public static void InitHotReload() {
			FSW = new FileSystemWatcher();
			FSW.Path = Path.GetFullPath("data");
			FSW.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;

			FSW.Changed += (S, Args) => {
				ReloadList.Add(Args.FullPath);
			};

			FSW.IncludeSubdirectories = true;
			FSW.EnableRaisingEvents = true;
		}

		public static void HandleHotReload() {
			for (int i = 0; i < ReloadList.Count; i++) {
				string FullPath = ReloadList[i];
				string FName = Path.GetFileNameWithoutExtension(FullPath);

				if (FullPath.EndsWith(".frag") || FullPath.EndsWith(".vert")) {
					if (TryGetResource(FName, out EngineResource<Shader> R)) {
						if ((DateTime.Now - R.LastUpdate).TotalSeconds > 1) {

							try {
								Console.WriteLine("Reloading shader '{0}'", FName);
								GetShader(FName, true);
							} catch (Exception E) {
								Console.WriteLine("Failed to reload shader: {0}", E.Message);
							}
						}
					}
				}
			}

			ReloadList.Clear();
		}

		public static void InitResources() {
			AtlasTexture = GetTexture("atlas.png", TextureFilter.Point);
			ItemTexture = GetTexture("items.png", TextureFilter.Point);
		}

		static bool TryGetResource<T>(string Name, out EngineResource<T> OutRes) {
			for (int i = 0; i < ResourceList.Count; i++) {
				if (ResourceList[i] is EngineResource<T> R && R.Name == Name) {
					OutRes = R;
					return true;
				}
			}

			OutRes = null;
			return false;
		}

		static void RemoveResource(EngineResourceBase Res) {
			if (ResourceList.Contains(Res))
				ResourceList.Remove(Res);
		}

		static void AddResource<T>(string Name, EngineResource<T> Res) {
			if (TryGetResource<T>(Name, out EngineResource<T> OutR))
				return;

			Res.LastUpdate = DateTime.Now;
			Res.Name = Name;
			ResourceList.Add(Res);
		}

		static Dictionary<string, Texture2D[]> TexCollections = new Dictionary<string, Texture2D[]>();

		public static void CreateCollection(string Name, params Texture2D[] Textures) {
			if (TexCollections.ContainsKey(Name))
				TexCollections.Remove(Name);

			TexCollections.Add(Name, Textures);
		}

		public static Texture2D GetFromCollection(string Name) {
			if (TexCollections.ContainsKey(Name)) {
				Texture2D[] Tex = TexCollections[Name];
				int Idx = Random.Shared.Next(0, Tex.Length);
				return Tex[Idx];
			}

			throw new FileNotFoundException();
		}

		public static Texture2D GetTexture(string FilePath, TextureFilter TexFilt = TextureFilter.Anisotropic16X) {
			FilePath = Path.GetFullPath(Path.Combine("data/textures", FilePath)).Replace("\\", "/");

			if (Textures.ContainsKey(FilePath))
				return Textures[FilePath];

			if (!File.Exists(FilePath))
				throw new Exception("File not found " + FilePath);

			Image Img = Raylib.LoadImage(FilePath);
			Texture2D Tex = Raylib.LoadTextureFromImage(Img);


			Tex.Mipmaps = 4;
			Raylib.SetTextureFilter(Tex, TexFilt);
			Raylib.SetTextureWrap(Tex, TextureWrap.Clamp);
			Raylib.GenTextureMipmaps(ref Tex);

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

		public static Shader GetShader(string ShaderName, bool Reload = false) {
			string ShaderPath = Path.GetFullPath(Path.Combine("data/shaders", ShaderName)).Replace("\\", "/");
			string FragShaderPath = ShaderPath + "/" + ShaderName + ".frag";
			string VertShaderPath = ShaderPath + "/" + ShaderName + ".vert";

			EngineResource<Shader> UnloadShader = null;

			if (TryGetResource(ShaderName, out EngineResource<Shader> Shd)) {
				if (Reload) {
					UnloadShader = Shd;
				} else {
					return Shd.Value;
				}
			}

			if (!File.Exists(VertShaderPath) || !File.Exists(FragShaderPath))
				throw new Exception("File not found " + ShaderName + " (.vert and .frag)");

			string VertexSrc = File.ReadAllText(VertShaderPath);
			string FragmentSrc = File.ReadAllText(FragShaderPath);

			//Shader S = Raylib.LoadShader(VertShaderPath, FragShaderPath);
			Shader S = Raylib.LoadShaderFromMemory(VertexSrc, FragmentSrc);

			if (S.Id == 0 && S.Locs == null) {
				throw new Exception("Shader failed to compile " + ShaderName);
			}


			if (UnloadShader != null) {
				Raylib.UnloadShader(UnloadShader.Value);
				RemoveResource(UnloadShader);
			}

			AddResource(ShaderName, new EngineResource<Shader>(ShaderName, VertShaderPath + ";" + FragShaderPath, S));
			return S;
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

		/*internal static Texture2D GetBlockTexture(BlockType BType) {
			BlockInfo.GetBlockTexCoords(BType, new Vector3(0, 1, 0), out Vector2 UVSize, out Vector2 UVPos);
			
			Raylib.texture
		}*/
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
