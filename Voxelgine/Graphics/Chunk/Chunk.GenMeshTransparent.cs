using Raylib_cs;
using System.Numerics;

using Voxelgine.Engine;

using System.Collections.Generic;

namespace Voxelgine.Graphics
{
	public unsafe partial class Chunk
	{
		// For transparent blocks, like glass. Method does not calculate AO
		// Generates both front and back faces for glass-like blocks
		Mesh GenMeshTransparent()
		{
			MeshBuilder TranspVerts = new MeshBuilder();

			for (int x = 0; x < ChunkSize; x++)
			{
				for (int y = 0; y < ChunkSize; y++)
				{
					for (int z = 0; z < ChunkSize; z++)
					{
						WorldMap.GetWorldPos(x, y, z, GlobalChunkIndex, out Vector3 GlobalBlockPos);

						PlacedBlock CurBlock = GetBlock(x, y, z);
						if (CurBlock.Type == BlockType.None || BlockInfo.IsOpaque(CurBlock.Type))
							continue;

						TranspVerts.SetPositionOffset(new Vector3(x, y, z) * BlockSize);

						// Check if this block needs backface rendering
						bool needsBackface = BlockInfo.NeedsBackfaceRendering(CurBlock.Type);

						BlockType XPosType = GetBlock(x + 1, y, z).Type;
						BlockType XNegType = GetBlock(x - 1, y, z).Type;
						BlockType YPosType = GetBlock(x, y + 1, z).Type;
						BlockType YNegType = GetBlock(x, y - 1, z).Type;
						BlockType ZPosType = GetBlock(x, y, z + 1).Type;
						BlockType ZNegType = GetBlock(x, y, z - 1).Type;

						// For transparent blocks, skip faces only if the neighbor is the same type (to avoid z-fighting and allow for proper blending)
						bool XPosSkipFace = (XPosType == CurBlock.Type);
						bool XNegSkipFace = (XNegType == CurBlock.Type);
						bool YPosSkipFace = (YPosType == CurBlock.Type);
						bool YNegSkipFace = (YNegType == CurBlock.Type);
						bool ZPosSkipFace = (ZPosType == CurBlock.Type);
						bool ZNegSkipFace = (ZNegType == CurBlock.Type);

						// X++
						if (!XPosSkipFace)
						{
							Vector3 CurDir = new Vector3(1, 0, 0);
							Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
							SetBlockTextureUV(CurBlock.Type, CurDir, TranspVerts);
							// Front face
							TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 1, 1), new Vector2(0, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 0, 0), new Vector2(1, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), CurDir, FaceClr);
							// Back face (reversed winding order)
							if (needsBackface)
							{
								TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 1, 1), new Vector2(0, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 0, 0), new Vector2(1, 0), -CurDir, FaceClr);
							}
						}
						// X--
						if (!XNegSkipFace)
						{
							Vector3 CurDir = new Vector3(-1, 0, 0);
							Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
							SetBlockTextureUV(CurBlock.Type, CurDir, TranspVerts);
							// Front face
							TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(1, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 1, 0), new Vector2(0, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(0, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 0, 1), new Vector2(1, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(1, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(0, 0), CurDir, FaceClr);
							// Back face
							if (needsBackface)
							{
								TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(0, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 1, 0), new Vector2(0, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(1, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(0, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(1, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 0, 1), new Vector2(1, 0), -CurDir, FaceClr);
							}
						}
						// Y++
						if (!YPosSkipFace)
						{
							Vector3 CurDir = new Vector3(0, 1, 0);
							Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
							SetBlockTextureUV(CurBlock.Type, CurDir, TranspVerts);
							// Front face
							TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 1, 0), new Vector2(0, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 1, 1), new Vector2(1, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 0), CurDir, FaceClr);
							// Back face
							if (needsBackface)
							{
								TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 1, 0), new Vector2(0, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 1, 1), new Vector2(1, 0), -CurDir, FaceClr);
							}
						}
						// Y--
						if (!YNegSkipFace)
						{
							Vector3 CurDir = new Vector3(0, -1, 0);
							Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
							SetBlockTextureUV(CurBlock.Type, CurDir, TranspVerts);
							// Front face
							TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 0, 1), new Vector2(1, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 0, 0), new Vector2(0, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 1), CurDir, FaceClr);
							// Back face
							if (needsBackface)
							{
								TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 0, 1), new Vector2(1, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 0, 0), new Vector2(0, 1), -CurDir, FaceClr);
							}
						}
						// Z++
						if (!ZPosSkipFace)
						{
							Vector3 CurDir = new Vector3(0, 0, 1);
							Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
							SetBlockTextureUV(CurBlock.Type, CurDir, TranspVerts);
							// Front face
							TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(1, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 1, 1), new Vector2(1, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 0, 1), new Vector2(0, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(1, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 1), CurDir, FaceClr);
							// Back face
							if (needsBackface)
							{
								TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 1, 1), new Vector2(1, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(1, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(1, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 0, 1), new Vector2(0, 0), -CurDir, FaceClr);
							}
						}
						// Z--
						if (!ZNegSkipFace)
						{
							Vector3 CurDir = new Vector3(0, 0, -1);
							Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
							SetBlockTextureUV(CurBlock.Type, CurDir, TranspVerts);
							// Front face
							TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(0, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 0, 0), new Vector2(0, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 1, 0), new Vector2(1, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(0, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 0), CurDir, FaceClr);
							// Back face
							if (needsBackface)
							{
								TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 0, 0), new Vector2(0, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(0, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(0, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 1, 0), new Vector2(1, 1), -CurDir, FaceClr);
							}
						}
					}
				}
			}

			return TranspVerts.ToMesh();
		}

		/// <summary>
		/// Generates a list of transparent faces with world positions for depth-sorted rendering.
		/// </summary>
		List<TransparentFace> GenTransparentFaces(Vector3 chunkWorldPos)
		{
			List<TransparentFace> faces = new List<TransparentFace>();

			for (int x = 0; x < ChunkSize; x++)
			{
				for (int y = 0; y < ChunkSize; y++)
				{
					for (int z = 0; z < ChunkSize; z++)
					{
						PlacedBlock CurBlock = GetBlock(x, y, z);
						if (CurBlock.Type == BlockType.None || BlockInfo.IsOpaque(CurBlock.Type))
							continue;

						Vector3 blockWorldPos = chunkWorldPos + new Vector3(x, y, z) * BlockSize;

						BlockType XPosType = GetBlock(x + 1, y, z).Type;
						BlockType XNegType = GetBlock(x - 1, y, z).Type;
						BlockType YPosType = GetBlock(x, y + 1, z).Type;
						BlockType YNegType = GetBlock(x, y - 1, z).Type;
						BlockType ZPosType = GetBlock(x, y, z + 1).Type;
						BlockType ZNegType = GetBlock(x, y, z - 1).Type;

						bool XPosSkipFace = (XPosType == CurBlock.Type);
						bool XNegSkipFace = (XNegType == CurBlock.Type);
						bool YPosSkipFace = (YPosType == CurBlock.Type);
						bool YNegSkipFace = (YNegType == CurBlock.Type);
						bool ZPosSkipFace = (ZPosType == CurBlock.Type);
						bool ZNegSkipFace = (ZNegType == CurBlock.Type);

						BlockInfo.GetBlockTexCoords(CurBlock.Type, Vector3.UnitX, out Vector2 uvSize, out Vector2 uvPos);

						// X++
						if (!XPosSkipFace)
						{
							Vector3 faceCenter = blockWorldPos + new Vector3(1, 0.5f, 0.5f);
							Color clr = CurBlock.GetBlockLight(new Vector3(1, 0, 0)).ToColor();
							BlockInfo.GetBlockTexCoords(CurBlock.Type, new Vector3(1, 0, 0), out uvSize, out uvPos);
							faces.Add(CreateFace(faceCenter, blockWorldPos, new Vector3(1, 0, 0), clr, uvPos, uvSize));
						}
						// X--
						if (!XNegSkipFace)
						{
							Vector3 faceCenter = blockWorldPos + new Vector3(0, 0.5f, 0.5f);
							Color clr = CurBlock.GetBlockLight(new Vector3(-1, 0, 0)).ToColor();
							BlockInfo.GetBlockTexCoords(CurBlock.Type, new Vector3(-1, 0, 0), out uvSize, out uvPos);
							faces.Add(CreateFace(faceCenter, blockWorldPos, new Vector3(-1, 0, 0), clr, uvPos, uvSize));
						}
						// Y++
						if (!YPosSkipFace)
						{
							Vector3 faceCenter = blockWorldPos + new Vector3(0.5f, 1, 0.5f);
							Color clr = CurBlock.GetBlockLight(new Vector3(0, 1, 0)).ToColor();
							BlockInfo.GetBlockTexCoords(CurBlock.Type, new Vector3(0, 1, 0), out uvSize, out uvPos);
							faces.Add(CreateFace(faceCenter, blockWorldPos, new Vector3(0, 1, 0), clr, uvPos, uvSize));
						}
						// Y--
						if (!YNegSkipFace)
						{
							Vector3 faceCenter = blockWorldPos + new Vector3(0.5f, 0, 0.5f);
							Color clr = CurBlock.GetBlockLight(new Vector3(0, -1, 0)).ToColor();
							BlockInfo.GetBlockTexCoords(CurBlock.Type, new Vector3(0, -1, 0), out uvSize, out uvPos);
							faces.Add(CreateFace(faceCenter, blockWorldPos, new Vector3(0, -1, 0), clr, uvPos, uvSize));
						}
						// Z++
						if (!ZPosSkipFace)
						{
							Vector3 faceCenter = blockWorldPos + new Vector3(0.5f, 0.5f, 1);
							Color clr = CurBlock.GetBlockLight(new Vector3(0, 0, 1)).ToColor();
							BlockInfo.GetBlockTexCoords(CurBlock.Type, new Vector3(0, 0, 1), out uvSize, out uvPos);
							faces.Add(CreateFace(faceCenter, blockWorldPos, new Vector3(0, 0, 1), clr, uvPos, uvSize));
						}
						// Z--
						if (!ZNegSkipFace)
						{
							Vector3 faceCenter = blockWorldPos + new Vector3(0.5f, 0.5f, 0);
							Color clr = CurBlock.GetBlockLight(new Vector3(0, 0, -1)).ToColor();
							BlockInfo.GetBlockTexCoords(CurBlock.Type, new Vector3(0, 0, -1), out uvSize, out uvPos);
							faces.Add(CreateFace(faceCenter, blockWorldPos, new Vector3(0, 0, -1), clr, uvPos, uvSize));
						}
					}
				}
			}

			return faces;
		}

		TransparentFace CreateFace(Vector3 center, Vector3 blockPos, Vector3 normal, Color clr, Vector2 uvPos, Vector2 uvSize)
		{
			Vertex3[] verts = new Vertex3[6];

			// Apply the same UV transformation as SetBlockTextureUV/MeshBuilder
			// Original: UVPos + new Vector2(0, UVSize.Y), UVSize * new Vector2(1, -1)
			Vector2 transformedUVPos = uvPos + new Vector2(0, uvSize.Y);
			Vector2 transformedUVSize = uvSize * new Vector2(1, -1);

			// UV corners after transformation (matching how MeshBuilder.Add works: UVPos + UV * UVSize)
			Vector2 uv00 = transformedUVPos + new Vector2(0, 0) * transformedUVSize;  // bottom-left
			Vector2 uv10 = transformedUVPos + new Vector2(1, 0) * transformedUVSize;  // bottom-right
			Vector2 uv11 = transformedUVPos + new Vector2(1, 1) * transformedUVSize;  // top-right
			Vector2 uv01 = transformedUVPos + new Vector2(0, 1) * transformedUVSize;  // top-left

			if (normal.X > 0)
			{ // X++
				verts[0] = new Vertex3(blockPos + new Vector3(1, 1, 0), uv11, normal, clr);
				verts[1] = new Vertex3(blockPos + new Vector3(1, 1, 1), uv01, normal, clr);
				verts[2] = new Vertex3(blockPos + new Vector3(1, 0, 1), uv00, normal, clr);
				verts[3] = new Vertex3(blockPos + new Vector3(1, 0, 0), uv10, normal, clr);
				verts[4] = new Vertex3(blockPos + new Vector3(1, 1, 0), uv11, normal, clr);
				verts[5] = new Vertex3(blockPos + new Vector3(1, 0, 1), uv00, normal, clr);
			}
			else if (normal.X < 0)
			{ // X--
				verts[0] = new Vertex3(blockPos + new Vector3(0, 1, 1), uv11, normal, clr);
				verts[1] = new Vertex3(blockPos + new Vector3(0, 1, 0), uv01, normal, clr);
				verts[2] = new Vertex3(blockPos + new Vector3(0, 0, 0), uv00, normal, clr);
				verts[3] = new Vertex3(blockPos + new Vector3(0, 0, 1), uv10, normal, clr);
				verts[4] = new Vertex3(blockPos + new Vector3(0, 1, 1), uv11, normal, clr);
				verts[5] = new Vertex3(blockPos + new Vector3(0, 0, 0), uv00, normal, clr);
			}
			else if (normal.Y > 0)
			{ // Y++
				verts[0] = new Vertex3(blockPos + new Vector3(1, 1, 0), uv11, normal, clr);
				verts[1] = new Vertex3(blockPos + new Vector3(0, 1, 0), uv01, normal, clr);
				verts[2] = new Vertex3(blockPos + new Vector3(0, 1, 1), uv00, normal, clr);
				verts[3] = new Vertex3(blockPos + new Vector3(1, 1, 1), uv10, normal, clr);
				verts[4] = new Vertex3(blockPos + new Vector3(1, 1, 0), uv11, normal, clr);
				verts[5] = new Vertex3(blockPos + new Vector3(0, 1, 1), uv00, normal, clr);
			}
			else if (normal.Y < 0)
			{ // Y--
				verts[0] = new Vertex3(blockPos + new Vector3(1, 0, 1), uv00, normal, clr);
				verts[1] = new Vertex3(blockPos + new Vector3(0, 0, 1), uv10, normal, clr);
				verts[2] = new Vertex3(blockPos + new Vector3(0, 0, 0), uv11, normal, clr);
				verts[3] = new Vertex3(blockPos + new Vector3(1, 0, 0), uv01, normal, clr);
				verts[4] = new Vertex3(blockPos + new Vector3(1, 0, 1), uv00, normal, clr);
				verts[5] = new Vertex3(blockPos + new Vector3(0, 0, 0), uv11, normal, clr);
			}
			else if (normal.Z > 0)
			{ // Z++
				verts[0] = new Vertex3(blockPos + new Vector3(1, 0, 1), uv10, normal, clr);
				verts[1] = new Vertex3(blockPos + new Vector3(1, 1, 1), uv11, normal, clr);
				verts[2] = new Vertex3(blockPos + new Vector3(0, 1, 1), uv01, normal, clr);
				verts[3] = new Vertex3(blockPos + new Vector3(0, 0, 1), uv00, normal, clr);
				verts[4] = new Vertex3(blockPos + new Vector3(1, 0, 1), uv10, normal, clr);
				verts[5] = new Vertex3(blockPos + new Vector3(0, 1, 1), uv01, normal, clr);
			}
			else
			{ // Z--
				verts[0] = new Vertex3(blockPos + new Vector3(1, 1, 0), uv01, normal, clr);
				verts[1] = new Vertex3(blockPos + new Vector3(1, 0, 0), uv00, normal, clr);
				verts[2] = new Vertex3(blockPos + new Vector3(0, 0, 0), uv10, normal, clr);
				verts[3] = new Vertex3(blockPos + new Vector3(0, 1, 0), uv11, normal, clr);
				verts[4] = new Vertex3(blockPos + new Vector3(1, 1, 0), uv01, normal, clr);
				verts[5] = new Vertex3(blockPos + new Vector3(0, 0, 0), uv10, normal, clr);
			}

			return new TransparentFace(center, verts);
		}
	}
}
