using Raylib_cs;
using System.Numerics;

using Voxelgine.Engine;

using System;

namespace Voxelgine.Graphics
{
	public unsafe partial class Chunk
	{
		Mesh GenMesh(Vector3? cameraChunkIndex = null, float aoApproxDistance = 6f)
		{
			MeshBuilder OpaqueVerts = new MeshBuilder();

			Vector3 Size = new Vector3(BlockSize);

			Color AOColor = new Color(128, 128, 128);
			AOColor = Utils.ColorMul(AOColor, AOColor);

			Vector3 chunkIdx = GlobalChunkIndex;
			bool useApproxAO = false;

			if (cameraChunkIndex != null)
				useApproxAO = IsDistantChunk(chunkIdx, cameraChunkIndex.Value, aoApproxDistance);

			for (int x = 0; x < ChunkSize; x++)
			{
				for (int y = 0; y < ChunkSize; y++)
				{
					for (int z = 0; z < ChunkSize; z++)
					{
						WorldMap.GetWorldPos(x, y, z, GlobalChunkIndex, out Vector3 GlobalBlockPos);

						PlacedBlock CurBlock = null;
						if ((CurBlock = GetBlock(x, y, z)).Type != BlockType.None)
						{
							// --- Optimization: skip face culling for fully enclosed opaque blocks ---
							if (BlockInfo.IsOpaque(CurBlock.Type)
								&& BlockInfo.IsOpaque(GetBlock(x + 1, y, z).Type)
								&& BlockInfo.IsOpaque(GetBlock(x - 1, y, z).Type)
								&& BlockInfo.IsOpaque(GetBlock(x, y + 1, z).Type)
								&& BlockInfo.IsOpaque(GetBlock(x, y - 1, z).Type)
								&& BlockInfo.IsOpaque(GetBlock(x, y, z + 1).Type)
								&& BlockInfo.IsOpaque(GetBlock(x, y, z - 1).Type))
							{
								// All neighbors are opaque, skip this block
								continue;
							}
							OpaqueVerts.SetPositionOffset(new Vector3(x, y, z) * BlockSize);

							BlockType XPosType = GetBlock(x + 1, y, z).Type;
							BlockType XNegType = GetBlock(x - 1, y, z).Type;
							BlockType YPosType = GetBlock(x, y + 1, z).Type;
							BlockType YNegType = GetBlock(x, y - 1, z).Type;
							BlockType ZPosType = GetBlock(x, y, z + 1).Type;
							BlockType ZNegType = GetBlock(x, y, z - 1).Type;

							if (!BlockInfo.IsOpaque(CurBlock.Type))
								continue;

							bool XPosSkipFace = false;
							bool XNegSkipFace = false;
							bool YPosSkipFace = false;
							bool YNegSkipFace = false;
							bool ZPosSkipFace = false;
							bool ZNegSkipFace = false;

							if (BlockInfo.IsOpaque(CurBlock.Type))
							{
								XPosSkipFace = BlockInfo.IsOpaque(XPosType);
								XNegSkipFace = BlockInfo.IsOpaque(XNegType);
								YPosSkipFace = BlockInfo.IsOpaque(YPosType);
								YNegSkipFace = BlockInfo.IsOpaque(YNegType);
								ZPosSkipFace = BlockInfo.IsOpaque(ZPosType);
								ZNegSkipFace = BlockInfo.IsOpaque(ZNegType);
							}

							if (BlockInfo.CustomModel(CurBlock.Type))
							{
								if (!XPosSkipFace || !XNegSkipFace || !YPosSkipFace || !YNegSkipFace || !ZPosSkipFace || !ZNegSkipFace)
								{

									Model Mdl = BlockInfo.GetCustomModel(CurBlock.Type);

									SetBlockTextureUV(CurBlock.Type, Vector3.UnitY, OpaqueVerts);

									for (int j = 0; j < Mdl.MeshCount; j++)
									{
										for (int i = 0; i < Mdl.Meshes[j].VertexCount; i++)
										{
											Vector3 Vert = ((Vector3*)Mdl.Meshes[j].Vertices)[i];
											Vector2 UV = new Vector2(0, 1) + ((Vector2*)Mdl.Meshes[j].TexCoords)[i] * new Vector2(1, -1);
											OpaqueVerts.Add(Vert + new Vector3(0.5f, 0, 0.5f), UV, Vector3.Zero, Color.White);
										}
									}



									Logging.WriteLine("!");

								}
							}
							else
							{

								// X++
								if (!XPosSkipFace)
								{
									Vector3 CurDir = new Vector3(1, 0, 0);
									Color FaceClr = Utils.ColorMul(GetFaceLightColor(x, y, z, CurDir), ChunkColor);
									SetBlockTextureUV(CurBlock.Type, CurDir, OpaqueVerts);

									OpaqueVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, 0, -1), new Vector3(1, 1, -1), new Vector3(1, 1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 1, 1), new Vector2(0, 1), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, -1, 0), new Vector3(1, -1, 1), new Vector3(1, 0, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 0, 0), new Vector2(1, 0), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, 0, -1), new Vector3(1, -1, -1), new Vector3(1, -1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, 0, -1), new Vector3(1, 1, -1), new Vector3(1, 1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, -1, 0), new Vector3(1, -1, 1), new Vector3(1, 0, 1), useApproxAO)));
								}

