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

    public static MeshData GenerateNeighbourAware(BlockChunkData chunkData, MaterialLookup materialLookup, float voxelSize)
    {
      MeshData mesh = new();
      mesh.Reserve(4096, 6144);
      GenerateNeighbourAware(chunkData, materialLookup, voxelSize, mesh);
      return mesh;
    }

    public static MeshData GenerateNeighbourAware(BlockChunkData chunkData, WorldGenerationSnapshot snapshot, float voxelSize)
    {
      MeshData mesh = new();
      mesh.Reserve(4096, 6144);
      GenerateNeighbourAware(chunkData, snapshot, voxelSize, mesh);
      return mesh;
    }

    public static MeshData GenerateNeighbourAware(BlockChunkData chunkData, BlockChunkNeighborhood neighborhood, float voxelSize)
    {
      MeshData mesh = new();
      mesh.Reserve(4096, 6144);
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

    public static void GenerateNeighbourAware(BlockChunkData chunkData, WorldGenerationSnapshot snapshot, float voxelSize, MeshData mesh)
    {
      GenerateNeighbourAware(
        chunkData,
        worldVoxelCoord => BlockChunkBuilder.SampleMaterialAtWorldVoxel(worldVoxelCoord, snapshot),
        voxelSize,
        mesh
      );
    }

    public static void GenerateNeighbourAware(BlockChunkData chunkData, MaterialLookup materialLookup, float voxelSize, MeshData mesh)
    {
      mesh.Reset();

      if (chunkData == null)
      {
        return;
      }

      const int size = VoxelConstants.ChunkSize;
      Span<int> mask = stackalloc int[size * size];
      float safeVoxelSize = Mathf.Max(0.0001f, voxelSize);

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
              ushort materialA = GetMaterialNeighbourAware(chunkData, materialLookup, x[0], x[1], x[2]);
              ushort materialB = GetMaterialNeighbourAware(chunkData, materialLookup, x[0] + q[0], x[1] + q[1], x[2] + q[2]);

              bool solidA = materialA != 0;
              bool solidB = materialB != 0;

              if (solidA == solidB) mask[n++] = 0;
              else if (solidA) mask[n++] = materialA;
              else mask[n++] = -materialB;
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
              while (i + width < size && mask[n + width] == currentMask) width++;

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

                if (done) break;
                height++;
              }

              Span<int> start = stackalloc int[3];
              start[0] = x[0];
              start[1] = x[1];
              start[2] = x[2];
              start[u] = i;
              start[v] = j;

              AddGreedyQuad(
                mesh,
                axis,
                u,
                v,
                start[0],
                start[1],
                start[2],
                width,
                height,
                currentMask > 0,
                (ushort)Math.Abs(currentMask),
                safeVoxelSize
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

    private static ushort GetMaterialNeighbourAware(BlockChunkData chunkData, MaterialLookup materialLookup, int localX, int localY, int localZ)
    {
      const int size = VoxelConstants.ChunkSize;
      if (localX >= 0 && localX < size && localY >= 0 && localY < size && localZ >= 0 && localZ < size)
      {
        return chunkData.GetVoxel(localX, localY, localZ).MaterialId;
      }

      return materialLookup(chunkData.LocalToWorldVoxel(localX, localY, localZ));
    }

    private static void AddGreedyQuad(
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

      Vector3Int start = new(startX, startY, startZ);
      Vector3Int d1 = Vector3Int.zero;
      Vector3Int d2 = Vector3Int.zero;
      d1[u] = width;
      d2[v] = height;

      Vector3 p0 = ToPosition(start.x, start.y, start.z, voxelSize);
      Vector3 p1 = ToPosition(start.x + d1.x, start.y + d1.y, start.z + d1.z, voxelSize);
      Vector3 p2 = ToPosition(start.x + d1.x + d2.x, start.y + d1.y + d2.y, start.z + d1.z + d2.z, voxelSize);
      Vector3 p3 = ToPosition(start.x + d2.x, start.y + d2.y, start.z + d2.z, voxelSize);

      if (positiveFace)
      {
        AddQuad(mesh, p0, p3, p2, p1, normal, materialId, new Vector2(width, height));
      }
      else
      {
        AddQuad(mesh, p0, p1, p2, p3, normal, materialId, new Vector2(width, height));
      }
    }

    private static Vector3 ToPosition(int x, int y, int z, float voxelSize)
    {
      return new Vector3(x * voxelSize, y * voxelSize, z * voxelSize);
    }

    private static void AddQuad(MeshData mesh, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 normal, ushort materialId, Vector2 uvScale)
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