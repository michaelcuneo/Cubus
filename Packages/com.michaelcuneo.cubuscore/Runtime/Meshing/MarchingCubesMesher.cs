using System;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing
{
  public static class MarchingCubesMesher
  {
    private struct MaterialBlend
    {
      public ushort Id0;
      public ushort Id1;
      public ushort Id2;
      public ushort Id3;

      public float W0;
      public float W1;
      public float W2;
      public float W3;
    }

    public static void DisposePersistent()
    {
      // This mesher no longer owns persistent native buffers.
    }

    public static Mesh GenerateMeshDirect(
      Vector3Int chunkCoord,
      WorldGenerationSnapshot snapshot,
      int cellStep,
      bool flipWinding = true,
      Func<Vector3Int, DensityVoxel> sampleVoxelAtWorld = null)
    {
      MeshData meshData = GenerateMeshData(chunkCoord, snapshot, cellStep, flipWinding, sampleVoxelAtWorld);

      if (meshData == null || meshData.IsEmpty)
      {
        if (meshData != null)
        {
          MeshDataPool.Return(meshData);
        }

        return null;
      }

      Mesh mesh = meshData.ToUnityMeshFast();
      mesh.name = $"Density Chunk {chunkCoord}";
      MeshDataPool.Return(meshData);
      return mesh;
    }

    public static MeshData GenerateMeshData(
      Vector3Int chunkCoord,
      WorldGenerationSnapshot snapshot,
      int cellStep,
      bool flipWinding,
      Func<Vector3Int, DensityVoxel> sampleVoxelAtWorld)
    {
      int safeCellStep = Mathf.Clamp(cellStep, 1, 8);
      int numCellsAxis = Mathf.CeilToInt((float)VoxelConstants.ChunkSize / safeCellStep);
      int numSamplesAxis = numCellsAxis + 1;
      int totalSamples = numSamplesAxis * numSamplesAxis * numSamplesAxis;

      float[] densityGrid = new float[totalSamples];
      ushort[] materialGrid = new ushort[totalSamples];

      for (int sz = 0; sz < numSamplesAxis; sz++)
      {
        for (int sy = 0; sy < numSamplesAxis; sy++)
        {
          for (int sx = 0; sx < numSamplesAxis; sx++)
          {
            int lx = sx * safeCellStep;
            int ly = sy * safeCellStep;
            int lz = sz * safeCellStep;
            int index = GridIndex(sx, sy, sz, numSamplesAxis);

            if (sampleVoxelAtWorld != null)
            {
              Vector3Int worldVoxel = new(
                chunkCoord.x * VoxelConstants.ChunkSize + lx,
                chunkCoord.y * VoxelConstants.ChunkSize + ly,
                chunkCoord.z * VoxelConstants.ChunkSize + lz);

              DensityVoxel voxel = sampleVoxelAtWorld(worldVoxel);
              densityGrid[index] = voxel.Density;
              materialGrid[index] = voxel.Density > 0.0f
                ? (ushort)Mathf.Clamp(voxel.MaterialId, 1, 65535)
                : (ushort)0;
            }
            else
            {
              SampleTerrain(snapshot, chunkCoord, lx, ly, lz, out float density, out ushort materialId);
              densityGrid[index] = density;
              materialGrid[index] = density > 0.0f ? materialId : (ushort)0;
            }
          }
        }
      }

      int estimatedVerts = Mathf.Max(256, numCellsAxis * numCellsAxis * numCellsAxis * 3);
      int estimatedIndices = Mathf.Max(384, numCellsAxis * numCellsAxis * numCellsAxis * 6);
      MeshData mesh = MeshDataPool.Rent(estimatedVerts, estimatedIndices);

      float[] densities = new float[8];
      ushort[] materials = new ushort[8];
      Vector3[] positions = new Vector3[8];
      Vector3[] normals = new Vector3[8];
      Vector3[] edgeVertices = new Vector3[12];
      int[] edgeIndices = new int[12];

      for (int z = 0; z < numCellsAxis; z++)
      {
        for (int y = 0; y < numCellsAxis; y++)
        {
          for (int x = 0; x < numCellsAxis; x++)
          {
            int cubeIndex = 0;

            for (int corner = 0; corner < 8; corner++)
            {
              int sx = x + MarchingCubesTables.CubeCornerOffset[corner, 0];
              int sy = y + MarchingCubesTables.CubeCornerOffset[corner, 1];
              int sz = z + MarchingCubesTables.CubeCornerOffset[corner, 2];
              int sampleIndex = GridIndex(sx, sy, sz, numSamplesAxis);

              float density = densityGrid[sampleIndex];
              densities[corner] = density;
              materials[corner] = materialGrid[sampleIndex];
              normals[corner] = ComputeNormal(densityGrid, sx, sy, sz, numSamplesAxis);
              positions[corner] = new Vector3(
                sx * safeCellStep * snapshot.VoxelSize,
                sy * safeCellStep * snapshot.VoxelSize,
                sz * safeCellStep * snapshot.VoxelSize);

              if (density > 0.0f)
              {
                cubeIndex |= 1 << corner;
              }
            }

            if (cubeIndex == 0 || cubeIndex == 255)
            {
              continue;
            }

            if (MarchingCubesTables.TriangleTable[cubeIndex, 0] < 0)
            {
              continue;
            }

            for (int i = 0; i < 12; i++)
            {
              edgeIndices[i] = -1;
            }

            MaterialBlend slotBlend = BuildMaterialBlend(densities, materials);

            for (int t = 0; t < 16; t += 3)
            {
              int e0 = MarchingCubesTables.TriangleTable[cubeIndex, t + 0];
              if (e0 < 0)
              {
                break;
              }

              int e1 = MarchingCubesTables.TriangleTable[cubeIndex, t + 1];
              int e2 = MarchingCubesTables.TriangleTable[cubeIndex, t + 2];

              if (e1 < 0 || e2 < 0 || e0 >= 12 || e1 >= 12 || e2 >= 12)
              {
                break;
              }

              int v0 = AddOrGetEdgeVertex(mesh, e0, positions, densities, normals, materials, slotBlend, edgeVertices, edgeIndices);
              int v1 = AddOrGetEdgeVertex(mesh, e1, positions, densities, normals, materials, slotBlend, edgeVertices, edgeIndices);
              int v2 = AddOrGetEdgeVertex(mesh, e2, positions, densities, normals, materials, slotBlend, edgeVertices, edgeIndices);

              if (v0 < 0 || v1 < 0 || v2 < 0 || v0 == v1 || v1 == v2 || v0 == v2)
              {
                continue;
              }

              if (flipWinding)
              {
                mesh.Triangles.Add(v0);
                mesh.Triangles.Add(v2);
                mesh.Triangles.Add(v1);
              }
              else
              {
                mesh.Triangles.Add(v0);
                mesh.Triangles.Add(v1);
                mesh.Triangles.Add(v2);
              }
            }
          }
        }
      }

      if (mesh.IsEmpty)
      {
        MeshDataPool.Return(mesh);
        return null;
      }

      return mesh;
    }

    private static int GridIndex(int sx, int sy, int sz, int numSamplesAxis)
    {
      return sx + numSamplesAxis * (sy + numSamplesAxis * sz);
    }

    private static void SampleTerrain(
      WorldGenerationSnapshot snapshot,
      Vector3Int chunkCoord,
      int lx,
      int ly,
      int lz,
      out float density,
      out ushort materialId)
    {
      int wx = chunkCoord.x * VoxelConstants.ChunkSize + lx;
      int wy = chunkCoord.y * VoxelConstants.ChunkSize + ly;
      int wz = chunkCoord.z * VoxelConstants.ChunkSize + lz;

      double tWx = wx;
      double tWy = wz;
      double tWz = wy;
      double sampleScale = snapshot.DensitySampleScale <= 0.0f ? 1.0 : snapshot.DensitySampleScale;

      double sampleX = tWx * sampleScale;
      double sampleY = tWy * sampleScale;
      double sampleZ = tWz * sampleScale;

      double surfaceHeight = TerrainHeight.ComputeSurfaceHeight(snapshot.TerrainProfile, sampleX, sampleY);
      double d = surfaceHeight - sampleZ;

      if (snapshot.TerrainProfile.CaveStrength > 0.0f)
      {
        d -= TerrainCaves.CarveAmount(snapshot.TerrainProfile, sampleX, sampleY, sampleZ, surfaceHeight);
      }

      density = (float)d;

      if (density <= 0.0f)
      {
        materialId = 0;
        return;
      }

      float depthBelowSurface = (float)(surfaceHeight - sampleZ);
      int selected = snapshot.TerrainProfile.GetMaterialId(depthBelowSurface, sampleX, sampleY, sampleZ);
      materialId = (ushort)Mathf.Clamp(selected, 1, 65535);
    }

    private static Vector3 ComputeNormal(float[] densityGrid, int sx, int sy, int sz, int numSamplesAxis)
    {
      int sxL = Mathf.Max(sx - 1, 0);
      int sxR = Mathf.Min(sx + 1, numSamplesAxis - 1);
      int syD = Mathf.Max(sy - 1, 0);
      int syU = Mathf.Min(sy + 1, numSamplesAxis - 1);
      int szB = Mathf.Max(sz - 1, 0);
      int szF = Mathf.Min(sz + 1, numSamplesAxis - 1);

      float dL = densityGrid[GridIndex(sxL, sy, sz, numSamplesAxis)];
      float dR = densityGrid[GridIndex(sxR, sy, sz, numSamplesAxis)];
      float dD = densityGrid[GridIndex(sx, syD, sz, numSamplesAxis)];
      float dU = densityGrid[GridIndex(sx, syU, sz, numSamplesAxis)];
      float dB = densityGrid[GridIndex(sx, sy, szB, numSamplesAxis)];
      float dF = densityGrid[GridIndex(sx, sy, szF, numSamplesAxis)];

      Vector3 normal = new(dL - dR, dD - dU, dB - dF);
      return normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.up;
    }

    private static int AddOrGetEdgeVertex(
      MeshData mesh,
      int edgeIndex,
      Vector3[] positions,
      float[] densities,
      Vector3[] normals,
      ushort[] materials,
      MaterialBlend slotBlend,
      Vector3[] edgeVertices,
      int[] edgeIndices)
    {
      if (edgeIndices[edgeIndex] >= 0)
      {
        return edgeIndices[edgeIndex];
      }

      int cornerA = MarchingCubesTables.EdgeConnection[edgeIndex, 0];
      int cornerB = MarchingCubesTables.EdgeConnection[edgeIndex, 1];

      Vector3 position = InterpolateVertex(
        positions[cornerA],
        positions[cornerB],
        densities[cornerA],
        densities[cornerB]);

      Vector3 normal = InterpolateNormal(
        normals[cornerA],
        normals[cornerB],
        densities[cornerA],
        densities[cornerB]);

      MaterialBlend edgeBlend = BuildEdgeMaterialBlend(
        slotBlend,
        materials[cornerA],
        materials[cornerB],
        densities[cornerA],
        densities[cornerB]);

      int index = mesh.Vertices.Count;
      mesh.Vertices.Add(position);
      mesh.Normals.Add(normal);
      mesh.Colors.Add(MaterialBlendToColor(edgeBlend));
      mesh.UVs.Add(MaterialBlendIds01(edgeBlend));
      mesh.UV1s.Add(MaterialBlendIds23(edgeBlend));

      edgeVertices[edgeIndex] = position;
      edgeIndices[edgeIndex] = index;
      return index;
    }

    private static MaterialBlend BuildMaterialBlend(float[] densities, ushort[] materials)
    {
      MaterialBlend blend = default;

      for (int i = 0; i < 8; i++)
      {
        ushort materialId = materials[i];

        if (materialId == 0 || densities[i] <= 0.0f)
        {
          continue;
        }

        AddMaterialWeight(ref blend, materialId, Mathf.Max(0.001f, densities[i]));
      }

      NormalizeMaterialBlend(ref blend);
      return blend;
    }

    private static MaterialBlend BuildEdgeMaterialBlend(
      MaterialBlend slotBlend,
      ushort materialA,
      ushort materialB,
      float densityA,
      float densityB)
    {
      MaterialBlend blend = new()
      {
        Id0 = slotBlend.Id0,
        Id1 = slotBlend.Id1,
        Id2 = slotBlend.Id2,
        Id3 = slotBlend.Id3
      };

      float t = EdgeInterpolationT(densityA, densityB);

      if (materialA != 0)
      {
        AddMaterialWeight(ref blend, materialA, 1.0f - t);
      }

      if (materialB != 0)
      {
        AddMaterialWeight(ref blend, materialB, t);
      }

      NormalizeMaterialBlend(ref blend);
      return blend;
    }

    private static void AddMaterialWeight(ref MaterialBlend blend, ushort materialId, float weight)
    {
      if (materialId == 0 || weight <= 0.0f)
      {
        return;
      }

      if (blend.Id0 == materialId)
      {
        blend.W0 += weight;
        return;
      }

      if (blend.Id1 == materialId)
      {
        blend.W1 += weight;
        return;
      }

      if (blend.Id2 == materialId)
      {
        blend.W2 += weight;
        return;
      }

      if (blend.Id3 == materialId)
      {
        blend.W3 += weight;
        return;
      }

      if (blend.Id0 == 0)
      {
        blend.Id0 = materialId;
        blend.W0 = weight;
        return;
      }

      if (blend.Id1 == 0)
      {
        blend.Id1 = materialId;
        blend.W1 = weight;
        return;
      }

      if (blend.Id2 == 0)
      {
        blend.Id2 = materialId;
        blend.W2 = weight;
        return;
      }

      if (blend.Id3 == 0)
      {
        blend.Id3 = materialId;
        blend.W3 = weight;
        return;
      }

      if (weight > blend.W0 && blend.W0 <= blend.W1 && blend.W0 <= blend.W2 && blend.W0 <= blend.W3)
      {
        blend.Id0 = materialId;
        blend.W0 = weight;
        return;
      }

      if (weight > blend.W1 && blend.W1 <= blend.W0 && blend.W1 <= blend.W2 && blend.W1 <= blend.W3)
      {
        blend.Id1 = materialId;
        blend.W1 = weight;
        return;
      }

      if (weight > blend.W2 && blend.W2 <= blend.W0 && blend.W2 <= blend.W1 && blend.W2 <= blend.W3)
      {
        blend.Id2 = materialId;
        blend.W2 = weight;
        return;
      }

      if (weight > blend.W3)
      {
        blend.Id3 = materialId;
        blend.W3 = weight;
      }
    }

    private static void NormalizeMaterialBlend(ref MaterialBlend blend)
    {
      float total = blend.W0 + blend.W1 + blend.W2 + blend.W3;

      if (total <= 0.000001f)
      {
        if (blend.Id0 == 0)
        {
          blend.Id0 = 1;
        }

        blend.W0 = 1.0f;
        blend.W1 = 0.0f;
        blend.W2 = 0.0f;
        blend.W3 = 0.0f;
        return;
      }

      float inv = 1.0f / total;
      blend.W0 *= inv;
      blend.W1 *= inv;
      blend.W2 *= inv;
      blend.W3 *= inv;
    }

    private static Color32 MaterialBlendToColor(MaterialBlend blend)
    {
      return new Color32(
        (byte)Mathf.Clamp(Mathf.RoundToInt(blend.W0 * 255.0f), 0, 255),
        (byte)Mathf.Clamp(Mathf.RoundToInt(blend.W1 * 255.0f), 0, 255),
        (byte)Mathf.Clamp(Mathf.RoundToInt(blend.W2 * 255.0f), 0, 255),
        (byte)Mathf.Clamp(Mathf.RoundToInt(blend.W3 * 255.0f), 0, 255));
    }

    private static Vector2 MaterialBlendIds01(MaterialBlend blend)
    {
      return new Vector2(blend.Id0, blend.Id1);
    }

    private static Vector2 MaterialBlendIds23(MaterialBlend blend)
    {
      return new Vector2(blend.Id2, blend.Id3);
    }

    private static float EdgeInterpolationT(float d0, float d1)
    {
      float denominator = d0 - d1;

      if (Mathf.Abs(denominator) < 0.000001f)
      {
        return 0.5f;
      }

      return Mathf.Clamp01(d0 / denominator);
    }

    private static Vector3 InterpolateVertex(Vector3 p0, Vector3 p1, float d0, float d1)
    {
      return p0 + (p1 - p0) * EdgeInterpolationT(d0, d1);
    }

    private static Vector3 InterpolateNormal(Vector3 n0, Vector3 n1, float d0, float d1)
    {
      Vector3 normal = n0 + (n1 - n0) * EdgeInterpolationT(d0, d1);
      return normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.up;
    }
  }
}