								// X--
								if (!XNegSkipFace)
								{
									Vector3 CurDir = new Vector3(-1, 0, 0);
									Color FaceClr = GetFaceLightColor(x, y, z, CurDir);
									SetBlockTextureUV(CurBlock.Type, CurDir, OpaqueVerts);

									OpaqueVerts.Add(new Vector3(0, 1, 1), new Vector2(1, 1), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 1, 0), new Vector3(-1, 1, 1), new Vector3(-1, 0, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 1, 0), new Vector2(0, 1), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 0, -1), new Vector3(-1, 1, -1), new Vector3(-1, 1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 0, 0), new Vector2(0, 0), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 0, -1), new Vector3(-1, -1, -1), new Vector3(-1, -1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 0, 1), new Vector2(1, 0), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, -1, 0), new Vector3(-1, -1, 1), new Vector3(-1, 0, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 1, 1), new Vector2(1, 1), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 1, 0), new Vector3(-1, 1, 1), new Vector3(-1, 0, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 0, 0), new Vector2(0, 0), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 0, -1), new Vector3(-1, -1, -1), new Vector3(-1, -1, 0), useApproxAO)));
								}

								// Y++
								if (!YPosSkipFace)
								{
									Vector3 CurDir = new Vector3(0, 1, 0);
									Color FaceClr = GetFaceLightColor(x, y, z, CurDir);
									SetBlockTextureUV(CurBlock.Type, CurDir, OpaqueVerts);

									OpaqueVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, -1), new Vector3(1, 1, -1), new Vector3(1, 1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 1, 0), new Vector2(0, 1), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, -1), new Vector3(-1, 1, -1), new Vector3(-1, 1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 0), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 1, 0), new Vector3(-1, 1, 1), new Vector3(0, 1, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 1, 1), new Vector2(1, 0), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(0, 1, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, -1), new Vector3(1, 1, -1), new Vector3(1, 1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 0), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 1, 0), new Vector3(-1, 1, 1), new Vector3(0, 1, 1), useApproxAO)));
								}

								// Y--
								if (!YNegSkipFace)
								{
									Vector3 CurDir = new Vector3(0, -1, 0);
									Color FaceClr = GetFaceLightColor(x, y, z, CurDir);
									SetBlockTextureUV(CurBlock.Type, CurDir, OpaqueVerts);

									OpaqueVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, 1), new Vector3(1, -1, 1), new Vector3(1, -1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 0, 1), new Vector2(1, 0), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, 1), new Vector3(-1, -1, 1), new Vector3(-1, -1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 1), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, -1, 0), new Vector3(-1, -1, -1), new Vector3(0, -1, -1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 0, 0), new Vector2(0, 1), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, -1, 0), new Vector3(1, -1, -1), new Vector3(0, -1, -1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, 1), new Vector3(1, -1, 1), new Vector3(1, -1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 1), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, -1, 0), new Vector3(-1, -1, -1), new Vector3(0, -1, -1), useApproxAO)));
								}

								// Z++
								if (!ZPosSkipFace)
								{
									Vector3 CurDir = new Vector3(0, 0, 1);
									Color FaceClr = GetFaceLightColor(x, y, z, CurDir);
									SetBlockTextureUV(CurBlock.Type, CurDir, OpaqueVerts);

									OpaqueVerts.Add(new Vector3(1, 0, 1), new Vector2(1, 0), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 0, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 1, 1), new Vector2(1, 1), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 0, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 1), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, 1), new Vector3(-1, 1, 1), new Vector3(-1, 0, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 0, 1), new Vector2(0, 0), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 0, 1), new Vector3(-1, -1, 1), new Vector3(0, -1, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 0, 1), new Vector2(1, 0), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 0, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 1), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, 1), new Vector3(-1, 1, 1), new Vector3(-1, 0, 1), useApproxAO)));
								}

								// Z--
								if (!ZNegSkipFace)
								{
									Vector3 CurDir = new Vector3(0, 0, -1);
									Color FaceClr = GetFaceLightColor(x, y, z, CurDir);
									SetBlockTextureUV(CurBlock.Type, CurDir, OpaqueVerts);

									OpaqueVerts.Add(new Vector3(1, 1, 0), new Vector2(0, 1), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, -1), new Vector3(1, 1, -1), new Vector3(1, 0, -1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 0, 0), new Vector2(0, 0), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, -1), new Vector3(1, -1, -1), new Vector3(1, 0, -1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 0), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, -1), new Vector3(-1, -1, -1), new Vector3(-1, 0, -1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 1, 0), new Vector2(1, 1), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 0, -1), new Vector3(-1, 1, -1), new Vector3(0, 1, -1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 1, 0), new Vector2(0, 1), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, -1), new Vector3(1, 1, -1), new Vector3(1, 0, -1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 0), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, -1), new Vector3(-1, -1, -1), new Vector3(-1, 0, -1), useApproxAO)));
								}
							}
						}

					}
				}
			}

			return OpaqueVerts.ToMesh();
		}
	}
}
