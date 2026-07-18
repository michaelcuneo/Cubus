using System.Collections;
using Assets.Demo.Scripts.Spawn;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Editing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace Assets.Demo.Scripts.Player
{
  [DefaultExecutionOrder(-100)]
  public sealed class DemoPlayerInitialSpawn : MonoBehaviour
  {
    [SerializeField] private CubusWorld world;
    [SerializeField] private bool assignAsStreamingViewer = true;

    [Header("Spawn Grounding")]
    [SerializeField] private float terrainColliderWaitTimeoutSeconds = 15.0f;
    [SerializeField] private float terrainColliderRetrySeconds = 0.05f;

    [Header("Components To Disable Until Spawn")]
    [SerializeField] private CharacterController characterController;
    [SerializeField] private MonoBehaviour firstPersonController;

    private Coroutine spawnRoutine;

    private void Awake()
    {
      ResolveReferences();
      SetPlayerEnabled(false);
    }

    private void OnEnable()
    {
      ResolveReferences();

      if (world == null)
      {
        Debug.LogError("DemoPlayerInitialSpawn could not find CubusWorld.");
        return;
      }

      world.OnInitialTerrainReady -= HandleInitialTerrainReady;
      world.OnInitialTerrainReady += HandleInitialTerrainReady;

      if (world.IsInitialTerrainReady)
      {
        HandleInitialTerrainReady(world.SuggestedSpawnLocation);
      }
    }

    private void OnDisable()
    {
      if (world != null)
      {
        world.OnInitialTerrainReady -= HandleInitialTerrainReady;
      }

      if (spawnRoutine != null)
      {
        StopCoroutine(spawnRoutine);
        spawnRoutine = null;
      }
    }

    private void ResolveReferences()
    {
      if (world == null)
      {
        world = FindAnyObjectByType<CubusWorld>();
      }

      if (characterController == null)
      {
        characterController = GetComponent<CharacterController>();
      }

      if (firstPersonController == null)
      {
        firstPersonController = GetComponent<DemoFirstPersonController>();
      }
    }

    private void SetPlayerEnabled(bool enabled)
    {
      if (firstPersonController != null)
      {
        firstPersonController.enabled = enabled;
      }

      if (characterController != null)
      {
        characterController.enabled = enabled;
      }
    }

    private void HandleInitialTerrainReady(Vector3 suggestedSpawnLocation)
    {
      SetPlayerEnabled(false);

      if (spawnRoutine != null)
      {
        StopCoroutine(spawnRoutine);
      }

      Vector3 requestedSpawnLocation = ResolveRequestedSpawnLocation(suggestedSpawnLocation);
      spawnRoutine = StartCoroutine(SpawnWhenTerrainColliderReady(requestedSpawnLocation));
    }

    private Vector3 ResolveRequestedSpawnLocation(Vector3 suggestedSpawnLocation)
    {
      DemoInitialTerrainSpawnGate spawnGate = FindAnyObjectByType<DemoInitialTerrainSpawnGate>();
      WorldStreamer streamer = world != null ? world.GetComponent<WorldStreamer>() : null;

      if (spawnGate == null || streamer == null)
      {
        return suggestedSpawnLocation;
      }

      return spawnGate.SnapSpawnToSurface
          ? streamer.CalculateSurfaceWorldPositionFromVoxel(
              spawnGate.DesiredSpawnVoxel,
              spawnGate.SpawnClearance)
          : streamer.VoxelToWorldPosition(spawnGate.DesiredSpawnVoxel);
    }

    private IEnumerator SpawnWhenTerrainColliderReady(Vector3 requestedSpawnLocation)
    {
      float startTime = Time.realtimeSinceStartup;
      float retryDelay = Mathf.Max(0.01f, terrainColliderRetrySeconds);
      float timeout = Mathf.Max(0.0f, terrainColliderWaitTimeoutSeconds);

      while (true)
      {
        Physics.SyncTransforms();

        if (TryResolveTerrainSpawnPosition(requestedSpawnLocation, out Vector3 spawnPosition))
        {
          ApplySpawnPosition(spawnPosition);
          spawnRoutine = null;
          yield break;
        }

        bool timedOut = timeout > 0.0f &&
                        Time.realtimeSinceStartup - startTime >= timeout;

        if (timedOut)
        {
          Debug.LogWarning(
            $"DemoPlayerInitialSpawn timed out waiting for a terrain collider below {requestedSpawnLocation}. " +
            "Keeping the player controller disabled to avoid falling through the world.");
          spawnRoutine = null;
          yield break;
        }

        yield return new WaitForSecondsRealtime(retryDelay);
      }
    }

    private void ApplySpawnPosition(Vector3 spawnPosition)
    {
      transform.SetPositionAndRotation(
        spawnPosition,
        Quaternion.Euler(0.0f, transform.rotation.eulerAngles.y, 0.0f)
      );

      Physics.SyncTransforms();

      if (assignAsStreamingViewer)
      {
        WorldStreamer streamer = FindAnyObjectByType<WorldStreamer>();

        if (streamer != null)
        {
          streamer.SetViewer(transform);
        }
      }

      BlockEditTool editTool = BlockEditTool.FindWorldEditTool()
          ?? FindAnyObjectByType<BlockEditTool>();

      if (editTool != null)
      {
        editTool.SetProtectedTransform(transform);
      }

      SetPlayerEnabled(true);
      Debug.Log($"Demo player spawned at {transform.position}");
    }

    private bool TryResolveTerrainSpawnPosition(
      Vector3 requestedSpawnLocation,
      out Vector3 spawnPosition)
    {
      spawnPosition = requestedSpawnLocation;

      if (characterController == null)
      {
        return true;
      }

      const float rayUp = 128.0f;
      const float rayDistance = 512.0f;
      Vector3 rayOrigin = requestedSpawnLocation + Vector3.up * rayUp;
      RaycastHit[] hits = Physics.RaycastAll(
        rayOrigin,
        Vector3.down,
        rayDistance,
        ~0,
        QueryTriggerInteraction.Ignore
      );

      float bestDistance = float.PositiveInfinity;
      Vector3 bestPoint = requestedSpawnLocation;

      for (int i = 0; i < hits.Length; i++)
      {
        RaycastHit hit = hits[i];

        if (hit.collider == null ||
            hit.collider.GetComponentInParent<ChunkView>() == null)
        {
          continue;
        }

        if (hit.distance < bestDistance)
        {
          bestDistance = hit.distance;
          bestPoint = hit.point;
        }
      }

      if (float.IsInfinity(bestDistance))
      {
        return false;
      }

      float controllerBottomOffset =
        characterController.center.y - characterController.height * 0.5f;
      float y = bestPoint.y - controllerBottomOffset +
                characterController.skinWidth + 0.05f;

      spawnPosition = new Vector3(
        requestedSpawnLocation.x,
        y,
        requestedSpawnLocation.z
      );
      return true;
    }
  }
}
