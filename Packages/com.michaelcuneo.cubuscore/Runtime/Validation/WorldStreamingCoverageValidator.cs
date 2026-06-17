using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Validation
{
  public sealed class WorldStreamingCoverageValidator : MonoBehaviour
  {
    [SerializeField] private CubusWorld sourceWorld;
    [SerializeField] private WorldRenderer sourceRenderer;
    [SerializeField] private WorldStreamer sourceStreamer;
    [SerializeField] private bool validateOnStart;
    [SerializeField] private bool logMissingChunks;
    [SerializeField] private bool logRenderedChunksOutsideKeepSet;
    [SerializeField, Min(0)] private int toleratedUnqueuedMissingDesiredRenderableChunks = 0;
    [SerializeField, Min(0)] private int toleratedRenderedChunksOutsideKeepSet = 0;

    public bool LastValidationPassed { get; private set; }
    public string LastValidationMessage { get; private set; } = "Not run.";

    private void Start()
    {
      if (validateOnStart)
      {
        ValidateAndLog();
      }
    }

    [ContextMenu("Validate Streaming Coverage")]
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
        return Fail("Streaming coverage validation failed: no CubusWorld was found.", out message);
      }

      if (sourceRenderer == null)
      {
        return Fail("Streaming coverage validation failed: no WorldRenderer was found.", out message);
      }

      if (sourceStreamer == null)
      {
        return Fail("Streaming coverage validation failed: no WorldStreamer was found. This validator needs the streamer diagnostic sets.", out message);
      }

      if (sourceWorld.Settings == null)
      {
        return Fail("Streaming coverage validation failed: CubusWorld has no WorldSettings.", out message);
      }

      WorldSettings settings = sourceWorld.Settings;
      HashSet<Vector3Int> desiredSet = new(sourceStreamer.DesiredChunkCoords);
      HashSet<Vector3Int> keepSet = new(sourceStreamer.KeepChunkCoords);
      HashSet<Vector3Int> pendingLoadSet = new(sourceStreamer.PendingLoadCoords);
      HashSet<Vector3Int> pendingRenderSet = new(sourceStreamer.PendingRenderCoords);
      HashSet<Vector3Int> knownEmptySet = new(sourceStreamer.KnownEmptyChunks);

      int desiredChunks = desiredSet.Count;
      int keepChunks = keepSet.Count;
      int desiredChunksWithData = 0;
      int desiredKnownEmptyChunks = 0;
      int desiredRenderableChunks = 0;
      int renderedDesiredChunks = 0;
      int missingDesiredDataChunks = 0;
      int missingDesiredDataPendingLoadChunks = 0;
      int unqueuedMissingDesiredDataChunks = 0;
      int missingDesiredRenderableChunks = 0;
      int missingDesiredRenderablePendingRenderChunks = 0;
      int unqueuedMissingDesiredRenderableChunks = 0;
      int activeRenderedChunks = sourceRenderer.ActiveChunkViews.Count;
      int renderedOutsideDesiredSet = 0;
      int renderedOutsideKeepSet = 0;
      int renderedKnownEmptyChunks = 0;

      foreach (Vector3Int chunkCoord in desiredSet)
      {
        bool isKnownEmpty = knownEmptySet.Contains(chunkCoord);
        bool isPendingLoad = pendingLoadSet.Contains(chunkCoord);
        bool isPendingRender = pendingRenderSet.Contains(chunkCoord);
        bool hasChunkData = HasChunkData(settings.TerrainSystem, chunkCoord);
        bool hasRenderedView = sourceRenderer.HasChunkView(chunkCoord);
        bool shouldRender = HasRenderableSurface(settings.TerrainSystem, chunkCoord, hasChunkData);

        if (hasChunkData)
        {
          desiredChunksWithData++;
        }
        else if (isKnownEmpty)
        {
          desiredKnownEmptyChunks++;
        }
        else
        {
          missingDesiredDataChunks++;

          if (isPendingLoad)
          {
            missingDesiredDataPendingLoadChunks++;
          }
          else
          {
            unqueuedMissingDesiredDataChunks++;
            if (logMissingChunks)
            {
              Debug.LogWarning($"Desired streaming chunk has no data, is not known-empty, and is not pending load. Chunk={chunkCoord}", this);
            }
          }
        }

        if (shouldRender)
        {
          desiredRenderableChunks++;
          if (!hasRenderedView)
          {
            missingDesiredRenderableChunks++;

            if (isPendingRender)
            {
              missingDesiredRenderablePendingRenderChunks++;
            }
            else
            {
              unqueuedMissingDesiredRenderableChunks++;
              if (logMissingChunks)
              {
                Debug.LogWarning($"Desired renderable chunk is missing a rendered ChunkView and is not pending render. Chunk={chunkCoord}", this);
              }
            }
          }
        }

        if (hasRenderedView)
        {
          renderedDesiredChunks++;
        }
      }

      foreach (KeyValuePair<Vector3Int, ChunkView> pair in sourceRenderer.ActiveChunkViews)
      {
        if (!desiredSet.Contains(pair.Key))
        {
          renderedOutsideDesiredSet++;
        }

        if (!keepSet.Contains(pair.Key))
        {
          renderedOutsideKeepSet++;
          if (logRenderedChunksOutsideKeepSet)
          {
            Debug.LogWarning($"Rendered chunk is outside the streamer's keep set. Chunk={pair.Key}", pair.Value);
          }
        }

        if (knownEmptySet.Contains(pair.Key))
        {
          renderedKnownEmptyChunks++;
          if (logRenderedChunksOutsideKeepSet)
          {
            Debug.LogWarning($"Rendered chunk is marked known-empty by streamer. Chunk={pair.Key}", pair.Value);
          }
        }
      }

      string diagnostics =
        $"Streaming coverage report. " +
        $"HasLastViewerChunk={sourceStreamer.HasLastViewerChunkCoord}, " +
        $"LastViewerChunk={sourceStreamer.LastViewerChunkCoord}, " +
        $"SpawnTargetChunk={sourceStreamer.SpawnTargetChunkCoord}, " +
        $"InitialTerrainReady={sourceStreamer.HasBroadcastInitialTerrainReady}, " +
        $"DesiredChunks={desiredChunks}, " +
        $"KeepChunks={keepChunks}, " +
        $"DesiredChunksWithData={desiredChunksWithData}, " +
        $"DesiredKnownEmptyChunks={desiredKnownEmptyChunks}, " +
        $"DesiredRenderableChunks={desiredRenderableChunks}, " +
        $"RenderedDesiredChunks={renderedDesiredChunks}, " +
        $"MissingDesiredDataChunks={missingDesiredDataChunks}, " +
        $"MissingDesiredDataPendingLoadChunks={missingDesiredDataPendingLoadChunks}, " +
        $"UnqueuedMissingDesiredDataChunks={unqueuedMissingDesiredDataChunks}, " +
        $"MissingDesiredRenderableChunks={missingDesiredRenderableChunks}, " +
        $"MissingDesiredRenderablePendingRenderChunks={missingDesiredRenderablePendingRenderChunks}, " +
        $"UnqueuedMissingDesiredRenderableChunks={unqueuedMissingDesiredRenderableChunks}, " +
        $"ActiveRenderedChunks={activeRenderedChunks}, " +
        $"RenderedOutsideDesiredSet={renderedOutsideDesiredSet}, " +
        $"RenderedOutsideKeepSet={renderedOutsideKeepSet}, " +
        $"RenderedKnownEmptyChunks={renderedKnownEmptyChunks}, " +
        $"PendingLoad={sourceStreamer.PendingLoadCount}, " +
        $"PendingRender={sourceStreamer.PendingRenderCount}, " +
        $"PendingLoadSet={sourceStreamer.PendingLoadSetCount}, " +
        $"PendingRenderSet={sourceStreamer.PendingRenderSetCount}, " +
        $"KnownEmpty={sourceStreamer.KnownEmptyChunkCount}, " +
        $"ActiveChunkLoadTasks={sourceStreamer.ActiveChunkLoadTaskCount}, " +
        $"ActiveDensityBuildTasks={sourceStreamer.ActiveDensityBuildTaskCount}";

      if (unqueuedMissingDesiredRenderableChunks > toleratedUnqueuedMissingDesiredRenderableChunks)
      {
        return Fail(
          $"Streaming coverage validation suspicious: unqueued missing desired renderable chunks exceeded tolerance. {diagnostics}",
          out message
        );
      }

      if (renderedOutsideKeepSet > toleratedRenderedChunksOutsideKeepSet)
      {
        return Fail(
          $"Streaming coverage validation suspicious: rendered chunks outside streamer keep set exceeded tolerance. {diagnostics}",
          out message
        );
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

      if (sourceStreamer == null)
      {
        sourceStreamer = GetComponent<WorldStreamer>();
      }

      if (sourceWorld == null && sourceRenderer != null)
      {
        sourceWorld = sourceRenderer.GetComponent<CubusWorld>();
      }

      if (sourceWorld == null && sourceStreamer != null)
      {
        sourceWorld = sourceStreamer.GetComponent<CubusWorld>();
      }

      if (sourceRenderer == null && sourceWorld != null)
      {
        sourceRenderer = sourceWorld.GetComponent<WorldRenderer>();
      }

      if (sourceStreamer == null && sourceWorld != null)
      {
        sourceStreamer = sourceWorld.GetComponent<WorldStreamer>();
      }
    }

    private bool HasChunkData(TerrainSystem terrainSystem, Vector3Int chunkCoord)
    {
      return terrainSystem switch
      {
        TerrainSystem.Block => sourceWorld.Data.BlockChunks.ContainsKey(chunkCoord),
        TerrainSystem.SmoothDensity => sourceWorld.Data.DensityChunks.ContainsKey(chunkCoord),
        _ => false
      };
    }

    private bool HasRenderableSurface(TerrainSystem terrainSystem, Vector3Int chunkCoord, bool hasChunkData)
    {
      if (!hasChunkData)
      {
        return false;
      }

      switch (terrainSystem)
      {
        case TerrainSystem.Block:
          return sourceWorld.Data.BlockChunks.TryGetValue(chunkCoord, out BlockChunkData blockChunk) &&
                 blockChunk != null &&
                 blockChunk.HasAnySolidVoxel();

        case TerrainSystem.SmoothDensity:
        default:
          return sourceWorld.Data.DensityChunks.TryGetValue(chunkCoord, out DensityChunkData densityChunk) &&
                 densityChunk != null &&
                 densityChunk.HasSurfaceCrossing();
      }
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
