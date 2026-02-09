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
			// Early-out for empty chunks
			if (NonAirBlockCount == 0)
				return new MeshBuilder().ToMesh();

			BuildPaddedCache();

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
						PlacedBlock CurBlock = _paddedBlocks[(x + 1) + PaddedSize * ((y + 1) + PaddedSize * (z + 1))];
						if (!BlockInfo.IsRendered(CurBlock.Type) || !BlockInfo.IsOpaque(CurBlock.Type))
							continue;

						if (BlockInfo.CustomModel(CurBlock.Type))
							continue;

						// Fetch all 6 neighbors once from padded cache
						PlacedBlock nXPos = PaddedGet(x + 1, y, z);
						PlacedBlock nXNeg = PaddedGet(x - 1, y, z);
						PlacedBlock nYPos = PaddedGet(x, y + 1, z);
						PlacedBlock nYNeg = PaddedGet(x, y - 1, z);
						PlacedBlock nZPos = PaddedGet(x, y, z + 1);
						PlacedBlock nZNeg = PaddedGet(x, y, z - 1);

						bool xPosOpaque = BlockInfo.IsOpaque(nXPos.Type);
						bool xNegOpaque = BlockInfo.IsOpaque(nXNeg.Type);
						bool yPosOpaque = BlockInfo.IsOpaque(nYPos.Type);
						bool yNegOpaque = BlockInfo.IsOpaque(nYNeg.Type);
						bool zPosOpaque = BlockInfo.IsOpaque(nZPos.Type);
						bool zNegOpaque = BlockInfo.IsOpaque(nZNeg.Type);

						// Fully enclosed — skip entirely
						if (xPosOpaque && xNegOpaque && yPosOpaque && yNegOpaque && zPosOpaque && zNegOpaque)
							continue;

						OpaqueVerts.SetPositionOffset(new Vector3(x, y, z) * BlockSize);

						// X++
						if (!xPosOpaque)
						{
							Vector3 CurDir = new Vector3(1, 0, 0);
							Color FaceClr = Utils.ColorMul(nXPos.GetBlockLight(new Vector3(-1, 0, 0)).ToColor(), ChunkColor);
							SetBlockTextureUV(CurBlock.Type, CurDir, OpaqueVerts);

							OpaqueVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 1, 0, -1, 1, 1, -1, 1, 1, 0, useApproxAO)));
							OpaqueVerts.Add(new Vector3(1, 1, 1), new Vector2(0, 1), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 1, 1, 0, 1, 1, 1, 1, 0, 1, useApproxAO)));
							OpaqueVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 1, -1, 0, 1, -1, 1, 1, 0, 1, useApproxAO)));
							OpaqueVerts.Add(new Vector3(1, 0, 0), new Vector2(1, 0), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 1, 0, -1, 1, -1, -1, 1, -1, 0, useApproxAO)));
							OpaqueVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 1, 0, -1, 1, 1, -1, 1, 1, 0, useApproxAO)));
							OpaqueVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 1, -1, 0, 1, -1, 1, 1, 0, 1, useApproxAO)));
						}

						// X--
						if (!xNegOpaque)
						{
							Vector3 CurDir = new Vector3(-1, 0, 0);
							Color FaceClr = nXNeg.GetBlockLight(new Vector3(1, 0, 0)).ToColor();
							SetBlockTextureUV(CurBlock.Type, CurDir, OpaqueVerts);

							OpaqueVerts.Add(new Vector3(0, 1, 1), new Vector2(1, 1), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, -1, 1, 0, -1, 1, 1, -1, 0, 1, useApproxAO)));
							OpaqueVerts.Add(new Vector3(0, 1, 0), new Vector2(0, 1), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, -1, 0, -1, -1, 1, -1, -1, 1, 0, useApproxAO)));
							OpaqueVerts.Add(new Vector3(0, 0, 0), new Vector2(0, 0), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, -1, 0, -1, -1, -1, -1, -1, -1, 0, useApproxAO)));
							OpaqueVerts.Add(new Vector3(0, 0, 1), new Vector2(1, 0), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, -1, -1, 0, -1, -1, 1, -1, 0, 1, useApproxAO)));
							OpaqueVerts.Add(new Vector3(0, 1, 1), new Vector2(1, 1), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, -1, 1, 0, -1, 1, 1, -1, 0, 1, useApproxAO)));
							OpaqueVerts.Add(new Vector3(0, 0, 0), new Vector2(0, 0), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, -1, 0, -1, -1, -1, -1, -1, -1, 0, useApproxAO)));
						}

						// Y++
						if (!yPosOpaque)
						{
							Vector3 CurDir = new Vector3(0, 1, 0);
							Color FaceClr = nYPos.GetBlockLight(new Vector3(0, -1, 0)).ToColor();
							SetBlockTextureUV(CurBlock.Type, CurDir, OpaqueVerts);

							OpaqueVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 0, 1, -1, 1, 1, -1, 1, 1, 0, useApproxAO)));
							OpaqueVerts.Add(new Vector3(0, 1, 0), new Vector2(0, 1), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 0, 1, -1, -1, 1, -1, -1, 1, 0, useApproxAO)));
							OpaqueVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 0), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, -1, 1, 0, -1, 1, 1, 0, 1, 1, useApproxAO)));
							OpaqueVerts.Add(new Vector3(1, 1, 1), new Vector2(1, 0), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 1, 1, 0, 1, 1, 1, 0, 1, 1, useApproxAO)));
							OpaqueVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 0, 1, -1, 1, 1, -1, 1, 1, 0, useApproxAO)));
							OpaqueVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 0), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, -1, 1, 0, -1, 1, 1, 0, 1, 1, useApproxAO)));
						}

						// Y--
						if (!yNegOpaque)
						{
							Vector3 CurDir = new Vector3(0, -1, 0);
							Color FaceClr = nYNeg.GetBlockLight(new Vector3(0, 1, 0)).ToColor();
							SetBlockTextureUV(CurBlock.Type, CurDir, OpaqueVerts);

							OpaqueVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 0, -1, 1, 1, -1, 1, 1, -1, 0, useApproxAO)));
							OpaqueVerts.Add(new Vector3(0, 0, 1), new Vector2(1, 0), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 0, -1, 1, -1, -1, 1, -1, -1, 0, useApproxAO)));
							OpaqueVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 1), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, -1, -1, 0, -1, -1, -1, 0, -1, -1, useApproxAO)));
							OpaqueVerts.Add(new Vector3(1, 0, 0), new Vector2(0, 1), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 1, -1, 0, 1, -1, -1, 0, -1, -1, useApproxAO)));
							OpaqueVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 0, -1, 1, 1, -1, 1, 1, -1, 0, useApproxAO)));
							OpaqueVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 1), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, -1, -1, 0, -1, -1, -1, 0, -1, -1, useApproxAO)));
						}

						// Z++
						if (!zPosOpaque)
						{
							Vector3 CurDir = new Vector3(0, 0, 1);
							Color FaceClr = nZPos.GetBlockLight(new Vector3(0, 0, -1)).ToColor();
							SetBlockTextureUV(CurBlock.Type, CurDir, OpaqueVerts);

							OpaqueVerts.Add(new Vector3(1, 0, 1), new Vector2(1, 0), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 0, -1, 1, 1, -1, 1, 1, 0, 1, useApproxAO)));
							OpaqueVerts.Add(new Vector3(1, 1, 1), new Vector2(1, 1), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 0, 1, 1, 1, 1, 1, 1, 0, 1, useApproxAO)));
							OpaqueVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 1), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 0, 1, 1, -1, 1, 1, -1, 0, 1, useApproxAO)));
							OpaqueVerts.Add(new Vector3(0, 0, 1), new Vector2(0, 0), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, -1, 0, 1, -1, -1, 1, 0, -1, 1, useApproxAO)));
							OpaqueVerts.Add(new Vector3(1, 0, 1), new Vector2(1, 0), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 0, -1, 1, 1, -1, 1, 1, 0, 1, useApproxAO)));
							OpaqueVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 1), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 0, 1, 1, -1, 1, 1, -1, 0, 1, useApproxAO)));
						}

						// Z--
						if (!zNegOpaque)
						{
							Vector3 CurDir = new Vector3(0, 0, -1);
							Color FaceClr = nZNeg.GetBlockLight(new Vector3(0, 0, 1)).ToColor();
							SetBlockTextureUV(CurBlock.Type, CurDir, OpaqueVerts);

							OpaqueVerts.Add(new Vector3(1, 1, 0), new Vector2(0, 1), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 0, 1, -1, 1, 1, -1, 1, 0, -1, useApproxAO)));
							OpaqueVerts.Add(new Vector3(1, 0, 0), new Vector2(0, 0), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 0, -1, -1, 1, -1, -1, 1, 0, -1, useApproxAO)));
							OpaqueVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 0), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 0, -1, -1, -1, -1, -1, -1, 0, -1, useApproxAO)));
							OpaqueVerts.Add(new Vector3(0, 1, 0), new Vector2(1, 1), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, -1, 0, -1, -1, 1, -1, 0, 1, -1, useApproxAO)));
							OpaqueVerts.Add(new Vector3(1, 1, 0), new Vector2(0, 1), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 0, 1, -1, 1, 1, -1, 1, 0, -1, useApproxAO)));
							OpaqueVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 0), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColorPadded(x, y, z, 0, -1, -1, -1, -1, -1, -1, 0, -1, useApproxAO)));
						}
					}

				}
			}

							return OpaqueVerts.ToMesh();
							}

							/// <summary>
							/// Bakes all foliage blocks into a single mesh by extracting vertices from the
							/// pre-loaded CustomModel variants and transforming them to chunk-local positions.
							/// Each foliage block's vertices are tinted with the block's light color.
							/// </summary>
							Mesh GenMeshFoliage()
							{
								if (NonAirBlockCount == 0)
									return new MeshBuilder().ToMesh();

								// Padded cache is already built by GenMesh (called first in RecalcModel)
								MeshBuilder foliageVerts = new MeshBuilder();

								for (int x = 0; x < ChunkSize; x++)
								{
									for (int y = 0; y < ChunkSize; y++)
									{
										for (int z = 0; z < ChunkSize; z++)
										{
											PlacedBlock curBlock = _paddedBlocks[(x + 1) + PaddedSize * ((y + 1) + PaddedSize * (z + 1))];
											if (curBlock.Type != BlockType.Foliage)
												continue;

											// Sample light from the foliage block (non-opaque, so light propagates into it)
											Color lightColor = curBlock.GetBlockLight(Vector3.UnitY).ToColor();

											// Compute global position for deterministic variant hash
											int gx = (int)(GlobalChunkIndex.X * ChunkSize) + x;
											int gy = (int)(GlobalChunkIndex.Y * ChunkSize) + y;
											int gz = (int)(GlobalChunkIndex.Z * ChunkSize) + z;

											CustomModel model = BlockInfo.GetBlockJsonModel(BlockType.Foliage, gx, gy, gz);

											// Center model in block (matching the +0.5 XZ offset used by custom model drawing)
											Matrix4x4 blockMatrix = Matrix4x4.CreateTranslation(new Vector3(x + 0.5f, y, z + 0.5f));

											foreach (CustomMesh cmesh in model.Meshes)
											{
												Matrix4x4 worldMatrix = cmesh.Matrix * cmesh.GetCombinedAnimationMatrix() * blockMatrix;

												int vertCount = cmesh.Mesh.VertexCount;
												for (int v = 0; v < vertCount; v++)
												{
													Vector3 pos = Vector3.Transform(cmesh.Mesh.VerticesAs<Vector3>()[v], worldMatrix);
													Vector2 uv = cmesh.Mesh.TexCoordsAs<Vector2>()[v];
													Vector3 normal = Vector3.TransformNormal(cmesh.Mesh.NormalsAs<Vector3>()[v], worldMatrix);

													foliageVerts.Add(new Vertex3(pos, uv, normal, lightColor));
												}
											}
										}
									}
								}

								return foliageVerts.ToMesh();
							}
						}
					}
