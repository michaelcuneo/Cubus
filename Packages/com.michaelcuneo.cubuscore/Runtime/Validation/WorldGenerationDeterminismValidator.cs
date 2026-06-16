using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Validation
{
  public sealed class WorldGenerationDeterminismValidator : MonoBehaviour
  {
    [SerializeField] private CubusWorld sourceWorld;
    [SerializeField] private WorldSettings settings = new();
    [SerializeField, Min(0)] private int radiusInChunks = 2;
    [SerializeField] private bool validateOnStart;

    public bool LastValidationPassed { get; private set; }
    public string LastValidationMessage { get; private set; } = "Not run.";

    private void Start()
    {
      if (validateOnStart)
      {
        ValidateAndLog();
      }
    }

    [ContextMenu("Validate Deterministic World Generation")]
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
      WorldSettings activeSettings = ResolveSettings();

      if (activeSettings == null)
      {
        return Fail("Determinism validation failed: no WorldSettings are available.", out message);
      }

      if (!activeSettings.TryValidateConfiguration(out string configError))
      {
        return Fail($"Determinism validation failed: invalid WorldSettings. {configError}", out message);
      }

      List<Vector3Int> coords = BuildChunkCoordList(activeSettings, radiusInChunks);

      if (coords.Count == 0)
      {
        return Fail("Determinism validation failed: no chunk coordinates were selected.", out message);
      }

      bool passed = activeSettings.TerrainSystem == TerrainSystem.Block
        ? ValidateBlockChunks(activeSettings, coords, out message)
        : ValidateDensityChunks(activeSettings, coords, out message);

      LastValidationPassed = passed;
      LastValidationMessage = message;
      return passed;
    }

    private WorldSettings ResolveSettings()
    {
      if (sourceWorld != null)
      {
        return sourceWorld.Settings;
      }

      return settings;
    }

    private bool ValidateBlockChunks(
      WorldSettings activeSettings,
      List<Vector3Int> coords,
      out string message)
    {
      Dictionary<Vector3Int, ulong> firstPassHashes = new(coords.Count);
      WorldGenerator generator = new(activeSettings);

      for (int i = 0; i < coords.Count; i++)
      {
        Vector3Int coord = coords[i];
        BlockChunkData chunk = new(coord);
        generator.GenerateBlockChunkData(chunk);
        firstPassHashes[coord] = ChunkDeterminismHash.Hash(chunk);
      }

      for (int i = coords.Count - 1; i >= 0; i--)
      {
        Vector3Int coord = coords[i];
        BlockChunkData second = new(coord);
        generator.GenerateBlockChunkData(second);

        ulong secondHash = ChunkDeterminismHash.Hash(second);
        if (firstPassHashes[coord] == secondHash)
        {
          continue;
        }

        BlockChunkData first = new(coord);
        generator.GenerateBlockChunkData(first);

        ChunkDeterminismHash.AreEqual(first, second, out string diff);
        return Fail(
          $"Block determinism validation failed at chunk {coord}. " +
          $"FirstHash={firstPassHashes[coord]}, SecondHash={secondHash}. {diff}",
          out message
        );
      }

      message = $"Block determinism validation passed for {coords.Count} chunks.";
      return true;
    }

    private bool ValidateDensityChunks(
      WorldSettings activeSettings,
      List<Vector3Int> coords,
      out string message)
    {
      Dictionary<Vector3Int, ulong> firstPassHashes = new(coords.Count);
      WorldGenerator generator = new(activeSettings);

      for (int i = 0; i < coords.Count; i++)
      {
        Vector3Int coord = coords[i];
        DensityChunkData chunk = new(coord);
        generator.FillDensityChunkFromTerrainSampler(chunk);
        firstPassHashes[coord] = ChunkDeterminismHash.Hash(chunk);
      }

      for (int i = coords.Count - 1; i >= 0; i--)
      {
        Vector3Int coord = coords[i];
        DensityChunkData second = new(coord);
        generator.FillDensityChunkFromTerrainSampler(second);

        ulong secondHash = ChunkDeterminismHash.Hash(second);
        if (firstPassHashes[coord] == secondHash)
        {
          continue;
        }

        DensityChunkData first = new(coord);
        generator.FillDensityChunkFromTerrainSampler(first);

        ChunkDeterminismHash.AreEqual(first, second, out string diff);
        return Fail(
          $"Density determinism validation failed at chunk {coord}. " +
          $"FirstHash={firstPassHashes[coord]}, SecondHash={secondHash}. {diff}",
          out message
        );
      }

      message = $"Density determinism validation passed for {coords.Count} chunks.";
      return true;
    }

    private static List<Vector3Int> BuildChunkCoordList(WorldSettings activeSettings, int radius)
    {
      List<Vector3Int> coords = new();

      int safeRadius = Mathf.Max(0, radius);
      int minX = -safeRadius;
      int maxX = safeRadius;
      int minZ = -safeRadius;
      int maxZ = safeRadius;

      if (activeSettings.UseFixedGenerationBounds)
      {
        activeSettings.GetGenerationChunkBoundsXZ(out int boundMinX, out int boundMaxX, out int boundMinZ, out int boundMaxZ);
        minX = Mathf.Max(minX, boundMinX);
        maxX = Mathf.Min(maxX, boundMaxX);
        minZ = Mathf.Max(minZ, boundMinZ);
        maxZ = Mathf.Min(maxZ, boundMaxZ);
      }

      int minY;
      int maxY;

      if (activeSettings.TerrainSystem == TerrainSystem.Block)
      {
        activeSettings.GetEffectiveBlockChunkYRange(out minY, out maxY);
      }
      else
      {
        activeSettings.GetEffectiveDensityChunkYRange(out minY, out maxY);
      }

      for (int y = minY; y <= maxY; y++)
      {
        for (int z = minZ; z <= maxZ; z++)
        {
          for (int x = minX; x <= maxX; x++)
          {
            Vector3Int coord = new(x, y, z);
            if (activeSettings.IsInsideWorldBounds(coord))
            {
              coords.Add(coord);
            }
          }
        }
      }

      return coords;
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
