using RaylibSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaylibTest.Engine {
	unsafe static class ResMgr {
		static Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();

		public static Texture2D AtlasTexture = GetTexture("atlas.png");

		public static Texture2D GetTexture(string FilePath) {
			FilePath = Path.GetFullPath(Path.Combine("data/textures", FilePath)).Replace("\\", "/");

			if (Textures.ContainsKey(FilePath))
				return Textures[FilePath];

			if (!File.Exists(FilePath))
				throw new Exception("File not found " + FilePath);

			Image Img = Raylib.LoadImage(FilePath);
			Texture2D Tex = Raylib.LoadTextureFromImage(Img);
			Raylib.SetTextureFilter(Tex, TextureFilterMode.FILTER_ANISOTROPIC_16X);
			Textures.Add(FilePath, Tex);

			return GetTexture(FilePath);
		}
	}
}
