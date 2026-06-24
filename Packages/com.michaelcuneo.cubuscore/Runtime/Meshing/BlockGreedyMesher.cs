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

      GenerateNeighbourAware(
          chunkData,
          materialLookup,
          voxelSize,
          mesh
      );

      return mesh;
    }

    public static MeshData GenerateNeighbourAware(
        BlockChunkData chunkData,
        WorldGenerationSnapshot snapshot,
        float voxelSize)
    {
      MeshData mesh = MeshDataPool.Rent(4096, 6144);

      GenerateNeighbourAware(
          chunkData,
          snapshot,
          voxelSize,
          mesh
      );

      return mesh;
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
        WorldGenerationSnapshot snapshot,
        float voxelSize,
        MeshData mesh,
        bool? knownHasAnySolidVoxel = null)
    {
      mesh.Reset();

      const int size = VoxelConstants.ChunkSize;
      Span<int> mask = stackalloc int[size * size];

      for (int axis = 0; axis < 3; axis++)
      {
        int u = (axis + 1) % 3;
        int v = (axis + 2) % 3;

        int q0 = axis == 0 ? 1 : 0;
        int q1 = axis == 1 ? 1 : 0;
        int q2 = axis == 2 ? 1 : 0;

        for (int axisCoord = -1; axisCoord < size;)
        {
          int baseX = axis == 0 ? axisCoord : 0;
          int baseY = axis == 1 ? axisCoord : 0;
          int baseZ = axis == 2 ? axisCoord : 0;

          int n = 0;

          for (int vv = 0; vv < size; vv++)
          {
            for (int uu = 0; uu < size; uu++)
            {
              int lx = baseX;
              int ly = baseY;
              int lz = baseZ;

              if (u == 0) lx = uu;
              else if (u == 1) ly = uu;
              else lz = uu;

              if (v == 0) lx = vv;
              else if (v == 1) ly = vv;
              else lz = vv;

              ushort materialA = GetMaterialNeighbourAware(
                  chunkData,
                  snapshot,
                  lx,
                  ly,
                  lz
              );

              ushort materialB = GetMaterialNeighbourAware(
                  chunkData,
                  snapshot,
                  lx + q0,
                  ly + q1,
                  lz + q2
              );

              bool solidA = materialA != 0;
              bool solidB = materialB != 0;

              if (solidA == solidB)
              {
                mask[n++] = 0;
              }
              else if (solidA)
              {
                mask[n++] = axisCoord >= 0 ? materialA : 0;
              }
              else
              {
                mask[n++] = (axisCoord + 1) < size ? -materialB : 0;
              }
            }
          }

          axisCoord++;

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

              int startX = axis == 0 ? axisCoord : 0;
              int startY = axis == 1 ? axisCoord : 0;
              int startZ = axis == 2 ? axisCoord : 0;

              if (u == 0) startX = i;
              else if (u == 1) startY = i;
              else startZ = i;

              if (v == 0) startX = j;
              else if (v == 1) startY = j;
              else startZ = j;

              bool positiveFace = currentMask > 0;
              ushort materialId = (ushort)Math.Abs(currentMask);

              AddVoxelTiledGreedyQuad(
                  mesh,
                  axis,
                  startX,
                  startY,
                  startZ,
                  width,
                  height,
                  positiveFace,
                  materialId,
                  voxelSize
              );

              for (int y = 0; y < height; y++)
              {
                for (int x = 0; x < width; x++)
                {
                  mask[n + x + y * size] = 0;
                }
              }

              i += width;
              n += width;
            }
          }
        }
      }

      MarkCompleteWithoutGeometryIfSolid(chunkData, mesh, knownHasAnySolidVoxel);
    }

    public static void GenerateNeighbourAware(
        BlockChunkData chunkData,
        MaterialLookup materialLookup,
        float voxelSize,
        MeshData mesh,
        bool? knownHasAnySolidVoxel = null)
    {
      mesh.Reset();

      const int size = VoxelConstants.ChunkSize;
      Span<int> mask = stackalloc int[size * size];

      for (int axis = 0; axis < 3; axis++)
      {
        int u = (axis + 1) % 3;
        int v = (axis + 2) % 3;

        int q0 = axis == 0 ? 1 : 0;
        int q1 = axis == 1 ? 1 : 0;
        int q2 = axis == 2 ? 1 : 0;

        for (int axisCoord = -1; axisCoord < size;)
        {
          int baseX = axis == 0 ? axisCoord : 0;
          int baseY = axis == 1 ? axisCoord : 0;
          int baseZ = axis == 2 ? axisCoord : 0;

          int n = 0;

          for (int vv = 0; vv < size; vv++)
          {
            for (int uu = 0; uu < size; uu++)
            {
              int lx = baseX;
              int ly = baseY;
              int lz = baseZ;

              if (u == 0) lx = uu;
              else if (u == 1) ly = uu;
              else lz = uu;

              if (v == 0) lx = vv;
              else if (v == 1) ly = vv;
              else lz = vv;

              ushort materialA = GetMaterialNeighbourAware(
                  chunkData,
                  materialLookup,
                  lx,
                  ly,
                  lz
              );

              ushort materialB = GetMaterialNeighbourAware(
                  chunkData,
                  materialLookup,
                  lx + q0,
                  ly + q1,
                  lz + q2
              );

              bool solidA = materialA != 0;
              bool solidB = materialB != 0;

              if (solidA == solidB)
              {
                mask[n++] = 0;
              }
              else if (solidA)
              {
                mask[n++] = axisCoord >= 0 ? materialA : 0;
              }
              else
              {
                mask[n++] = (axisCoord + 1) < size ? -materialB : 0;
              }
            }
          }

          axisCoord++;

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

              int startX = axis == 0 ? axisCoord : 0;
              int startY = axis == 1 ? axisCoord : 0;
              int startZ = axis == 2 ? axisCoord : 0;

              if (u == 0) startX = i;
              else if (u == 1) startY = i;
              else startZ = i;

              if (v == 0) startX = j;
              else if (v == 1) startY = j;
              else startZ = j;

              bool positiveFace = currentMask > 0;
              ushort materialId = (ushort)Math.Abs(currentMask);

              AddVoxelTiledGreedyQuad(
                  mesh,
                  axis,
                  startX,
                  startY,
                  startZ,
                  width,
                  height,
                  positiveFace,
                  materialId,
                  voxelSize
              );

              for (int y = 0; y < height; y++)
              {
                for (int x = 0; x < width; x++)
                {
                  mask[n + x + y * size] = 0;
                }
              }

              i += width;
              n += width;
            }
          }
        }
      }

      MarkCompleteWithoutGeometryIfSolid(chunkData, mesh, knownHasAnySolidVoxel);
    }

    private static void MarkCompleteWithoutGeometryIfSolid(
        BlockChunkData chunkData,
        MeshData mesh,
        bool? knownHasAnySolidVoxel = null)
    {
      if (chunkData == null || mesh == null || !mesh.IsEmpty)
      {
        return;
      }

      bool hasAnySolidVoxel = knownHasAnySolidVoxel ?? chunkData.HasAnySolidVoxel();

      if (hasAnySolidVoxel)
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

    /// <summary>
    /// Emits a single greedy quad spanning <paramref name="width"/> x
    /// <paramref name="height"/> voxels on the plane at axis coordinate
    /// <c>start[axis]</c>, using the same per-voxel UV tiling, winding and packed
    /// material colour as the chunk mesher. Exposed so the Distant-Horizon LOD
    /// skirt builder produces geometry that textures identically to the terrain.
    /// </summary>
    public static void AppendGreedyQuad(
        MeshData mesh,
        int axis,
        int startX,
        int startY,
        int startZ,
        int width,
        int height,
        bool positiveFace,
        ushort materialId,
        float voxelSize)
    {
      AddVoxelTiledGreedyQuad(
          mesh,
          axis,
          startX,
          startY,
          startZ,
          width,
          height,
          positiveFace,
          materialId,
          voxelSize
      );
    }

    private static void AddVoxelTiledGreedyQuad(
        MeshData mesh,
        int axis,
        int startX,
        int startY,
        int startZ,
        int width,
        int height,
        bool positiveFace,
        ushort materialId,
        float voxelSize)
    {
      Vector3 normal;
      Vector3 p0;
      Vector3 p1;
      Vector3 p2;
      Vector3 p3;

      if (axis == 0)
      {
        normal = positiveFace ? Vector3.right : Vector3.left;

        p0 = ToPosition(startX, startY, startZ, voxelSize);
        p1 = ToPosition(startX, startY + width, startZ, voxelSize);
        p2 = ToPosition(startX, startY + width, startZ + height, voxelSize);
        p3 = ToPosition(startX, startY, startZ + height, voxelSize);
      }
      else if (axis == 1)
      {
        normal = positiveFace ? Vector3.up : Vector3.down;

        p0 = ToPosition(startX, startY, startZ, voxelSize);
        p1 = ToPosition(startX, startY, startZ + width, voxelSize);
        p2 = ToPosition(startX + height, startY, startZ + width, voxelSize);
        p3 = ToPosition(startX + height, startY, startZ, voxelSize);
      }
      else
      {
        normal = positiveFace ? Vector3.forward : Vector3.back;

        p0 = ToPosition(startX, startY, startZ, voxelSize);
        p1 = ToPosition(startX + width, startY, startZ, voxelSize);
        p2 = ToPosition(startX + width, startY + height, startZ, voxelSize);
        p3 = ToPosition(startX, startY + height, startZ, voxelSize);
      }

      Vector3 v0;
      Vector3 v1;
      Vector3 v2;
      Vector3 v3;

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

      mesh.AddQuad(
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
  }
}