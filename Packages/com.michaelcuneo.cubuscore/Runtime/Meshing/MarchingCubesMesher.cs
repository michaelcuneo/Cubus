using System;
using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using Unity.Collections;
using Unity.Jobs;
#if UNITY_BURST
using Unity.Burst;
#endif
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif
#if UNITY_MATHEMATICS
using Unity.Mathematics;
#endif

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing
{
  public static class MarchingCubesMesher
  {
    private struct Vertex
    {
      public Vector3 Position;
      public Vector3 Normal;
      public Color32 Color;
      public Vector2 UV;
    }

    private static NativeArray<int> s_CubeCornerOffset;
    private static NativeArray<int> s_EdgeConnection;
    private static NativeArray<int> s_TriangleTable;
    private static bool s_TablesInitialized;
    private static bool s_CleanupRegistered;

    private static class StripeBuffers
    {
      public static NativeArray<int> VCounts;
      public static NativeArray<int> ICounts;
      public static NativeArray<int> VOffsets;
      public static NativeArray<int> IOffsets;
      public static NativeArray<Vector3> Verts;
      public static NativeArray<Vector3> Normals;
      public static NativeArray<Vector2> UVs;
      public static NativeArray<Color32> Colors;
      public static NativeArray<int> Indices;

      public static int StripesCap;
      public static int VertCap;
      public static int IndexCap;

      public static void EnsureCounts(int stripes)
      {
        if (!VCounts.IsCreated || StripesCap < stripes)
        {
          if (VCounts.IsCreated)
          {
            VCounts.Dispose();
            ICounts.Dispose();
            VOffsets.Dispose();
            IOffsets.Dispose();
          }

          VCounts = new NativeArray<int>(stripes, Allocator.Persistent, NativeArrayOptions.ClearMemory);
          ICounts = new NativeArray<int>(stripes, Allocator.Persistent, NativeArrayOptions.ClearMemory);
          VOffsets = new NativeArray<int>(stripes, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
          IOffsets = new NativeArray<int>(stripes, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
          StripesCap = stripes;
        }
      }

      public static void Ensure(int stripes, int totalVerts, int totalIndices)
      {
        EnsureCounts(stripes);

        if (!Verts.IsCreated || VertCap < totalVerts)
        {
          if (Verts.IsCreated)
          {
            Verts.Dispose();
            Normals.Dispose();
            UVs.Dispose();
            Colors.Dispose();
          }

          Verts = new NativeArray<Vector3>(totalVerts, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
          Normals = new NativeArray<Vector3>(totalVerts, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
          UVs = new NativeArray<Vector2>(totalVerts, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
          Colors = new NativeArray<Color32>(totalVerts, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
          VertCap = totalVerts;
        }

        if (!Indices.IsCreated || IndexCap < totalIndices)
        {
          if (Indices.IsCreated)
          {
            Indices.Dispose();
          }

          Indices = new NativeArray<int>(totalIndices, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
          IndexCap = totalIndices;
        }
      }
    }

    private static void EnsureTables()
    {
      if (s_TablesInitialized) return;
      s_CubeCornerOffset = new NativeArray<int>(8 * 3, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
      s_EdgeConnection = new NativeArray<int>(12 * 2, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
      s_TriangleTable = new NativeArray<int>(256 * 16, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
      for (int i = 0; i < 8; i++) for (int j = 0; j < 3; j++) s_CubeCornerOffset[i * 3 + j] = MarchingCubesTables.CubeCornerOffset[i, j];
      for (int i = 0; i < 12; i++) for (int j = 0; j < 2; j++) s_EdgeConnection[i * 2 + j] = MarchingCubesTables.EdgeConnection[i, j];
      for (int i = 0; i < 256; i++) for (int j = 0; j < 16; j++) s_TriangleTable[i * 16 + j] = MarchingCubesTables.TriangleTable[i, j];
      s_TablesInitialized = true;
    }

    public static void DisposePersistent()
    {
      if (s_TablesInitialized)
      {
        if (s_CubeCornerOffset.IsCreated) s_CubeCornerOffset.Dispose();
        if (s_EdgeConnection.IsCreated) s_EdgeConnection.Dispose();
        if (s_TriangleTable.IsCreated) s_TriangleTable.Dispose();
        s_TablesInitialized = false;
      }

      // Stripe buffers
      if (StripeBuffers.VCounts.IsCreated) StripeBuffers.VCounts.Dispose();
      if (StripeBuffers.ICounts.IsCreated) StripeBuffers.ICounts.Dispose();
      if (StripeBuffers.VOffsets.IsCreated) StripeBuffers.VOffsets.Dispose();
      if (StripeBuffers.IOffsets.IsCreated) StripeBuffers.IOffsets.Dispose();
      if (StripeBuffers.Verts.IsCreated) StripeBuffers.Verts.Dispose();
      if (StripeBuffers.Normals.IsCreated) StripeBuffers.Normals.Dispose();
      if (StripeBuffers.UVs.IsCreated) StripeBuffers.UVs.Dispose();
      if (StripeBuffers.Colors.IsCreated) StripeBuffers.Colors.Dispose();
      if (StripeBuffers.Indices.IsCreated) StripeBuffers.Indices.Dispose();
      StripeBuffers.StripesCap = 0; StripeBuffers.VertCap = 0; StripeBuffers.IndexCap = 0;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void OnReloadDispose()
    {
      DisposePersistent();
      s_CleanupRegistered = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterCleanup()
    {
      if (s_CleanupRegistered)
      {
        return;
      }

      s_CleanupRegistered = true;

      Application.quitting -= DisposePersistent;
      Application.quitting += DisposePersistent;

#if UNITY_EDITOR
      EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
      EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
#endif
    }

#if UNITY_EDITOR
    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
      if (state == PlayModeStateChange.ExitingPlayMode)
      {
        DisposePersistent();
      }
    }
#endif
    public static Mesh GenerateMeshDirect(
        Vector3Int chunkCoord,
        WorldGenerationSnapshot snapshot,
        int cellStep,
      bool flipWinding = true,
      Func<Vector3Int, CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels.DensityVoxel> sampleVoxelAtWorld = null)
    {
      // Force full-resolution density meshing for correctness. Coarser sampling currently introduces holes/artifacts.
      int safeCellStep = 1;
      int numCellsAxis = Mathf.CeilToInt((float)VoxelConstants.ChunkSize / safeCellStep);
      int numSamplesAxis = numCellsAxis + 1;
      int totalSamples = numSamplesAxis * numSamplesAxis * numSamplesAxis;

      NativeArray<float> densityGrid = new(totalSamples, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
      NativeArray<Vector3> normalGrid = new(totalSamples, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
      NativeArray<ushort> materialGrid = new(totalSamples, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

      try
      {
        JobHandle densityHandle;
        if (sampleVoxelAtWorld == null)
        {
          var densityJob = new BuildDensityMaterialGridJob
          {
            Density = densityGrid,
            Material = materialGrid,
            ChunkCoord = chunkCoord,
            SafeCellStep = safeCellStep,
            NumSamplesAxis = numSamplesAxis,
            Snapshot = snapshot
          };
          densityHandle = densityJob.Schedule(totalSamples, 64);
        }
        else
        {
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
                int index = sx + numSamplesAxis * (sy + numSamplesAxis * sz);
                var voxel = sampleVoxelAtWorld(worldVoxel);
                densityGrid[index] = voxel.Density;
                materialGrid[index] = voxel.MaterialId;
              }
            }
          }
          densityHandle = default;
        }

        var normalJob = new BuildNormalGridJob
        {
          Density = densityGrid,
          Normals = normalGrid,
          NumSamplesAxis = numSamplesAxis
        };
        normalJob.Schedule(totalSamples, 64, densityHandle).Complete();

        EnsureTables();

        int stripes = numCellsAxis;
        int cellsPerStripe = numCellsAxis * numCellsAxis;
        const int MaxTrisPerCell = 5;
        int maxIndicesPerStripe = cellsPerStripe * MaxTrisPerCell * 3;
        int maxVertsPerStripe = maxIndicesPerStripe;
        int totalMaxVerts = stripes * maxVertsPerStripe;
        int totalMaxIndices = stripes * maxIndicesPerStripe;

        StripeBuffers.Ensure(stripes, totalMaxVerts, totalMaxIndices);
        for (int s = 0; s < stripes; s++)
        {
          StripeBuffers.VCounts[s] = 0;
          StripeBuffers.ICounts[s] = 0;
        }

        var stripeJob = new TriangulationStripeJob
        {
          Density = densityGrid,
          Normals = normalGrid,
          Materials = materialGrid,
          SafeCellStep = safeCellStep,
          NumCellsAxis = numCellsAxis,
          NumSamplesAxis = numSamplesAxis,
          VoxelSize = snapshot.VoxelSize,
          FlipWinding = flipWinding,
          CubeCornerOffset = s_CubeCornerOffset,
          EdgeConnection = s_EdgeConnection,
          TriangleTable = s_TriangleTable,
          MaxVertsPerStripe = maxVertsPerStripe,
          MaxIndicesPerStripe = maxIndicesPerStripe,
          StripeVertices = StripeBuffers.Verts,
          StripeNormals = StripeBuffers.Normals,
          StripeUVs = StripeBuffers.UVs,
          StripeColors = StripeBuffers.Colors,
          StripeIndices = StripeBuffers.Indices,
          StripeVertexCounts = StripeBuffers.VCounts,
          StripeIndexCounts = StripeBuffers.ICounts
        };
        stripeJob.Schedule(stripes, 1).Complete();

        int totalVerts = 0;
        int totalIndices = 0;
        for (int s = 0; s < stripes; s++)
        {
          totalVerts += StripeBuffers.VCounts[s];
          totalIndices += StripeBuffers.ICounts[s];
        }

        NativeArray<Vertex> finalVertices = new NativeArray<Vertex>(totalVerts, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<int> finalIndices = new NativeArray<int>(totalIndices, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        try
        {
          int vertexOffset = 0;
          int indexOffset = 0;
          for (int s = 0; s < stripes; s++)
          {
            int sv = StripeBuffers.VCounts[s];
            int si = StripeBuffers.ICounts[s];
            int srcVBase = s * maxVertsPerStripe;
            int srcIBase = s * maxIndicesPerStripe;

            for (int i = 0; i < sv; i++)
            {
              Vertex vert;
              vert.Position = StripeBuffers.Verts[srcVBase + i];
              vert.Normal = StripeBuffers.Normals[srcVBase + i];
              vert.Color = StripeBuffers.Colors[srcVBase + i];
              vert.UV = StripeBuffers.UVs[srcVBase + i];
              finalVertices[vertexOffset + i] = vert;
            }

            for (int i = 0; i < si; i++)
            {
              finalIndices[indexOffset + i] = StripeBuffers.Indices[srcIBase + i] + vertexOffset;
            }

            vertexOffset += sv;
            indexOffset += si;
          }

          // Recompute per-vertex normals from the density gradient for seam-free lighting.
          // Both paths sample density at ±1 world-voxel positions so neighbours across chunk
          // boundaries produce identical normals on both sides — eliminating visible seams.
          if (totalVerts > 0)
          {
            int csz = VoxelConstants.ChunkSize;
            double chx = (double)chunkCoord.x * csz;
            double chy = (double)chunkCoord.y * csz;
            double chz = (double)chunkCoord.z * csz;
            double voxSize = (double)snapshot.VoxelSize;

            for (int i = 0; i < totalVerts; i++)
            {
              Vector3 p = finalVertices[i].Position;

              float dL, dR, dD, dU, dB, dF;

              if (sampleVoxelAtWorld != null)
              {
                // Callback path: sample world-voxel neighbours directly so the gradient
                // crosses chunk boundaries and both sides of every seam agree.
                int wvx = chunkCoord.x * csz + Mathf.RoundToInt(p.x / snapshot.VoxelSize);
                int wvy = chunkCoord.y * csz + Mathf.RoundToInt(p.y / snapshot.VoxelSize);
                int wvz = chunkCoord.z * csz + Mathf.RoundToInt(p.z / snapshot.VoxelSize);

                dL = sampleVoxelAtWorld(new Vector3Int(wvx - 1, wvy, wvz)).Density;
                dR = sampleVoxelAtWorld(new Vector3Int(wvx + 1, wvy, wvz)).Density;
                dD = sampleVoxelAtWorld(new Vector3Int(wvx, wvy - 1, wvz)).Density;
                dU = sampleVoxelAtWorld(new Vector3Int(wvx, wvy + 1, wvz)).Density;
                dB = sampleVoxelAtWorld(new Vector3Int(wvx, wvy, wvz - 1)).Density;
                dF = sampleVoxelAtWorld(new Vector3Int(wvx, wvy, wvz + 1)).Density;
              }
              else
              {
                // Procedural path: re-evaluate the noise function directly so normals
                // are continuous across chunk boundaries.
                double vx = chx + (double)p.x / voxSize;
                double vy = chy + (double)p.y / voxSize;
                double vz = chz + (double)p.z / voxSize;
                double s = snapshot.DensitySampleScale <= 0.0f ? 1.0 : (double)snapshot.DensitySampleScale;

                TerrainSamplerBurst.Sample(snapshot.TerrainProfile, (vx - 1) * s, vz * s, vy * s, out dL, out _);
                TerrainSamplerBurst.Sample(snapshot.TerrainProfile, (vx + 1) * s, vz * s, vy * s, out dR, out _);
                TerrainSamplerBurst.Sample(snapshot.TerrainProfile, vx * s, vz * s, (vy - 1) * s, out dD, out _);
                TerrainSamplerBurst.Sample(snapshot.TerrainProfile, vx * s, vz * s, (vy + 1) * s, out dU, out _);
                TerrainSamplerBurst.Sample(snapshot.TerrainProfile, vx * s, (vz - 1) * s, vy * s, out dB, out _);
                TerrainSamplerBurst.Sample(snapshot.TerrainProfile, vx * s, (vz + 1) * s, vy * s, out dF, out _);
              }

              Vector3 g = new Vector3(dL - dR, dD - dU, dB - dF);
              float m2 = g.x * g.x + g.y * g.y + g.z * g.z;
              Vertex vert = finalVertices[i];
              Vector3 normal = m2 > 0.000001f ? g / Mathf.Sqrt(m2) : Vector3.up;
              vert.Normal = normal;
              vert.UV = ProjectUv(vert.Position, normal);
              finalVertices[i] = vert;
            }
          }

          Mesh mesh = new()
          {
            name = $"Density Chunk {chunkCoord}",
            indexFormat = totalVerts > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
          };

          List<Vector3> vertices = new(totalVerts);
          List<Vector3> normals = new(totalVerts);
          List<Vector2> uvs = new(totalVerts);
          List<Color32> colors = new(totalVerts);
          List<int> indices = new(totalIndices);

          for (int i = 0; i < totalVerts; i++)
          {
            Vertex v = finalVertices[i];
            vertices.Add(v.Position);
            normals.Add(v.Normal);
            uvs.Add(v.UV);
            colors.Add(v.Color);
          }

          for (int i = 0; i < totalIndices; i++)
          {
            indices.Add(finalIndices[i]);
          }

          mesh.SetVertices(vertices);
          mesh.SetNormals(normals);
          mesh.SetUVs(0, uvs);
          mesh.SetColors(colors);
          mesh.SetTriangles(indices, 0, true);

          // Do NOT call RecalculateNormals here — smooth gradient normals are already
          // baked by BuildNormalGridJob. Calling RecalculateNormals overwrites them with
          // flat per-triangle normals, which produces the faceted "diamond" look.

          if (totalVerts > 0)
          {
            Vector3 min = finalVertices[0].Position;
            Vector3 max = finalVertices[0].Position;

            for (int i = 1; i < totalVerts; i++)
            {
              Vector3 v = finalVertices[i].Position;
              if (v.x < min.x) min.x = v.x;
              if (v.y < min.y) min.y = v.y;
              if (v.z < min.z) min.z = v.z;
              if (v.x > max.x) max.x = v.x;
              if (v.y > max.y) max.y = v.y;
              if (v.z > max.z) max.z = v.z;
            }

            mesh.bounds = new Bounds((min + max) * 0.5f, max - min);
          }
          else
          {
            mesh.bounds = new Bounds(Vector3.zero, Vector3.zero);
          }

          return mesh;
        }
        finally
        {
          if (finalVertices.IsCreated) finalVertices.Dispose();
          if (finalIndices.IsCreated) finalIndices.Dispose();
        }
      }
      finally
      {
        if (densityGrid.IsCreated) densityGrid.Dispose();
        if (normalGrid.IsCreated) normalGrid.Dispose();
        if (materialGrid.IsCreated) materialGrid.Dispose();
      }
    }


    private struct TriangulationStripeJob : IJobParallelFor
    {
      [ReadOnly] public NativeArray<float> Density;
      [ReadOnly] public NativeArray<Vector3> Normals;
      [ReadOnly] public NativeArray<ushort> Materials;
      public int SafeCellStep;
      public int NumCellsAxis;
      public int NumSamplesAxis;
      public float VoxelSize;
      public bool FlipWinding;

      [ReadOnly] public NativeArray<int> CubeCornerOffset; // 8*3
      [ReadOnly] public NativeArray<int> EdgeConnection;   // 12*2
      [ReadOnly] public NativeArray<int> TriangleTable;    // 256*16

      public int MaxVertsPerStripe;
      public int MaxIndicesPerStripe;
      [NativeDisableParallelForRestriction] public NativeArray<Vector3> StripeVertices;
      [NativeDisableParallelForRestriction] public NativeArray<Vector3> StripeNormals;
      [NativeDisableParallelForRestriction] public NativeArray<Vector2> StripeUVs;
      [NativeDisableParallelForRestriction] public NativeArray<Color32> StripeColors;
      [NativeDisableParallelForRestriction] public NativeArray<int> StripeIndices;
      public NativeArray<int> StripeVertexCounts;
      public NativeArray<int> StripeIndexCounts;

      public void Execute(int cz)
      {
        float[] densities = new float[8];
        Vector3[] positions = new Vector3[8];
        Vector3[] normals = new Vector3[8];
        ushort[] materials = new ushort[8];
        Vector3[] edgeVertices = new Vector3[12];
        Vector3[] edgeNormals = new Vector3[12];
        int[] edgeIndices = new int[12];
        bool[] hasEdge = new bool[12];

        int vBase = cz * MaxVertsPerStripe;
        int iBase = cz * MaxIndicesPerStripe;
        int vWrite = 0;
        int iWrite = 0;

        for (int cy = 0; cy < NumCellsAxis; cy++)
        {
          for (int cx = 0; cx < NumCellsAxis; cx++)
          {
            int cubeIndex = 0;

            for (int corner = 0; corner < 8; corner++)
            {
              int sx = cx + CubeCornerOffset[corner * 3 + 0];
              int sy = cy + CubeCornerOffset[corner * 3 + 1];
              int sz = cz + CubeCornerOffset[corner * 3 + 2];
              int sampleIndex = sx + NumSamplesAxis * (sy + NumSamplesAxis * sz);

              densities[corner] = Density[sampleIndex];
              normals[corner] = Normals[sampleIndex];
              materials[corner] = Materials[sampleIndex];

              positions[corner] = new Vector3(
                  sx * SafeCellStep * VoxelSize,
                  sy * SafeCellStep * VoxelSize,
                  sz * SafeCellStep * VoxelSize
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

            if (TriangleTable[cubeIndex * 16 + 0] < 0)
            {
              continue;
            }

            for (int i = 0; i < 12; i++) { edgeIndices[i] = -1; hasEdge[i] = false; }

            ushort materialId = ChooseMaterial(densities, materials);
            Color32 vertexColor = MaterialToColor(materialId);

            for (int t = 0; t < 16; t += 3)
            {
              int edge0 = TriangleTable[cubeIndex * 16 + t + 0];
              if (edge0 < 0) break;
              int edge1 = TriangleTable[cubeIndex * 16 + t + 1];
              int edge2 = TriangleTable[cubeIndex * 16 + t + 2];
              if (edge1 < 0 || edge2 < 0 || edge0 >= 12 || edge1 >= 12 || edge2 >= 12) break;

              int v0 = BuildEdgeIndex(edge0, positions, densities, normals, edgeVertices, edgeNormals, edgeIndices, hasEdge, vertexColor, vBase, ref vWrite);
              int v1 = BuildEdgeIndex(edge1, positions, densities, normals, edgeVertices, edgeNormals, edgeIndices, hasEdge, vertexColor, vBase, ref vWrite);
              int v2 = BuildEdgeIndex(edge2, positions, densities, normals, edgeVertices, edgeNormals, edgeIndices, hasEdge, vertexColor, vBase, ref vWrite);
              if (v0 < 0 || v1 < 0 || v2 < 0 || v0 == v1 || v1 == v2 || v0 == v2) continue;

              if (iWrite + 3 > MaxIndicesPerStripe)
              {
                continue;
              }

              if (FlipWinding)
              {
                StripeIndices[iBase + (iWrite++)] = v0;
                StripeIndices[iBase + (iWrite++)] = v2;
                StripeIndices[iBase + (iWrite++)] = v1;
              }
              else
              {
                StripeIndices[iBase + (iWrite++)] = v0;
                StripeIndices[iBase + (iWrite++)] = v1;
                StripeIndices[iBase + (iWrite++)] = v2;
              }
            }
          }
        }

        StripeVertexCounts[cz] = vWrite;
        StripeIndexCounts[cz] = iWrite;
      }

      private int BuildEdgeIndex(
          int edgeIndex,
          Vector3[] positions,
          float[] densities,
          Vector3[] normals,
          Vector3[] edgeVertices,
          Vector3[] edgeNormals,
          int[] edgeIndices,
          bool[] hasEdge,
          Color32 vertexColor,
          int vBase,
          ref int vWrite)
      {
        if (hasEdge[edgeIndex])
        {
          return edgeIndices[edgeIndex];
        }

        int cornerA = EdgeConnection[edgeIndex * 2 + 0];
        int cornerB = EdgeConnection[edgeIndex * 2 + 1];

        edgeVertices[edgeIndex] = InterpolateVertex(
            positions[cornerA],
            positions[cornerB],
            densities[cornerA],
            densities[cornerB]
        );

        edgeNormals[edgeIndex] = InterpolateNormal(
            normals[cornerA],
            normals[cornerB],
            densities[cornerA],
            densities[cornerB]
        );

        Vector3 finalNormal = edgeNormals[edgeIndex].sqrMagnitude > 0.000001f
            ? edgeNormals[edgeIndex].normalized
            : Vector3.up;

        if (vWrite >= MaxVertsPerStripe)
        {
          return -1;
        }

        int newIndex = vWrite;
        StripeVertices[vBase + vWrite] = edgeVertices[edgeIndex];
        StripeNormals[vBase + vWrite] = finalNormal;
        StripeUVs[vBase + vWrite] = ProjectUv(edgeVertices[edgeIndex], finalNormal);
        StripeColors[vBase + vWrite] = vertexColor;
        vWrite++;

        edgeIndices[edgeIndex] = newIndex;
        hasEdge[edgeIndex] = true;
        return newIndex;
      }
    }

    private struct TriangulationStripeCountJob : IJobParallelFor
    {
      [ReadOnly] public NativeArray<float> Density;
      [ReadOnly] public NativeArray<ushort> Materials;
      public int SafeCellStep;
      public int NumCellsAxis;
      public int NumSamplesAxis;

      [ReadOnly] public NativeArray<int> CubeCornerOffset; // 8*3
      [ReadOnly] public NativeArray<int> TriangleTable;    // 256*16

      public NativeArray<int> StripeVertexCounts;
      public NativeArray<int> StripeIndexCounts;

      public void Execute(int cz)
      {
        int vCount = 0;
        int iCount = 0;

        float[] densities = new float[8];

        for (int cy = 0; cy < NumCellsAxis; cy++)
        {
          for (int cx = 0; cx < NumCellsAxis; cx++)
          {
            int cubeIndex = 0;

            for (int corner = 0; corner < 8; corner++)
            {
              int sx = cx + CubeCornerOffset[corner * 3 + 0];
              int sy = cy + CubeCornerOffset[corner * 3 + 1];
              int sz = cz + CubeCornerOffset[corner * 3 + 2];
              int sampleIndex = sx + NumSamplesAxis * (sy + NumSamplesAxis * sz);

              float d = Density[sampleIndex];
              densities[corner] = d;
              if (d > 0.0f) cubeIndex |= 1 << corner;
            }

            if (cubeIndex == 0 || cubeIndex == 255) continue;
            if (TriangleTable[cubeIndex * 16 + 0] < 0) continue;

            // Each triangle adds 3 indices and 3 unique vertices worst-case (edge caching ignored for count)
            for (int t = 0; t < 16; t += 3)
            {
              int e0 = TriangleTable[cubeIndex * 16 + t + 0];
              if (e0 < 0) break;
              int e1 = TriangleTable[cubeIndex * 16 + t + 1];
              int e2 = TriangleTable[cubeIndex * 16 + t + 2];
              if (e1 < 0 || e2 < 0) break;
              iCount += 3;
              vCount += 3; // upper-bound; build will reuse via edge cache if possible, final may be lower
            }
          }
        }

        StripeVertexCounts[cz] = vCount;
        StripeIndexCounts[cz] = iCount;
      }
    }

    private struct TriangulationStripeBuildJob : IJobParallelFor
    {
      [ReadOnly] public NativeArray<float> Density;
      [ReadOnly] public NativeArray<Vector3> Normals;
      [ReadOnly] public NativeArray<ushort> Materials;
      public int SafeCellStep;
      public int NumCellsAxis;
      public int NumSamplesAxis;
      public float VoxelSize;
      public bool FlipWinding;

      [ReadOnly] public NativeArray<int> CubeCornerOffset; // 8*3
      [ReadOnly] public NativeArray<int> EdgeConnection;   // 12*2
      [ReadOnly] public NativeArray<int> TriangleTable;    // 256*16

      [ReadOnly] public NativeArray<int> StripeVertexOffsets;
      [ReadOnly] public NativeArray<int> StripeIndexOffsets;
      [NativeDisableParallelForRestriction] public NativeArray<Vector3> FinalVertices;
      [NativeDisableParallelForRestriction] public NativeArray<Vector3> FinalNormals;
      [NativeDisableParallelForRestriction] public NativeArray<Vector2> FinalUVs;
      [NativeDisableParallelForRestriction] public NativeArray<Color32> FinalColors;
      [NativeDisableParallelForRestriction] public NativeArray<int> FinalIndices;

      public void Execute(int cz)
      {
        float[] densities = new float[8];
        Vector3[] positions = new Vector3[8];
        Vector3[] normals = new Vector3[8];
        ushort[] materials = new ushort[8];
        Vector3[] edgeVertices = new Vector3[12];
        Vector3[] edgeNormals = new Vector3[12];
        int[] edgeIndices = new int[12];
        bool[] hasEdge = new bool[12];

        int vWrite = 0;
        int iWrite = 0;
        int vBase = StripeVertexOffsets[cz];
        int iBase = StripeIndexOffsets[cz];
        int iCap = (cz == NumCellsAxis - 1 ? FinalIndices.Length : StripeIndexOffsets[cz + 1]) - iBase;

        for (int cy = 0; cy < NumCellsAxis; cy++)
        {
          for (int cx = 0; cx < NumCellsAxis; cx++)
          {
            int cubeIndex = 0;

            for (int corner = 0; corner < 8; corner++)
            {
              int sx = cx + CubeCornerOffset[corner * 3 + 0];
              int sy = cy + CubeCornerOffset[corner * 3 + 1];
              int sz = cz + CubeCornerOffset[corner * 3 + 2];
              int sampleIndex = sx + NumSamplesAxis * (sy + NumSamplesAxis * sz);

              densities[corner] = Density[sampleIndex];
              normals[corner] = Normals[sampleIndex];
              materials[corner] = Materials[sampleIndex];

              positions[corner] = new Vector3(
                  sx * SafeCellStep * VoxelSize,
                  sy * SafeCellStep * VoxelSize,
                  sz * SafeCellStep * VoxelSize
              );

              if (densities[corner] > 0.0f)
              {
                cubeIndex |= 1 << corner;
              }
            }

            if (cubeIndex == 0 || cubeIndex == 255) continue;
            if (TriangleTable[cubeIndex * 16 + 0] < 0) continue;

            for (int i = 0; i < 12; i++) { edgeIndices[i] = -1; hasEdge[i] = false; }

            ushort materialId = ChooseMaterial(densities, materials);
            Color32 vertexColor = MaterialToColor(materialId);

            for (int t = 0; t < 16; t += 3)
            {
              int edge0 = TriangleTable[cubeIndex * 16 + t + 0]; if (edge0 < 0) break;
              int edge1 = TriangleTable[cubeIndex * 16 + t + 1];
              int edge2 = TriangleTable[cubeIndex * 16 + t + 2];
              if (edge1 < 0 || edge2 < 0 || edge0 >= 12 || edge1 >= 12 || edge2 >= 12) break;

              int v0 = BuildEdgeIndex(edge0, positions, densities, normals, edgeVertices, edgeNormals, edgeIndices, hasEdge, vertexColor, vBase, ref vWrite);
              int v1 = BuildEdgeIndex(edge1, positions, densities, normals, edgeVertices, edgeNormals, edgeIndices, hasEdge, vertexColor, vBase, ref vWrite);
              int v2 = BuildEdgeIndex(edge2, positions, densities, normals, edgeVertices, edgeNormals, edgeIndices, hasEdge, vertexColor, vBase, ref vWrite);
              if (v0 < 0 || v1 < 0 || v2 < 0 || v0 == v1 || v1 == v2 || v0 == v2) continue;

              if (iWrite + 3 > iCap) continue; // prevent overflow

              if (FlipWinding)
              {
                FinalIndices[iBase + (iWrite++)] = v0 + vBase;
                FinalIndices[iBase + (iWrite++)] = v2 + vBase;
                FinalIndices[iBase + (iWrite++)] = v1 + vBase;
              }
              else
              {
                FinalIndices[iBase + (iWrite++)] = v0 + vBase;
                FinalIndices[iBase + (iWrite++)] = v1 + vBase;
                FinalIndices[iBase + (iWrite++)] = v2 + vBase;
              }
            }
          }
        }
      }

      private int BuildEdgeIndex(
      int edgeIndex,
      Vector3[] positions,
      float[] densities,
      Vector3[] normals,
      Vector3[] edgeVertices,
      Vector3[] edgeNormals,
      int[] edgeIndices,
      bool[] hasEdge,
      Color32 vertexColor,
      int vBase,
        ref int vWrite)
      {
        if (hasEdge[edgeIndex]) return edgeIndices[edgeIndex];

        int cornerA = EdgeConnection[edgeIndex * 2 + 0];
        int cornerB = EdgeConnection[edgeIndex * 2 + 1];
        edgeVertices[edgeIndex] = InterpolateVertex(positions[cornerA], positions[cornerB], densities[cornerA], densities[cornerB]);
        edgeNormals[edgeIndex] = InterpolateNormal(normals[cornerA], normals[cornerB], densities[cornerA], densities[cornerB]);
        Vector3 finalNormal = edgeNormals[edgeIndex].sqrMagnitude > 0.000001f ? edgeNormals[edgeIndex].normalized : Vector3.up;

        int newIndex = vWrite;
        FinalVertices[vBase + vWrite] = edgeVertices[edgeIndex];
        FinalNormals[vBase + vWrite] = finalNormal;
        FinalUVs[vBase + vWrite] = ProjectUv(edgeVertices[edgeIndex], finalNormal);
        FinalColors[vBase + vWrite] = vertexColor;
        vWrite++;

        edgeIndices[edgeIndex] = newIndex;
        hasEdge[edgeIndex] = true;
        return newIndex;
      }
    }

    private struct TriangulationStripeExactCountJob : IJobParallelFor
    {
      [ReadOnly] public NativeArray<float> Density;
      public int SafeCellStep;
      public int NumCellsAxis;
      public int NumSamplesAxis;
      [ReadOnly] public NativeArray<int> CubeCornerOffset;
      [ReadOnly] public NativeArray<int> TriangleTable;
      public NativeArray<int> StripeVertexCounts;
      public NativeArray<int> StripeIndexCounts;

      public void Execute(int cz)
      {
        int vCount = 0; int iCount = 0;
        float[] densities = new float[8];

        for (int cy = 0; cy < NumCellsAxis; cy++)
        {
          for (int cx = 0; cx < NumCellsAxis; cx++)
          {
            int cubeIndex = 0;
            for (int corner = 0; corner < 8; corner++)
            {
              int sx = cx + CubeCornerOffset[corner * 3 + 0];
              int sy = cy + CubeCornerOffset[corner * 3 + 1];
              int sz = cz + CubeCornerOffset[corner * 3 + 2];
              int sampleIndex = sx + NumSamplesAxis * (sy + NumSamplesAxis * sz);
              float d = Density[sampleIndex];
              densities[corner] = d;
              if (d > 0.0f) cubeIndex |= 1 << corner;
            }

            if (cubeIndex == 0 || cubeIndex == 255) continue;
            if (TriangleTable[cubeIndex * 16 + 0] < 0) continue;

            bool[] hasEdge = new bool[12];
            for (int t = 0; t < 16; t += 3)
            {
              int e0 = TriangleTable[cubeIndex * 16 + t + 0];
              if (e0 < 0) break;
              int e1 = TriangleTable[cubeIndex * 16 + t + 1];
              int e2 = TriangleTable[cubeIndex * 16 + t + 2];
              if (e1 < 0 || e2 < 0) break;

              iCount += 3;
              if (!hasEdge[e0]) { hasEdge[e0] = true; vCount++; }
              if (!hasEdge[e1]) { hasEdge[e1] = true; vCount++; }
              if (!hasEdge[e2]) { hasEdge[e2] = true; vCount++; }
            }
          }
        }

        StripeVertexCounts[cz] = vCount;
        StripeIndexCounts[cz] = iCount;
      }
    }

    private struct TriangulationStripeBuildMeshJob : IJobParallelFor
    {
      [ReadOnly] public NativeArray<float> Density;
      [ReadOnly] public NativeArray<Vector3> Normals;
      [ReadOnly] public NativeArray<ushort> Materials;
      public int SafeCellStep;
      public int NumCellsAxis;
      public int NumSamplesAxis;
      public float VoxelSize;
      public bool FlipWinding;
      [ReadOnly] public NativeArray<int> CubeCornerOffset;
      [ReadOnly] public NativeArray<int> EdgeConnection;
      [ReadOnly] public NativeArray<int> TriangleTable;
      [ReadOnly] public NativeArray<int> StripeVertexOffsets;
      [ReadOnly] public NativeArray<int> StripeIndexOffsets;

      [NativeDisableParallelForRestriction] public NativeArray<Vertex> OutVertices;
      [NativeDisableParallelForRestriction] public NativeArray<int> OutIndices;

      public void Execute(int cz)
      {
        float[] densities = new float[8];
        Vector3[] positions = new Vector3[8];
        Vector3[] normals = new Vector3[8];
        ushort[] materials = new ushort[8];
        Vector3[] edgeVertices = new Vector3[12];
        Vector3[] edgeNormals = new Vector3[12];
        int[] edgeIndices = new int[12];
        bool[] hasEdge = new bool[12];

        int vWrite = 0; int iWrite = 0;
        int vBase = StripeVertexOffsets[cz];
        int iBase = StripeIndexOffsets[cz];

        for (int cy = 0; cy < NumCellsAxis; cy++)
        {
          for (int cx = 0; cx < NumCellsAxis; cx++)
          {
            int cubeIndex = 0;
            for (int corner = 0; corner < 8; corner++)
            {
              int sx = cx + CubeCornerOffset[corner * 3 + 0];
              int sy = cy + CubeCornerOffset[corner * 3 + 1];
              int sz = cz + CubeCornerOffset[corner * 3 + 2];
              int sampleIndex = sx + NumSamplesAxis * (sy + NumSamplesAxis * sz);
              densities[corner] = Density[sampleIndex];
              normals[corner] = Normals[sampleIndex];
              materials[corner] = Materials[sampleIndex];
              positions[corner] = new Vector3(
                  sx * SafeCellStep * VoxelSize,
                  sy * SafeCellStep * VoxelSize,
                  sz * SafeCellStep * VoxelSize
              );
              if (densities[corner] > 0.0f) cubeIndex |= 1 << corner;
            }

            if (cubeIndex == 0 || cubeIndex == 255) continue;
            if (TriangleTable[cubeIndex * 16 + 0] < 0) continue;
            for (int i = 0; i < 12; i++) { edgeIndices[i] = -1; hasEdge[i] = false; }

            ushort mat = ChooseMaterial(densities, materials);
            Color32 col = MaterialToColor(mat);


            for (int t = 0; t < 16; t += 3)
            {
              int e0 = TriangleTable[cubeIndex * 16 + t + 0]; if (e0 < 0) break;
              int e1 = TriangleTable[cubeIndex * 16 + t + 1];
              int e2 = TriangleTable[cubeIndex * 16 + t + 2];
              if (e1 < 0 || e2 < 0) break;

              int lv0;
              if (hasEdge[e0]) lv0 = edgeIndices[e0];
              else
              {
                int a = EdgeConnection[e0 * 2 + 0];
                int b = EdgeConnection[e0 * 2 + 1];
                Vector3 v = InterpolateVertex(positions[a], positions[b], densities[a], densities[b]);
                Vector3 n = InterpolateNormal(normals[a], normals[b], densities[a], densities[b]);
                int newIndex = vBase + vWrite;
                Vertex vert; vert.Position = v; vert.Normal = n; vert.Color = col; vert.UV = ProjectUv(v, n);
                OutVertices[newIndex] = vert;
                edgeIndices[e0] = vWrite; hasEdge[e0] = true; lv0 = edgeIndices[e0]; vWrite++;
              }

              int lv1;
              if (hasEdge[e1]) lv1 = edgeIndices[e1];
              else
              {
                int a = EdgeConnection[e1 * 2 + 0];
                int b = EdgeConnection[e1 * 2 + 1];
                Vector3 v = InterpolateVertex(positions[a], positions[b], densities[a], densities[b]);
                Vector3 n = InterpolateNormal(normals[a], normals[b], densities[a], densities[b]);
                int newIndex = vBase + vWrite;
                Vertex vert; vert.Position = v; vert.Normal = n; vert.Color = col; vert.UV = ProjectUv(v, n);
                OutVertices[newIndex] = vert;
                edgeIndices[e1] = vWrite; hasEdge[e1] = true; lv1 = edgeIndices[e1]; vWrite++;
              }

              int lv2;
              if (hasEdge[e2]) lv2 = edgeIndices[e2];
              else
              {
                int a = EdgeConnection[e2 * 2 + 0];
                int b = EdgeConnection[e2 * 2 + 1];
                Vector3 v = InterpolateVertex(positions[a], positions[b], densities[a], densities[b]);
                Vector3 n = InterpolateNormal(normals[a], normals[b], densities[a], densities[b]);
                int newIndex = vBase + vWrite;
                Vertex vert; vert.Position = v; vert.Normal = n; vert.Color = col; vert.UV = ProjectUv(v, n);
                OutVertices[newIndex] = vert;
                edgeIndices[e2] = vWrite; hasEdge[e2] = true; lv2 = edgeIndices[e2]; vWrite++;
              }
              if (lv0 < 0 || lv1 < 0 || lv2 < 0) continue;

              if (FlipWinding)
              {
                OutIndices[iBase + (iWrite++)] = vBase + lv0;
                OutIndices[iBase + (iWrite++)] = vBase + lv2;
                OutIndices[iBase + (iWrite++)] = vBase + lv1;
              }
              else
              {
                OutIndices[iBase + (iWrite++)] = vBase + lv0;
                OutIndices[iBase + (iWrite++)] = vBase + lv1;
                OutIndices[iBase + (iWrite++)] = vBase + lv2;
              }
            }
          }
        }
      }
    }

#if UNITY_BURST
    [BurstCompile]
#endif
    private struct BuildDensityMaterialGridJob : IJobParallelFor
    {
      public NativeArray<float> Density;
      public NativeArray<ushort> Material;
      public int SafeCellStep;
      public int NumSamplesAxis;
      public Vector3Int ChunkCoord;
      public WorldGenerationSnapshot Snapshot;

      public void Execute(int index)
      {
        int syz = index / NumSamplesAxis;
        int sx = index - syz * NumSamplesAxis;
        int sz = syz / NumSamplesAxis;
        int sy = syz - sz * NumSamplesAxis;

        int lx = sx * SafeCellStep;
        int ly = sy * SafeCellStep;
        int lz = sz * SafeCellStep;

        int wx = ChunkCoord.x * VoxelConstants.ChunkSize + lx;
        int wy = ChunkCoord.y * VoxelConstants.ChunkSize + ly;
        int wz = ChunkCoord.z * VoxelConstants.ChunkSize + lz;

        // Note: TerrainSampler maps y/z differently (wy=z, wz=y)
        double tWx = wx;
        double tWy = wz; // z -> wy
        double tWz = wy; // y -> wz

        double s = Snapshot.DensitySampleScale <= 0.0f ? 1.0 : (double)Snapshot.DensitySampleScale;
        TerrainSamplerBurst.Sample(Snapshot.TerrainProfile, tWx * s, tWy * s, tWz * s, out float density, out int solidMatId);

        Density[index] = density;
        Material[index] = density > 0.0f ? (ushort)Mathf.Clamp(solidMatId, 1, 65535) : (ushort)0;
      }
    }

#if UNITY_BURST
    [BurstCompile]
#endif
    private struct BuildNormalGridJob : IJobParallelFor
    {
      [ReadOnly] public NativeArray<float> Density;
      public NativeArray<Vector3> Normals;
      public int NumSamplesAxis;
      // previous seam experiment removed

      public void Execute(int index)
      {
        int syz = index / NumSamplesAxis;
        int sx = index - syz * NumSamplesAxis;
        int sz = syz / NumSamplesAxis;
        int sy = syz - sz * NumSamplesAxis;

        int sxL = Mathf.Max(sx - 1, 0);
        int sxR = Mathf.Min(sx + 1, NumSamplesAxis - 1);
        int syD = Mathf.Max(sy - 1, 0);
        int syU = Mathf.Min(sy + 1, NumSamplesAxis - 1);
        int szB = Mathf.Max(sz - 1, 0);
        int szF = Mathf.Min(sz + 1, NumSamplesAxis - 1);

        int idxL = sxL + NumSamplesAxis * (sy + NumSamplesAxis * sz);
        int idxR = sxR + NumSamplesAxis * (sy + NumSamplesAxis * sz);
        int idxD = sx + NumSamplesAxis * (syD + NumSamplesAxis * sz);
        int idxU = sx + NumSamplesAxis * (syU + NumSamplesAxis * sz);
        int idxB = sx + NumSamplesAxis * (sy + NumSamplesAxis * szB);
        int idxF = sx + NumSamplesAxis * (sy + NumSamplesAxis * szF);

        float dL = Density[idxL];
        float dR = Density[idxR];
        float dD = Density[idxD];
        float dU = Density[idxU];
        float dB = Density[idxB];
        float dF = Density[idxF];

#if UNITY_MATHEMATICS
        float3 nf = new float3(dL - dR, dD - dU, dB - dF);
        float mag2 = math.lengthsq(nf);
        if (mag2 > 0.000001f)
        {
          nf *= math.rsqrt(mag2);
          Normals[index] = new Vector3(nf.x, nf.y, nf.z);
        }
        else
        {
          Normals[index] = Vector3.up;
        }
#else
        Vector3 n = new Vector3(dL - dR, dD - dU, dB - dF);
        float mag2 = n.x * n.x + n.y * n.y + n.z * n.z;
        Normals[index] = mag2 > 0.000001f ? n / Mathf.Sqrt(mag2) : Vector3.up;
#endif
      }
    }

#if UNITY_BURST
    [BurstCompile]
#endif
    private static class TerrainSamplerBurst
    {
      public static void Sample(
          TerrainGenerationProfileSnapshot profile,
          double wx,
          double wy,
          double wz,
          out float density,
          out int solidMaterialId)
      {
        double edgeT = 0.0;
        double edgeFalloff = 1.0;

        if (profile.UseWorldEdgeFalloff)
        {
          double distance2D = System.Math.Sqrt(wx * wx + wy * wy);
          double safeWorldEdgeRadius = System.Math.Max(1.0, profile.WorldEdgeRadius);
          edgeT = Clamp01(distance2D / safeWorldEdgeRadius);
          double smoothEdge = SmoothStep(edgeT);
          edgeFalloff = 1.0 - smoothEdge;
        }

        double baseHeight = profile.BaseHeight;
        double mainHeight = profile.HeightScale * edgeFalloff;

        double continental =
            System.Math.Sin(wx * 0.0065 + wy * 0.0027) * 18.0 +
            System.Math.Cos(wy * 0.0058 - wx * 0.0021) * 16.0 +
            System.Math.Sin((wx + wy) * 0.0042) * 12.0;

        double hills =
            System.Math.Sin(wx * 0.018 + wy * 0.011) * 10.0 +
            System.Math.Cos(wy * 0.021 - wx * 0.009) * 9.0 +
            System.Math.Sin((wx - wy) * 0.016) * 7.0;

        double detail =
            System.Math.Sin(wx * 0.055 + wy * 0.037) * 3.5 +
            System.Math.Cos(wy * 0.061 - wx * 0.024) * 3.0;

        double ridgeBase = System.Math.Abs(
            System.Math.Sin(wx * 0.013 + wy * 0.019) +
            System.Math.Cos(wx * 0.017 - wy * 0.011)
        );

        double ridges = System.Math.Pow(Clamp(ridgeBase * 0.5, 0.0, 1.0), 2.0) * 24.0;

        double edgeDrop = profile.UseWorldEdgeFalloff
            ? System.Math.Pow(edgeT, 3.0) * 140.0
            : 0.0;

        double valleyMask = Clamp(
            System.Math.Sin(wx * 0.007) * 0.5 +
            System.Math.Cos(wy * 0.006) * 0.5,
            -1.0,
            1.0
        );

        double valleyCut = System.Math.Max(0.0, valleyMask) * 18.0 * edgeFalloff;

        double surfaceHeight =
            baseHeight +
            mainHeight +
            continental * edgeFalloff +
            hills * edgeFalloff +
            detail * edgeFalloff +
            ridges * edgeFalloff -
            edgeDrop -
            valleyCut;

        if (profile.UseWorldEdgeFalloff && edgeT > 0.78)
        {
          double outerT = Clamp01((edgeT - 0.78) / 0.22);
          double outerSmooth = SmoothStep(outerT);
          surfaceHeight = Lerp(surfaceHeight, profile.WorldEdgeTargetHeight, outerSmooth);
        }

        double d = surfaceHeight - wz;

        // Caves
        if (profile.CaveStrength > 0.0f && wz < surfaceHeight - profile.CaveStartDepth)
        {
          double frequency = System.Math.Max(0.0001, profile.CaveFrequency);

          double caveA = System.Math.Sin(wx * frequency + wy * frequency * 0.37 + wz * frequency * 1.71);
          double caveB = System.Math.Cos(wy * frequency * 1.23 - wz * frequency * 0.89 + wx * frequency * 0.53);
          double caveC = System.Math.Sin((wx + wy - wz) * frequency * 0.61);
          double combined = (caveA + caveB + caveC) / 3.0;
          double caveAmount = System.Math.Max(0.0, combined) * profile.CaveStrength;
          d -= caveAmount;
        }

        density = (float)d;

        if (density <= 0.0f)
        {
          solidMaterialId = 0;
          return;
        }

        float depthBelowSurface = (float)surfaceHeight - (float)wz;
        if (depthBelowSurface <= 3.0f)
        {
          solidMaterialId = ClampMat(profile.SurfaceMaterialId);
        }
        else if (depthBelowSurface <= 18.0f)
        {
          solidMaterialId = ClampMat(profile.SubsurfaceMaterialId);
        }
        else
        {
          solidMaterialId = ClampMat(profile.StoneMaterialId);
        }
      }

      private static int ClampMat(int v) => Mathf.Clamp(v, 1, 65535);
      private static double Clamp01(double v) => Clamp(v, 0.0, 1.0);
      private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);
      private static double SmoothStep(double t) => t * t * (3.0 - 2.0 * t);
      private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }

    private static Vector3 InterpolateVertex(Vector3 p0, Vector3 p1, float d0, float d1)
    {
      float denominator = d0 - d1;

      if (Mathf.Abs(denominator) < 0.000001f)
      {
        return (p0 + p1) * 0.5f;
      }

      float t = Mathf.Clamp01(d0 / denominator);
#if UNITY_MATHEMATICS
      var a = new Unity.Mathematics.float3(p0.x, p0.y, p0.z);
      var b = new Unity.Mathematics.float3(p1.x, p1.y, p1.z);
      var r = Unity.Mathematics.math.lerp(a, b, t);
      return new Vector3(r.x, r.y, r.z);
#else
      return p0 + (p1 - p0) * t;
#endif
    }

    private static Vector3 InterpolateNormal(Vector3 n0, Vector3 n1, float d0, float d1)
    {
      float denominator = d0 - d1;

      if (Mathf.Abs(denominator) < 0.000001f)
      {
        Vector3 average = n0 + n1;
        return average.sqrMagnitude > 0.000001f ? average.normalized : Vector3.up;
      }

      float t = Mathf.Clamp01(d0 / denominator);
      Vector3 normal = n0 + (n1 - n0) * t;
      return normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.up;
    }

    private static ushort ChooseMaterial(float[] densities, ushort[] materials)
    {
      ushort bestMaterial = 0;
      float bestDensity = float.MinValue;

      for (int i = 0; i < 8; i++)
      {
        if (densities[i] <= 0.0f || densities[i] <= bestDensity || materials[i] == 0)
        {
          continue;
        }

        bestDensity = densities[i];
        bestMaterial = materials[i];
      }

      return bestMaterial != 0 ? bestMaterial : (ushort)1;
    }

    private static Color32 MaterialToColor(ushort materialId)
    {
      byte value = (byte)Mathf.Clamp(120 + materialId * 20, 80, 220);
      return new Color32(value, value, value, 255);
    }

    private static Vector2 ProjectUv(Vector3 p, Vector3 n)
    {
      const float uvScale = 0.05f;
#if UNITY_MATHEMATICS
      var an = Unity.Mathematics.math.abs(new Unity.Mathematics.float3(n.x, n.y, n.z));
      if (an.y >= an.x && an.y >= an.z)
      {
        return new Vector2(p.x * uvScale, p.z * uvScale);
      }
      if (an.x >= an.z)
      {
        return new Vector2(p.z * uvScale, p.y * uvScale);
      }
      return new Vector2(p.x * uvScale, p.y * uvScale);
#else
      Vector3 absNormal = new(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));

      if (absNormal.y >= absNormal.x && absNormal.y >= absNormal.z)
      {
        return new Vector2(p.x * uvScale, p.z * uvScale);
      }

      if (absNormal.x >= absNormal.z)
      {
        return new Vector2(p.z * uvScale, p.y * uvScale);
      }

      return new Vector2(p.x * uvScale, p.y * uvScale);
#endif
    }
  }
}
