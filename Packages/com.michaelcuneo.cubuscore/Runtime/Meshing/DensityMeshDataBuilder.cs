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
    public static MeshData Generate(
      Vector3Int chunkCoord,
      WorldGenerationSnapshot snapshot,
      int cellStep,
      bool flipWinding,
      Func<Vector3Int, DensityVoxel> sampleVoxelAtWorld)
    {
      if (sampleVoxelAtWorld == null) return null;

      int safeCellStep = Mathf.Clamp(cellStep, 1, 4);
      int numCellsAxis = Mathf.CeilToInt((float)VoxelConstants.ChunkSize / safeCellStep);
      int numSamplesAxis = numCellsAxis + 1;
      int totalSamples = numSamplesAxis * numSamplesAxis * numSamplesAxis;
      int baseWorldX = chunkCoord.x * VoxelConstants.ChunkSize;
      int baseWorldY = chunkCoord.y * VoxelConstants.ChunkSize;
      int baseWorldZ = chunkCoord.z * VoxelConstants.ChunkSize;
      float[] densityGrid = ArrayPool<float>.Shared.Rent(totalSamples);
      DensityMaterialSet[] materialGrid = ArrayPool<DensityMaterialSet>.Shared.Rent(totalSamples);

      try
      {
        int writeIndex = 0;
        for (int sz = 0; sz < numSamplesAxis; sz++)
        {
          int worldZ = baseWorldZ + sz * safeCellStep;
          for (int sy = 0; sy < numSamplesAxis; sy++)
          {
            int worldY = baseWorldY + sy * safeCellStep;
            for (int sx = 0; sx < numSamplesAxis; sx++)
            {
              int worldX = baseWorldX + sx * safeCellStep;
              DensityVoxel voxel = sampleVoxelAtWorld(new Vector3Int(worldX, worldY, worldZ));
              densityGrid[writeIndex] = voxel.Density;
              materialGrid[writeIndex] = voxel.Materials;
              writeIndex++;
            }
          }
        }

        return BuildFromGrids(densityGrid, materialGrid, numCellsAxis, safeCellStep * snapshot.VoxelSize, flipWinding);
      }
      finally
      {
        ArrayPool<float>.Shared.Return(densityGrid);
        ArrayPool<DensityMaterialSet>.Shared.Return(materialGrid);
      }
    }

    public static MeshData GenerateFromChunkSnapshots(
      Vector3Int chunkCoord,
      WorldGenerationSnapshot snapshot,
      int cellStep,
      bool flipWinding,
      DensityChunkData rootChunkData,
      IReadOnlyDictionary<Vector3Int, DensityChunkData> chunkDataSnapshots,
      Func<Vector3Int, DensityVoxel> fallbackSampleVoxelAtWorld)
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
      DensityMaterialSet[] materialGrid = ArrayPool<DensityMaterialSet>.Shared.Rent(totalSamples);

      try
      {
        int writeIndex = 0;
        for (int sz = 0; sz < numSamplesAxis; sz++)
        {
          int lz = sz * safeCellStep;
          int rootZBase = chunkSize * chunkSize * lz;
          int worldZ = baseWorldZ + lz;
          for (int sy = 0; sy < numSamplesAxis; sy++)
          {
            int ly = sy * safeCellStep;
            int rootYZBase = rootZBase + chunkSize * ly;
            int worldY = baseWorldY + ly;
            for (int sx = 0; sx < numSamplesAxis; sx++)
            {
              int lx = sx * safeCellStep;
              DensityVoxel voxel;
              if (lx < chunkSize && ly < chunkSize && lz < chunkSize)
              {
                voxel = rootVoxels[rootYZBase + lx];
              }
              else
              {
                voxel = SampleSnapshotOrFallback(chunkDataSnapshots, fallbackSampleVoxelAtWorld, new Vector3Int(baseWorldX + lx, worldY, worldZ));
              }

              densityGrid[writeIndex] = voxel.Density;
              materialGrid[writeIndex] = voxel.Materials;
              writeIndex++;
            }
          }
        }

        return BuildFromGrids(densityGrid, materialGrid, numCellsAxis, safeCellStep * snapshot.VoxelSize, flipWinding);
      }
      finally
      {
        ArrayPool<float>.Shared.Return(densityGrid);
        ArrayPool<DensityMaterialSet>.Shared.Return(materialGrid);
      }
    }

    private static DensityVoxel SampleSnapshotOrFallback(
      IReadOnlyDictionary<Vector3Int, DensityChunkData> chunkDataSnapshots,
      Func<Vector3Int, DensityVoxel> fallbackSampleVoxelAtWorld,
      Vector3Int worldVoxel)
    {
      Vector3Int sampleChunkCoord = VoxelMath.WorldVoxelToChunkCoord(worldVoxel);
      Vector3Int localCoord = VoxelMath.WorldVoxelToLocalCoord(worldVoxel);
      if (chunkDataSnapshots != null && chunkDataSnapshots.TryGetValue(sampleChunkCoord, out DensityChunkData chunkData) && chunkData != null && chunkData.IsInBounds(localCoord.x, localCoord.y, localCoord.z))
      {
        return chunkData.GetVoxel(localCoord.x, localCoord.y, localCoord.z);
      }

      return fallbackSampleVoxelAtWorld != null ? fallbackSampleVoxelAtWorld(worldVoxel) : DensityVoxel.Empty;
    }

    public static MeshData BuildFromGrids(
      float[] densityGrid,
      DensityMaterialSet[] materialGrid,
      int numCellsAxis,
      float cellWorldSize,
      bool flipWinding)
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
        Span<DensityMaterialSet> materials = stackalloc DensityMaterialSet[8];
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

              DensityMaterialSet cellSlots = BuildCellMaterialSlots(materials, densities);

              for (int i = 0; i < 12; i++) edgeIndices[i] = -1;

              for (int t = 0; t < 16; t += 3)
              {
                int e0 = MarchingCubesTables.TriangleTable[cubeIndex, t + 0];
                if (e0 < 0) break;
                int e1 = MarchingCubesTables.TriangleTable[cubeIndex, t + 1];
                int e2 = MarchingCubesTables.TriangleTable[cubeIndex, t + 2];
                if (e1 < 0 || e2 < 0 || e0 >= 12 || e1 >= 12 || e2 >= 12) break;

                int v0 = AddOrGetEdgeVertex(mesh, e0, positions, densities, materials, cellSlots, normals, edgeVertices, edgeNormals, edgeIndices);
                int v1 = AddOrGetEdgeVertex(mesh, e1, positions, densities, materials, cellSlots, normals, edgeVertices, edgeNormals, edgeIndices);
                int v2 = AddOrGetEdgeVertex(mesh, e2, positions, densities, materials, cellSlots, normals, edgeVertices, edgeNormals, edgeIndices);

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

    private static int AddOrGetEdgeVertex(
      MeshData mesh,
      int edgeIndex,
      Span<Vector3> positions,
      Span<float> densities,
      Span<DensityMaterialSet> materials,
      DensityMaterialSet cellSlots,
      Span<Vector3> normals,
      Span<Vector3> edgeVertices,
      Span<Vector3> edgeNormals,
      Span<int> edgeIndices)
    {
      if (edgeIndices[edgeIndex] >= 0) return edgeIndices[edgeIndex];

      int cornerA = MarchingCubesTables.EdgeConnection[edgeIndex, 0];
      int cornerB = MarchingCubesTables.EdgeConnection[edgeIndex, 1];

      Vector3 position = InterpolateVertex(positions[cornerA], positions[cornerB], densities[cornerA], densities[cornerB]);
      Vector3 normal = InterpolateNormal(normals[cornerA], normals[cornerB], densities[cornerA], densities[cornerB]);
      normal = normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.up;

      BuildSplatPayload(positions, densities, materials, cellSlots, position, out Color32 splatWeights, out Vector2 materialIds01, out Vector2 materialIds23);

      int index = mesh.Vertices.Count;
      mesh.Vertices.Add(position);
      mesh.Normals.Add(normal);
      mesh.Colors.Add(splatWeights);
      mesh.UVs.Add(materialIds01);
      mesh.UV1s.Add(materialIds23);

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

    private static DensityMaterialSet BuildCellMaterialSlots(Span<DensityMaterialSet> materials, Span<float> densities)
    {
      Span<ushort> ids = stackalloc ushort[4];
      int count = 0;

      for (int corner = 0; corner < 8; corner++)
      {
        if (densities[corner] <= -0.75f) continue;
        DensityMaterialSet set = materials[corner];
        AddSlotId(ref count, ids, set.Material0, set.Weight0);
        AddSlotId(ref count, ids, set.Material1, set.Weight1);
        AddSlotId(ref count, ids, set.Material2, set.Weight2);
        AddSlotId(ref count, ids, set.Material3, set.Weight3);
        if (count >= 4) break;
      }

      return new DensityMaterialSet
      {
        Material0 = count > 0 ? ids[0] : (ushort)1,
        Material1 = count > 1 ? ids[1] : (ushort)0,
        Material2 = count > 2 ? ids[2] : (ushort)0,
        Material3 = count > 3 ? ids[3] : (ushort)0,
        Weight0 = 255,
        Weight1 = 0,
        Weight2 = 0,
        Weight3 = 0
      };
    }

    private static void AddSlotId(ref int count, Span<ushort> ids, ushort materialId, byte weight)
    {
      if (materialId == 0 || weight == 0 || count >= ids.Length) return;
      for (int i = 0; i < count; i++) if (ids[i] == materialId) return;
      ids[count++] = materialId;
    }

    private static void BuildSplatPayload(Span<Vector3> positions, Span<float> densities, Span<DensityMaterialSet> materials, DensityMaterialSet cellSlots, Vector3 surfacePosition, out Color32 splatWeights, out Vector2 materialIds01, out Vector2 materialIds23)
    {
      Span<ushort> ids = stackalloc ushort[16];
      Span<float> weights = stackalloc float[16];
      int uniqueCount = 0;

      for (int corner = 0; corner < 8; corner++)
      {
        DensityMaterialSet set = materials[corner];
        float cornerWeight = CalculateCornerMaterialInfluence(positions[corner], densities[corner], surfacePosition);
        if (cornerWeight <= 0.000001f) continue;
        AccumulateMaterial(ref uniqueCount, ids, weights, set.Material0, set.Weight0 * cornerWeight);
        AccumulateMaterial(ref uniqueCount, ids, weights, set.Material1, set.Weight1 * cornerWeight);
        AccumulateMaterial(ref uniqueCount, ids, weights, set.Material2, set.Weight2 * cornerWeight);
        AccumulateMaterial(ref uniqueCount, ids, weights, set.Material3, set.Weight3 * cornerWeight);
      }

      ushort material0 = cellSlots.Material0 != 0 ? cellSlots.Material0 : (ushort)1;
      ushort material1 = cellSlots.Material1;
      ushort material2 = cellSlots.Material2;
      ushort material3 = cellSlots.Material3;

      float weight0 = GetAccumulatedWeight(ids, weights, uniqueCount, material0);
      float weight1 = GetAccumulatedWeight(ids, weights, uniqueCount, material1);
      float weight2 = GetAccumulatedWeight(ids, weights, uniqueCount, material2);
      float weight3 = GetAccumulatedWeight(ids, weights, uniqueCount, material3);
      if (weight0 + weight1 + weight2 + weight3 <= 0.000001f) weight0 = 1.0f;

      NormalizeTopFourWeights(weight0, weight1, weight2, weight3, out byte packed0, out byte packed1, out byte packed2, out byte packed3);
      splatWeights = new Color32(packed0, packed1, packed2, packed3);
      materialIds01 = new Vector2(material0, material1);
      materialIds23 = new Vector2(material2, material3);
    }

    private static float CalculateCornerMaterialInfluence(Vector3 cornerPosition, float density, Vector3 surfacePosition)
    {
      if (density <= -0.75f) return 0.0f;
      float distanceSq = (cornerPosition - surfacePosition).sqrMagnitude;
      float distanceWeight = 1.0f / Mathf.Max(0.0001f, distanceSq + 0.08f);
      float densityWeight = Mathf.Clamp01(density + 0.75f);
      return distanceWeight * densityWeight;
    }

    private static void AccumulateMaterial(ref int uniqueCount, Span<ushort> ids, Span<float> weights, ushort materialId, float weight)
    {
      if (materialId == 0 || weight <= 0.000001f) return;
      for (int i = 0; i < uniqueCount; i++)
      {
        if (ids[i] == materialId)
        {
          weights[i] += weight;
          return;
        }
      }
      if (uniqueCount >= ids.Length) return;
      ids[uniqueCount] = materialId;
      weights[uniqueCount] = weight;
      uniqueCount++;
    }

    private static float GetAccumulatedWeight(Span<ushort> ids, Span<float> weights, int count, ushort materialId)
    {
      if (materialId == 0) return 0.0f;
      float result = 0.0f;
      int safeCount = Mathf.Min(count, ids.Length);
      for (int i = 0; i < safeCount; i++) if (ids[i] == materialId) result += weights[i];
      return result;
    }

    private static void NormalizeTopFourWeights(float weight0, float weight1, float weight2, float weight3, out byte packed0, out byte packed1, out byte packed2, out byte packed3)
    {
      float total = weight0 + weight1 + weight2 + weight3;
      if (total <= 0.000001f)
      {
        packed0 = 255;
        packed1 = 0;
        packed2 = 0;
        packed3 = 0;
        return;
      }

      float invTotal = 1.0f / total;
      int p0 = Mathf.Clamp(Mathf.RoundToInt(weight0 * invTotal * 255.0f), 0, 255);
      int p1 = Mathf.Clamp(Mathf.RoundToInt(weight1 * invTotal * 255.0f), 0, 255);
      int p2 = Mathf.Clamp(Mathf.RoundToInt(weight2 * invTotal * 255.0f), 0, 255);
      int used = p0 + p1 + p2;
      int p3 = Mathf.Clamp(255 - used, 0, 255);
      packed0 = (byte)p0;
      packed1 = (byte)p1;
      packed2 = (byte)p2;
      packed3 = (byte)p3;
    }
  }
}
