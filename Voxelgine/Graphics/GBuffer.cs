using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Graphics {
	public unsafe class GBuffer : IDisposable {
		public RenderTexture2D Target;

		/*uint gStencil;
		uint gPosition;
		uint gNormal;
		uint gAlbedo;*/

		//Texture2D tStencil;
		Texture2D tPosition;
		Texture2D tNormal;
		Texture2D tAlbedo;

		private bool disposedValue;

		public GBuffer(int width, int height) {
			Target = new RenderTexture2D();

			// Load an empty framebuffer
			Target.Id = Rlgl.LoadFramebuffer();

			if (Target.Id > 0) {
				Rlgl.EnableFramebuffer(Target.Id);

				//gPosition = Rlgl.LoadTexture(null, width, height, PixelFormat.UncompressedR32G32B32A32, 1);
				//gNormal = Rlgl.LoadTexture(null, width, height, PixelFormat.UncompressedR32G32B32A32, 1);
				//gAlbedo = Rlgl.LoadTexture(null, width, height, PixelFormat.UncompressedR8G8B8A8, 1);
				//gStencil = Rlgl.LoadTexture(null, width, height, PixelFormat.UncompressedR8G8B8A8, 1);

				tPosition = new Texture2D() { Format = PixelFormat.UncompressedR16G16B16A16, Width = width, Height = height };
				tPosition.Id = Rlgl.LoadTexture(null, width, height, tPosition.Format, 1);
				Rlgl.EnableTexture(tPosition.Id);
				Rlgl.FramebufferAttach(Target.Id, tPosition.Id, FramebufferAttachType.ColorChannel1, FramebufferAttachTextureType.Texture2D, 0);

				tNormal = new Texture2D() { Format = PixelFormat.UncompressedR16G16B16A16, Width = width, Height = height };
				tNormal.Id = Rlgl.LoadTexture(null, width, height, tNormal.Format, 1);
				Rlgl.EnableTexture(tNormal.Id);
				Rlgl.FramebufferAttach(Target.Id, tNormal.Id, FramebufferAttachType.ColorChannel2, FramebufferAttachTextureType.Texture2D, 0);

				tAlbedo = new Texture2D() { Format = PixelFormat.UncompressedR8G8B8A8, Width = width, Height = height };
				tAlbedo.Id = Rlgl.LoadTexture(null, width, height, tAlbedo.Format, 1);
				Rlgl.EnableTexture(tAlbedo.Id);
				Rlgl.FramebufferAttach(Target.Id, tAlbedo.Id, FramebufferAttachType.ColorChannel3, FramebufferAttachTextureType.Texture2D, 0);
				
				
				//tStencil = new Texture2D() { Format = PixelFormat.UncompressedGrayscale, Width = width, Height = height };
				//tStencil.Id = Rlgl.LoadTexture(null, width, height, tStencil.Format, 1);


				// Create color texture (default to RGBA)

				Target.Texture.Width = width;
				Target.Texture.Height = height;
				Target.Texture.Format = PixelFormat.UncompressedR8G8B8A8;
				Target.Texture.Mipmaps = 1;
				Target.Texture.Id = Rlgl.LoadTexture(null, width, height, Target.Texture.Format, 1);


				// Create depth texture buffer (instead of raylib default renderbuffer)
				Target.Depth.Width = width;
				Target.Depth.Height = height;
				Target.Depth.Format = PixelFormat.UncompressedR32; // DEPTH_COMPONENT_24BIT?
				Target.Depth.Mipmaps = 1;
				Target.Depth.Id = Rlgl.LoadTextureDepth(width, height, false);

				// Attach color texture and depth texture to FBO
				Rlgl.FramebufferAttach(Target.Id, Target.Texture.Id, FramebufferAttachType.ColorChannel0, FramebufferAttachTextureType.Texture2D, 0);
				Rlgl.FramebufferAttach(Target.Id, Target.Depth.Id, FramebufferAttachType.Depth, FramebufferAttachTextureType.Texture2D, 0);
				//Rlgl.FramebufferAttach(Target.Id, tStencil.Id, FramebufferAttachType.Stencil, FramebufferAttachTextureType.Texture2D, 0);

				Rlgl.ActiveDrawBuffers(4);

				// Check if fbo is complete with attachments (valid)
				if (Rlgl.FramebufferComplete(Target.Id))
					Raylib.TraceLog(TraceLogLevel.Info, $"GBuffer FBO: [ID {Target.Id}] Framebuffer object created successfully");

				Rlgl.DisableFramebuffer();
			} else {
				Raylib.TraceLog(TraceLogLevel.Warning, "GBuffer FBO: Framebuffer object can not be created");
			}
		}

		void UnloadRenderTextureDepthTex() {
			if (Target.Id > 0) {
				// Color texture attached to FBO is deleted
				Rlgl.UnloadTexture(Target.Texture.Id);
				Rlgl.UnloadTexture(Target.Depth.Id);

				Raylib.UnloadTexture(tPosition);
				Raylib.UnloadTexture(tNormal);
				Raylib.UnloadTexture(tAlbedo);
				//Raylib.UnloadTexture(tStencil);

				// NOTE: Depth texture is automatically
				// queried and deleted before deleting framebuffer
				Rlgl.UnloadFramebuffer(Target.Id);
			}
		}

		protected virtual void Dispose(bool disposing) {
			if (!disposedValue) {
				if (disposing) {
					UnloadRenderTextureDepthTex();
				}

				disposedValue = true;
			}
		}

		public void Dispose() {
			// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}
	}
}
