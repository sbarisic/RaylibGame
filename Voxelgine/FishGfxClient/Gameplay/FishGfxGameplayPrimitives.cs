#if WINDOWS
using FishGfx;
using FishGfx.Graphics;
using System.Numerics;

namespace Voxelgine.FishGfxClient.Gameplay;

internal static class FishGfxGameplayPrimitives
{
	internal static void DrawWireBox(
		RenderPass pass,
		Vector3 minimum,
		Vector3 maximum,
		Color color,
		float thickness = 1
	)
	{
		ArgumentNullException.ThrowIfNull(pass);
		Vector3[] corners =
		{
			new(minimum.X, minimum.Y, minimum.Z),
			new(maximum.X, minimum.Y, minimum.Z),
			new(maximum.X, maximum.Y, minimum.Z),
			new(minimum.X, maximum.Y, minimum.Z),
			new(minimum.X, minimum.Y, maximum.Z),
			new(maximum.X, minimum.Y, maximum.Z),
			new(maximum.X, maximum.Y, maximum.Z),
			new(minimum.X, maximum.Y, maximum.Z),
		};

		DrawEdge(0, 1);
		DrawEdge(1, 2);
		DrawEdge(2, 3);
		DrawEdge(3, 0);
		DrawEdge(4, 5);
		DrawEdge(5, 6);
		DrawEdge(6, 7);
		DrawEdge(7, 4);
		DrawEdge(0, 4);
		DrawEdge(1, 5);
		DrawEdge(2, 6);
		DrawEdge(3, 7);

		void DrawEdge(int start, int end)
		{
			pass.DrawLine(
				new FishGfx.Vertex3(corners[start], color),
				new FishGfx.Vertex3(corners[end], color),
				thickness
			);
		}
	}
}
#endif
