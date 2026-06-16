using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Validation
{
  public sealed class WorldSpawnCameraValidator : MonoBehaviour
  {
    [SerializeField] private CubusWorld sourceWorld;
    [SerializeField] private WorldRenderer sourceRenderer;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Camera cameraToValidate;
    [SerializeField] private bool validateOnStart;
    [SerializeField] private bool logEveryChunkBounds;
    [SerializeField, Min(0.0f)] private float groundRaycastHeight = 512.0f;
    [SerializeField, Min(0.0f)] private float maxExpectedCameraDistanceFromTerrain = 512.0f;
    [SerializeField] private LayerMask groundRaycastMask = ~0;

    public bool LastValidationPassed { get; private set; }
    public string LastValidationMessage { get; private set; } = "Not run.";

    private void Start()
    {
      if (validateOnStart)
      {
        ValidateAndLog();
      }
    }

    [ContextMenu("Validate Spawn And Camera State")]
    public void ValidateAndLog()
    {
      if (Validate(out string message))
      {
        Debug.Log(message, this);
      }
      else
      {
        Debug.LogWarning(message, this);
      }
    }

    public bool Validate(out string message)
    {
      ResolveReferences();

      if (sourceWorld == null)
      {
        return Fail("Spawn/camera validation failed: no CubusWorld was found.", out message);
      }

      if (sourceRenderer == null)
      {
        return Fail("Spawn/camera validation failed: no WorldRenderer was found.", out message);
      }

      if (!TryGetCombinedRendererBounds(out Bounds terrainBounds, out int renderedChunkCount))
      {
        return Fail("Spawn/camera validation failed: no rendered chunk bounds were available.", out message);
      }

      Transform cameraTransform = cameraToValidate != null ? cameraToValidate.transform : null;
      Vector3 suggestedSpawn = sourceWorld.SuggestedSpawnLocation;
      Vector3 playerPosition = playerTransform != null ? playerTransform.position : Vector3.positiveInfinity;
      Vector3 cameraPosition = cameraTransform != null ? cameraTransform.position : Vector3.positiveInfinity;

      bool hasPlayer = playerTransform != null;
      bool hasCamera = cameraToValidate != null;
      bool spawnInsideTerrainXZ = ContainsXZ(terrainBounds, suggestedSpawn);
      bool playerInsideTerrainXZ = hasPlayer && ContainsXZ(terrainBounds, playerPosition);
      bool cameraInsideTerrainXZ = hasCamera && ContainsXZ(terrainBounds, cameraPosition);
      bool cameraInsideTerrainBounds = hasCamera && terrainBounds.Contains(cameraPosition);
      bool playerInsideTerrainBounds = hasPlayer && terrainBounds.Contains(playerPosition);
      float playerDistanceToTerrainBounds = hasPlayer ? DistanceToBounds(terrainBounds, playerPosition) : float.PositiveInfinity;
      float cameraDistanceToTerrainBounds = hasCamera ? DistanceToBounds(terrainBounds, cameraPosition) : float.PositiveInfinity;

      bool hasGroundHit = TryRaycastGround(
        hasPlayer ? playerPosition : suggestedSpawn,
        out RaycastHit groundHit
      );

      float playerHeightAboveGround = hasGroundHit && hasPlayer
        ? playerPosition.y - groundHit.point.y
        : float.NaN;

      float cameraHeightAboveGround = hasGroundHit && hasCamera
        ? cameraPosition.y - groundHit.point.y
        : float.NaN;

      string diagnostics =
        $"Spawn/camera validation report. " +
        $"WorldReady={sourceWorld.IsWorldReady}, " +
        $"InitialTerrainReady={sourceWorld.IsInitialTerrainReady}, " +
        $"RenderedChunks={renderedChunkCount}, " +
        $"TerrainBounds={terrainBounds}, " +
        $"SuggestedSpawn={suggestedSpawn}, " +
        $"SpawnInsideTerrainXZ={spawnInsideTerrainXZ}, " +
        $"Player={(hasPlayer ? playerTransform.name : "None")}, " +
        $"PlayerPosition={(hasPlayer ? playerPosition.ToString() : "None")}, " +
        $"PlayerInsideTerrainXZ={playerInsideTerrainXZ}, " +
        $"PlayerInsideTerrainBounds={playerInsideTerrainBounds}, " +
        $"PlayerDistanceToTerrainBounds={playerDistanceToTerrainBounds}, " +
        $"Camera={(hasCamera ? cameraToValidate.name : "None")}, " +
        $"CameraPosition={(hasCamera ? cameraPosition.ToString() : "None")}, " +
        $"CameraForward={(hasCamera ? cameraTransform.forward.ToString() : "None")}, " +
        $"CameraNearClip={(hasCamera ? cameraToValidate.nearClipPlane.ToString() : "None")}, " +
        $"CameraFarClip={(hasCamera ? cameraToValidate.farClipPlane.ToString() : "None")}, " +
        $"CameraCullingMask={(hasCamera ? cameraToValidate.cullingMask.ToString() : "None")}, " +
        $"CameraInsideTerrainXZ={cameraInsideTerrainXZ}, " +
        $"CameraInsideTerrainBounds={cameraInsideTerrainBounds}, " +
        $"CameraDistanceToTerrainBounds={cameraDistanceToTerrainBounds}, " +
        $"GroundRayHit={hasGroundHit}, " +
        $"GroundHitPoint={(hasGroundHit ? groundHit.point.ToString() : "None")}, " +
        $"GroundHitObject={(hasGroundHit && groundHit.collider != null ? groundHit.collider.name : "None")}, " +
        $"PlayerHeightAboveGround={(float.IsNaN(playerHeightAboveGround) ? "None" : playerHeightAboveGround.ToString())}, " +
        $"CameraHeightAboveGround={(float.IsNaN(cameraHeightAboveGround) ? "None" : cameraHeightAboveGround.ToString())}";

      bool suspicious = false;
      string reason = null;

      if (!sourceWorld.IsInitialTerrainReady)
      {
        suspicious = true;
        reason = "CubusWorld.IsInitialTerrainReady is false, so the spawn broadcast may not have happened yet.";
      }
      else if (!spawnInsideTerrainXZ)
      {
        suspicious = true;
        reason = "SuggestedSpawnLocation is outside the rendered terrain X/Z bounds.";
      }
      else if (hasPlayer && !playerInsideTerrainXZ)
      {
        suspicious = true;
        reason = "Player is outside the rendered terrain X/Z bounds.";
      }
      else if (hasCamera && !cameraInsideTerrainXZ)
      {
        suspicious = true;
        reason = "Camera is outside the rendered terrain X/Z bounds.";
      }
      else if (hasCamera && cameraDistanceToTerrainBounds > maxExpectedCameraDistanceFromTerrain)
      {
        suspicious = true;
        reason = "Camera is farther from rendered terrain bounds than expected.";
      }
      else if (hasCamera && cameraInsideTerrainBounds)
      {
        suspicious = true;
        reason = "Camera is inside the combined rendered terrain bounds. It may be clipped into terrain.";
      }
      else if (hasPlayer && playerInsideTerrainBounds)
      {
        suspicious = true;
        reason = "Player origin is inside the combined rendered terrain bounds. Check controller height and spawn clearance.";
      }
      else if (!hasGroundHit)
      {
        suspicious = true;
        reason = "Ground raycast did not hit rendered/collidable terrain under the player/spawn position.";
      }

      if (suspicious)
      {
        return Fail($"Spawn/camera validation suspicious: {reason} {diagnostics}", out message);
      }

      LastValidationPassed = true;
      LastValidationMessage = diagnostics;
      message = diagnostics;
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

      if (cameraToValidate == null)
      {
        cameraToValidate = Camera.main;
      }

      if (playerTransform == null && cameraToValidate != null)
      {
        playerTransform = cameraToValidate.transform.root;
      }
    }

    private bool TryGetCombinedRendererBounds(out Bounds combinedBounds, out int renderedChunkCount)
    {
      combinedBounds = default;
      renderedChunkCount = 0;

      IReadOnlyDictionary<Vector3Int, ChunkView> activeViews = sourceRenderer.ActiveChunkViews;
      if (activeViews == null || activeViews.Count == 0)
      {
        return false;
      }

      bool hasBounds = false;
      foreach (KeyValuePair<Vector3Int, ChunkView> pair in activeViews)
      {
        ChunkView chunkView = pair.Value;
        if (chunkView == null)
        {
          continue;
        }

        MeshRenderer meshRenderer = chunkView.GetComponent<MeshRenderer>();
        if (meshRenderer == null || !meshRenderer.enabled)
        {
          continue;
        }

        if (!hasBounds)
        {
          combinedBounds = meshRenderer.bounds;
          hasBounds = true;
        }
        else
        {
          combinedBounds.Encapsulate(meshRenderer.bounds);
        }

        renderedChunkCount++;

        if (logEveryChunkBounds)
        {
          Debug.Log($"Chunk {pair.Key} renderer bounds: {meshRenderer.bounds}", chunkView);
        }
      }

      return hasBounds;
    }

    private bool TryRaycastGround(Vector3 referencePosition, out RaycastHit hit)
    {
      Vector3 origin = new(referencePosition.x, referencePosition.y + groundRaycastHeight, referencePosition.z);
      float distance = groundRaycastHeight * 2.0f;
      return Physics.Raycast(origin, Vector3.down, out hit, distance, groundRaycastMask, QueryTriggerInteraction.Ignore);
    }

    private static bool ContainsXZ(Bounds bounds, Vector3 point)
    {
      return point.x >= bounds.min.x &&
             point.x <= bounds.max.x &&
             point.z >= bounds.min.z &&
             point.z <= bounds.max.z;
    }

    private static float DistanceToBounds(Bounds bounds, Vector3 point)
    {
      Vector3 closest = bounds.ClosestPoint(point);
      return Vector3.Distance(point, closest);
    }

    private bool Fail(string failureMessage, out string message)
    {
      LastValidationPassed = false;
      LastValidationMessage = failureMessage;
      message = failureMessage;
      return false;
    }
  }
}
