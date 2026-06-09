using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Editing
{
  public sealed class SmoothDensityEditTool : MonoBehaviour
  {
    [Header("Brush")]
    [SerializeField] private float traceDistance = 128.0f;
    [SerializeField] private float radiusVoxels = 4.0f;
    [SerializeField] private float densityStrength = 2.5f;
    [SerializeField] private ushort addMaterialId = 1;
    [SerializeField] private float editInterval = 0.08f;

    private float timeSinceLastEdit = float.MaxValue;
    private readonly HashSet<Vector3Int> dirtyChunks = new();
    private CubusWorld world;
    private WorldStreamer streamer;

    private void Awake()
    {
      if (world == null)
      {
        world = GetComponent<CubusWorld>();
      }

      if (streamer == null)
      {
        streamer = GetComponent<WorldStreamer>();
      }
    }

    private void Update()
    {
      timeSinceLastEdit += Time.deltaTime;
    }

    public bool TryEditFromRay(
        Ray ray,
        bool addDensity)
    {
      if (timeSinceLastEdit < editInterval)
      {
        return false;
      }

      if (world == null || world.Settings.TerrainSystem != TerrainSystem.SmoothDensity)
      {
        return false;
      }

      RaycastHit hit = default;
      bool hitChunk = false;
      float bestDist = float.PositiveInfinity;
      foreach (RaycastHit candidate in Physics.RaycastAll(ray, traceDistance, ~0, QueryTriggerInteraction.Ignore))
      {
        if (candidate.collider != null &&
            candidate.collider.GetComponentInParent<Rendering.ChunkView>() != null &&
            candidate.distance < bestDist)
        {
          bestDist = candidate.distance;
          hit = candidate;
          hitChunk = true;
        }
      }

      if (!hitChunk)
      {
        Debug.DrawRay(ray.origin, ray.direction * traceDistance, Color.red, 0.5f);
        return false;
      }

      float epsilon = Mathf.Max(0.05f, radiusVoxels * 0.2f);
      Vector3 localHitPoint = transform.InverseTransformPoint(hit.point);
      Vector3 localNormal = transform.InverseTransformDirection(hit.normal).normalized;
      Vector3 voxelPoint = localHitPoint / world.Settings.VoxelSize;

      if (addDensity)
      {
        voxelPoint += localNormal * epsilon;
      }
      else
      {
        voxelPoint -= localNormal * epsilon;
      }

      Vector3 worldVoxelPosition = voxelPoint;

      dirtyChunks.Clear();

      int changedCount = addDensity
          ? world.AddDensityInSphere(
              worldVoxelPosition,
              radiusVoxels,
              densityStrength,
              addMaterialId,
              dirtyChunks
          )
          : world.RemoveDensityInSphere(
              worldVoxelPosition,
              radiusVoxels,
              densityStrength,
              dirtyChunks
          );

      if (changedCount <= 0)
      {
        return false;
      }

      timeSinceLastEdit = 0.0f;

      if (streamer != null)
      {
        streamer.RebuildDensityChunks(dirtyChunks);
      }

      return true;
    }
  }
}