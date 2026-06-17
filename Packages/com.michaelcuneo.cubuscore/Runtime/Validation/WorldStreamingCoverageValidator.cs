using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Validation
{
  public sealed class WorldStreamingCoverageValidator : MonoBehaviour
  {
    [SerializeField] private CubusWorld sourceWorld;
    [SerializeField] private WorldRenderer sourceRenderer;
    [SerializeField] private Transform viewer;
    [SerializeField] private bool validateOnStart;
    [SerializeField] private bool logMissingChunks;
    [SerializeField] private bool logRenderedChunksOutsideExpectedSet;
    [SerializeField, Min(0)] private int horizontalRadiusOverride = 0;
    [SerializeField, Min(0)] private int chunksBelowSurface = 1;
    [SerializeField, Min(0)] private int chunksAboveSurface = 1;
    [SerializeField, Min(0)] private int toleratedMissingSurfaceChunks = 0;

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

      if (sourceWorld.Settings == null)
      {
        return Fail("Streaming coverage validation failed: CubusWorld has no WorldSettings.", out message);
      }

      Transform referenceTransform = viewer != null ? viewer : Camera.main != null ? Camera.main.transform : null;
      Vector3 referencePosition = referenceTransform != null
        ? referenceTransform.position
        : sourceWorld.SuggestedSpawnLocation;

      WorldSettings settings = sourceWorld.Settings;
      float voxelSize = Mathf.Max(0.0001f, settings.VoxelSize);
      Vector3 localReference = sourceRenderer.transform.InverseTransformPoint(referencePosition);
      Vector3 voxelReference = localReference / voxelSize;

      Vector3Int viewerChunkCoord = new(
        VoxelMath.FloorDiv(Mathf.FloorToInt(voxelReference.x), VoxelConstants.ChunkSize),
        VoxelMath.FloorDiv(Mathf.FloorToInt(voxelReference.y), VoxelConstants.ChunkSize),
        VoxelMath.FloorDiv(Mathf.FloorToInt(voxelReference.z), VoxelConstants.ChunkSize)
      );

      int horizontalRadius = horizontalRadiusOverride > 0
        ? horizontalRadiusOverride
        : Mathf.Max(1, settings.ViewDistanceInChunks);

      WorldGenerator generator = new(settings);
      HashSet<Vector3Int> expected = new();
      int expectedSurfaceBandChunks = 0;
      int expectedRenderableSurfaceChunks = 0;
      int missingDataChunks = 0;
      int missingRenderedSurfaceChunks = 0;
      int renderedExpectedChunks = 0;
      int renderedUnexpectedChunks = 0;

      for (int dz = -horizontalRadius; dz <= horizontalRadius; dz++)
      {
        for (int dx = -horizontalRadius; dx <= horizontalRadius; dx++)
        {
          int chunkX = viewerChunkCoord.x + dx;
          int chunkZ = viewerChunkCoord.z + dz;
          int surfaceChunkY = generator.GetSurfaceChunkYForChunkColumn(new Vector2Int(chunkX, chunkZ));
          int minY = surfaceChunkY - chunksBelowSurface;
          int maxY = surfaceChunkY + chunksAboveSurface;

          ClampYRangeToSettings(settings, ref minY, ref maxY);

          for (int y = minY; y <= maxY; y++)
          {
            Vector3Int chunkCoord = new(chunkX, y, chunkZ);
            if (!settings.IsInsideWorldBounds(chunkCoord))
            {
              continue;
            }

            expected.Add(chunkCoord);
            expectedSurfaceBandChunks++;

            bool hasChunkData = HasChunkData(settings.TerrainSystem, chunkCoord);
            bool hasRenderedView = sourceRenderer.HasChunkView(chunkCoord);
            bool shouldRender = HasRenderableSurface(settings.TerrainSystem, chunkCoord, hasChunkData);

            if (!hasChunkData)
            {
              missingDataChunks++;
              if (logMissingChunks)
              {
                Debug.LogWarning($"Expected streaming chunk has no data yet. Chunk={chunkCoord}", this);
              }
            }

            if (shouldRender)
            {
              expectedRenderableSurfaceChunks++;
              if (!hasRenderedView)
              {
                missingRenderedSurfaceChunks++;
                if (logMissingChunks)
                {
                  Debug.LogWarning($"Renderable streaming chunk is missing a rendered ChunkView. Chunk={chunkCoord}", this);
                }
              }
            }

            if (hasRenderedView)
            {
              renderedExpectedChunks++;
            }
          }
        }
      }

      foreach (KeyValuePair<Vector3Int, ChunkView> pair in sourceRenderer.ActiveChunkViews)
      {
        if (!expected.Contains(pair.Key))
        {
          renderedUnexpectedChunks++;
          if (logRenderedChunksOutsideExpectedSet)
          {
            Debug.Log($"Rendered chunk is outside validator expected set. Chunk={pair.Key}", pair.Value);
          }
        }
      }

      string diagnostics =
        $"Streaming coverage report. " +
        $"ViewerChunk={viewerChunkCoord}, " +
        $"Radius={horizontalRadius}, " +
        $"YBand=-{chunksBelowSurface}/+{chunksAboveSurface}, " +
        $"ExpectedSurfaceBandChunks={expectedSurfaceBandChunks}, " +
        $"ExpectedRenderableSurfaceChunks={expectedRenderableSurfaceChunks}, " +
        $"RenderedExpectedChunks={renderedExpectedChunks}, " +
        $"ActiveRenderedChunks={sourceRenderer.ActiveChunkViews.Count}, " +
        $"MissingDataChunks={missingDataChunks}, " +
        $"MissingRenderedSurfaceChunks={missingRenderedSurfaceChunks}, " +
        $"RenderedUnexpectedChunks={renderedUnexpectedChunks}";

      if (missingRenderedSurfaceChunks > toleratedMissingSurfaceChunks)
      {
        return Fail(
          $"Streaming coverage validation suspicious: missing rendered surface chunks exceeded tolerance. {diagnostics}",
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

      if (sourceWorld == null && sourceRenderer != null)
      {
        sourceWorld = sourceRenderer.GetComponent<CubusWorld>();
      }

      if (sourceRenderer == null && sourceWorld != null)
      {
        sourceRenderer = sourceWorld.GetComponent<WorldRenderer>();
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

    private static void ClampYRangeToSettings(WorldSettings settings, ref int minY, ref int maxY)
    {
      if (settings.TerrainSystem == TerrainSystem.Block)
      {
        settings.GetEffectiveBlockChunkYRange(out int generatedMinY, out int generatedMaxY);
        minY = Mathf.Max(minY, generatedMinY);
        maxY = Mathf.Min(maxY, generatedMaxY);
        return;
      }

      settings.GetEffectiveDensityChunkYRange(out int densityMinY, out int densityMaxY);
      minY = Mathf.Max(minY, densityMinY);
      maxY = Mathf.Min(maxY, densityMaxY);
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
