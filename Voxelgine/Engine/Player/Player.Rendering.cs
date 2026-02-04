using Raylib_cs;
using RaylibGame.Engine;
using System.Numerics;

namespace Voxelgine.Engine
{
	public unsafe partial class Player
	{
		const bool DEBUG_PLAYER = true;

		public void Draw(float TimeAlpha, ref GameFrameInfo LastFrame, ref GameFrameInfo CurFame)
		{
			if (LocalPlayer)
			{

				RenderTexture2D RT = Program.Window.ViewmodelRT;
				Raylib.BeginTextureMode(RT);
				{
					Raylib.ClearBackground(new Color(0, 0, 0, 0));

					Raylib.BeginMode3D(RenderCam); // Use interpolated render camera
					{
						Shader DefaultShader = ResMgr.GetShader("default");
						Raylib.BeginShaderMode(DefaultShader);
						ViewMdl.DrawViewModel(this, TimeAlpha, ref LastFrame, ref CurFame);
						Raylib.EndShaderMode();
					}
					Raylib.EndMode3D();

				}
				Raylib.EndTextureMode();
				Raylib.BeginTextureMode(Program.Window.WindowG.Target);

				Rectangle Src = new Rectangle(0, 0, RT.Texture.Width, -RT.Texture.Height);
				Rectangle Dst = new Rectangle(0, 0, RT.Texture.Width, RT.Texture.Height);
				Raylib.DrawTexturePro(RT.Texture, Src, Dst, Vector2.Zero, 0, Color.White);
			}

			if (!DEBUG_PLAYER && LocalPlayer)
				return;
		}
	}
}
