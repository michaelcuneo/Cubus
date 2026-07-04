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
      MaterialBlend[] materialBlendGrid = new MaterialBlend[totalSamples];

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
              materialBlendGrid[index] = voxel.Density > 0.0f
                ? SingleMaterialBlend((ushort)Mathf.Clamp(voxel.MaterialId, 1, 65535))
                : default;
            }
            else
            {
              SampleTerrain(snapshot, chunkCoord, lx, ly, lz, out float density, out MaterialBlend materialBlend);
              densityGrid[index] = density;
              materialBlendGrid[index] = density > 0.0f ? materialBlend : default;
            }
          }
        }
      }

      int estimatedVerts = Mathf.Max(256, numCellsAxis * numCellsAxis * numCellsAxis * 3);
      int estimatedIndices = Mathf.Max(384, numCellsAxis * numCellsAxis * numCellsAxis * 6);
      MeshData mesh = MeshDataPool.Rent(estimatedVerts, estimatedIndices);

      float[] densities = new float[8];
      MaterialBlend[] sampleBlends = new MaterialBlend[8];
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
              sampleBlends[corner] = materialBlendGrid[sampleIndex];
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

            MaterialBlend slotBlend = BuildCellSlotBlend(densities, sampleBlends);

            if (sampleVoxelAtWorld == null)
            {
              AddSurfaceEdgeSlotsToCellBlend(ref slotBlend, snapshot, chunkCoord, cubeIndex, positions, densities);
            }

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

              int v0 = AddOrGetEdgeVertex(mesh, e0, positions, densities, normals, sampleBlends, slotBlend, snapshot, chunkCoord, sampleVoxelAtWorld == null, edgeVertices, edgeIndices);
              int v1 = AddOrGetEdgeVertex(mesh, e1, positions, densities, normals, sampleBlends, slotBlend, snapshot, chunkCoord, sampleVoxelAtWorld == null, edgeVertices, edgeIndices);
              int v2 = AddOrGetEdgeVertex(mesh, e2, positions, densities, normals, sampleBlends, slotBlend, snapshot, chunkCoord, sampleVoxelAtWorld == null, edgeVertices, edgeIndices);

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
      out MaterialBlend materialBlend)
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
        materialBlend = default;
        return;
      }

      float depthBelowSurface = (float)(surfaceHeight - sampleZ);
      materialBlend = BuildProfileMaterialBlend(snapshot.TerrainProfile, depthBelowSurface, sampleX, sampleY, sampleZ);
    }

    private static MaterialBlend BuildSurfacePositionMaterialBlend(
      WorldGenerationSnapshot snapshot,
      Vector3Int chunkCoord,
      Vector3 localPosition)
    {
      float voxelSize = Mathf.Max(0.0001f, snapshot.VoxelSize);
      double wx = chunkCoord.x * VoxelConstants.ChunkSize + localPosition.x / voxelSize;
      double wy = chunkCoord.y * VoxelConstants.ChunkSize + localPosition.y / voxelSize;
      double wz = chunkCoord.z * VoxelConstants.ChunkSize + localPosition.z / voxelSize;

      double sampleScale = snapshot.DensitySampleScale <= 0.0f ? 1.0 : snapshot.DensitySampleScale;
      double sampleX = wx * sampleScale;
      double sampleY = wz * sampleScale;
      double sampleZ = wy * sampleScale;
      double surfaceHeight = TerrainHeight.ComputeSurfaceHeight(snapshot.TerrainProfile, sampleX, sampleY);
      float depthBelowSurface = (float)(surfaceHeight - sampleZ);

      return BuildProfileMaterialBlend(snapshot.TerrainProfile, depthBelowSurface, sampleX, sampleY, sampleZ);
    }

    private static MaterialBlend BuildProfileMaterialBlend(
      TerrainGenerationProfileSnapshot profile,
      float depthBelowSurface,
      double wx,
      double wy,
      double wz)
    {
      if (profile.MaterialLayers.Length == 0)
      {
        return SingleMaterialBlend(1);
      }

      int selectedIndex = profile.MaterialLayers.Length - 1;
      for (int i = 0; i < profile.MaterialLayers.Length; i++)
      {
        if (depthBelowSurface <= profile.MaterialLayers[i].MaxDepthBelowSurface)
        {
          selectedIndex = i;
          break;
        }
      }

      MaterialLayerSnapshotEntry selected = profile.MaterialLayers[selectedIndex];

      if (selectedIndex > 0)
      {
        MaterialLayerSnapshotEntry previous = profile.MaterialLayers[selectedIndex - 1];
        float boundary = previous.MaxDepthBelowSurface;
        float blendWidth = Mathf.Max(0.001f, selected.BlendWidth);
        float t = Mathf.Clamp01((depthBelowSurface - boundary) / blendWidth);

        if (t > 0.0f && t < 1.0f)
        {
          return BuildLayerTransitionBlend(previous, selected, t, wx, wy, wz, profile.WorldSeed);
        }
      }

      if (selectedIndex + 1 < profile.MaterialLayers.Length)
      {
        MaterialLayerSnapshotEntry next = profile.MaterialLayers[selectedIndex + 1];
        float boundary = selected.MaxDepthBelowSurface;
        float blendWidth = Mathf.Max(0.001f, next.BlendWidth);
        float t = Mathf.Clamp01((depthBelowSurface - (boundary - blendWidth)) / blendWidth);

        if (t > 0.0f && t < 1.0f)
        {
          return BuildLayerTransitionBlend(selected, next, t, wx, wy, wz, profile.WorldSeed);
        }
      }

      return SingleMaterialBlend((ushort)Mathf.Clamp(selected.MaterialId, 1, 65535));
    }

    private static MaterialBlend BuildLayerTransitionBlend(
      MaterialLayerSnapshotEntry lower,
      MaterialLayerSnapshotEntry upper,
      float t,
      double wx,
      double wy,
      double wz,
      int seed)
    {
      float scale = Mathf.Max(0.0001f, upper.NoiseScale);
      float noise = HashNoise01(
        (float)(wx * scale),
        (float)(wy * scale),
        (float)(wz * scale),
        seed + upper.MaterialId * 131 + lower.MaterialId * 17);

      float jitter = (noise - 0.5f) * Mathf.Clamp01(upper.NoiseStrength) * 0.5f;
      float eased = Mathf.Clamp01(t + jitter);
      eased = eased * eased * (3.0f - 2.0f * eased);

      float lowerWeightBias = Mathf.Max(0.0001f, lower.Weight);
      float upperWeightBias = Mathf.Max(0.0001f, upper.Weight);
      float lowerWeight = (1.0f - eased) * lowerWeightBias;
      float upperWeight = eased * upperWeightBias;

      MaterialBlend blend = default;
      AddMaterialWeight(ref blend, (ushort)Mathf.Clamp(lower.MaterialId, 1, 65535), lowerWeight);
      AddMaterialWeight(ref blend, (ushort)Mathf.Clamp(upper.MaterialId, 1, 65535), upperWeight);
      NormalizeMaterialBlend(ref blend);
      return blend;
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

    private static void AddSurfaceEdgeSlotsToCellBlend(
      ref MaterialBlend slotBlend,
      WorldGenerationSnapshot snapshot,
      Vector3Int chunkCoord,
      int cubeIndex,
      Vector3[] positions,
      float[] densities)
    {
      bool[] usedEdges = new bool[12];

      for (int t = 0; t < 16; t += 3)
      {
        int e0 = MarchingCubesTables.TriangleTable[cubeIndex, t + 0];
        if (e0 < 0)
        {
          break;
        }

        int e1 = MarchingCubesTables.TriangleTable[cubeIndex, t + 1];
        int e2 = MarchingCubesTables.TriangleTable[cubeIndex, t + 2];
        AddSurfaceEdgeSlot(ref slotBlend, usedEdges, e0, snapshot, chunkCoord, positions, densities);
        AddSurfaceEdgeSlot(ref slotBlend, usedEdges, e1, snapshot, chunkCoord, positions, densities);
        AddSurfaceEdgeSlot(ref slotBlend, usedEdges, e2, snapshot, chunkCoord, positions, densities);
      }

      NormalizeMaterialBlend(ref slotBlend);
    }

    private static void AddSurfaceEdgeSlot(
      ref MaterialBlend slotBlend,
      bool[] usedEdges,
      int edgeIndex,
      WorldGenerationSnapshot snapshot,
      Vector3Int chunkCoord,
      Vector3[] positions,
      float[] densities)
    {
      if (edgeIndex < 0 || edgeIndex >= 12 || usedEdges[edgeIndex])
      {
        return;
      }

      usedEdges[edgeIndex] = true;
      int cornerA = MarchingCubesTables.EdgeConnection[edgeIndex, 0];
      int cornerB = MarchingCubesTables.EdgeConnection[edgeIndex, 1];
      Vector3 position = InterpolateVertex(positions[cornerA], positions[cornerB], densities[cornerA], densities[cornerB]);
      MaterialBlend surfaceBlend = BuildSurfacePositionMaterialBlend(snapshot, chunkCoord, position);
      AddBlendWeighted(ref slotBlend, surfaceBlend, 1.0f);
    }

    private static int AddOrGetEdgeVertex(
      MeshData mesh,
      int edgeIndex,
      Vector3[] positions,
      float[] densities,
      Vector3[] normals,
      MaterialBlend[] sampleBlends,
      MaterialBlend slotBlend,
      WorldGenerationSnapshot snapshot,
      Vector3Int chunkCoord,
      bool useExactSurfaceMaterial,
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

      MaterialBlend rawBlend = useExactSurfaceMaterial
        ? BuildSurfacePositionMaterialBlend(snapshot, chunkCoord, position)
        : BuildInterpolatedEdgeMaterialBlend(sampleBlends[cornerA], sampleBlends[cornerB], densities[cornerA], densities[cornerB]);

      MaterialBlend edgeBlend = ProjectBlendToFixedSlots(slotBlend, rawBlend);

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

    private static MaterialBlend BuildCellSlotBlend(float[] densities, MaterialBlend[] sampleBlends)
    {
      MaterialBlend blend = default;

      for (int i = 0; i < 8; i++)
      {
        if (densities[i] <= 0.0f)
        {
          continue;
        }

        AddBlendWeighted(ref blend, sampleBlends[i], Mathf.Max(0.001f, densities[i]));
      }

      NormalizeMaterialBlend(ref blend);
      return blend;
    }

    private static MaterialBlend BuildInterpolatedEdgeMaterialBlend(
      MaterialBlend sampleA,
      MaterialBlend sampleB,
      float densityA,
      float densityB)
    {
      MaterialBlend blend = default;
      float t = EdgeInterpolationT(densityA, densityB);
      AddBlendWeighted(ref blend, sampleA, 1.0f - t);
      AddBlendWeighted(ref blend, sampleB, t);
      NormalizeMaterialBlend(ref blend);
      return blend;
    }

    private static MaterialBlend ProjectBlendToFixedSlots(MaterialBlend slots, MaterialBlend source)
    {
      MaterialBlend result = new()
      {
        Id0 = slots.Id0,
        Id1 = slots.Id1,
        Id2 = slots.Id2,
        Id3 = slots.Id3,
        W0 = GetBlendWeight(source, slots.Id0),
        W1 = GetBlendWeight(source, slots.Id1),
        W2 = GetBlendWeight(source, slots.Id2),
        W3 = GetBlendWeight(source, slots.Id3)
      };

      NormalizeMaterialBlend(ref result);
      return result;
    }

    private static MaterialBlend SingleMaterialBlend(ushort materialId)
    {
      return new MaterialBlend
      {
        Id0 = materialId == 0 ? (ushort)1 : materialId,
        W0 = 1.0f
      };
    }

    private static void AddBlendWeighted(ref MaterialBlend target, MaterialBlend source, float weight)
    {
      AddMaterialWeight(ref target, source.Id0, source.W0 * weight);
      AddMaterialWeight(ref target, source.Id1, source.W1 * weight);
      AddMaterialWeight(ref target, source.Id2, source.W2 * weight);
      AddMaterialWeight(ref target, source.Id3, source.W3 * weight);
    }

    private static float GetBlendWeight(MaterialBlend blend, ushort materialId)
    {
      if (materialId == 0)
      {
        return 0.0f;
      }

      float weight = 0.0f;
      if (blend.Id0 == materialId) weight += blend.W0;
      if (blend.Id1 == materialId) weight += blend.W1;
      if (blend.Id2 == materialId) weight += blend.W2;
      if (blend.Id3 == materialId) weight += blend.W3;
      return weight;
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

    private static float HashNoise01(float x, float y, float z, int seed)
    {
      float n = x * 12.9898f + y * 78.233f + z * 37.719f + seed * 0.12345f;
      return Mathf.Repeat(Mathf.Sin(n) * 43758.5453f, 1.0f);
    }
  }
}
