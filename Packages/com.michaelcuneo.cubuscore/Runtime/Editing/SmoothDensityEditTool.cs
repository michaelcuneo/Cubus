using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Editing
{
  /// <summary>
  /// Density (smooth-terrain) sculpting tool with selectable brushes:
  /// <b>Raise</b> (build up), <b>Lower</b> (dig), <b>Smooth</b> (soften bumps) and
  /// <b>Flatten</b> (level toward the brushed height). Works in SmoothDensity and
  /// Hybrid worlds.
  /// </summary>
  public sealed class SmoothDensityEditTool : MonoBehaviour
  {
    public enum DensityBrush
    {
      Raise,
      Lower,
      Smooth,
      Flatten,
    }

    [Header("Brush")]
    [SerializeField] private DensityBrush brush = DensityBrush.Raise;
    [SerializeField] private float traceDistance = 128.0f;
    [SerializeField] private float radiusVoxels = 4.0f;
    [SerializeField] private ushort addMaterialId = 1;
    [SerializeField] private float editInterval = 0.08f;

    [Header("Strength")]
    [Tooltip("Density added/removed per application by the Raise and Lower brushes.")]
    [SerializeField] private float densityStrength = 2.5f;
    [Tooltip("0..1 pull toward the neighbour average per application (Smooth brush).")]
    [SerializeField] private float smoothStrength = 0.5f;
    [Tooltip("0..1 pull toward the target plane per application (Flatten brush).")]
    [SerializeField] private float flattenStrength = 0.5f;

    private float timeSinceLastEdit = float.MaxValue;
    private readonly HashSet<Vector3Int> dirtyChunks = new();
    private CubusWorld world;
    private WorldStreamer streamer;

    /// <summary>The brush this tool applies from <see cref="TryEditFromRay(Ray)"/>.</summary>
    public DensityBrush Brush
    {
      get => brush;
      set => brush = value;
    }

    public float RadiusVoxels
    {
      get => radiusVoxels;
      set => radiusVoxels = Mathf.Max(0.1f, value);
    }

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

    /// <summary>Applies the currently selected <see cref="Brush"/>.</summary>
    public bool TryEditFromRay(Ray ray)
    {
      return ApplyBrush(ray, brush);
    }

    /// <summary>
    /// Back-compatible two-button entry point: <paramref name="addDensity"/> true
    /// raises, false lowers. Prefer <see cref="TryEditFromRay(Ray)"/> with a
    /// selected <see cref="Brush"/>.
    /// </summary>
    public bool TryEditFromRay(Ray ray, bool addDensity)
    {
      return ApplyBrush(ray, addDensity ? DensityBrush.Raise : DensityBrush.Lower);
    }

    /// <summary>
    /// Applies a density brush with explicit per-call parameters, ignoring this
    /// component's serialized brush/strength fields. Used by data-driven tooling
    /// such as <see cref="CubusEditController"/>.
    /// </summary>
    public bool TryApply(Ray ray, DensityBrush activeBrush, float radius, float strength, ushort materialId, List<DensityVoxelEdit> collectedEdits = null)
    {
      return ApplyBrush(ray, activeBrush, Mathf.Max(0.1f, radius), strength, materialId, collectedEdits);
    }

    private float StrengthForBrush(DensityBrush activeBrush)
    {
      return activeBrush switch
      {
        DensityBrush.Smooth => smoothStrength,
        DensityBrush.Flatten => flattenStrength,
        _ => densityStrength,
      };
    }

    private bool ApplyBrush(Ray ray, DensityBrush activeBrush)
    {
      return ApplyBrush(ray, activeBrush, radiusVoxels, StrengthForBrush(activeBrush), addMaterialId);
    }

    private bool ApplyBrush(Ray ray, DensityBrush activeBrush, float radius, float strength, ushort materialId, List<DensityVoxelEdit> collectedEdits = null)
    {
      if (timeSinceLastEdit < editInterval)
      {
        return false;
      }

      if (world == null || world.Settings == null)
      {
        return false;
      }

      // Density sculpting operates on the smooth density layer, which exists in
      // SmoothDensity mode and as the base terrain in Hybrid mode. Pure Block
      // worlds have no density field to carve.
      if (world.Settings.TerrainSystem != TerrainSystem.SmoothDensity &&
          world.Settings.TerrainSystem != TerrainSystem.Hybrid)
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

      float epsilon = Mathf.Max(0.05f, radius * 0.2f);
      Vector3 localHitPoint = transform.InverseTransformPoint(hit.point);
      Vector3 localNormal = transform.InverseTransformDirection(hit.normal).normalized;
      Vector3 voxelPoint = localHitPoint / world.Settings.VoxelSize;

      // Raise builds outward along the surface normal, Lower digs inward; Smooth
      // and Flatten stay centred on the hit surface.
      if (activeBrush == DensityBrush.Raise)
      {
        voxelPoint += localNormal * epsilon;
      }
      else if (activeBrush == DensityBrush.Lower)
      {
        voxelPoint -= localNormal * epsilon;
      }

      dirtyChunks.Clear();

      int changedCount = activeBrush switch
      {
        DensityBrush.Raise => world.AddDensityInSphere(
            voxelPoint, radius, strength, materialId, dirtyChunks, collectedEdits),
        DensityBrush.Lower => world.RemoveDensityInSphere(
            voxelPoint, radius, strength, dirtyChunks, collectedEdits),
        DensityBrush.Smooth => world.SmoothDensityInSphere(
            voxelPoint, radius, strength, dirtyChunks, collectedEdits),
        DensityBrush.Flatten => world.FlattenDensityInSphere(
            voxelPoint, radius, voxelPoint.y, strength, dirtyChunks, collectedEdits),
        _ => 0,
      };

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