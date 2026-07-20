using System.Collections;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace Assets.Demo.Scripts.Spawn
{
  public sealed class DemoInitialTerrainSpawnGate : MonoBehaviour
  {
    [SerializeField] private CubusWorld world;
    [SerializeField] private WorldStreamer streamer;

    [Header("Spawn")]
    [SerializeField] private Vector3Int desiredSpawnVoxel = Vector3Int.zero;
    [SerializeField] private bool snapSpawnToSurface = true;
    [SerializeField][Min(0.0f)] private float spawnClearance = 2.0f;

    [Header("Readiness")]
    [SerializeField] private bool requireFullViewDistanceBeforeSpawn = true;
    [SerializeField][Min(1)] private int requiredRenderedChunks = 9;
    [SerializeField] private bool requireSpawnCollisionBeforeRelease = true;
    [SerializeField][Min(1.0f)] private float spawnCollisionProbeHeight = 128.0f;
    [SerializeField][Min(1.0f)] private float spawnCollisionProbeDistance = 512.0f;

    public Vector3Int DesiredSpawnVoxel
    {
      get => desiredSpawnVoxel;
      set => desiredSpawnVoxel = value;
    }

    public bool SnapSpawnToSurface
    {
      get => snapSpawnToSurface;
      set => snapSpawnToSurface = value;
    }

    public float SpawnClearance
    {
      get => spawnClearance;
      set => spawnClearance = Mathf.Max(0.0f, value);
    }

    public bool RequireFullViewDistanceBeforeSpawn
    {
      get => requireFullViewDistanceBeforeSpawn;
      set => requireFullViewDistanceBeforeSpawn = value;
    }

    public int RequiredRenderedChunks
    {
      get => requiredRenderedChunks;
      set => requiredRenderedChunks = Mathf.Max(1, value);
    }

    public bool RequireSpawnCollisionBeforeRelease
    {
      get => requireSpawnCollisionBeforeRelease;
      set => requireSpawnCollisionBeforeRelease = value;
    }

    public bool IsWaiting { get; private set; }

    private void Awake()
    {
      ResolveReferences();
    }

    public IEnumerator WaitAndRelease()
    {
      ResolveReferences();

      if (world == null || streamer == null)
      {
        yield break;
      }

      IsWaiting = true;

      streamer.SetStreamingFocusVoxel(desiredSpawnVoxel, requireFullViewDistanceBeforeSpawn);

      while (!world.IsInitialTerrainReady)
      {
        Vector3Int spawnChunk = snapSpawnToSurface
            ? streamer.GetSurfaceChunkCoordFromVoxel(desiredSpawnVoxel)
            : streamer.VoxelToChunkCoord(desiredSpawnVoxel);

        if (!streamer.HasRenderedTerrainForChunk(spawnChunk))
        {
          streamer.EnsureChunkQueuedForRender(spawnChunk);
          yield return null;
          continue;
        }

        if (!streamer.HasTerrainCollisionForChunk(spawnChunk))
        {
          streamer.EnsureChunkQueuedForRender(spawnChunk);
          streamer.EnsureTerrainCollisionForChunk(spawnChunk);
          yield return null;
          continue;
        }

        if (!streamer.IsInitialTerrainCoverageRendered(requiredRenderedChunks, requireFullViewDistanceBeforeSpawn))
        {
          streamer.EnsureDesiredTerrainCoverageQueued(requiredRenderedChunks);
          yield return null;
          continue;
        }

        if (snapSpawnToSurface && requireSpawnCollisionBeforeRelease && !HasSpawnTerrainCollision())
        {
          yield return null;
          continue;
        }

        Vector3 spawnWorld = snapSpawnToSurface
            ? streamer.CalculateSurfaceWorldPositionFromVoxel(desiredSpawnVoxel, spawnClearance)
            : streamer.VoxelToWorldPosition(desiredSpawnVoxel);

        world.BroadcastInitialTerrainReady(spawnWorld);
        streamer.ClearStreamingFocusVoxel();
        break;
      }

      IsWaiting = false;
    }

    private bool HasSpawnTerrainCollision()
    {
      if (streamer == null)
      {
        return false;
      }

      Vector3 probeCenter = streamer.CalculateSurfaceWorldPositionFromVoxel(desiredSpawnVoxel, spawnClearance);
      Vector3 rayOrigin = probeCenter + Vector3.up * Mathf.Max(1.0f, spawnCollisionProbeHeight);
      float rayDistance = Mathf.Max(1.0f, spawnCollisionProbeDistance);

      RaycastHit[] hits = Physics.RaycastAll(
        rayOrigin,
        Vector3.down,
        rayDistance,
        ~0,
        QueryTriggerInteraction.Ignore);

      for (int i = 0; i < hits.Length; i++)
      {
        Collider hitCollider = hits[i].collider;
        if (hitCollider == null)
        {
          continue;
        }

        if (hitCollider.GetComponentInParent<ChunkView>() != null)
        {
          return true;
        }
      }

      return false;
    }

    private void ResolveReferences()
    {
      if (world == null)
      {
        world = FindAnyObjectByType<CubusWorld>();
      }

      if (streamer == null && world != null)
      {
        streamer = world.GetComponent<WorldStreamer>();
      }
    }
  }
}
