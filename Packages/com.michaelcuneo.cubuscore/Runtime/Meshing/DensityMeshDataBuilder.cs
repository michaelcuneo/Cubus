using System;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing
{
  public static class DensityMeshDataBuilder
  {
    public static MeshData Generate(
        Vector3Int chunkCoord,
        WorldGenerationSnapshot snapshot,
        int cellStep,
        bool flipWinding,
        Func<Vector3Int, DensityVoxel> sampleVoxelAtWorld)
    {
      if (sampleVoxelAtWorld == null)
      {
        return null;
      }

      int safeCellStep = Mathf.Clamp(cellStep, 1, 4);
      int numCellsAxis = Mathf.CeilToInt((float)VoxelConstants.ChunkSize / safeCellStep);
      int numSamplesAxis = numCellsAxis + 1;
      int totalSamples = numSamplesAxis * numSamplesAxis * numSamplesAxis;

      float[] densityGrid = new float[totalSamples];
      ushort[] materialGrid = new ushort[totalSamples];
      Vector3[] normalGrid = new Vector3[totalSamples];

      for (int sz = 0; sz < numSamplesAxis; sz++)
      {
        for (int sy = 0; sy < numSamplesAxis; sy++)
        {
          for (int sx = 0; sx < numSamplesAxis; sx++)
          {
            int lx = sx * safeCellStep;
            int ly = sy * safeCellStep;
            int lz = sz * safeCellStep;

            Vector3Int worldVoxel = new(
                chunkCoord.x * VoxelConstants.ChunkSize + lx,
                chunkCoord.y * VoxelConstants.ChunkSize + ly,
                chunkCoord.z * VoxelConstants.ChunkSize + lz
            );

            DensityVoxel voxel = sampleVoxelAtWorld(worldVoxel);
            int index = ToSampleIndex(sx, sy, sz, numSamplesAxis);
            densityGrid[index] = voxel.Density;
            materialGrid[index] = voxel.MaterialId;
          }
        }
      }

      BuildNormalGrid(densityGrid, normalGrid, numSamplesAxis);

      MeshData mesh = MeshDataPool.Rent(4096, 6144);

      Span<float> densities = stackalloc float[8];
      Span<Vector3> positions = stackalloc Vector3[8];
      Span<Vector3> normals = stackalloc Vector3[8];
      Span<ushort> materials = stackalloc ushort[8];
      Span<Vector3> edgeVertices = stackalloc Vector3[12];
      Span<Vector3> edgeNormals = stackalloc Vector3[12];
      Span<int> edgeIndices = stackalloc int[12];

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
              int sampleIndex = ToSampleIndex(sx, sy, sz, numSamplesAxis);

              densities[corner] = densityGrid[sampleIndex];
              normals[corner] = normalGrid[sampleIndex];
              materials[corner] = materialGrid[sampleIndex];

              positions[corner] = new Vector3(
                  sx * safeCellStep * snapshot.VoxelSize,
                  sy * safeCellStep * snapshot.VoxelSize,
                  sz * safeCellStep * snapshot.VoxelSize
              );

              if (densities[corner] > 0.0f)
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

            ushort materialId = ChooseMaterial(densities, materials);
            Color32 color = MaterialToColor(materialId);

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

              int v0 = AddOrGetEdgeVertex(mesh, e0, positions, densities, normals, edgeVertices, edgeNormals, edgeIndices, color);
              int v1 = AddOrGetEdgeVertex(mesh, e1, positions, densities, normals, edgeVertices, edgeNormals, edgeIndices, color);
              int v2 = AddOrGetEdgeVertex(mesh, e2, positions, densities, normals, edgeVertices, edgeNormals, edgeIndices, color);

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

    private static int ToSampleIndex(int sx, int sy, int sz, int samplesAxis)
    {
      return sx + samplesAxis * (sy + samplesAxis * sz);
    }

    private static void BuildNormalGrid(float[] densities, Vector3[] normals, int samplesAxis)
    {
      for (int z = 0; z < samplesAxis; z++)
      {
        for (int y = 0; y < samplesAxis; y++)
        {
          for (int x = 0; x < samplesAxis; x++)
          {
            float dx = SampleDensity(densities, samplesAxis, x + 1, y, z) - SampleDensity(densities, samplesAxis, x - 1, y, z);
            float dy = SampleDensity(densities, samplesAxis, x, y + 1, z) - SampleDensity(densities, samplesAxis, x, y - 1, z);
            float dz = SampleDensity(densities, samplesAxis, x, y, z + 1) - SampleDensity(densities, samplesAxis, x, y, z - 1);

            Vector3 normal = new Vector3(-dx, -dy, -dz);
            normals[ToSampleIndex(x, y, z, samplesAxis)] = normal.sqrMagnitude > 0.000001f
                ? normal.normalized
                : Vector3.up;
          }
        }
      }
    }

    private static float SampleDensity(float[] densities, int samplesAxis, int x, int y, int z)
    {
      x = Mathf.Clamp(x, 0, samplesAxis - 1);
      y = Mathf.Clamp(y, 0, samplesAxis - 1);
      z = Mathf.Clamp(z, 0, samplesAxis - 1);
      return densities[ToSampleIndex(x, y, z, samplesAxis)];
    }

    private static int AddOrGetEdgeVertex(
        MeshData mesh,
        int edgeIndex,
        Span<Vector3> positions,
        Span<float> densities,
        Span<Vector3> normals,
        Span<Vector3> edgeVertices,
        Span<Vector3> edgeNormals,
        Span<int> edgeIndices,
        Color32 color)
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
          densities[cornerB]
      );

      Vector3 normal = InterpolateNormal(
          normals[cornerA],
          normals[cornerB],
          densities[cornerA],
          densities[cornerB]
      );

      normal = normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.up;

      int index = mesh.Vertices.Count;
      mesh.Vertices.Add(position);
      mesh.Normals.Add(normal);
      mesh.UVs.Add(ProjectUv(position, normal));
      mesh.Colors.Add(color);

      edgeVertices[edgeIndex] = position;
      edgeNormals[edgeIndex] = normal;
      edgeIndices[edgeIndex] = index;

      return index;
    }

    private static Vector3 InterpolateVertex(Vector3 a, Vector3 b, float da, float db)
    {
      float denom = da - db;
      if (Mathf.Abs(denom) < 0.000001f)
      {
        return (a + b) * 0.5f;
      }

      float t = Mathf.Clamp01(da / (da - db));
      return a + (b - a) * t;
    }

    private static Vector3 InterpolateNormal(Vector3 a, Vector3 b, float da, float db)
    {
      float denom = da - db;
      if (Mathf.Abs(denom) < 0.000001f)
      {
        return (a + b) * 0.5f;
      }

      float t = Mathf.Clamp01(da / (da - db));
      return Vector3.Lerp(a, b, t);
    }

    private static Vector2 ProjectUv(Vector3 position, Vector3 normal)
    {
      Vector3 abs = new(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));

      if (abs.y >= abs.x && abs.y >= abs.z)
      {
        return new Vector2(position.x, position.z);
      }

      if (abs.x >= abs.y && abs.x >= abs.z)
      {
        return new Vector2(position.z, position.y);
      }

      return new Vector2(position.x, position.y);
    }

    private static ushort ChooseMaterial(Span<float> densities, Span<ushort> materials)
    {
      ushort selected = 0;
      float bestDensity = float.NegativeInfinity;

      for (int i = 0; i < 8; i++)
      {
        if (densities[i] <= 0.0f || materials[i] == 0)
        {
          continue;
        }

        if (densities[i] > bestDensity)
        {
          bestDensity = densities[i];
          selected = materials[i];
        }
      }

      return selected == 0 ? (ushort)1 : selected;
    }

    private static Color32 MaterialToColor(ushort materialId)
    {
      return new Color32(
          (byte)(materialId & 0xFF),
          (byte)((materialId >> 8) & 0xFF),
          255,
          255
      );
    }
  }
}
