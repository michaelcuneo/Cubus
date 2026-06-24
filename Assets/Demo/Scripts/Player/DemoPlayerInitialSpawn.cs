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

    [Header("Components To Disable Until Spawn")]
    [SerializeField] private CharacterController characterController;
    [SerializeField] private MonoBehaviour firstPersonController;

    private void Awake()
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

      SetPlayerEnabled(false);
    }

    private void OnEnable()
    {
      if (world == null)
      {
        world = FindAnyObjectByType<CubusWorld>();
      }

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

      Vector3 spawnPosition = ResolveTerrainSpawnPosition(suggestedSpawnLocation);

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

    private Vector3 ResolveTerrainSpawnPosition(Vector3 suggestedSpawnLocation)
    {
      if (characterController == null)
      {
        return suggestedSpawnLocation;
      }

      const float rayUp = 128.0f;
      const float rayDistance = 512.0f;
      Vector3 rayOrigin = suggestedSpawnLocation + Vector3.up * rayUp;
      RaycastHit[] hits = Physics.RaycastAll(
          rayOrigin,
          Vector3.down,
          rayDistance,
          ~0,
          QueryTriggerInteraction.Ignore
      );

      float bestDistance = float.PositiveInfinity;
      Vector3 bestPoint = suggestedSpawnLocation;

      for (int i = 0; i < hits.Length; i++)
      {
        RaycastHit hit = hits[i];

        if (hit.collider == null)
        {
          continue;
        }

        if (hit.collider.GetComponentInParent<ChunkView>() == null)
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
        return suggestedSpawnLocation;
      }

      float halfHeight = characterController.height * 0.5f;
      float y = bestPoint.y + halfHeight + characterController.skinWidth + 0.05f;
      return new Vector3(suggestedSpawnLocation.x, y, suggestedSpawnLocation.z);
    }
  }
}