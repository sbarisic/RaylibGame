using Raylib_cs;
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

				RenderTexture2D RT = Eng.DI.GetRequiredService<IGameWindow>().ViewmodelRT;
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
			}

			if (!DEBUG_PLAYER && LocalPlayer)
				return;
		}

		/// <summary>
		/// Draws the viewmodel render texture overlay onto the main render target.
		/// Must be called OUTSIDE of BeginMode3D to avoid 3D projection of the 2D overlay.
		/// Re-activates WindowG.Target because Player.Draw's EndTextureMode restores the default framebuffer.
		/// </summary>
		public void DrawViewModelOverlay()
		{
			if (!LocalPlayer)
				return;

			IGameWindow gw = Eng.DI.GetRequiredService<IGameWindow>();
			Raylib.BeginTextureMode(gw.WindowG.Target);

			RenderTexture2D RT = gw.ViewmodelRT;
			Rectangle Src = new Rectangle(0, 0, RT.Texture.Width, -RT.Texture.Height);
			Rectangle Dst = new Rectangle(0, 0, RT.Texture.Width, RT.Texture.Height);
			Raylib.DrawTexturePro(RT.Texture, Src, Dst, Vector2.Zero, 0, Color.White);
		}
	}
}
