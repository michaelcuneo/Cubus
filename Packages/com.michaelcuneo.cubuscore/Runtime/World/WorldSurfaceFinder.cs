using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World
{
  public sealed class WorldSurfaceFinder
  {
    private readonly CubusWorld world;
    private delegate bool TrySpawnAtColumn(Vector3 worldPosition, float spawnClearance, out Vector3 spawnWorldLocation);

    public WorldSurfaceFinder(CubusWorld world)
    {
      this.world = world;
    }

    public bool FindInitialDensitySpawnLocation(
        Vector3 desiredWorldLocation,
        float horizontalSearchRadius,
        int searchBelowVoxels,
        int searchAboveVoxels,
        float spawnClearance,
        out Vector3 spawnWorldLocation)
    {
      return SearchAround(
          desiredWorldLocation,
          horizontalSearchRadius,
          spawnClearance,
          (Vector3 pos, float clearance, out Vector3 spawn) =>
              TryDensityColumn(pos, clearance, searchBelowVoxels, searchAboveVoxels, out spawn),
          out spawnWorldLocation);
    }

    private bool TryDensityColumn(
      Vector3 worldPosition,
      float spawnClearance,
      int searchBelowVoxels,
      int searchAboveVoxels,
      out Vector3 spawnWorldLocation)
    {
      spawnWorldLocation = default;

      float voxelSize = world.Settings.VoxelSize;
      Vector3 localPosition = world.transform.InverseTransformPoint(worldPosition);
      Vector3 voxelPos = localPosition / voxelSize;

      int voxelX = Mathf.FloorToInt(voxelPos.x);
      int voxelZ = Mathf.FloorToInt(voxelPos.z);

      // Approximate surface height (in voxel units).
      TerrainSampler sampler = new(
          world.Settings.GetActiveGenerationProfile(),
          world.Settings.GetActiveBiomeId()
      );

      float scale = Mathf.Max(0.001f, world.Settings.DensitySampleScale);
      TerrainSample sample = sampler.Sample(new Vector3((voxelX + 0.5f) * scale, 0.0f, (voxelZ + 0.5f) * scale));
      int estimatedSurfaceY = Mathf.FloorToInt(sample.SurfaceHeight);

      int minY = estimatedSurfaceY - Mathf.Max(0, searchBelowVoxels);
      int maxY = estimatedSurfaceY + Mathf.Max(0, searchAboveVoxels);

      int highestSolidY = int.MinValue;

      for (int y = minY; y <= maxY; y++)
      {
        float density = world.GetDensityAtWorldVoxel(new Vector3Int(voxelX, y, voxelZ));
        if (density > 0.0f)
        {
          highestSolidY = y;
        }
      }

      if (highestSolidY == int.MinValue)
      {
        return false;
      }

      Vector3 localSurface = new(
          voxelX + 0.5f,
          highestSolidY + 1.0f + spawnClearance,
          voxelZ + 0.5f
      );

      spawnWorldLocation = world.transform.TransformPoint(localSurface * voxelSize);
      return true;
    }
    public bool FindInitialBlockSpawnLocation(
        Vector3 desiredWorldLocation,
        float horizontalSearchRadius,
        int searchBelowVoxels,
        int searchAboveVoxels,
        float spawnClearance,
        out Vector3 spawnWorldLocation)
    {
      return SearchAround(
          desiredWorldLocation,
          horizontalSearchRadius,
          spawnClearance,
          TryGeneratedColumn,
          out spawnWorldLocation);
    }

    private bool TryGeneratedColumn(
        Vector3 worldPosition,
        float spawnClearance,
        out Vector3 spawnWorldLocation)
    {
      spawnWorldLocation = default;

      float voxelSize = world.Settings.VoxelSize;

      Vector3 localPosition = world.transform.InverseTransformPoint(worldPosition);
      Vector3 voxelPosition = localPosition / voxelSize;

      int voxelX = Mathf.FloorToInt(voxelPosition.x);
      int voxelZ = Mathf.FloorToInt(voxelPosition.z);

      int chunkX = VoxelMath.FloorDiv(voxelX, VoxelConstants.ChunkSize);
      int chunkZ = VoxelMath.FloorDiv(voxelZ, VoxelConstants.ChunkSize);
      int localX = voxelX - chunkX * VoxelConstants.ChunkSize;
      int localZ = voxelZ - chunkZ * VoxelConstants.ChunkSize;

      world.Settings.GetEffectiveBlockChunkYRange(out int minChunkY, out int maxChunkY);

      int highestSolidY = int.MinValue;

      for (int chunkY = minChunkY; chunkY <= maxChunkY; chunkY++)
      {
        Vector3Int coord = new(chunkX, chunkY, chunkZ);

        if (!world.Data.BlockChunks.TryGetValue(coord, out var chunk) || chunk == null)
        {
          continue;
        }

        for (int localY = 0; localY < VoxelConstants.ChunkSize; localY++)
        {
          if (chunk.GetVoxel(localX, localY, localZ).MaterialId == 0)
          {
            continue;
          }

          int worldY = chunkY * VoxelConstants.ChunkSize + localY;

          if (worldY > highestSolidY)
          {
            highestSolidY = worldY;
          }
        }
      }

      if (highestSolidY == int.MinValue)
      {
        return false;
      }

      Vector3 localSurface = new(
        voxelX + 0.5f,
        highestSolidY + 1.0f + spawnClearance,
        voxelZ + 0.5f
      );

      spawnWorldLocation = world.transform.TransformPoint(localSurface * voxelSize);
      return true;
    }

    private bool SearchAround(
        Vector3 desiredWorldLocation,
        float horizontalSearchRadius,
        float spawnClearance,
        TrySpawnAtColumn tryColumn,
        out Vector3 spawnWorldLocation)
    {
      spawnWorldLocation = default;

      if (world == null)
      {
        return false;
      }

      if (tryColumn(desiredWorldLocation, spawnClearance, out spawnWorldLocation))
      {
        return true;
      }

      float voxelSize = world.Settings.VoxelSize;
      int rings = Mathf.Max(1, Mathf.CeilToInt(horizontalSearchRadius / voxelSize));

      for (int ring = 1; ring <= rings; ring++)
      {
        float radius = ring * voxelSize;
        int samples = Mathf.Max(8, ring * 8);

        for (int i = 0; i < samples; i++)
        {
          float angle = (float)i / samples * Mathf.PI * 2.0f;

          Vector3 candidate = desiredWorldLocation;
          candidate.x += Mathf.Cos(angle) * radius;
          candidate.z += Mathf.Sin(angle) * radius;

          if (tryColumn(candidate, spawnClearance, out spawnWorldLocation))
          {
            return true;
          }
        }
      }

      return false;
    }
  }
}