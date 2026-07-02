using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Editing
{
  public sealed class BlockEditTool : MonoBehaviour
  {
    [Header("Edit")]
    [SerializeField] private BlockEditShape editShape = BlockEditShape.SingleVoxel;
    [SerializeField] private ushort addMaterialId = 1;
    [SerializeField] private float sphereRadiusVoxels = 3.0f;
    [SerializeField] private Vector3Int boxSize = new(4, 4, 4);

    [Header("Safety")]
    [SerializeField] private Transform protectedTransform;
    [SerializeField] private CharacterController protectedCharacterController;
    [SerializeField] private bool preventAddingInsideProtectedBounds = true;
    [SerializeField] private float protectedBoundsPadding = 0.05f;

    private readonly HashSet<Vector3Int> dirtyChunks = new();

    public event System.Action<IReadOnlyCollection<Vector3Int>> BlockChunksEdited;

    private CubusWorld world;

    public BlockEditShape EditShape
    {
      get => editShape;
      set => editShape = value;
    }

    public ushort AddMaterialId
    {
      get => addMaterialId;
      set => addMaterialId = value;
    }

    public float SphereRadiusVoxels
    {
      get => sphereRadiusVoxels;
      set => sphereRadiusVoxels = Mathf.Max(0.0f, value);
    }

    public Vector3Int BoxSize
    {
      get => boxSize;
      set => boxSize = new Vector3Int(
          Mathf.Max(1, value.x),
          Mathf.Max(1, value.y),
          Mathf.Max(1, value.z)
      );
    }

    private void Awake()
    {
      world = GetComponent<CubusWorld>();
    }

    /// <summary>
    /// Whether this tool is attached to a <see cref="CubusWorld"/> it can edit.
    /// Resolves lazily so it is correct regardless of Awake ordering.
    /// </summary>
    public bool HasWorld
    {
      get
      {
        if (world == null)
        {
          world = GetComponent<CubusWorld>();
        }
        return world != null;
      }
    }

    /// <summary>
    /// Finds the active <see cref="BlockEditTool"/> that owns a <see cref="CubusWorld"/>.
    /// Networked components use this to avoid binding to a world-less tool that a
    /// RequireComponent added on a different GameObject.
    /// </summary>
    public static BlockEditTool FindWorldEditTool()
    {
      foreach (BlockEditTool tool in FindObjectsByType<BlockEditTool>(FindObjectsSortMode.None))
      {
        if (tool.HasWorld)
        {
          return tool;
        }
      }
      return null;
    }

    public bool TryEditFromRay(
        Ray ray,
        float traceDistance,
        BlockEditOperation operation)
    {
      if (!TryResolveEditVoxel(ray, traceDistance, operation, out Vector3Int editVoxel))
      {
        return false;
      }

      DrawDebugVoxel(
          editVoxel,
          operation == BlockEditOperation.Add ? Color.green : Color.red
      );

      return ApplyEdit(editVoxel, operation).Changed;
    }

    /// <summary>
    /// Resolves the voxel a ray edit would target without mutating the world.
    /// Networked editing uses this to forward the intended edit to the server
    /// instead of applying it locally.
    /// </summary>
    public bool TryResolveEditVoxel(
        Ray ray,
        float traceDistance,
        BlockEditOperation operation,
        out Vector3Int editVoxel)
    {
      editVoxel = default;

      if (world == null || world.Settings == null)
      {
        return false;
      }

      // Block editing operates on the block layer. That layer exists in Block
      // mode and as the sparse buildable layer in Hybrid mode; pure SmoothDensity
      // has no block layer to edit.
      if (world.Settings.TerrainSystem != TerrainSystem.Block &&
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

      editVoxel = GetEditVoxelFromHit(hit, operation);

      Debug.DrawRay(hit.point, hit.normal * 0.75f, Color.yellow, 1.0f);
      Debug.DrawLine(ray.origin, hit.point, Color.cyan, 1.0f);

      return true;
    }

    /// <summary>
    /// Applies a single authoritative voxel edit received from the network and
    /// routes it through the same dirty-chunk remesh + persistence path used by
    /// local edits. <paramref name="materialId"/> 0 removes the voxel.
    /// </summary>
    public void ApplyNetworkVoxelEdit(Vector3Int worldVoxel, ushort materialId)
    {
      if (world == null)
      {
        return;
      }

      if (!world.SetBlockMaterialAtWorldVoxel(worldVoxel, materialId))
      {
        return;
      }

      dirtyChunks.Clear();
      world.AddDirtyChunkAndNeighbours(worldVoxel, dirtyChunks);
      BlockChunksEdited?.Invoke(dirtyChunks);
    }

    private Vector3Int GetEditVoxelFromHit(
    RaycastHit hit,
    BlockEditOperation operation)
    {
      const float epsilon = 0.01f;

      Vector3 localHitPoint = transform.InverseTransformPoint(hit.point);
      Vector3 localNormal = transform.InverseTransformDirection(hit.normal).normalized;

      Vector3 voxelPoint = localHitPoint / world.Settings.VoxelSize;

      if (operation == BlockEditOperation.Add)
      {
        voxelPoint += localNormal * epsilon;
      }
      else
      {
        voxelPoint -= localNormal * epsilon;
      }

      return new Vector3Int(
          Mathf.FloorToInt(voxelPoint.x),
          Mathf.FloorToInt(voxelPoint.y),
          Mathf.FloorToInt(voxelPoint.z)
      );
    }

    private void DrawDebugVoxel(Vector3Int voxel, Color color)
    {
      Vector3 localCenter =
          new Vector3(voxel.x + 0.5f, voxel.y + 0.5f, voxel.z + 0.5f) *
          world.Settings.VoxelSize;

      Vector3 worldCenter = transform.TransformPoint(localCenter);
      float size = world.Settings.VoxelSize;

      Debug.DrawLine(worldCenter + new Vector3(-size, 0, 0) * 0.5f, worldCenter + new Vector3(size, 0, 0) * 0.5f, color, 1.0f);
      Debug.DrawLine(worldCenter + new Vector3(0, -size, 0) * 0.5f, worldCenter + new Vector3(0, size, 0) * 0.5f, color, 1.0f);
      Debug.DrawLine(worldCenter + new Vector3(0, 0, -size) * 0.5f, worldCenter + new Vector3(0, 0, size) * 0.5f, color, 1.0f);
    }

    public BlockEditResult ApplyEdit(
        Vector3 worldVoxelPosition,
        BlockEditOperation operation)
    {
      dirtyChunks.Clear();

      // Guard removes against the protected bounds here too (the per-shape paths
      // only gate Add). This stops a local remove from deleting the floor.
      if (operation == BlockEditOperation.Remove)
      {
        Vector3Int removeCenter = new(
            Mathf.FloorToInt(worldVoxelPosition.x),
            Mathf.FloorToInt(worldVoxelPosition.y),
            Mathf.FloorToInt(worldVoxelPosition.z)
        );

        if (!CanEditVoxelAt(removeCenter, operation))
        {
          return new BlockEditResult(0, new HashSet<Vector3Int>());
        }
      }

      int changedCount = 0;

      switch (editShape)
      {
        case BlockEditShape.SingleVoxel:
          changedCount = ApplySingleVoxelEdit(worldVoxelPosition, operation);
          break;

        case BlockEditShape.Sphere:
          changedCount = ApplySphereEdit(worldVoxelPosition, operation);
          break;

        case BlockEditShape.Box:
          changedCount = ApplyBoxEdit(worldVoxelPosition, operation);
          break;
      }

      if (changedCount > 0)
      {
        BlockChunksEdited?.Invoke(dirtyChunks);
      }

      return new BlockEditResult(
          changedCount,
          new HashSet<Vector3Int>(dirtyChunks)
      );
    }

    private int ApplySingleVoxelEdit(
        Vector3 worldVoxelPosition,
        BlockEditOperation operation)
    {
      Vector3Int voxel = new(
          Mathf.FloorToInt(worldVoxelPosition.x),
          Mathf.FloorToInt(worldVoxelPosition.y),
          Mathf.FloorToInt(worldVoxelPosition.z)
      );

      if (operation == BlockEditOperation.Add && !CanAddVoxelAt(voxel))
      {
        return 0;
      }

      bool changed = operation == BlockEditOperation.Add
          ? world.AddBlock(voxel, addMaterialId)
          : world.RemoveBlock(voxel);

      if (!changed)
      {
        return 0;
      }

      world.AddDirtyChunkAndNeighbours(voxel, dirtyChunks);
      return 1;
    }

    private int ApplySphereEdit(
        Vector3 worldVoxelPosition,
        BlockEditOperation operation)
    {
      if (operation == BlockEditOperation.Add)
      {
        Vector3Int centerVoxel = new(
            Mathf.FloorToInt(worldVoxelPosition.x),
            Mathf.FloorToInt(worldVoxelPosition.y),
            Mathf.FloorToInt(worldVoxelPosition.z)
        );

        if (!CanAddVoxelAt(centerVoxel))
        {
          return 0;
        }

        return world.AddBlocksInSphere(
            worldVoxelPosition,
            sphereRadiusVoxels,
            addMaterialId,
            dirtyChunks
        );
      }

      return world.RemoveBlocksInSphere(
          worldVoxelPosition,
          sphereRadiusVoxels,
          dirtyChunks
      );
    }
    private int ApplyBoxEdit(
        Vector3 worldVoxelPosition,
        BlockEditOperation operation)
    {
      Vector3Int center = new(
          Mathf.FloorToInt(worldVoxelPosition.x),
          Mathf.FloorToInt(worldVoxelPosition.y),
          Mathf.FloorToInt(worldVoxelPosition.z)
      );

      if (operation == BlockEditOperation.Add && !CanAddVoxelAt(center))
      {
        return 0;
      }

      Vector3Int min = center - new Vector3Int(
          boxSize.x / 2,
          boxSize.y / 2,
          boxSize.z / 2
      );

      BoundsInt bounds = new(min, boxSize);

      if (operation == BlockEditOperation.Add)
      {
        return world.AddBlocksInBox(bounds, addMaterialId, dirtyChunks);
      }

      return world.RemoveBlocksInBox(bounds, dirtyChunks);
    }

    public void SetProtectedTransform(Transform target)
    {
      protectedTransform = target;

      if (protectedTransform != null)
      {
        protectedCharacterController = protectedTransform.GetComponent<CharacterController>();
      }
      else
      {
        protectedCharacterController = null;
      }
    }

    private bool CanAddVoxelAt(Vector3Int worldVoxel) =>
        CanEditVoxelAt(worldVoxel, BlockEditOperation.Add);

    /// <summary>
    /// Whether an edit may target <paramref name="worldVoxel"/> without violating
    /// the protected bounds. <see cref="BlockEditOperation.Add"/> is rejected when
    /// the voxel overlaps the protected capsule (so you can't entomb yourself).
    /// <see cref="BlockEditOperation.Remove"/> additionally protects the voxel
    /// directly beneath the capsule so you can't carve the floor out from under
    /// the player. Always allowed when protection is disabled or no protected
    /// target is set. Networked editing calls this before forwarding an intent to
    /// the server, because the authoritative apply path bypasses local guards.
    /// </summary>
    public bool CanEditVoxelAt(Vector3Int worldVoxel, BlockEditOperation operation)
    {
      if (!preventAddingInsideProtectedBounds)
      {
        return true;
      }

      if (world == null || world.Settings == null)
      {
        return true;
      }

      if (!TryGetProtectedBounds(out Bounds protectedBounds))
      {
        return true;
      }

      float voxelSize = world.Settings.VoxelSize;
      protectedBounds.Expand(protectedBoundsPadding);

      if (operation == BlockEditOperation.Remove)
      {
        // Extend the protected volume one voxel downward so the supporting floor
        // voxel is covered and can't be removed beneath the player's feet.
        protectedBounds.Encapsulate(new Vector3(
            protectedBounds.center.x,
            protectedBounds.min.y - voxelSize,
            protectedBounds.center.z
        ));
      }

      Vector3 voxelWorldCenter = transform.TransformPoint(
          new Vector3(
              worldVoxel.x + 0.5f,
              worldVoxel.y + 0.5f,
              worldVoxel.z + 0.5f
          ) * voxelSize
      );

      Bounds voxelBounds = new(voxelWorldCenter, Vector3.one * voxelSize);

      return !protectedBounds.Intersects(voxelBounds);
    }

    private bool TryGetProtectedBounds(out Bounds protectedBounds)
    {
      if (protectedCharacterController != null)
      {
        protectedBounds = protectedCharacterController.bounds;
        return true;
      }

      if (protectedTransform != null)
      {
        protectedBounds = new Bounds(
            protectedTransform.position,
            Vector3.one * world.Settings.VoxelSize
        );
        return true;
      }

      protectedBounds = default;
      return false;
    }
  }
}