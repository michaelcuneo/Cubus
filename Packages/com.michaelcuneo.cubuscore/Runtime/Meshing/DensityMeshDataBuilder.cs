using System;
using System.Buffers;
using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing
{
  public static class DensityMeshDataBuilder
  {
    public static MeshData Generate(Vector3Int chunkCoord, WorldGenerationSnapshot snapshot, int cellStep, bool flipWinding, Func<Vector3Int, DensityVoxel> sampleVoxelAtWorld)
    {
      if (sampleVoxelAtWorld == null) return null;

      int safeCellStep = Mathf.Clamp(cellStep, 1, 4);
      int numCellsAxis = Mathf.CeilToInt((float)VoxelConstants.ChunkSize / safeCellStep);
      int numSamplesAxis = numCellsAxis + 1;
      int totalSamples = numSamplesAxis * numSamplesAxis * numSamplesAxis;
      float[] densityGrid = ArrayPool<float>.Shared.Rent(totalSamples);
      ushort[] materialGrid = ArrayPool<ushort>.Shared.Rent(totalSamples);

      try
      {
        for (int sz = 0; sz < numSamplesAxis; sz++)
        for (int sy = 0; sy < numSamplesAxis; sy++)
        for (int sx = 0; sx < numSamplesAxis; sx++)
        {
          int lx = sx * safeCellStep;
          int ly = sy * safeCellStep;
          int lz = sz * safeCellStep;
          Vector3Int worldVoxel = new(chunkCoord.x * VoxelConstants.ChunkSize + lx, chunkCoord.y * VoxelConstants.ChunkSize + ly, chunkCoord.z * VoxelConstants.ChunkSize + lz);
          DensityVoxel voxel = sampleVoxelAtWorld(worldVoxel);
          int index = ToSampleIndex(sx, sy, sz, numSamplesAxis);
          densityGrid[index] = voxel.Density;
          materialGrid[index] = voxel.MaterialId;
        }

        return BuildFromGrids(densityGrid, materialGrid, numCellsAxis, safeCellStep * snapshot.VoxelSize, flipWinding);
      }
      finally
      {
        ArrayPool<float>.Shared.Return(densityGrid);
        ArrayPool<ushort>.Shared.Return(materialGrid);
      }
    }

    public static MeshData GenerateFromChunkSnapshots(Vector3Int chunkCoord, WorldGenerationSnapshot snapshot, int cellStep, bool flipWinding, DensityChunkData rootChunkData, IReadOnlyDictionary<Vector3Int, DensityChunkData> chunkDataSnapshots, Func<Vector3Int, DensityVoxel> fallbackSampleVoxelAtWorld)
    {
      if (rootChunkData == null) return Generate(chunkCoord, snapshot, cellStep, flipWinding, fallbackSampleVoxelAtWorld);

      const int chunkSize = VoxelConstants.ChunkSize;
      int safeCellStep = Mathf.Clamp(cellStep, 1, 4);
      int numCellsAxis = Mathf.CeilToInt((float)chunkSize / safeCellStep);
      int numSamplesAxis = numCellsAxis + 1;
      int totalSamples = numSamplesAxis * numSamplesAxis * numSamplesAxis;
      int baseWorldX = chunkCoord.x * chunkSize;
      int baseWorldY = chunkCoord.y * chunkSize;
      int baseWorldZ = chunkCoord.z * chunkSize;
      DensityVoxel[] rootVoxels = rootChunkData.GetRawVoxelArray();
      float[] densityGrid = ArrayPool<float>.Shared.Rent(totalSamples);
      ushort[] materialGrid = ArrayPool<ushort>.Shared.Rent(totalSamples);

      try
      {
        for (int sz = 0; sz < numSamplesAxis; sz++)
        {
          int lz = sz * safeCellStep;
          for (int sy = 0; sy < numSamplesAxis; sy++)
          {
            int ly = sy * safeCellStep;
            for (int sx = 0; sx < numSamplesAxis; sx++)
            {
              int lx = sx * safeCellStep;
              DensityVoxel voxel;
              if (lx < chunkSize && ly < chunkSize && lz < chunkSize)
              {
                voxel = rootVoxels[lx + chunkSize * (ly + chunkSize * lz)];
              }
              else
              {
                Vector3Int worldVoxel = new(baseWorldX + lx, baseWorldY + ly, baseWorldZ + lz);
                voxel = SampleSnapshotOrFallback(chunkDataSnapshots, fallbackSampleVoxelAtWorld, worldVoxel);
              }

              int index = ToSampleIndex(sx, sy, sz, numSamplesAxis);
              densityGrid[index] = voxel.Density;
              materialGrid[index] = voxel.MaterialId;
            }
          }
        }

        return BuildFromGrids(densityGrid, materialGrid, numCellsAxis, safeCellStep * snapshot.VoxelSize, flipWinding);
      }
      finally
      {
        ArrayPool<float>.Shared.Return(densityGrid);
        ArrayPool<ushort>.Shared.Return(materialGrid);
      }
    }

    private static DensityVoxel SampleSnapshotOrFallback(IReadOnlyDictionary<Vector3Int, DensityChunkData> chunkDataSnapshots, Func<Vector3Int, DensityVoxel> fallbackSampleVoxelAtWorld, Vector3Int worldVoxel)
    {
      Vector3Int sampleChunkCoord = VoxelMath.WorldVoxelToChunkCoord(worldVoxel);
      Vector3Int localCoord = VoxelMath.WorldVoxelToLocalCoord(worldVoxel);
      if (chunkDataSnapshots != null && chunkDataSnapshots.TryGetValue(sampleChunkCoord, out DensityChunkData chunkData) && chunkData != null && chunkData.IsInBounds(localCoord.x, localCoord.y, localCoord.z))
      {
        return chunkData.GetVoxel(localCoord.x, localCoord.y, localCoord.z);
      }

      return fallbackSampleVoxelAtWorld != null ? fallbackSampleVoxelAtWorld(worldVoxel) : DensityVoxel.Empty;
    }

    public static MeshData BuildFromGrids(float[] densityGrid, ushort[] materialGrid, int numCellsAxis, float cellWorldSize, bool flipWinding)
    {
      if (densityGrid == null || materialGrid == null || numCellsAxis <= 0) return null;

      int numSamplesAxis = numCellsAxis + 1;
      int samplesAxisSquared = numSamplesAxis * numSamplesAxis;
      int totalSamples = numSamplesAxis * numSamplesAxis * numSamplesAxis;
      Vector3[] normalCache = ArrayPool<Vector3>.Shared.Rent(totalSamples);
      bool[] normalComputed = ArrayPool<bool>.Shared.Rent(totalSamples);
      Array.Clear(normalComputed, 0, totalSamples);
      MeshData mesh = MeshDataPool.Rent(4096, 6144);

      try
      {
        Span<float> densities = stackalloc float[8];
        Span<Vector3> positions = stackalloc Vector3[8];
        Span<Vector3> normals = stackalloc Vector3[8];
        Span<ushort> materials = stackalloc ushort[8];
        Span<Vector3> edgeVertices = stackalloc Vector3[12];
        Span<Vector3> edgeNormals = stackalloc Vector3[12];
        Span<int> edgeIndices = stackalloc int[12];

        for (int z = 0; z < numCellsAxis; z++)
        for (int y = 0; y < numCellsAxis; y++)
        for (int x = 0; x < numCellsAxis; x++)
        {
          int i000 = x + numSamplesAxis * (y + numSamplesAxis * z);
          int i100 = i000 + 1;
          int i010 = i000 + numSamplesAxis;
          int i110 = i010 + 1;
          int i001 = i000 + samplesAxisSquared;
          int i101 = i001 + 1;
          int i011 = i001 + numSamplesAxis;
          int i111 = i011 + 1;

          float d0 = densityGrid[i000];
          float d1 = densityGrid[i100];
          float d2 = densityGrid[i110];
          float d3 = densityGrid[i010];
          float d4 = densityGrid[i001];
          float d5 = densityGrid[i101];
          float d6 = densityGrid[i111];
          float d7 = densityGrid[i011];

          int cubeIndex = 0;
          if (d0 > 0.0f) cubeIndex |= 1;
          if (d1 > 0.0f) cubeIndex |= 2;
          if (d2 > 0.0f) cubeIndex |= 4;
          if (d3 > 0.0f) cubeIndex |= 8;
          if (d4 > 0.0f) cubeIndex |= 16;
          if (d5 > 0.0f) cubeIndex |= 32;
          if (d6 > 0.0f) cubeIndex |= 64;
          if (d7 > 0.0f) cubeIndex |= 128;

          if (cubeIndex == 0 || cubeIndex == 255) continue;
          if (MarchingCubesTables.TriangleTable[cubeIndex, 0] < 0) continue;

          densities[0] = d0;
          densities[1] = d1;
          densities[2] = d2;
          densities[3] = d3;
          densities[4] = d4;
          densities[5] = d5;
          densities[6] = d6;
          densities[7] = d7;

          materials[0] = materialGrid[i000];
          materials[1] = materialGrid[i100];
          materials[2] = materialGrid[i110];
          materials[3] = materialGrid[i010];
          materials[4] = materialGrid[i001];
          materials[5] = materialGrid[i101];
          materials[6] = materialGrid[i111];
          materials[7] = materialGrid[i011];

          float px0 = x * cellWorldSize;
          float py0 = y * cellWorldSize;
          float pz0 = z * cellWorldSize;
          float px1 = (x + 1) * cellWorldSize;
          float py1 = (y + 1) * cellWorldSize;
          float pz1 = (z + 1) * cellWorldSize;

          positions[0] = new Vector3(px0, py0, pz0);
          positions[1] = new Vector3(px1, py0, pz0);
          positions[2] = new Vector3(px1, py1, pz0);
          positions[3] = new Vector3(px0, py1, pz0);
          positions[4] = new Vector3(px0, py0, pz1);
          positions[5] = new Vector3(px1, py0, pz1);
          positions[6] = new Vector3(px1, py1, pz1);
          positions[7] = new Vector3(px0, py1, pz1);

          normals[0] = SampleNormalCached(densityGrid, normalCache, normalComputed, numSamplesAxis, 0, 0, i000);
          normals[1] = SampleNormalCached(densityGrid, normalCache, normalComputed, numSamplesAxis, 1, 0, i100);
          normals[2] = SampleNormalCached(densityGrid, normalCache, normalComputed, numSamplesAxis, 1, 1, i110);
          normals[3] = SampleNormalCached(densityGrid, normalCache, normalComputed, numSamplesAxis, 0, 1, i010);
          normals[4] = SampleNormalCached(densityGrid, normalCache, normalComputed, numSamplesAxis, 0, 0, i001);
          normals[5] = SampleNormalCached(densityGrid, normalCache, normalComputed, numSamplesAxis, 1, 0, i101);
          normals[6] = SampleNormalCached(densityGrid, normalCache, normalComputed, numSamplesAxis, 1, 1, i111);
          normals[7] = SampleNormalCached(densityGrid, normalCache, normalComputed, numSamplesAxis, 0, 1, i011);

          for (int i = 0; i < 12; i++) edgeIndices[i] = -1;
          ushort materialId = ChooseMaterial(densities, materials);
          Color32 color = MaterialToColor(materialId);

          for (int t = 0; t < 16; t += 3)
          {
            int e0 = MarchingCubesTables.TriangleTable[cubeIndex, t + 0];
            if (e0 < 0) break;
            int e1 = MarchingCubesTables.TriangleTable[cubeIndex, t + 1];
            int e2 = MarchingCubesTables.TriangleTable[cubeIndex, t + 2];
            if (e1 < 0 || e2 < 0 || e0 >= 12 || e1 >= 12 || e2 >= 12) break;

            int v0 = AddOrGetEdgeVertex(mesh, e0, positions, densities, normals, edgeVertices, edgeNormals, edgeIndices, color);
            int v1 = AddOrGetEdgeVertex(mesh, e1, positions, densities, normals, edgeVertices, edgeNormals, edgeIndices, color);
            int v2 = AddOrGetEdgeVertex(mesh, e2, positions, densities, normals, edgeVertices, edgeNormals, edgeIndices, color);
            if (v0 < 0 || v1 < 0 || v2 < 0 || v0 == v1 || v1 == v2 || v0 == v2) continue;

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

        if (mesh.IsEmpty)
        {
          MeshDataPool.Return(mesh);
          return null;
        }

        return mesh;
      }
      finally
      {
        ArrayPool<Vector3>.Shared.Return(normalCache);
        ArrayPool<bool>.Shared.Return(normalComputed);
      }
    }

    private static int ToSampleIndex(int sx, int sy, int sz, int samplesAxis) => sx + samplesAxis * (sy + samplesAxis * sz);

    private static Vector3 SampleNormalCached(float[] densities, Vector3[] normalCache, bool[] normalComputed, int samplesAxis, int xOffset, int yOffset, int index)
    {
      if (normalComputed[index]) return normalCache[index];
      Vector3 normal = SampleNormal(densities, samplesAxis, xOffset, yOffset, index);
      normalCache[index] = normal;
      normalComputed[index] = true;
      return normal;
    }

    private static Vector3 SampleNormal(float[] densities, int samplesAxis, int xOffset, int yOffset, int index)
    {
      int samplesAxisSquared = samplesAxis * samplesAxis;
      int ix = index % samplesAxis;
      int iz = index / samplesAxisSquared;

      int leftIndex = xOffset == 0 && ix == 0 ? index : index - 1;
      int rightIndex = xOffset == 1 && ix == samplesAxis - 1 ? index : index + 1;
      int downIndex = yOffset == 0 && ((index / samplesAxis) % samplesAxis) == 0 ? index : index - samplesAxis;
      int upIndex = yOffset == 1 && ((index / samplesAxis) % samplesAxis) == samplesAxis - 1 ? index : index + samplesAxis;
      int backIndex = iz == 0 ? index : index - samplesAxisSquared;
      int forwardIndex = iz == samplesAxis - 1 ? index : index + samplesAxisSquared;

      float dx = densities[rightIndex] - densities[leftIndex];
      float dy = densities[upIndex] - densities[downIndex];
      float dz = densities[forwardIndex] - densities[backIndex];
      Vector3 normal = new(-dx, -dy, -dz);
      return normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.up;
    }

    private static int AddOrGetEdgeVertex(MeshData mesh, int edgeIndex, Span<Vector3> positions, Span<float> densities, Span<Vector3> normals, Span<Vector3> edgeVertices, Span<Vector3> edgeNormals, Span<int> edgeIndices, Color32 color)
    {
      if (edgeIndices[edgeIndex] >= 0) return edgeIndices[edgeIndex];
      int cornerA = MarchingCubesTables.EdgeConnection[edgeIndex, 0];
      int cornerB = MarchingCubesTables.EdgeConnection[edgeIndex, 1];
      Vector3 position = InterpolateVertex(positions[cornerA], positions[cornerB], densities[cornerA], densities[cornerB]);
      Vector3 normal = InterpolateNormal(normals[cornerA], normals[cornerB], densities[cornerA], densities[cornerB]);
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
      if (Mathf.Abs(denom) < 0.000001f) return (a + b) * 0.5f;
      float t = Mathf.Clamp01(da / (da - db));
      return a + (b - a) * t;
    }

    private static Vector3 InterpolateNormal(Vector3 a, Vector3 b, float da, float db)
    {
      float denom = da - db;
      if (Mathf.Abs(denom) < 0.000001f) return (a + b) * 0.5f;
      float t = Mathf.Clamp01(da / (da - db));
      return Vector3.Lerp(a, b, t);
    }

    private static Vector2 ProjectUv(Vector3 position, Vector3 normal)
    {
      Vector3 abs = new(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
      if (abs.y >= abs.x && abs.y >= abs.z) return new Vector2(position.x, position.z);
      if (abs.x >= abs.y && abs.x >= abs.z) return new Vector2(position.z, position.y);
      return new Vector2(position.x, position.y);
    }

    private static ushort ChooseMaterial(Span<float> densities, Span<ushort> materials)
    {
      ushort selected = 0;
      float bestDensity = float.NegativeInfinity;
      for (int i = 0; i < 8; i++)
      {
        if (densities[i] <= 0.0f || materials[i] == 0) continue;
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
      return new Color32((byte)(materialId & 0xFF), (byte)((materialId >> 8) & 0xFF), 255, 255);
    }
  }
}
