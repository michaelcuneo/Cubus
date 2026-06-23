using System;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing
{
  public static class BlockGreedyMesher
  {
    public delegate ushort MaterialLookup(Vector3Int worldVoxelCoord);

    public static MeshData GenerateNeighbourAware(
        BlockChunkData chunkData,
        MaterialLookup materialLookup,
        float voxelSize)
    {
      MeshData mesh = MeshDataPool.Rent(4096, 6144);

      GenerateNeighbourAware(chunkData, materialLookup, voxelSize, mesh);

      return mesh;
    }

    public static MeshData GenerateNeighbourAware(
        BlockChunkData chunkData,
        WorldGenerationSnapshot snapshot,
        float voxelSize)
    {
      MeshData mesh = MeshDataPool.Rent(4096, 6144);

      GenerateNeighbourAware(chunkData, snapshot, voxelSize, mesh);

      return mesh;
    }

    public static void GenerateNeighbourAware(
        BlockChunkData chunkData,
        WorldGenerationSnapshot snapshot,
        float voxelSize,
        MeshData mesh)
    {
      mesh.Reset();

      const int size = VoxelConstants.ChunkSize;
      Span<int> mask = stackalloc int[size * size];

      for (int axis = 0; axis < 3; axis++)
      {
        int u = (axis + 1) % 3;
        int v = (axis + 2) % 3;

        Span<int> x = stackalloc int[3];
        Span<int> q = stackalloc int[3];

        q[axis] = 1;

        for (x[axis] = -1; x[axis] < size;)
        {
          int n = 0;

          for (x[v] = 0; x[v] < size; x[v]++)
          {
            for (x[u] = 0; x[u] < size; x[u]++)
            {
              ushort materialA = GetMaterialNeighbourAware(
                  chunkData,
                  snapshot,
                  x[0],
                  x[1],
                  x[2]
              );

              ushort materialB = GetMaterialNeighbourAware(
                  chunkData,
                  snapshot,
                  x[0] + q[0],
                  x[1] + q[1],
                  x[2] + q[2]
              );

              bool solidA = materialA != 0;
              bool solidB = materialB != 0;

              if (solidA == solidB)
              {
                mask[n++] = 0;
              }
              else if (solidA)
              {
                // Face is owned by voxel A (at x[axis]); only emit it when A is
                // inside this chunk. On the x[axis] == -1 boundary plane A is the
                // neighbour voxel, whose face belongs to the neighbour chunk's
                // own mesh - emitting it here would double-draw the face (and,
                // when the neighbour is the unloaded solid fallback, paint a
                // phantom one-sided cap with the fallback material).
                mask[n++] = x[axis] >= 0 ? materialA : 0;
              }
              else
              {
                // Face is owned by voxel B (at x[axis] + 1); only emit it when B
                // is inside this chunk (the x[axis] == size - 1 plane has B as the
                // neighbour voxel, owned by the neighbour chunk's mesh).
                mask[n++] = (x[axis] + 1) < size ? -materialB : 0;
              }
            }
          }

          x[axis]++;

          n = 0;

          for (int j = 0; j < size; j++)
          {
            for (int i = 0; i < size;)
            {
              int currentMask = mask[n];

              if (currentMask == 0)
              {
                i++;
                n++;
                continue;
              }

              int width = 1;

              while (i + width < size && mask[n + width] == currentMask)
              {
                width++;
              }

              int height = 1;
              bool done = false;

              while (j + height < size)
              {
                for (int k = 0; k < width; k++)
                {
                  if (mask[n + k + height * size] != currentMask)
                  {
                    done = true;
                    break;
                  }
                }

                if (done)
                {
                  break;
                }

                height++;
              }

              Span<int> start = stackalloc int[3];
              start[0] = x[0];
              start[1] = x[1];
              start[2] = x[2];

              start[u] = i;
              start[v] = j;

              bool positiveFace = currentMask > 0;
              ushort materialId = (ushort)Math.Abs(currentMask);

              AddVoxelTiledGreedyQuad(
                  mesh,
                  axis,
                  u,
                  v,
                  start[0],
                  start[1],
                  start[2],
                  width,
                  height,
                  positiveFace,
                  materialId,
                  voxelSize
              );

              for (int y = 0; y < height; y++)
              {
                for (int x2 = 0; x2 < width; x2++)
                {
                  mask[n + x2 + y * size] = 0;
                }
              }

              i += width;
              n += width;
            }
          }
        }
      }

      MarkCompleteWithoutGeometryIfSolid(chunkData, mesh);
    }

    public static MeshData GenerateNeighbourAware(
        BlockChunkData chunkData,
        BlockChunkNeighborhood neighborhood,
        float voxelSize)
    {
      MeshData mesh = MeshDataPool.Rent(4096, 6144);

      GenerateNeighbourAware(
          chunkData,
          worldVoxelCoord =>
          {
            Vector3Int local = worldVoxelCoord - new Vector3Int(
                chunkData.ChunkCoord.x * VoxelConstants.ChunkSize,
                chunkData.ChunkCoord.y * VoxelConstants.ChunkSize,
                chunkData.ChunkCoord.z * VoxelConstants.ChunkSize
            );

            return neighborhood.GetMaterial(local.x, local.y, local.z);
          },
          voxelSize,
          mesh
      );

      return mesh;
    }

    public static void GenerateNeighbourAware(
        BlockChunkData chunkData,
        MaterialLookup materialLookup,
        float voxelSize,
        MeshData mesh)
    {
      mesh.Reset();

      const int size = VoxelConstants.ChunkSize;
      Span<int> mask = stackalloc int[size * size];

      for (int axis = 0; axis < 3; axis++)
      {
        int u = (axis + 1) % 3;
        int v = (axis + 2) % 3;

        Span<int> x = stackalloc int[3];
        Span<int> q = stackalloc int[3];

        q[axis] = 1;

        for (x[axis] = -1; x[axis] < size;)
        {
          int n = 0;

          for (x[v] = 0; x[v] < size; x[v]++)
          {
            for (x[u] = 0; x[u] < size; x[u]++)
            {
              ushort materialA = GetMaterialNeighbourAware(
                  chunkData,
                  materialLookup,
                  x[0],
                  x[1],
                  x[2]
              );

              ushort materialB = GetMaterialNeighbourAware(
                  chunkData,
                  materialLookup,
                  x[0] + q[0],
                  x[1] + q[1],
                  x[2] + q[2]
              );

              bool solidA = materialA != 0;
              bool solidB = materialB != 0;

              if (solidA == solidB)
              {
                mask[n++] = 0;
              }
              else if (solidA)
              {
                // Face is owned by voxel A (at x[axis]); only emit it when A is
                // inside this chunk. On the x[axis] == -1 boundary plane A is the
                // neighbour voxel, whose face belongs to the neighbour chunk's
                // own mesh - emitting it here would double-draw the face (and,
                // when the neighbour is the unloaded solid fallback, paint a
                // phantom one-sided cap with the fallback material).
                mask[n++] = x[axis] >= 0 ? materialA : 0;
              }
              else
              {
                // Face is owned by voxel B (at x[axis] + 1); only emit it when B
                // is inside this chunk (the x[axis] == size - 1 plane has B as the
                // neighbour voxel, owned by the neighbour chunk's mesh).
                mask[n++] = (x[axis] + 1) < size ? -materialB : 0;
              }
            }
          }

          x[axis]++;

          n = 0;

          for (int j = 0; j < size; j++)
          {
            for (int i = 0; i < size;)
            {
              int currentMask = mask[n];

              if (currentMask == 0)
              {
                i++;
                n++;
                continue;
              }

              int width = 1;

              while (
                  i + width < size &&
                  mask[n + width] == currentMask)
              {
                width++;
              }

              int height = 1;
              bool done = false;

              while (j + height < size)
              {
                for (int k = 0; k < width; k++)
                {
                  if (mask[n + k + height * size] != currentMask)
                  {
                    done = true;
                    break;
                  }
                }

                if (done)
                {
                  break;
                }

                height++;
              }

              Span<int> start = stackalloc int[3];
              start[0] = x[0];
              start[1] = x[1];
              start[2] = x[2];

              start[u] = i;
              start[v] = j;

              bool positiveFace = currentMask > 0;
              ushort materialId = (ushort)Math.Abs(currentMask);

              AddVoxelTiledGreedyQuad(
                  mesh,
                  axis,
                  u,
                  v,
                  start[0],
                  start[1],
                  start[2],
                  width,
                  height,
                  positiveFace,
                  materialId,
                  voxelSize
              );

              for (int y = 0; y < height; y++)
              {
                for (int x2 = 0; x2 < width; x2++)
                {
                  mask[n + x2 + y * size] = 0;
                }
              }

              i += width;
              n += width;
            }
          }
        }
      }

      MarkCompleteWithoutGeometryIfSolid(chunkData, mesh);
    }

    private static void MarkCompleteWithoutGeometryIfSolid(BlockChunkData chunkData, MeshData mesh)
    {
      if (chunkData != null && mesh != null && mesh.IsEmpty && chunkData.HasAnySolidVoxel())
      {
        mesh.MarkCompleteWithoutGeometry();
      }
    }

    private static ushort GetMaterialNeighbourAware(
        BlockChunkData chunkData,
        MaterialLookup materialLookup,
        int localX,
        int localY,
        int localZ)
    {
      const int size = VoxelConstants.ChunkSize;

      if (
          localX >= 0 && localX < size &&
          localY >= 0 && localY < size &&
          localZ >= 0 && localZ < size)
      {
        return chunkData.GetVoxel(localX, localY, localZ).MaterialId;
      }

      Vector3Int worldVoxel = chunkData.LocalToWorldVoxel(localX, localY, localZ);
      return materialLookup(worldVoxel);
    }

    private static ushort GetMaterialNeighbourAware(
        BlockChunkData chunkData,
        WorldGenerationSnapshot snapshot,
        int localX,
        int localY,
        int localZ)
    {
      const int size = VoxelConstants.ChunkSize;

      if (
          localX >= 0 && localX < size &&
          localY >= 0 && localY < size &&
          localZ >= 0 && localZ < size)
      {
        return chunkData.GetVoxel(localX, localY, localZ).MaterialId;
      }

      Vector3Int worldVoxel = chunkData.LocalToWorldVoxel(localX, localY, localZ);
      return BlockChunkBuilder.SampleMaterialAtWorldVoxel(worldVoxel, snapshot);
    }

    private static void AddVoxelTiledGreedyQuad(
        MeshData mesh,
        int axis,
        int u,
        int v,
        int startX,
        int startY,
        int startZ,
        int width,
        int height,
        bool positiveFace,
        ushort materialId,
        float voxelSize)
    {
      Vector3 normal = Vector3.zero;
      normal[axis] = positiveFace ? 1.0f : -1.0f;

      // Emit a SINGLE quad spanning the whole greedy rectangle instead of
      // width*height unit quads. The CubusBiomeAtlasURP shader tiles the atlas
      // tile per voxel via frac(uv) and samples with explicit gradients
      // (SAMPLE_TEXTURE2D_GRAD using the unwrapped uv) so a UV that runs 0..N
      // across the merged face reproduces the exact per-voxel texturing the
      // old per-tile quads produced, with up to width*height fewer vertices.
      Vector3Int start = new(startX, startY, startZ);

      Vector3Int d1 = Vector3Int.zero;
      Vector3Int d2 = Vector3Int.zero;

      d1[u] = width;
      d2[v] = height;

      Vector3 p0 = ToPosition(
          start.x,
          start.y,
          start.z,
          voxelSize
      );

      Vector3 p1 = ToPosition(
          start.x + d1.x,
          start.y + d1.y,
          start.z + d1.z,
          voxelSize
      );

      Vector3 p2 = ToPosition(
          start.x + d1.x + d2.x,
          start.y + d1.y + d2.y,
          start.z + d1.z + d2.z,
          voxelSize
      );

      Vector3 p3 = ToPosition(
          start.x + d2.x,
          start.y + d2.y,
          start.z + d2.z,
          voxelSize
      );

      Vector3 v0;
      Vector3 v1;
      Vector3 v2;
      Vector3 v3;

      // UV runs one unit per voxel so frac() in the shader tiles correctly. The
      // winding (and therefore which face axis maps to uv.x vs uv.y) differs by
      // face sign, so the scale is transposed for negative faces to keep the
      // per-voxel texture orientation identical to the old per-tile output.
      Vector2 uvScale;

      if (positiveFace)
      {
        v0 = p0;
        v1 = p3;
        v2 = p2;
        v3 = p1;
        uvScale = new Vector2(width, height);
      }
      else
      {
        v0 = p0;
        v1 = p1;
        v2 = p2;
        v3 = p3;
        uvScale = new Vector2(height, width);
      }

      AddQuad(
          mesh,
          v0,
          v1,
          v2,
          v3,
          normal,
          materialId,
          uvScale
      );
    }

    private static Vector3 ToPosition(int x, int y, int z, float voxelSize)
    {
      return new Vector3(
          x * voxelSize,
          y * voxelSize,
          z * voxelSize
      );
    }

    private static void AddQuad(
        MeshData mesh,
        Vector3 v0,
        Vector3 v1,
        Vector3 v2,
        Vector3 v3,
        Vector3 normal,
        ushort materialId,
        Vector2 uvScale)
    {
      int startIndex = mesh.Vertices.Count;

      mesh.Vertices.Add(v0);
      mesh.Vertices.Add(v1);
      mesh.Vertices.Add(v2);
      mesh.Vertices.Add(v3);

      mesh.Triangles.Add(startIndex + 0);
      mesh.Triangles.Add(startIndex + 2);
      mesh.Triangles.Add(startIndex + 1);

      mesh.Triangles.Add(startIndex + 0);
      mesh.Triangles.Add(startIndex + 3);
      mesh.Triangles.Add(startIndex + 2);

      mesh.Normals.Add(normal);
      mesh.Normals.Add(normal);
      mesh.Normals.Add(normal);
      mesh.Normals.Add(normal);

      mesh.UVs.Add(new Vector2(0.0f, 0.0f));
      mesh.UVs.Add(new Vector2(0.0f, uvScale.y));
      mesh.UVs.Add(new Vector2(uvScale.x, uvScale.y));
      mesh.UVs.Add(new Vector2(uvScale.x, 0.0f));

      Color32 vertexColor = EncodeMaterialId(materialId);

      mesh.Colors.Add(vertexColor);
      mesh.Colors.Add(vertexColor);
      mesh.Colors.Add(vertexColor);
      mesh.Colors.Add(vertexColor);
    }

    private static Color32 EncodeMaterialId(ushort materialId)
    {
      byte low = (byte)(materialId & 0xFF);
      byte high = (byte)((materialId >> 8) & 0xFF);
      return new Color32(low, high, 0, 255);
    }
  }
}