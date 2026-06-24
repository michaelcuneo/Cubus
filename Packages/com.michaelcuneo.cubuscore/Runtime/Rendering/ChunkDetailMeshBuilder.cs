using System;
using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering
{
  /// <summary>
  /// Builds a deterministic per-chunk foliage mesh: cross-quad billboards scattered
  /// on the top-exposed surface voxels whose material has a <see cref="DetailScatterProfile"/>.
  /// Placement is hashed from world voxel coordinates so it is stable across reloads
  /// and identical for every client.
  /// </summary>
  public static class ChunkDetailMeshBuilder
  {
    private const int Size = VoxelConstants.ChunkSize;
    private const int MaxVertices = 60000;

    // Reused across builds (main-thread only) to avoid per-chunk GC churn.
    private static readonly List<Vector3> Positions = new(8192);
    private static readonly List<Vector3> Normals = new(8192);
    private static readonly List<Vector2> Uvs = new(8192);
    private static readonly List<Vector2> Uv2 = new(8192);
    private static readonly List<Color32> Colors = new(8192);
    private static readonly List<int> Triangles = new(12288);

    /// <summary>
    /// Rebuilds <paramref name="reuseMesh"/> (or allocates one when null) with the chunk's
    /// foliage. Returns null when no foliage is produced.
    /// </summary>
    public static Mesh Build(
        BlockChunkData chunkData,
        Func<Vector3Int, ushort> getMaterialAtWorldVoxel,
        DetailScatterDatabase database,
        float voxelSize,
        float densityMultiplier,
        Mesh reuseMesh)
    {
      if (chunkData == null || database == null || !database.HasAnyProfiles)
      {
        return null;
      }

      Positions.Clear();
      Normals.Clear();
      Uvs.Clear();
      Uv2.Clear();
      Colors.Clear();
      Triangles.Clear();

      int cols = Mathf.Max(1, database.AtlasColumns);
      int rows = Mathf.Max(1, database.AtlasRows);
      float insetU = 0.5f / (cols * 256f);
      float insetV = 0.5f / (rows * 256f);

      for (int x = 0; x < Size; x++)
      {
        for (int z = 0; z < Size; z++)
        {
          for (int y = 0; y < Size; y++)
          {
            Voxel voxel = chunkData.GetVoxel(x, y, z);
            if (!voxel.IsSolid)
            {
              continue;
            }

            // Surface voxel = solid with air directly above.
            bool aboveSolid;
            if (y < Size - 1)
            {
              aboveSolid = chunkData.GetVoxel(x, y + 1, z).IsSolid;
            }
            else
            {
              Vector3Int aboveWorld = chunkData.LocalToWorldVoxel(x, y + 1, z);
              aboveSolid = getMaterialAtWorldVoxel(aboveWorld) != 0;
            }

            if (aboveSolid)
            {
              continue;
            }

            DetailScatterProfile profile = database.GetProfile(voxel.MaterialId);
            if (profile == null || profile.AtlasTiles == null || profile.AtlasTiles.Length == 0)
            {
              continue;
            }

            Vector3Int worldVoxel = chunkData.LocalToWorldVoxel(x, y, z);
            uint state = Hash(worldVoxel.x, worldVoxel.y, worldVoxel.z);

            if (Rand01(ref state) > Mathf.Clamp01(profile.Coverage))
            {
              continue;
            }

            int maxClusters = Mathf.Max(0, Mathf.RoundToInt(profile.MaxClustersPerVoxel * Mathf.Max(0f, densityMultiplier)));
            if (maxClusters <= 0)
            {
              continue;
            }

            int clusters = 1 + Mathf.FloorToInt(Rand01(ref state) * maxClusters);
            clusters = Mathf.Clamp(clusters, 1, maxClusters);

            float baseX = x * voxelSize;
            float baseZ = z * voxelSize;
            float topY = (y + 1) * voxelSize;

            for (int c = 0; c < clusters; c++)
            {
              if (Positions.Count >= MaxVertices)
              {
                break;
              }

              float jx = baseX + (0.15f + Rand01(ref state) * 0.7f) * voxelSize;
              float jz = baseZ + (0.15f + Rand01(ref state) * 0.7f) * voxelSize;
              Vector3 basePos = new(jx, topY - 0.02f * voxelSize, jz);

              float height = Mathf.Lerp(profile.MinHeight, profile.MaxHeight, Rand01(ref state)) * voxelSize;
              float width = Mathf.Lerp(profile.MinWidth, profile.MaxWidth, Rand01(ref state)) * voxelSize;
              float yaw = Rand01(ref state) * Mathf.PI;
              float phase = Rand01(ref state);

              int tile = profile.AtlasTiles[Mathf.Min(profile.AtlasTiles.Length - 1,
                  Mathf.FloorToInt(Rand01(ref state) * profile.AtlasTiles.Length))];

              Color32 tint = JitterTint(profile.Tint, profile.TintVariation, ref state);
              float swayTop = Mathf.Clamp01(profile.SwayStrength * 0.5f) * 2f; // 0..2 -> stored as top height factor

              ComputeTileUv(tile, cols, rows, insetU, insetV,
                  out float u0, out float u1, out float vBottom, out float vTop);

              Vector3 dirA = new(Mathf.Cos(yaw) * width * 0.5f, 0f, Mathf.Sin(yaw) * width * 0.5f);
              Vector3 dirB = new(-Mathf.Sin(yaw) * width * 0.5f, 0f, Mathf.Cos(yaw) * width * 0.5f);

              AddQuad(basePos, dirA, height, u0, u1, vBottom, vTop, swayTop, phase, tint);
              AddQuad(basePos, dirB, height, u0, u1, vBottom, vTop, swayTop, phase, tint);
            }
          }
        }
      }

      if (Positions.Count == 0)
      {
        return null;
      }

      Mesh mesh = reuseMesh;
      if (mesh == null)
      {
        mesh = new Mesh { name = "Chunk Foliage" };
      }
      else
      {
        mesh.Clear();
      }

      mesh.indexFormat = Positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
      mesh.SetVertices(Positions);
      mesh.SetNormals(Normals);
      mesh.SetUVs(0, Uvs);
      mesh.SetUVs(1, Uv2);
      mesh.SetColors(Colors);
      mesh.SetTriangles(Triangles, 0, true);
      mesh.bounds = new Bounds(
          new Vector3(Size * voxelSize * 0.5f, Size * voxelSize * 0.5f, Size * voxelSize * 0.5f),
          new Vector3(Size * voxelSize, Size * voxelSize + 4f, Size * voxelSize));

      return mesh;
    }

    private static void AddQuad(
        Vector3 basePos,
        Vector3 dir,
        float height,
        float u0,
        float u1,
        float vBottom,
        float vTop,
        float swayTop,
        float phase,
        Color32 tint)
    {
      int i0 = Positions.Count;

      Vector3 up = new(0f, height, 0f);
      Vector3 bl = basePos - dir;
      Vector3 br = basePos + dir;
      Vector3 tr = br + up;
      Vector3 tl = bl + up;

      // Soft AO: darken the rooted base slightly.
      Color32 baseCol = Scale(tint, 0.72f);

      Positions.Add(bl); Positions.Add(br); Positions.Add(tr); Positions.Add(tl);

      Vector3 n = Vector3.up;
      Normals.Add(n); Normals.Add(n); Normals.Add(n); Normals.Add(n);

      Uvs.Add(new Vector2(u0, vBottom));
      Uvs.Add(new Vector2(u1, vBottom));
      Uvs.Add(new Vector2(u1, vTop));
      Uvs.Add(new Vector2(u0, vTop));

      // x = wind height factor (0 base, swayTop at tip), y = per-cluster phase.
      Uv2.Add(new Vector2(0f, phase));
      Uv2.Add(new Vector2(0f, phase));
      Uv2.Add(new Vector2(swayTop, phase));
      Uv2.Add(new Vector2(swayTop, phase));

      Colors.Add(baseCol); Colors.Add(baseCol); Colors.Add(tint); Colors.Add(tint);

      Triangles.Add(i0); Triangles.Add(i0 + 1); Triangles.Add(i0 + 2);
      Triangles.Add(i0); Triangles.Add(i0 + 2); Triangles.Add(i0 + 3);
    }

    private static void ComputeTileUv(
        int tile, int cols, int rows, float insetU, float insetV,
        out float u0, out float u1, out float vBottom, out float vTop)
    {
      tile = Mathf.Clamp(tile, 0, cols * rows - 1);
      int col = tile % cols;
      int row = tile / cols; // 0 = top image row

      u0 = (float)col / cols + insetU;
      u1 = (float)(col + 1) / cols - insetU;

      // Image row 0 is the top of the texture => highest V.
      vTop = 1f - (float)row / rows - insetV;
      vBottom = 1f - (float)(row + 1) / rows + insetV;
    }

    private static Color32 JitterTint(Color tint, float variation, ref uint state)
    {
      if (variation <= 0f)
      {
        return tint;
      }

      float b = 1f - variation * 0.5f + Rand01(ref state) * variation;
      return new Color(
          Mathf.Clamp01(tint.r * b),
          Mathf.Clamp01(tint.g * b),
          Mathf.Clamp01(tint.b * b),
          tint.a);
    }

    private static Color32 Scale(Color32 c, float f)
    {
      return new Color32(
          (byte)(c.r * f),
          (byte)(c.g * f),
          (byte)(c.b * f),
          c.a);
    }

    private static uint Hash(int x, int y, int z)
    {
      unchecked
      {
        uint h = 2166136261u;
        h = (h ^ (uint)x) * 16777619u;
        h = (h ^ (uint)y) * 16777619u;
        h = (h ^ (uint)z) * 16777619u;
        h ^= h >> 15;
        h *= 0x2c1b3c6du;
        h ^= h >> 12;
        h *= 0x297a2d39u;
        h ^= h >> 15;
        return h == 0u ? 0x9e3779b9u : h;
      }
    }

    private static float Rand01(ref uint state)
    {
      unchecked
      {
        state = state * 747796405u + 2891336453u;
        uint r = ((state >> (int)((state >> 28) + 4u)) ^ state) * 277803737u;
        r = (r >> 22) ^ r;
        return (r & 0xFFFFFFu) / 16777216f;
      }
    }
  }
}
