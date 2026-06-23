using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Validation
{
  public sealed class WorldRenderSpatialValidator : MonoBehaviour
  {
    [SerializeField] private CubusWorld sourceWorld;
    [SerializeField] private WorldRenderer sourceRenderer;
    [SerializeField] private bool validateOnStart;
    [SerializeField] private bool rebuildRendererBeforeValidation;
    [SerializeField] private bool logEveryChunk;
    [SerializeField, Min(0.0001f)] private float positionTolerance = 0.001f;
    [SerializeField, Min(1.0f)] private float maxBoundsDriftInChunks = 2.0f;

    public bool LastValidationPassed { get; private set; }
    public string LastValidationMessage { get; private set; } = "Not run.";

    private void Start()
    {
      if (validateOnStart)
      {
        ValidateAndLog();
      }
    }

    [ContextMenu("Validate Rendered Chunk Spatial State")]
    public void ValidateAndLog()
    {
      if (Validate(out string message))
      {
        Debug.Log(message, this);
      }
      else
      {
        Debug.LogError(message, this);
      }
    }

    public bool Validate(out string message)
    {
      ResolveReferences();

      if (sourceWorld == null)
      {
        return Fail("Render spatial validation failed: no CubusWorld was found.", out message);
      }

      if (sourceRenderer == null)
      {
        return Fail("Render spatial validation failed: no WorldRenderer was found.", out message);
      }

      if (rebuildRendererBeforeValidation)
      {
        sourceRenderer.RebuildAll();
      }

      IReadOnlyDictionary<Vector3Int, ChunkView> activeViews = sourceRenderer.ActiveChunkViews;
      if (activeViews == null || activeViews.Count == 0)
      {
        return Fail("Render spatial validation failed: WorldRenderer has no active chunk views.", out message);
      }

      float voxelSize = Mathf.Max(0.0001f, sourceWorld.Settings.VoxelSize);
      float chunkWorldSize = VoxelConstants.ChunkSize * voxelSize;
      float maxBoundsDrift = chunkWorldSize * maxBoundsDriftInChunks;

      int checkedCount = 0;
      int renderedCount = 0;
      int colliderCount = 0;
      int emptyPlaceholderCount = 0;
      Bounds combinedRendererBounds = default;
      bool hasCombinedBounds = false;

      foreach (KeyValuePair<Vector3Int, ChunkView> pair in activeViews)
      {
        Vector3Int chunkCoord = pair.Key;
        ChunkView chunkView = pair.Value;
        checkedCount++;

        if (chunkView == null)
        {
          return Fail($"Render spatial validation failed: active chunk view is null for {chunkCoord}.", out message);
        }

        if (!chunkView.IsActive)
        {
          return Fail($"Render spatial validation failed: chunk {chunkCoord} exists in ActiveChunkViews but ChunkView.IsActive is false.", out message);
        }

        if (chunkView.ChunkCoord != chunkCoord)
        {
          return Fail($"Render spatial validation failed: dictionary coord {chunkCoord} does not match ChunkView coord {chunkView.ChunkCoord}.", out message);
        }

        Vector3 expectedLocalPosition = VoxelMath.ChunkCoordToWorldPosition(chunkCoord, voxelSize);
        Vector3 actualLocalPosition = chunkView.transform.localPosition;
        float localPositionError = Vector3.Distance(expectedLocalPosition, actualLocalPosition);

        if (localPositionError > positionTolerance)
        {
          return Fail(
            $"Render spatial validation failed: chunk {chunkCoord} local position mismatch. " +
            $"Expected={expectedLocalPosition}, Actual={actualLocalPosition}, Error={localPositionError}",
            out message
          );
        }

        MeshFilter meshFilter = chunkView.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = chunkView.GetComponent<MeshRenderer>();
        MeshCollider meshCollider = chunkView.GetComponent<MeshCollider>();
        Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;

        if (meshFilter == null)
        {
          return Fail($"Render spatial validation failed: chunk {chunkCoord} has no MeshFilter.", out message);
        }

        if (meshRenderer == null)
        {
          return Fail($"Render spatial validation failed: chunk {chunkCoord} has no MeshRenderer.", out message);
        }

        if (meshCollider == null)
        {
          return Fail($"Render spatial validation failed: chunk {chunkCoord} has no MeshCollider.", out message);
        }

        if (mesh == null)
        {
          return Fail($"Render spatial validation failed: chunk {chunkCoord} has no shared mesh.", out message);
        }

        // A fully-occluded solid chunk meshes to zero geometry, but the renderer
        // deliberately keeps a placeholder ChunkView (MeshData.completeWithoutGeometry)
        // so the streamer treats the chunk as handled instead of re-queuing it forever.
        // Such a view legitimately has an empty mesh; accept it as a placeholder and
        // skip the geometry-dependent checks below. Any other empty view is a defect.
        bool hasGeometry = mesh.vertexCount > 0 && mesh.subMeshCount > 0 && mesh.GetIndexCount(0) > 0;
        if (!hasGeometry)
        {
          if (!IsOccludedSolidPlaceholder(chunkCoord))
          {
            return Fail(
              $"Render spatial validation failed: chunk {chunkCoord} has an empty mesh but is not a fully-occluded solid chunk (unexpected empty view).",
              out message
            );
          }

          emptyPlaceholderCount++;
          continue;
        }

        if (!meshRenderer.enabled)
        {
          return Fail($"Render spatial validation failed: chunk {chunkCoord} MeshRenderer is disabled despite having mesh data.", out message);
        }

        if (meshRenderer.sharedMaterial == null)
        {
          return Fail($"Render spatial validation failed: chunk {chunkCoord} MeshRenderer has no material.", out message);
        }

        Vector3 expectedWorldOrigin = sourceRenderer.transform.TransformPoint(expectedLocalPosition);
        Vector3 rendererCenter = meshRenderer.bounds.center;
        float boundsCenterDistance = Vector3.Distance(expectedWorldOrigin, rendererCenter);

        if (boundsCenterDistance > maxBoundsDrift)
        {
          return Fail(
            $"Render spatial validation failed: chunk {chunkCoord} renderer bounds are too far from the expected chunk origin. " +
            $"ExpectedWorldOrigin={expectedWorldOrigin}, RendererBoundsCenter={rendererCenter}, Distance={boundsCenterDistance}, MaxAllowed={maxBoundsDrift}",
            out message
          );
        }

        if (meshCollider.enabled && meshCollider.sharedMesh != null)
        {
          colliderCount++;
        }

        renderedCount++;

        if (!hasCombinedBounds)
        {
          combinedRendererBounds = meshRenderer.bounds;
          hasCombinedBounds = true;
        }
        else
        {
          combinedRendererBounds.Encapsulate(meshRenderer.bounds);
        }

        if (logEveryChunk)
        {
          Debug.Log(
            $"Rendered chunk {chunkCoord}: " +
            $"ExpectedLocal={expectedLocalPosition}, " +
            $"ActualLocal={actualLocalPosition}, " +
            $"WorldPosition={chunkView.transform.position}, " +
            $"MeshBounds={mesh.bounds}, " +
            $"RendererBounds={meshRenderer.bounds}, " +
            $"Verts={mesh.vertexCount}, " +
            $"Indices={mesh.GetIndexCount(0)}, " +
            $"RendererEnabled={meshRenderer.enabled}, " +
            $"Material={meshRenderer.sharedMaterial}, " +
            $"ColliderEnabled={meshCollider.enabled}, " +
            $"ColliderMesh={(meshCollider.sharedMesh != null)}",
            chunkView
          );
        }
      }

      message =
        $"Render spatial validation passed. " +
        $"Checked={checkedCount}, Rendered={renderedCount}, EmptyPlaceholders={emptyPlaceholderCount}, Colliders={colliderCount}, " +
        $"CombinedRendererBounds={(hasCombinedBounds ? combinedRendererBounds.ToString() : "None")}";

      LastValidationPassed = true;
      LastValidationMessage = message;
      return true;
    }

    private void ResolveReferences()
    {
      if (sourceWorld == null)
      {
        sourceWorld = GetComponent<CubusWorld>();
      }

      if (sourceRenderer == null)
      {
        sourceRenderer = GetComponent<WorldRenderer>();
      }

      if (sourceWorld == null && sourceRenderer != null)
      {
        sourceWorld = sourceRenderer.GetComponent<CubusWorld>();
      }

      if (sourceRenderer == null && sourceWorld != null)
      {
        sourceRenderer = sourceWorld.GetComponent<WorldRenderer>();
      }
    }

    private bool Fail(string failureMessage, out string message)
    {
      LastValidationPassed = false;
      LastValidationMessage = failureMessage;
      message = failureMessage;
      return false;
    }

    // True when an empty active view is the renderer's intentional placeholder for a
    // fully-occluded solid block chunk (meshed to zero geometry). Density chunks with
    // no surface are marked known-empty and never get a view, so they never apply.
    private bool IsOccludedSolidPlaceholder(Vector3Int chunkCoord)
    {
      if (sourceWorld == null || sourceWorld.Data == null || sourceWorld.Settings == null)
      {
        return false;
      }

      if (sourceWorld.Settings.TerrainSystem != TerrainSystem.Block)
      {
        return false;
      }

      return sourceWorld.Data.BlockChunks.TryGetValue(chunkCoord, out BlockChunkData blockChunk) &&
             blockChunk != null &&
             blockChunk.HasAnySolidVoxel();
    }
  }
}
