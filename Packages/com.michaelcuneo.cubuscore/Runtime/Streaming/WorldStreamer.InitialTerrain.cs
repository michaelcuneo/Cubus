using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    private Vector3 GetSpawnReferencePosition()
    {
      return viewer != null ? viewer.position : desiredInitialSpawnLocation;
    }

    private Vector3Int WorldToSurfaceChunkCoord(Vector3 worldPos)
    {
      Vector3Int c = WorldToChunkCoord(worldPos);
      c.y = GetSurfaceChunkYForColumn(c.x, c.z, c.y * VoxelConstants.ChunkSize);
      return c;
    }

    private void TryBroadcastInitialTerrainReady()
    {
      if (hasBroadcastInitialTerrainReady)
      {
        return;
      }

      if (!worldRenderer.HasChunkView(spawnTargetChunkCoord))
      {
        QueueSpawnTargetForRender();
        return;
      }

      if (worldRenderer.ActiveChunkViews.Count < initialSpawnRequiredRenderedChunks)
      {
        return;
      }

      Vector3 spawn = CalculateInitialSpawnLocation();
      world.BroadcastInitialTerrainReady(spawn);
      hasBroadcastInitialTerrainReady = true;

      ForceRefreshStreamingSet();
    }

    private Vector3 CalculateInitialSpawnLocation()
    {
      float voxelSize = Mathf.Max(0.0001f, world.Settings.VoxelSize);
      float densityScale = Mathf.Max(0.001f, world.Settings.DensitySampleScale);

      float localVoxelX = spawnTargetChunkCoord.x * VoxelConstants.ChunkSize + VoxelConstants.ChunkSize * 0.5f;
      float localVoxelZ = spawnTargetChunkCoord.z * VoxelConstants.ChunkSize + VoxelConstants.ChunkSize * 0.5f;

      Vector3Int sampleVoxel = new(
        Mathf.FloorToInt(localVoxelX),
        0,
        Mathf.FloorToInt(localVoxelZ)
      );

      TerrainSample sample = BiomeTerrainSampler.Sample(world.Settings, sampleVoxel, densityScale);
      float localVoxelY = sample.SurfaceHeight + Mathf.Max(0.0f, initialSpawnClearance);

      return transform.TransformPoint(new Vector3(
        localVoxelX * voxelSize,
        localVoxelY * voxelSize,
        localVoxelZ * voxelSize
      ));
    }
  }
}